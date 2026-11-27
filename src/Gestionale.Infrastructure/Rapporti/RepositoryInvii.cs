using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Gestionale.Application.Anagrafica;
using Gestionale.Application.Rapporti;
using Gestionale.Domain.Rapporti;
using Npgsql;

namespace Gestionale.Infrastructure.Rapporti;

/// <summary>
/// Invii al SIERT con Dapper. Il file si compone qui dal tracciato in vigore e
/// dai dati dei rapporti (ADR 0006); immutabilità, righe senza doppioni ed esiti
/// li garantiscono i trigger e le funzioni delle migrazioni 0010 e 0011.
/// </summary>
public sealed class RepositoryInvii(Database.Database db) : IRepositoryInvii
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private const string Riepilogo = """
        SELECT i.invio_catasto_id, m.codice AS modello, i.nome_file, i.versione_tracciato, i.stato,
               (SELECT count(*) FROM norma.invii_rapporti ir WHERE ir.invio_catasto_id = i.invio_catasto_id)::integer AS righe,
               i.caricato_il, i.elaborato_il, i.creato_il, i.versione
        FROM norma.invii_catasto i
        JOIN norma.modelli_rapporto m ON m.modello_rapporto_id = i.modello_rapporto_id
        """;

    public Task<Pagina<RiepilogoInvio>> CercaAsync(Contesto contesto, int numeroPagina, int dimensione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var totale = await c.ExecuteScalarAsync<int>(Comando("SELECT count(*) FROM norma.invii_catasto", null, t, ct));
            var elementi = await c.QueryAsync<RiepilogoInvio>(Comando(
                Riepilogo + " ORDER BY i.creato_il DESC, i.invio_catasto_id DESC LIMIT @dimensione OFFSET @salta",
                new { dimensione, salta = (numeroPagina - 1) * dimensione }, t, ct));
            return new Pagina<RiepilogoInvio>([.. elementi], totale, numeroPagina, dimensione);
        }, ct);

    public Task<InvioCatasto?> InvioAsync(Contesto contesto, Guid invioId, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var invio = await c.QuerySingleOrDefaultAsync<RiepilogoInvio>(Comando(
                Riepilogo + " WHERE i.invio_catasto_id = @invioId", new { invioId }, t, ct));
            if (invio is null)
            {
                return null;
            }

            var righe = await c.QueryAsync<RigaInvio>(Comando(
                """
                SELECT ir.riga, ir.rapporto_controllo_id, r.impianto_id, r.data_controllo,
                       CASE WHEN cl.tipo = 'privato' THEN cl.cognome || ' ' || cl.nome ELSE cl.denominazione END AS cliente,
                       ir.esito, ir.causa
                FROM norma.invii_rapporti ir
                JOIN norma.rapporti_controllo r ON r.rapporto_controllo_id = ir.rapporto_controllo_id
                JOIN impianti.impianti im ON im.impianto_id = r.impianto_id
                JOIN anagrafica.sedi s ON s.sede_id = im.sede_id
                JOIN anagrafica.clienti cl ON cl.cliente_id = s.cliente_id
                WHERE ir.invio_catasto_id = @invioId
                ORDER BY ir.riga
                """,
                new { invioId }, t, ct));

            return (InvioCatasto?)new InvioCatasto { Invio = invio, Rapporti = [.. righe] };
        }, ct);

    public Task<FileInvio?> FileAsync(Contesto contesto, Guid invioId, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            var riga = await c.QuerySingleOrDefaultAsync<RigaFile>(Comando(
                "SELECT nome_file, contenuto FROM norma.invii_catasto WHERE invio_catasto_id = @invioId", new { invioId }, t, ct));
            return riga is null ? null : new FileInvio(riga.NomeFile, new UTF8Encoding(false).GetBytes(riga.Contenuto));
        }, ct);

    public Task<EsitoRapporto> PreparaAsync(Contesto contesto, string modello, DateTimeOffset momento, CancellationToken ct) =>
        Esegui<EsitoRapporto>(contesto, async (c, t) =>
        {
            var azienda = await c.QuerySingleAsync<DatiAzienda>(Comando(
                "SELECT regione, codice_ditta_siert FROM sec.aziende WHERE azienda_id = @aziendaId", new { aziendaId = contesto.AziendaId }, t, ct));
            var (versione, campi) = await LetturaTracciato.LeggiAsync(c, t, modello, ct);

            // Il tracciato SIERT vale per la Toscana; il CURIT avrà il suo adattatore.
            if (azienda.Regione != "toscana" || versione is null)
            {
                return new EsitoRapporto.NonValida(ErroreRapporto.NessunRapportoDaInviare);
            }

            // Firmati o scartati, del modello, non già in un invio vivo; bloccati
            // finché il file non è registrato, così due preparazioni non li prendono entrambe.
            var candidati = (await c.QueryAsync<Candidato>(Comando(
                """
                SELECT r.rapporto_controllo_id, r.versione, r.data_controllo, r.dati::text AS dati, b.codice AS codice_bollino,
                       norma.errori_bloccanti(r.rapporto_controllo_id) AS errori
                FROM norma.rapporti_controllo r
                JOIN norma.modelli_rapporto m ON m.modello_rapporto_id = r.modello_rapporto_id
                LEFT JOIN norma.bollini b ON b.bollino_id = r.bollino_id
                WHERE m.codice = @modello
                  AND r.stato IN ('firmato', 'scartato')
                  AND NOT EXISTS (SELECT 1 FROM norma.invii_rapporti ir
                                  WHERE ir.rapporto_controllo_id = r.rapporto_controllo_id AND ir.esito <> 'scartato')
                ORDER BY r.data_controllo, r.rapporto_controllo_id
                FOR UPDATE OF r
                """,
                new { modello }, t, ct))).ToList();

            var inviabili = candidati.Where(r => r.Errori is null).ToList();
            var esclusi = candidati.Where(r => r.Errori is not null).Select(r => r.RapportoControlloId).ToList();
            if (inviabili.Count == 0)
            {
                return new EsitoRapporto.NonValida([ErroreRapporto.NessunRapportoDaInviare], [.. esclusi.Select(e => e.ToString())]);
            }

            var righe = inviabili.Select(r =>
            {
                var dati = JsonNode.Parse(r.Dati)!.AsObject();
                return RegoleRapporti.RigaInvio(campi, campo => campo.Origine switch
                {
                    OriginiCampo.Dati => RegoleRapporti.ValoreDati(dati, campo.Chiave),
                    OriginiCampo.Rapporto => r.DataControllo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    OriginiCampo.Azienda => azienda.CodiceDittaSiert?.ToString(CultureInfo.InvariantCulture),
                    OriginiCampo.Bollino => r.CodiceBollino,
                    _ => null,
                });
            });

            var contenuto = RegoleRapporti.ContenutoInvio(righe);
            var invioId = await c.ExecuteScalarAsync<Guid>(Comando(
                """
                INSERT INTO norma.invii_catasto (azienda_id, regione, sistema, tipo, modello_rapporto_id, versione_tracciato,
                                                 nome_file, contenuto, impronta_sha256, creato_da)
                SELECT @aziendaId, 'toscana', 'siert', 'import_massivo_rcee', m.modello_rapporto_id, @versione,
                       @nomeFile, @testo, @impronta, @utenteId
                FROM norma.modelli_rapporto m
                WHERE m.codice = @modello
                RETURNING invio_catasto_id
                """,
                new
                {
                    aziendaId = contesto.AziendaId,
                    versione,
                    nomeFile = RegoleRapporti.NomeFileInvio(modello, momento),
                    testo = Encoding.UTF8.GetString(contenuto),
                    impronta = SHA256.HashData(contenuto),
                    utenteId = contesto.UtenteId,
                    modello,
                },
                t, ct));

            await c.ExecuteAsync(Comando(
                """
                INSERT INTO norma.invii_rapporti (azienda_id, invio_catasto_id, rapporto_controllo_id, riga, versione_rapporto, creato_da)
                SELECT @aziendaId, @invioId, r.id, r.riga::integer, r.versione, @utenteId
                FROM unnest(@rapporti, @versioni) WITH ORDINALITY AS r (id, versione, riga)
                """,
                new
                {
                    aziendaId = contesto.AziendaId,
                    invioId,
                    rapporti = inviabili.Select(r => r.RapportoControlloId).ToArray(),
                    versioni = inviabili.Select(r => r.Versione).ToArray(),
                    utenteId = contesto.UtenteId,
                },
                t, ct));

            return new EsitoRapporto.InvioPreparato(invioId, 1, inviabili.Count, esclusi);
        }, ct);

    public Task<EsitoRapporto> SegnaCaricatoAsync(Contesto contesto, Guid invioId, int versione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            if (Controlla(await BloccaAsync(c, t, invioId, ct), versione, "preparato") is { } esito)
            {
                return esito;
            }

            await c.ExecuteAsync(Comando(
                "UPDATE norma.invii_catasto SET stato = 'caricato', caricato_il = now(), modificato_da = @utenteId WHERE invio_catasto_id = @invioId",
                new { invioId, utenteId = contesto.UtenteId }, t, ct));
            return await RiuscitaAsync(c, t, invioId, ct);
        }, ct);

    public Task<EsitoRapporto> AnnullaAsync(Contesto contesto, Guid invioId, int versione, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            if (Controlla(await BloccaAsync(c, t, invioId, ct), versione, "preparato") is { } esito)
            {
                return esito;
            }

            await c.ExecuteAsync(Comando("SELECT norma.annulla_invio(@invioId, @versione)", new { invioId, versione }, t, ct));
            return await RiuscitaAsync(c, t, invioId, ct);
        }, ct);

    public Task<EsitoRapporto> RegistraEsitiAsync(
        Contesto contesto, Guid invioId, int versione, IReadOnlyList<EsitoRigaInvio> esiti, CancellationToken ct) =>
        Esegui(contesto, async (c, t) =>
        {
            if (Controlla(await BloccaAsync(c, t, invioId, ct), versione, "caricato") is { } esito)
            {
                return esito;
            }

            var nellInvio = (await c.QueryAsync<Guid>(Comando(
                "SELECT rapporto_controllo_id FROM norma.invii_rapporti WHERE invio_catasto_id = @invioId", new { invioId }, t, ct)))
                .ToHashSet();
            var indicati = esiti.Select(e => e.RapportoControlloId!.Value).ToList();

            var estranei = indicati.Where(id => !nellInvio.Contains(id)).Select(id => id.ToString()).ToList();
            if (estranei.Count > 0)
            {
                return new EsitoRapporto.NonValida([ErroreRapporto.RapportoNonNellInvio], estranei);
            }

            var mancanti = nellInvio.Except(indicati).Select(id => id.ToString()).ToList();
            if (mancanti.Count > 0 || indicati.Count != indicati.Distinct().Count())
            {
                return new EsitoRapporto.NonValida([ErroreRapporto.EsitoInvioNonValido], mancanti);
            }

            var json = JsonSerializer.Serialize(esiti, s_json);
            await c.ExecuteAsync(Comando(
                "SELECT norma.registra_esiti_invio(@invioId, @versione, @json::jsonb)", new { invioId, versione, json }, t, ct));
            return await RiuscitaAsync(c, t, invioId, ct);
        }, ct);

    private static Task<StatoAttuale?> BloccaAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid invioId, CancellationToken ct) =>
        c.QuerySingleOrDefaultAsync<StatoAttuale>(Comando(
            "SELECT stato, versione FROM norma.invii_catasto WHERE invio_catasto_id = @invioId FOR UPDATE", new { invioId }, t, ct));

    private static EsitoRapporto? Controlla(StatoAttuale? attuale, int versione, string statoAmmesso) =>
        attuale switch
        {
            null => new EsitoRapporto.NonTrovata(),
            _ when attuale.Versione != versione => new EsitoRapporto.VersioneSuperata(),
            _ when attuale.Stato != statoAmmesso => new EsitoRapporto.StatoNonConsentito("invio_" + attuale.Stato),
            _ => null,
        };

    private static async Task<EsitoRapporto> RiuscitaAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid invioId, CancellationToken ct) =>
        new EsitoRapporto.Riuscita(invioId, await c.ExecuteScalarAsync<int>(Comando(
            "SELECT versione FROM norma.invii_catasto WHERE invio_catasto_id = @invioId", new { invioId }, t, ct)));

    private Task<T> Esegui<T>(Contesto contesto, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> lavoro, CancellationToken ct) =>
        db.EseguiAsync(contesto.AziendaId, contesto.UtenteId, lavoro, ct);

    private static CommandDefinition Comando(string sql, object? parametri, NpgsqlTransaction transazione, CancellationToken ct) =>
        new(sql, parametri, transazione, cancellationToken: ct);

    private sealed class StatoAttuale
    {
        public string Stato { get; init; } = "";

        public int Versione { get; init; }
    }

    private sealed class DatiAzienda
    {
        public string Regione { get; init; } = "";

        public int? CodiceDittaSiert { get; init; }
    }

    private sealed class Candidato
    {
        public Guid RapportoControlloId { get; init; }

        public int Versione { get; init; }

        public DateOnly DataControllo { get; init; }

        public string Dati { get; init; } = "{}";

        public string? CodiceBollino { get; init; }

        public string? Errori { get; init; }
    }

    private sealed class RigaFile
    {
        public string NomeFile { get; init; } = "";

        public string Contenuto { get; init; } = "";
    }
}
