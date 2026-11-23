namespace Gestionale.Tests.Unit.Schermate;

/// <summary>Dati inventati, come li restituirebbe l'API: nessun dato reale.</summary>
internal static class DatiFittizi
{
    public static readonly Guid Cliente = Guid.Parse("019ab000-0000-7000-8000-000000000001");
    public static readonly Guid SedeFirenze = Guid.Parse("019ab000-0000-7000-8000-000000000002");
    public static readonly Guid SedeLivorno = Guid.Parse("019ab000-0000-7000-8000-000000000003");
    public static readonly Guid Caldaia = Guid.Parse("019ab000-0000-7000-8000-000000000004");
    public static readonly Guid Climatizzatore = Guid.Parse("019ab000-0000-7000-8000-000000000005");
    public static readonly Guid Dismesso = Guid.Parse("019ab000-0000-7000-8000-000000000006");
    public static readonly Guid TipoRiscaldamento = Guid.Parse("019ab000-0000-7000-8000-000000000010");
    public static readonly Guid TipoClima = Guid.Parse("019ab000-0000-7000-8000-000000000011");
    public static readonly Guid TipoCaldaia = Guid.Parse("019ab000-0000-7000-8000-000000000012");
    public static readonly Guid GasNaturale = Guid.Parse("019ab000-0000-7000-8000-000000000013");

    public static object? Rispondi(HttpRequestMessage richiesta)
    {
        var percorso = richiesta.RequestUri!.AbsolutePath;
        return percorso switch
        {
            "/api/accesso" => new
            {
                esito = "completato",
                token = new { tokenAccesso = "a", accessoScadeIl = DateTimeOffset.UtcNow.AddMinutes(15), tokenRinnovo = "r", rinnovoScadeIl = DateTimeOffset.UtcNow.AddHours(12) },
            },
            "/api/impianti/voci" => Voci(),
            "/api/impianti" => new { elementi = Elenco(), totale = 3, numeroPagina = 1, dimensione = 25 },
            "/api/finalita-privacy" => Array.Empty<object>(),
            _ when percorso == $"/api/impianti/{Caldaia}" => SchedaCaldaia(),
            _ when percorso == $"/api/clienti/{Cliente}" => SchedaCliente(),
            _ => null,
        };
    }

    private static object Voci() => new
    {
        tipiImpianto = new[]
        {
            new { id = TipoRiscaldamento, codice = "prova_riscaldamento", nome = "Riscaldamento (voce di prova)", daVerificare = true },
            new { id = TipoClima, codice = "prova_climatizzazione", nome = "Climatizzazione (voce di prova)", daVerificare = true },
        },
        tipiGeneratore = new[] { new { id = TipoCaldaia, codice = "prova_caldaia", nome = "Caldaia (voce di prova)", daVerificare = true } },
        combustibili = new[] { new { id = GasNaturale, codice = "prova_gas_naturale", nome = "Gas naturale (voce di prova)", daVerificare = true } },
    };

    private static object[] Elenco() =>
    [
        Riepilogo(Caldaia, "Riscaldamento (voce di prova)", "attivo", SedeFirenze, "Via dei Platani", "12", "Firenze", "FI", "00012345678901", "CAT-PROVA-0001", 1),
        Riepilogo(Climatizzatore, "Climatizzazione (voce di prova)", "attivo", SedeFirenze, "Via dei Platani", "12", "Firenze", "FI", null, null, 2),
        Riepilogo(Dismesso, "Riscaldamento (voce di prova)", "dismesso", SedeLivorno, "Via del Mare", "3", "Livorno", "LI", null, "TARGA-PROVA-77", 0),
    ];

    private static object Riepilogo(
        Guid id, string tipo, string stato, Guid sede, string indirizzo, string civico, string comune, string provincia, string? pdr, string? codice, int generatori) => new
        {
            impiantoId = id,
            tipoImpianto = tipo,
            stato,
            sedeId = sede,
            clienteId = Cliente,
            cliente = "Rossi Mario",
            indirizzo,
            civico,
            comune,
            provincia,
            pdr,
            codiceCatasto = codice,
            numeroGeneratori = generatori,
            versione = 1,
        };

    private static object SchedaCaldaia() => new
    {
        impiantoId = Caldaia,
        sedeId = SedeFirenze,
        clienteId = Cliente,
        cliente = "Rossi Mario",
        indirizzo = "Via dei Platani",
        civico = "12",
        cap = "50100",
        comune = "Firenze",
        provincia = "FI",
        tipoImpiantoId = TipoRiscaldamento,
        tipoImpianto = "Riscaldamento (voce di prova)",
        tipoDaVerificare = true,
        stato = "attivo",
        annoInstallazione = 2015,
        potenzaUtileKw = 24.5m,
        combustibileId = GasNaturale,
        combustibile = "Gas naturale (voce di prova)",
        pdr = "00012345678901",
        proprietarioClienteId = Cliente,
        proprietario = "Rossi Mario",
        note = "Caldaia in cucina, accesso dal balcone.",
        versione = 3,
        indirizzoDuplicato = true,
        generatori = new object[]
        {
            new
            {
                generatoreId = Guid.Parse("019ab000-0000-7000-8000-000000000020"), tipoGeneratoreId = TipoCaldaia, tipoGeneratore = "Caldaia (voce di prova)",
                marca = "Marca Fittizia", modello = "Condens 24", matricola = "MAT-PROVA-0001", potenzaKw = 24m, combustibileId = GasNaturale,
                combustibile = "Gas naturale (voce di prova)", installatoIl = "2015-03-10", versione = 1, matricolaDuplicata = true,
            },
            new
            {
                generatoreId = Guid.Parse("019ab000-0000-7000-8000-000000000021"), tipoGeneratoreId = TipoCaldaia, tipoGeneratore = "Caldaia (voce di prova)",
                marca = "Altra Marca", modello = "Vecchia 20", installatoIl = "2002-10-01", dismessoIl = "2015-03-09", versione = 2, matricolaDuplicata = false,
            },
        },
        riferimentiCatasto = new object[]
        {
            new { riferimentoCatastoId = Guid.Parse("019ab000-0000-7000-8000-000000000030"), regione = "toscana", sistema = "siert", codice = "CAT-PROVA-0001", registratoIl = "2016-01-20", versione = 1 },
        },
    };

    private static object SchedaCliente() => new
    {
        clienteId = Cliente,
        tipo = "privato",
        nome = "Mario",
        cognome = "Rossi",
        codiceFiscale = "RSSMRA80A01H501U",
        versione = 1,
        sedi = new object[]
        {
            new { sedeId = SedeFirenze, indirizzo = "Via dei Platani", civico = "12", cap = "50100", comune = "Firenze", provincia = "FI", versione = 1 },
            new { sedeId = SedeLivorno, descrizione = "Casa al mare", indirizzo = "Via del Mare", civico = "3", cap = "57100", comune = "Livorno", provincia = "LI", versione = 1 },
            new { sedeId = Guid.Parse("019ab000-0000-7000-8000-000000000040"), indirizzo = "Via Nuova", civico = "1", cap = "50100", comune = "Firenze", provincia = "FI", versione = 1 },
        },
        contatti = Array.Empty<object>(),
        consensi = new object[]
        {
            new { finalita = "promemoria_scadenze", concesso = true, fonte = "modulo_firmato", registratoIl = "2026-11-02T10:00:00Z" },
            new { finalita = "comunicazioni_commerciali", concesso = false, fonte = "email", registratoIl = "2026-11-05T10:00:00Z" },
        },
    };
}
