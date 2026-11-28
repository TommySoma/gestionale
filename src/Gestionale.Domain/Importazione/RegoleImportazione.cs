using System.Globalization;
using System.Text;
using Gestionale.Domain.Anagrafica;
using Gestionale.Domain.Impianti;

namespace Gestionale.Domain.Importazione;

/// <summary>Motivi per cui un file o una riga non si importa; l'API li traduce in messaggi.</summary>
public enum ErroreImportazione
{
    FileVuoto,
    FileTroppoGrande,
    TroppeRigheFile,
    VirgoletteNonChiuse,
    ColonnaObbligatoriaMancante,
    ColonnaRipetuta,
    NumeroNonValido,
    DataNonValida,
    TipoClienteSconosciuto,
    EmailClienteNonValida,
    TelefonoNonValido,
    TipoImpiantoSconosciuto,
    CombustibileSconosciuto,
    TipoGeneratoreSconosciuto,
    PdrRipetutoNelFile,
    CodiceCatastoRipetutoNelFile,
    GeneratoreSenzaTipo,
    ClienteDiversoNelFile,
}

/// <summary>Situazioni che non bloccano l'importazione: l'anteprima le mostra.</summary>
public enum AvvisoImportazione
{
    ColonnaSconosciuta,
    ImpiantoGiaPresentePdr,
    ImpiantoGiaPresenteCatasto,
    ClienteSenzaCodici,
}

/// <summary>Un errore o un avviso su una riga del file (0 = l'intestazione); Codice è il nome dell'enumerazione.</summary>
public sealed record SegnalazioneRiga(int Riga, string Codice, string? Dettaglio = null);

public sealed record DatiGeneratoreImportato(
    string TipoGeneratore, string? Marca, string? Modello, string? Matricola, decimal? PotenzaKw, DateOnly? InstallatoIl, string? SiglaCatasto);

public sealed record DatiImpiantoImportato(
    string TipoImpianto,
    int? AnnoInstallazione,
    decimal? PotenzaUtileKw,
    string? Combustibile,
    string? Pdr,
    int? ManutenzioneMesi,
    string? Note,
    string? CodiceCatasto,
    DatiGeneratoreImportato? Generatore);

/// <summary>Una riga valida del file: cliente, sede e, se indicato, un impianto con il suo primo generatore.</summary>
public sealed record RigaImportata(
    int Riga, string ChiaveCliente, DatiCliente Cliente, string? Email, string? Telefono, DatiSede Sede, DatiImpiantoImportato? Impianto)
{
    /// <summary>Due righe con la stessa chiave parlano della stessa sede del cliente.</summary>
    public string ChiaveSede => string.Join('|', Sede.Indirizzo.ToLowerInvariant(), Sede.Civico?.ToLowerInvariant(), Sede.Cap);
}

public sealed record AnalisiImportazione(IReadOnlyList<RigaImportata> Righe, IReadOnlyList<SegnalazioneRiga> Errori, IReadOnlyList<SegnalazioneRiga> Avvisi);

/// <summary>
/// Importazione di clienti, sedi e impianti da un file CSV (ADR 0007): una riga per
/// impianto, intestazione obbligatoria, separatore punto e virgola, virgola o
/// tabulazione. Qui si leggono il file e le regole di forma; voci, doppioni con i
/// dati già presenti e scrittura li gestisce l'infrastruttura, in una transazione.
/// </summary>
public static class RegoleImportazione
{
    public const int DimensioneMassimaByte = 5 * 1024 * 1024;

    public const int RigheMassime = 5000;

    // Nomi accettati per ogni colonna, già normalizzati (minuscolo, senza accenti, _ al posto degli spazi).
    private static readonly Dictionary<string, string> s_alias = new(StringComparer.Ordinal)
    {
        ["tipo_cliente"] = Colonne.TipoCliente,
        ["tipo"] = Colonne.TipoCliente,
        ["denominazione"] = Colonne.Denominazione,
        ["ragione_sociale"] = Colonne.Denominazione,
        ["nome"] = Colonne.Nome,
        ["cognome"] = Colonne.Cognome,
        ["codice_fiscale"] = Colonne.CodiceFiscale,
        ["cf"] = Colonne.CodiceFiscale,
        ["partita_iva"] = Colonne.PartitaIva,
        ["p_iva"] = Colonne.PartitaIva,
        ["piva"] = Colonne.PartitaIva,
        ["pec"] = Colonne.Pec,
        ["email"] = Colonne.Email,
        ["e_mail"] = Colonne.Email,
        ["telefono"] = Colonne.Telefono,
        ["cellulare"] = Colonne.Telefono,
        ["indirizzo"] = Colonne.Indirizzo,
        ["via"] = Colonne.Indirizzo,
        ["civico"] = Colonne.Civico,
        ["cap"] = Colonne.Cap,
        ["comune"] = Colonne.Comune,
        ["provincia"] = Colonne.Provincia,
        ["codice_istat"] = Colonne.CodiceIstat,
        ["istat"] = Colonne.CodiceIstat,
        ["tipo_impianto"] = Colonne.TipoImpianto,
        ["anno_installazione"] = Colonne.AnnoInstallazione,
        ["potenza_kw"] = Colonne.PotenzaKw,
        ["potenza_utile_kw"] = Colonne.PotenzaKw,
        ["potenza"] = Colonne.PotenzaKw,
        ["combustibile"] = Colonne.Combustibile,
        ["pdr"] = Colonne.Pdr,
        ["manutenzione_mesi"] = Colonne.ManutenzioneMesi,
        ["note"] = Colonne.Note,
        ["codice_catasto"] = Colonne.CodiceCatasto,
        ["targa"] = Colonne.CodiceCatasto,
        ["tipo_generatore"] = Colonne.TipoGeneratore,
        ["marca"] = Colonne.Marca,
        ["modello"] = Colonne.Modello,
        ["matricola"] = Colonne.Matricola,
        ["potenza_generatore_kw"] = Colonne.PotenzaGeneratoreKw,
        ["installato_il"] = Colonne.InstallatoIl,
        ["sigla_catasto"] = Colonne.SiglaCatasto,
    };

    private static readonly string[] s_obbligatorie = [Colonne.Indirizzo, Colonne.Cap, Colonne.Comune, Colonne.Provincia];

    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");

    static RegoleImportazione() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Le colonne del modello, nell'ordine del file di esempio.</summary>
    public static IReadOnlyList<string> ColonneModello { get; } =
    [
        Colonne.TipoCliente, Colonne.Denominazione, Colonne.Nome, Colonne.Cognome, Colonne.CodiceFiscale, Colonne.PartitaIva,
        Colonne.Pec, Colonne.Email, Colonne.Telefono, Colonne.Indirizzo, Colonne.Civico, Colonne.Cap, Colonne.Comune,
        Colonne.Provincia, Colonne.CodiceIstat, Colonne.TipoImpianto, Colonne.AnnoInstallazione, Colonne.PotenzaKw,
        Colonne.Combustibile, Colonne.Pdr, Colonne.ManutenzioneMesi, Colonne.Note, Colonne.CodiceCatasto,
        Colonne.TipoGeneratore, Colonne.Marca, Colonne.Modello, Colonne.Matricola, Colonne.PotenzaGeneratoreKw,
        Colonne.InstallatoIl, Colonne.SiglaCatasto,
    ];

    /// <summary>
    /// Testo del file: UTF-8 (con o senza BOM), altrimenti Windows-1252, la codifica
    /// dei CSV salvati da Excel in italiano senza scegliere "UTF-8".
    /// </summary>
    public static string Decodifica(byte[] contenuto)
    {
        ArgumentNullException.ThrowIfNull(contenuto);

        try
        {
            var testo = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(contenuto);
            return testo.TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(contenuto);
        }
    }

    public static AnalisiImportazione Analizza(byte[] contenuto)
    {
        ArgumentNullException.ThrowIfNull(contenuto);

        if (contenuto.Length > DimensioneMassimaByte)
        {
            return Solo(ErroreImportazione.FileTroppoGrande);
        }

        var (righe, chiuse) = LeggiRighe(Decodifica(contenuto));
        if (!chiuse)
        {
            return Solo(ErroreImportazione.VirgoletteNonChiuse);
        }

        righe = [.. righe.Where(r => r.Valori.Any(v => v.Length > 0))];
        if (righe.Count < 2)
        {
            return Solo(ErroreImportazione.FileVuoto);
        }

        if (righe.Count - 1 > RigheMassime)
        {
            return Solo(ErroreImportazione.TroppeRigheFile, RigheMassime.ToString(CultureInfo.InvariantCulture));
        }

        var errori = new List<SegnalazioneRiga>();
        var avvisi = new List<SegnalazioneRiga>();

        // Intestazione: ogni colonna si riconosce dal nome o da un alias.
        var posizioni = new Dictionary<string, int>(StringComparer.Ordinal);
        var intestazione = righe[0].Valori;
        for (var i = 0; i < intestazione.Count; i++)
        {
            var nome = NormalizzaNome(intestazione[i]);
            if (nome.Length == 0)
            {
                continue;
            }

            if (!s_alias.TryGetValue(nome, out var colonna))
            {
                avvisi.Add(new(0, nameof(AvvisoImportazione.ColonnaSconosciuta), intestazione[i].Trim()));
            }
            else if (!posizioni.TryAdd(colonna, i))
            {
                errori.Add(new(0, nameof(ErroreImportazione.ColonnaRipetuta), intestazione[i].Trim()));
            }
        }

        foreach (var mancante in s_obbligatorie.Where(c => !posizioni.ContainsKey(c)))
        {
            errori.Add(new(0, nameof(ErroreImportazione.ColonnaObbligatoriaMancante), mancante));
        }

        if (errori.Count > 0)
        {
            return new AnalisiImportazione([], errori, avvisi);
        }

        var valide = new List<RigaImportata>();
        foreach (var riga in righe.Skip(1))
        {
            if (LeggiRiga(riga, posizioni, errori, avvisi) is { } letta)
            {
                valide.Add(letta);
            }
        }

        ControllaCoerenza(valide, errori);
        return new AnalisiImportazione(valide, errori, avvisi);
    }

    private static RigaImportata? LeggiRiga(
        (int Numero, IReadOnlyList<string> Valori) riga, Dictionary<string, int> posizioni, List<SegnalazioneRiga> errori, List<SegnalazioneRiga> avvisi)
    {
        var n = riga.Numero;
        var erroriPrima = errori.Count;
        string? Valore(string colonna) =>
            posizioni.TryGetValue(colonna, out var i) && i < riga.Valori.Count && riga.Valori[i].Trim() is { Length: > 0 } v ? v : null;
        void Errore(Enum codice, string? dettaglio = null) => errori.Add(new(n, codice.ToString(), dettaglio));

        // Cliente: il tipo si può omettere, e allora si deduce da nome e cognome.
        var tipo = Valore(Colonne.TipoCliente)?.ToLowerInvariant()
            ?? (Valore(Colonne.Nome) is not null && Valore(Colonne.Cognome) is not null ? TipiCliente.Privato : TipiCliente.Azienda);
        if (!TipiCliente.Tutti.Contains(tipo))
        {
            Errore(ErroreImportazione.TipoClienteSconosciuto, tipo);
        }

        var (cliente, erroriCliente) = RegoleAnagrafica.Valida(new DatiCliente(
            tipo, Valore(Colonne.Denominazione), Valore(Colonne.Nome), Valore(Colonne.Cognome),
            Valore(Colonne.CodiceFiscale), Valore(Colonne.PartitaIva), Valore(Colonne.Pec), null));
        foreach (var e in erroriCliente.Where(e => e != ErroreAnagrafica.TipoClienteNonValido))
        {
            Errore(e);
        }

        var email = Valore(Colonne.Email)?.ToLowerInvariant();
        if (email is not null && RegoleAnagrafica.Valida(new DatiContatto("email", email, null, true)).Errori.Count > 0)
        {
            Errore(ErroreImportazione.EmailClienteNonValida, email);
        }

        var telefono = Valore(Colonne.Telefono);
        if (telefono is not null && RegoleAnagrafica.Valida(new DatiContatto("telefono", telefono, null, true)).Errori.Count > 0)
        {
            Errore(ErroreImportazione.TelefonoNonValido, telefono);
        }

        var (sede, erroriSede) = RegoleAnagrafica.Valida(new DatiSede(
            null, Valore(Colonne.Indirizzo) ?? "", Valore(Colonne.Civico), Valore(Colonne.Cap) ?? "", Valore(Colonne.Comune) ?? "",
            Valore(Colonne.Provincia) ?? "", null, null, null, Valore(Colonne.CodiceIstat)));
        foreach (var e in erroriSede)
        {
            Errore(e);
        }

        var impianto = LeggiImpianto(Valore, Errore);

        if (errori.Count > erroriPrima)
        {
            return null;
        }

        // Un cliente si riconosce dai codici; senza, dal nome, e lo si segnala.
        var chiave = cliente.CodiceFiscale ?? cliente.PartitaIva;
        if (chiave is null)
        {
            avvisi.Add(new(n, nameof(AvvisoImportazione.ClienteSenzaCodici)));
            chiave = "nome:" + string.Join(' ', cliente.Denominazione, cliente.Cognome, cliente.Nome).Trim().ToLowerInvariant();
        }

        return new RigaImportata(n, chiave, cliente, email, telefono, sede, impianto);
    }

    private static DatiImpiantoImportato? LeggiImpianto(Func<string, string?> valore, Action<Enum, string?> errore)
    {
        var tipoImpianto = valore(Colonne.TipoImpianto);
        if (tipoImpianto is null)
        {
            // Senza tipo, la riga porta solo cliente e sede.
            return null;
        }

        var anno = Intero(valore(Colonne.AnnoInstallazione), Colonne.AnnoInstallazione, errore);
        var potenza = Decimale(valore(Colonne.PotenzaKw), Colonne.PotenzaKw, errore);
        var mesi = Intero(valore(Colonne.ManutenzioneMesi), Colonne.ManutenzioneMesi, errore);

        // Le regole dell'impianto, con segnaposto per sede e tipo: quelli li risolve l'infrastruttura.
        var (dati, erroriImpianto) = RegoleImpianti.Valida(new DatiImpianto(
            Guid.NewGuid(), Guid.NewGuid(), null, anno, potenza, null, valore(Colonne.Pdr), null, null, valore(Colonne.Note), null, mesi));
        foreach (var e in erroriImpianto)
        {
            errore(e, null);
        }

        var codiceCatasto = valore(Colonne.CodiceCatasto);
        if (codiceCatasto is not null && codiceCatasto.Any(char.IsWhiteSpace))
        {
            errore(ErroreImpianto.CodiceCatastoNonValido, codiceCatasto);
        }

        DatiGeneratoreImportato? generatore = null;
        var tipoGeneratore = valore(Colonne.TipoGeneratore);
        var datiGeneratore = new[] { Colonne.Marca, Colonne.Modello, Colonne.Matricola, Colonne.PotenzaGeneratoreKw, Colonne.InstallatoIl, Colonne.SiglaCatasto }
            .Any(c => valore(c) is not null);
        if (tipoGeneratore is null && datiGeneratore)
        {
            errore(ErroreImportazione.GeneratoreSenzaTipo, null);
        }
        else if (tipoGeneratore is not null)
        {
            var potenzaGeneratore = Decimale(valore(Colonne.PotenzaGeneratoreKw), Colonne.PotenzaGeneratoreKw, errore);
            var installato = Data(valore(Colonne.InstallatoIl), errore);
            var (g, erroriGeneratore) = RegoleImpianti.Valida(new DatiGeneratore(
                Guid.NewGuid(), valore(Colonne.Marca), valore(Colonne.Modello), valore(Colonne.Matricola), potenzaGeneratore, null,
                installato, null, null, valore(Colonne.SiglaCatasto)));
            foreach (var e in erroriGeneratore)
            {
                errore(e, null);
            }

            generatore = new DatiGeneratoreImportato(tipoGeneratore, g.Marca, g.Modello, g.Matricola, g.PotenzaKw, g.InstallatoIl, g.SiglaCatasto);
        }

        return new DatiImpiantoImportato(
            tipoImpianto, dati.AnnoInstallazione, dati.PotenzaUtileKw, valore(Colonne.Combustibile), dati.Pdr, dati.ManutenzioneMesi, dati.Note,
            codiceCatasto, generatore);
    }

    // Nel file un PDR o un codice catasto vale per un solo impianto, e un cliente ha un solo nome.
    private static void ControllaCoerenza(List<RigaImportata> righe, List<SegnalazioneRiga> errori)
    {
        foreach (var gruppo in righe.Where(r => r.Impianto?.Pdr is not null).GroupBy(r => r.Impianto!.Pdr!).Where(g => g.Count() > 1))
        {
            errori.AddRange(gruppo.Skip(1).Select(r => new SegnalazioneRiga(r.Riga, nameof(ErroreImportazione.PdrRipetutoNelFile), gruppo.Key)));
        }

        foreach (var gruppo in righe.Where(r => r.Impianto?.CodiceCatasto is not null)
                     .GroupBy(r => r.Impianto!.CodiceCatasto!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errori.AddRange(gruppo.Skip(1).Select(r => new SegnalazioneRiga(r.Riga, nameof(ErroreImportazione.CodiceCatastoRipetutoNelFile), gruppo.Key)));
        }

        foreach (var gruppo in righe.GroupBy(r => r.ChiaveCliente))
        {
            var primo = gruppo.First().Cliente;
            errori.AddRange(gruppo.Skip(1).Where(r => r.Cliente with { Note = null } != primo with { Note = null })
                .Select(r => new SegnalazioneRiga(r.Riga, nameof(ErroreImportazione.ClienteDiversoNelFile), gruppo.First().Riga.ToString(CultureInfo.InvariantCulture))));
        }
    }

    private static int? Intero(string? testo, string colonna, Action<Enum, string?> errore)
    {
        if (testo is null)
        {
            return null;
        }

        if (int.TryParse(testo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        errore(ErroreImportazione.NumeroNonValido, $"{colonna}: {testo}");
        return null;
    }

    // Numeri all'italiana (virgola decimale), ma anche con il punto se è l'unico separatore.
    private static decimal? Decimale(string? testo, string colonna, Action<Enum, string?> errore)
    {
        if (testo is null)
        {
            return null;
        }

        var cultura = testo.Contains(',', StringComparison.Ordinal) ? s_italiano : CultureInfo.InvariantCulture;
        if (decimal.TryParse(testo, NumberStyles.Number, cultura, out var n))
        {
            return n;
        }

        errore(ErroreImportazione.NumeroNonValido, $"{colonna}: {testo}");
        return null;
    }

    private static DateOnly? Data(string? testo, Action<Enum, string?> errore)
    {
        if (testo is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(testo, ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
        {
            return data;
        }

        errore(ErroreImportazione.DataNonValida, testo);
        return null;
    }

    /// <summary>"Ragione sociale" e "RAGIONE_SOCIALE" diventano "ragione_sociale"; gli accenti si tolgono.</summary>
    public static string NormalizzaNome(string nome)
    {
        ArgumentNullException.ThrowIfNull(nome);

        var senzaAccenti = string.Concat(nome.Trim().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
        var risultato = new StringBuilder();
        foreach (var c in senzaAccenti.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                risultato.Append(c);
            }
            else if (risultato.Length > 0 && risultato[^1] != '_')
            {
                risultato.Append('_');
            }
        }

        return risultato.ToString().TrimEnd('_');
    }

    /// <summary>
    /// Righe e campi del CSV, con le virgolette doppie come in RFC 4180 (anche a capo
    /// dentro un campo). Il separatore si sceglie dall'intestazione.
    /// </summary>
    public static (List<(int Numero, IReadOnlyList<string> Valori)> Righe, bool VirgoletteChiuse) LeggiRighe(string testo)
    {
        ArgumentNullException.ThrowIfNull(testo);

        var primaRiga = testo.Split('\n', 2)[0];
        var separatore = new[] { ';', '\t', ',' }.OrderByDescending(s => primaRiga.Count(c => c == s)).First();

        var righe = new List<(int, IReadOnlyList<string>)>();
        var campi = new List<string>();
        var campo = new StringBuilder();
        var tra = false;
        var numero = 1;
        var inizioRiga = 1;
        for (var i = 0; i < testo.Length; i++)
        {
            var c = testo[i];
            if (tra)
            {
                if (c == '"' && i + 1 < testo.Length && testo[i + 1] == '"')
                {
                    campo.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    tra = false;
                }
                else
                {
                    if (c == '\n')
                    {
                        numero++;
                    }

                    campo.Append(c);
                }
            }
            else if (c == '"' && campo.Length == 0)
            {
                tra = true;
            }
            else if (c == separatore)
            {
                campi.Add(campo.ToString());
                campo.Clear();
            }
            else if (c == '\n')
            {
                campi.Add(campo.ToString().TrimEnd('\r'));
                campo.Clear();
                righe.Add((inizioRiga, campi));
                campi = [];
                numero++;
                inizioRiga = numero;
            }
            else
            {
                campo.Append(c);
            }
        }

        if (campo.Length > 0 || campi.Count > 0)
        {
            campi.Add(campo.ToString().TrimEnd('\r'));
            righe.Add((inizioRiga, campi));
        }

        return (righe, !tra);
    }

    private static AnalisiImportazione Solo(ErroreImportazione errore, string? dettaglio = null) =>
        new([], [new SegnalazioneRiga(0, errore.ToString(), dettaglio)], []);

    /// <summary>Nomi canonici delle colonne, come nell'intestazione del file di esempio.</summary>
    public static class Colonne
    {
        public const string TipoCliente = "tipo_cliente";
        public const string Denominazione = "denominazione";
        public const string Nome = "nome";
        public const string Cognome = "cognome";
        public const string CodiceFiscale = "codice_fiscale";
        public const string PartitaIva = "partita_iva";
        public const string Pec = "pec";
        public const string Email = "email";
        public const string Telefono = "telefono";
        public const string Indirizzo = "indirizzo";
        public const string Civico = "civico";
        public const string Cap = "cap";
        public const string Comune = "comune";
        public const string Provincia = "provincia";
        public const string CodiceIstat = "codice_istat";
        public const string TipoImpianto = "tipo_impianto";
        public const string AnnoInstallazione = "anno_installazione";
        public const string PotenzaKw = "potenza_kw";
        public const string Combustibile = "combustibile";
        public const string Pdr = "pdr";
        public const string ManutenzioneMesi = "manutenzione_mesi";
        public const string Note = "note";
        public const string CodiceCatasto = "codice_catasto";
        public const string TipoGeneratore = "tipo_generatore";
        public const string Marca = "marca";
        public const string Modello = "modello";
        public const string Matricola = "matricola";
        public const string PotenzaGeneratoreKw = "potenza_generatore_kw";
        public const string InstallatoIl = "installato_il";
        public const string SiglaCatasto = "sigla_catasto";
    }
}
