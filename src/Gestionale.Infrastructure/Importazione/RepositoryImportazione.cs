using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Importazione;
using Gestionale.Domain.Impianti;
using Gestionale.Domain.Importazione;
using Npgsql;

namespace Gestionale.Infrastructure.Importazione;

/// <summary>
/// Scrittura dell'importazione in una transazione (ADR 0007). I clienti già presenti
/// (stesso codice fiscale o partita IVA) si usano senza modificarli; le sedi si
/// riconoscono da indirizzo, civico e CAP; un impianto con un PDR attivo o un codice
/// catasto già presenti si salta con un avviso. Con un errore la transazione si annulla.
/// </summary>
public sealed class RepositoryImportazione(Database.Database db) : IRepositoryImportazione
{
    private const string ValidaOggi = "valido_dal <= current_date AND (valido_al IS NULL OR valido_al >= current_date)";

    public async Task<EsitoImportazione> ImportaAsync(Contesto contesto, IReadOnlyList<RigaImportata> righe, bool conferma, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        ArgumentNullException.ThrowIfNull(righe);

        var esito = await db.EseguiAsync(
            contesto.AziendaId,
            contesto.UtenteId,
            (c, t) => new Lavoro(c, t, contesto, ct).EseguiAsync(righe),
            r => conferma && r.Errori.Count == 0,
            ct);

        // Scritti solo se confermati: l'anteprima ha fatto lo stesso lavoro e l'ha annullato.
        return esito with { Importata = conferma && esito.Errori.Count == 0 };
    }

    private sealed class Lavoro(NpgsqlConnection c, NpgsqlTransaction t, Contesto contesto, CancellationToken ct)
    {
        private readonly List<SegnalazioneRiga> _errori = [];
        private readonly List<SegnalazioneRiga> _avvisi = [];
        private int _clientiNuovi;
        private int _clientiEsistenti;
        private int _sediNuove;
        private int _impiantiNuovi;
        private int _impiantiSaltati;
        private int _generatori;

        public async Task<EsitoImportazione> EseguiAsync(IReadOnlyList<RigaImportata> righe)
        {
            var tipiImpianto = await VociAsync("impianti.tipi_impianto", "tipo_impianto_id");
            var combustibili = await VociAsync("impianti.combustibili", "combustibile_id");
            var tipiGeneratore = await VociAsync("impianti.tipi_generatore", "tipo_generatore_id");

            // Voci: il file usa il codice o il nome, senza distinguere le maiuscole.
            foreach (var r in righe.Where(r => r.Impianto is not null))
            {
                var i = r.Impianto!;
                if (!tipiImpianto.ContainsKey(i.TipoImpianto.ToLowerInvariant()))
                {
                    _errori.Add(new(r.Riga, nameof(ErroreImportazione.TipoImpiantoSconosciuto), i.TipoImpianto));
                }

                if (i.Combustibile is { } combustibile && !combustibili.ContainsKey(combustibile.ToLowerInvariant()))
                {
                    _errori.Add(new(r.Riga, nameof(ErroreImportazione.CombustibileSconosciuto), combustibile));
                }

                if (i.Generatore is { } g && !tipiGeneratore.ContainsKey(g.TipoGeneratore.ToLowerInvariant()))
                {
                    _errori.Add(new(r.Riga, nameof(ErroreImportazione.TipoGeneratoreSconosciuto), g.TipoGeneratore));
                }
            }

            if (_errori.Count > 0)
            {
                return Esito(righe.Count);
            }

            var regione = await c.ExecuteScalarAsync<string>(Comando("SELECT regione FROM sec.aziende WHERE azienda_id = @aziendaId", new { contesto.AziendaId }));
            var sistema = CatastiRegionali.SistemaPerRegione.GetValueOrDefault(regione ?? "");

            var codici = righe.Select(r => r.Cliente.CodiceFiscale).Concat(righe.Select(r => r.Cliente.PartitaIva)).OfType<string>().Distinct().ToArray();
            var esistenti = (await c.QueryAsync<ClienteEsistente>(Comando(
                """
                SELECT cliente_id, codice_fiscale, partita_iva FROM anagrafica.clienti
                WHERE eliminato_il IS NULL AND (codice_fiscale = ANY(@codici) OR partita_iva = ANY(@codici))
                """,
                new { codici }))).ToList();

            var pdrPresenti = (await c.QueryAsync<string>(Comando(
                "SELECT pdr FROM impianti.impianti WHERE pdr IS NOT NULL AND stato = 'attivo' AND eliminato_il IS NULL"))).ToHashSet(StringComparer.Ordinal);
            var catastoPresenti = (await c.QueryAsync<string>(Comando(
                "SELECT lower(codice) FROM impianti.riferimenti_catasto WHERE regione = @regione", new { regione }))).ToHashSet(StringComparer.Ordinal);

            foreach (var gruppo in righe.GroupBy(r => r.ChiaveCliente))
            {
                var primo = gruppo.First();
                var esistente = esistenti.FirstOrDefault(e =>
                    (primo.Cliente.CodiceFiscale is { } cf && e.CodiceFiscale == cf) || (primo.Cliente.PartitaIva is { } piva && e.PartitaIva == piva));
                var clienteId = esistente?.ClienteId ?? await CreaClienteAsync(primo);
                if (esistente is null)
                {
                    _clientiNuovi++;
                }
                else
                {
                    _clientiEsistenti++;
                }

                var sedi = esistente is null ? new Dictionary<string, Guid>(StringComparer.Ordinal) : await SediAsync(clienteId);
                foreach (var riga in gruppo)
                {
                    if (!sedi.TryGetValue(riga.ChiaveSede, out var sedeId))
                    {
                        sedeId = await CreaSedeAsync(clienteId, riga);
                        sedi[riga.ChiaveSede] = sedeId;
                        _sediNuove++;
                    }

                    if (riga.Impianto is not { } impianto)
                    {
                        continue;
                    }

                    if (impianto.Pdr is { } pdr && pdrPresenti.Contains(pdr))
                    {
                        _avvisi.Add(new(riga.Riga, nameof(AvvisoImportazione.ImpiantoGiaPresentePdr), pdr));
                        _impiantiSaltati++;
                        continue;
                    }

                    if (impianto.CodiceCatasto is { } codice && catastoPresenti.Contains(codice.ToLowerInvariant()))
                    {
                        _avvisi.Add(new(riga.Riga, nameof(AvvisoImportazione.ImpiantoGiaPresenteCatasto), codice));
                        _impiantiSaltati++;
                        continue;
                    }

                    await CreaImpiantoAsync(impianto, sedeId, tipiImpianto, combustibili, tipiGeneratore, regione, sistema);
                }
            }

            return Esito(righe.Count);
        }

        private async Task<Guid> CreaClienteAsync(RigaImportata riga)
        {
            var d = riga.Cliente;
            var clienteId = await c.ExecuteScalarAsync<Guid>(Comando(
                """
                INSERT INTO anagrafica.clienti (azienda_id, tipo, denominazione, nome, cognome, codice_fiscale, partita_iva, pec, creato_da)
                VALUES (@aziendaId, @Tipo, @Denominazione, @Nome, @Cognome, @CodiceFiscale, @PartitaIva, @Pec, @utenteId)
                RETURNING cliente_id
                """,
                new { contesto.AziendaId, d.Tipo, d.Denominazione, d.Nome, d.Cognome, d.CodiceFiscale, d.PartitaIva, d.Pec, contesto.UtenteId }));

            // Email e telefono del file diventano i contatti principali; un numero che comincia per 3 è un cellulare.
            var contatti = new List<(string Tipo, string Valore)>();
            if (riga.Email is { } email)
            {
                contatti.Add(("email", email));
            }

            if (riga.Telefono is { } telefono)
            {
                var cifre = string.Concat(telefono.Where(char.IsAsciiDigit));
                var nazionale = cifre.StartsWith("39", StringComparison.Ordinal) && telefono.TrimStart().StartsWith('+') ? cifre[2..] : cifre;
                contatti.Add((nazionale.StartsWith('3') ? "cellulare" : "telefono", telefono));
            }

            foreach (var (tipo, valore) in contatti)
            {
                await c.ExecuteAsync(Comando(
                    """
                    INSERT INTO anagrafica.contatti (azienda_id, cliente_id, tipo, valore, principale, creato_da)
                    VALUES (@aziendaId, @clienteId, @tipo, @valore, true, @utenteId)
                    """,
                    new { contesto.AziendaId, clienteId, tipo, valore, contesto.UtenteId }));
            }

            return clienteId;
        }

        private async Task<Dictionary<string, Guid>> SediAsync(Guid clienteId)
        {
            var sedi = await c.QueryAsync<(Guid SedeId, string Indirizzo, string? Civico, string Cap)>(Comando(
                "SELECT sede_id, indirizzo, civico, cap FROM anagrafica.sedi WHERE cliente_id = @clienteId AND eliminata_il IS NULL",
                new { clienteId }));
            var perChiave = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var s in sedi)
            {
                perChiave.TryAdd(string.Join('|', s.Indirizzo.ToLowerInvariant(), s.Civico?.ToLowerInvariant(), s.Cap), s.SedeId);
            }

            return perChiave;
        }

        private async Task<Guid> CreaSedeAsync(Guid clienteId, RigaImportata riga)
        {
            var s = riga.Sede;
            return await c.ExecuteScalarAsync<Guid>(Comando(
                """
                INSERT INTO anagrafica.sedi (azienda_id, cliente_id, indirizzo, civico, cap, comune, provincia, codice_istat_comune, creato_da)
                VALUES (@aziendaId, @clienteId, @Indirizzo, @Civico, @Cap, @Comune, @Provincia, @CodiceIstatComune, @utenteId)
                RETURNING sede_id
                """,
                new { contesto.AziendaId, clienteId, s.Indirizzo, s.Civico, s.Cap, s.Comune, s.Provincia, s.CodiceIstatComune, contesto.UtenteId }));
        }

        private async Task CreaImpiantoAsync(
            DatiImpiantoImportato i,
            Guid sedeId,
            Dictionary<string, Guid> tipiImpianto,
            Dictionary<string, Guid> combustibili,
            Dictionary<string, Guid> tipiGeneratore,
            string? regione,
            string? sistema)
        {
            var combustibileId = i.Combustibile is { } combustibile ? combustibili[combustibile.ToLowerInvariant()] : (Guid?)null;
            var impiantoId = await c.ExecuteScalarAsync<Guid>(Comando(
                """
                INSERT INTO impianti.impianti (azienda_id, sede_id, tipo_impianto_id, anno_installazione, potenza_utile_kw,
                                               combustibile_id, pdr, note, manutenzione_mesi, creato_da)
                VALUES (@aziendaId, @sedeId, @tipo, @AnnoInstallazione, @PotenzaUtileKw, @combustibileId, @Pdr, @Note, @ManutenzioneMesi, @utenteId)
                RETURNING impianto_id
                """,
                new
                {
                    contesto.AziendaId,
                    sedeId,
                    tipo = tipiImpianto[i.TipoImpianto.ToLowerInvariant()],
                    i.AnnoInstallazione,
                    i.PotenzaUtileKw,
                    combustibileId,
                    i.Pdr,
                    i.Note,
                    i.ManutenzioneMesi,
                    contesto.UtenteId,
                }));
            _impiantiNuovi++;

            if (i.CodiceCatasto is { } codice && sistema is not null)
            {
                await c.ExecuteAsync(Comando(
                    """
                    INSERT INTO impianti.riferimenti_catasto (azienda_id, impianto_id, regione, sistema, codice, creato_da)
                    VALUES (@aziendaId, @impiantoId, @regione, @sistema, @codice, @utenteId)
                    """,
                    new { contesto.AziendaId, impiantoId, regione, sistema, codice, contesto.UtenteId }));
            }

            if (i.Generatore is { } g)
            {
                await c.ExecuteAsync(Comando(
                    """
                    INSERT INTO impianti.generatori (azienda_id, impianto_id, tipo_generatore_id, marca, modello, matricola, potenza_kw,
                                                     combustibile_id, installato_il, sigla_catasto, creato_da)
                    VALUES (@aziendaId, @impiantoId, @tipo, @Marca, @Modello, @Matricola, @PotenzaKw, @combustibileId, @InstallatoIl, @SiglaCatasto, @utenteId)
                    """,
                    new
                    {
                        contesto.AziendaId,
                        impiantoId,
                        tipo = tipiGeneratore[g.TipoGeneratore.ToLowerInvariant()],
                        g.Marca,
                        g.Modello,
                        g.Matricola,
                        g.PotenzaKw,
                        combustibileId,
                        g.InstallatoIl,
                        g.SiglaCatasto,
                        contesto.UtenteId,
                    }));
                _generatori++;
            }
        }

        // Codice e nome della voce, in minuscolo, verso il suo identificativo.
        private async Task<Dictionary<string, Guid>> VociAsync(string tabella, string chiave)
        {
            var voci = await c.QueryAsync<(Guid Id, string Codice, string Nome)>(Comando(
                $"SELECT {chiave}, codice, nome FROM {tabella} WHERE {ValidaOggi}"));
            var perNome = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var v in voci)
            {
                perNome.TryAdd(v.Codice.ToLowerInvariant(), v.Id);
                perNome.TryAdd(v.Nome.ToLowerInvariant(), v.Id);
            }

            return perNome;
        }

        private EsitoImportazione Esito(int righe) => new(
            _errori.Count == 0, righe, _clientiNuovi, _clientiEsistenti, _sediNuove, _impiantiNuovi, _impiantiSaltati, _generatori, _errori, _avvisi);

        private CommandDefinition Comando(string sql, object? parametri = null) => new(sql, parametri, t, cancellationToken: ct);
    }

    private sealed class ClienteEsistente
    {
        public Guid ClienteId { get; init; }

        public string? CodiceFiscale { get; init; }

        public string? PartitaIva { get; init; }
    }
}
