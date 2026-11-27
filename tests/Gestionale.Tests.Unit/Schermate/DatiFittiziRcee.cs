namespace Gestionale.Tests.Unit.Schermate;

/// <summary>Risposte fittizie di rapporti di controllo, bollini e invii (M4), con un tracciato ridotto.</summary>
internal static partial class DatiFittizi
{
    public static readonly Guid RapportoBozza = Guid.Parse("019ab000-0000-7000-8000-0000000000c1");
    public static readonly Guid RapportoFirmato = Guid.Parse("019ab000-0000-7000-8000-0000000000c2");
    public static readonly Guid InvioCaricato = Guid.Parse("019ab000-0000-7000-8000-0000000000d1");
    public static readonly Guid TipoBollinoGt = Guid.Parse("019ab000-0000-7000-8000-0000000000d5");

    /// <summary>Corpo dell'ultima scrittura su rapporti, bollini o invii: i test lo controllano.</summary>
    public static string? UltimaScritturaRcee { get; private set; }

    private static object? RispondiRcee(HttpRequestMessage richiesta, string percorso)
    {
        var rcee = percorso.StartsWith("/api/rapporti-controllo", StringComparison.Ordinal)
            || percorso.StartsWith("/api/bollini", StringComparison.Ordinal)
            || percorso.StartsWith("/api/invii-catasto", StringComparison.Ordinal);
        if (!rcee)
        {
            return null;
        }

        if (richiesta.Method != HttpMethod.Get)
        {
            UltimaScritturaRcee = richiesta.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return percorso switch
            {
                "/api/bollini" => new { numero = 3 },
                "/api/invii-catasto" => new { id = InvioCaricato, versione = 1, righe = 1, esclusi = new[] { RapportoBozza } },
                "/api/rapporti-controllo" => new { id = RapportoBozza, versione = 1 },
                _ => new { id = Guid.Parse(percorso.Split('/')[3]), versione = 4 },
            };
        }

        return percorso switch
        {
            "/api/rapporti-controllo" => new { elementi = ElencoRapporti(), totale = 2, numeroPagina = 1, dimensione = 50 },
            "/api/rapporti-controllo/tracciato/tipo_1" => Tracciato(),
            _ when percorso == $"/api/rapporti-controllo/{RapportoBozza}" => Rapporto(RapportoBozza, "bozza", null),
            _ when percorso == $"/api/rapporti-controllo/{RapportoFirmato}" => Rapporto(RapportoFirmato, "firmato", "2027000000101"),
            "/api/bollini/tipi" => new object[]
            {
                new { tipoBollinoId = TipoBollinoGt, codice = "gt_fino_35", categoria = "GT", nome = "Gruppi termici fino a 35 kW (prova)", importo = 10m, disponibili = 2 },
                new { tipoBollinoId = Guid.Parse("019ab000-0000-7000-8000-0000000000d6"), codice = "gf_fino_35", categoria = "GF", nome = "Gruppi frigo fino a 35 kW (prova)", importo = 10m, disponibili = 0 },
            },
            "/api/bollini" => new
            {
                elementi = new object[]
                {
                    Bollino("019ab000-0000-7000-8000-0000000000d7", "2027000000100", "disponibile", null),
                    Bollino("019ab000-0000-7000-8000-0000000000d8", "2027000000101", "usato", RapportoFirmato),
                },
                totale = 2,
                numeroPagina = 1,
                dimensione = 50,
            },
            "/api/invii-catasto" => new
            {
                elementi = new[] { Invio("caricato") },
                totale = 1,
                numeroPagina = 1,
                dimensione = 50,
            },
            _ when percorso == $"/api/invii-catasto/{InvioCaricato}" => new
            {
                invio = Invio("caricato"),
                rapporti = new[]
                {
                    new { riga = 1, rapportoControlloId = RapportoFirmato, impiantoId = Caldaia, dataControllo = "2027-01-11", cliente = "Rossi Giulia (prova)", esito = "in_attesa", causa = (string?)null },
                },
            },
            _ => null,
        };
    }

    private static object[] ElencoRapporti() =>
    [
        new
        {
            rapportoControlloId = RapportoBozza, impiantoId = Caldaia, generatoreId = Guid.Parse("019ab000-0000-7000-8000-000000000020"), interventoId = InterventoChiuso,
            modello = "tipo_1", dataControllo = "2027-01-11", termineTrasmissione = "2027-01-18", stato = "bozza",
            cliente = "Rossi Giulia (prova)", indirizzo = "Via dei Platani 12", comune = "Firenze", codiceBollino = (string?)null, versione = 2,
        },
        new
        {
            rapportoControlloId = RapportoFirmato, impiantoId = Caldaia, generatoreId = Guid.Parse("019ab000-0000-7000-8000-000000000020"), interventoId = (Guid?)null,
            modello = "tipo_1", dataControllo = "2026-12-01", termineTrasmissione = "2027-01-30", stato = "firmato",
            cliente = "Rossi Giulia (prova)", indirizzo = "Via dei Platani 12", comune = "Firenze", codiceBollino = "2027000000101", versione = 5,
        },
    ];

    private static object Rapporto(Guid id, string stato, string? bollino) => new
    {
        rapportoControlloId = id,
        impiantoId = Caldaia,
        generatoreId = Guid.Parse("019ab000-0000-7000-8000-000000000020"),
        interventoId = id == RapportoBozza ? InterventoChiuso : (Guid?)null,
        modello = "tipo_1",
        tecnicoId = TecnicoMarco,
        tecnico = "Marco Bianchi",
        dataControllo = "2027-01-11",
        termineTrasmissione = "2027-01-18",
        stato,
        dati = new Dictionary<string, object>
        {
            ["codice_catasto"] = "0123456789",
            ["responsabile_impianto"] = "Rossi Giulia (prova)",
            ["indirizzo_impianto"] = "Via dei Platani, 12",
            ["potenza_utile"] = 24.5m,
            ["presenza_riflussi"] = "4",
            ["data_installazione"] = "2018-03-15",
        },
        bollinoId = bollino is null ? (Guid?)null : Guid.Parse("019ab000-0000-7000-8000-0000000000d8"),
        codiceBollino = bollino,
        tipoBollino = bollino is null ? null : "Gruppi termici fino a 35 kW (prova)",
        convalidatoIl = (string?)null,
        firmatoIl = stato == "firmato" ? "2027-01-12T10:00:00Z" : null,
        annullatoIl = (string?)null,
        motivoAnnullamento = (string?)null,
        creatoIl = "2027-01-11T11:00:00Z",
        versione = 2,
        errori = stato == "bozza"
            ? new object[]
            {
                new { campo = "NUMERO_BOLLINO", messaggio = "nessun bollino assegnato", bloccante = true },
                new { campo = "TEMPERATURA_FUMI", messaggio = "campo obbligatorio", bloccante = true },
                new { campo = (string?)null, messaggio = "termine di trasmissione vicino", bloccante = false },
            }
            : [],
    };

    // Poche colonne del tracciato tipo 1, con i tipi che il modulo deve saper mostrare.
    private static object[] Tracciato() =>
    [
        Campo(1, 1, "CODICE_CATASTO", "testo", 10, true, null, "dati", "codice_catasto"),
        Campo(2, 5, "RESPONSABILE_IMPIANTO", "testo", 200, true, null, "dati", "responsabile_impianto"),
        Campo(3, 11, "ID_DITTA_MANUTENTORE", "intero", null, true, null, "azienda", "codice_ditta_siert"),
        Campo(4, 17, "TRATTAMENTO_RISCALDAMENTO", "intero", null, true, ["16", "17", "18"], "dati", "trattamento_riscaldamento"),
        Campo(5, 27, "DATA_INSTALLAZIONE", "data", null, true, null, "dati", "data_installazione"),
        Campo(6, 34, "POTENZA_UTILE", "decimale", null, true, null, "dati", "potenza_utile"),
        Campo(7, 44, "PRESENZA_RIFLUSSI", "intero", null, true, ["3", "4", "9"], "dati", "presenza_riflussi"),
        Campo(8, 47, "TEMPERATURA_FUMI", "decimale", null, true, null, "dati", "temperatura_fumi"),
        Campo(9, 62, "IMPIANTO_PUO_FUNZIONARE_IN_SICUREZZA", "intero", null, true, ["21", "13"], "dati", "funziona_in_sicurezza"),
        Campo(10, 64, "DATA_CONTROLLO", "data", null, true, null, "rapporto", "data_controllo"),
        Campo(11, 68, "NUMERO_BOLLINO", "testo", 50, true, null, "bollino", "codice"),
    ];

    private static object Campo(int posizione, int numero, string nome, string tipo, int? lunghezza, bool obbligatorio, string[]? valori, string origine, string chiave) =>
        new { posizione, numeroCampo = numero, nome, tipo, lunghezza, obbligatorio, valoriAmmessi = valori, formato = (string?)null, origine, chiave };

    private static object Bollino(string id, string codice, string stato, Guid? rapporto) => new
    {
        bollinoId = Guid.Parse(id),
        codice,
        tipoBollinoId = TipoBollinoGt,
        tipoBollino = "Gruppi termici fino a 35 kW (prova)",
        categoria = "GT",
        importo = 10m,
        acquistatoIl = "2027-01-04",
        stato,
        rapportoControlloId = rapporto,
        note = (string?)null,
        versione = 1,
    };

    private static object Invio(string stato) => new
    {
        invioCatastoId = InvioCaricato,
        modello = "tipo_1",
        nomeFile = "rcee-tipo1-20270112-1.txt",
        versioneTracciato = "3.0",
        stato,
        righe = 1,
        caricatoIl = "2027-01-12T15:00:00Z",
        elaboratoIl = (string?)null,
        creatoIl = "2027-01-12T14:30:00Z",
        versione = 2,
    };
}

/// <summary>Anteprima fittizia di un'importazione da CSV con un errore e un avviso.</summary>
internal static partial class DatiFittizi
{
    private static object AnteprimaImportazione() => new
    {
        importata = false,
        righe = 3,
        clientiNuovi = 2,
        clientiEsistenti = 1,
        sediNuove = 2,
        impiantiNuovi = 2,
        impiantiSaltati = 1,
        generatori = 1,
        errori = new[] { new { riga = 3, codice = "TipoImpiantoSconosciuto", messaggio = "tipo di impianto sconosciuto: usa il codice o il nome di una voce in vigore: stufa" } },
        avvisi = new[] { new { riga = 4, codice = "ImpiantoGiaPresentePdr", messaggio = "impianto già presente con questo PDR: la riga lo salta: PDR-PROVA" } },
    };
}
