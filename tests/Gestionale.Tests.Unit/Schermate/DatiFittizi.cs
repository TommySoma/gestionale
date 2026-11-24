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
    public static readonly Guid TecnicoMarco = Guid.Parse("019ab000-0000-7000-8000-000000000060");
    public static readonly Guid TecnicoSara = Guid.Parse("019ab000-0000-7000-8000-000000000061");
    public static readonly Guid InterventoChiuso = Guid.Parse("019ab000-0000-7000-8000-000000000070");
    public static readonly Guid InterventoPianificato = Guid.Parse("019ab000-0000-7000-8000-000000000071");

    /// <summary>Lunedì della settimana mostrata nelle schermate del calendario.</summary>
    public static readonly DateOnly Settimana = new(2027, 1, 11);

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
            "/api/finalita-privacy" => new object[]
            {
                new { codice = "promemoria_scadenze", nome = "Promemoria delle scadenze", descrizione = "Avvisi per controlli e manutenzioni" },
                new { codice = "comunicazioni_commerciali", nome = "Comunicazioni commerciali", descrizione = "Offerte e novità" },
            },
            "/api/interventi/tecnici" => new object[] { new { utenteId = TecnicoMarco, nome = "Marco Bianchi" }, new { utenteId = TecnicoSara, nome = "Sara Neri" } },
            "/api/interventi" => ElencoInterventi(richiesta.RequestUri.Query),
            _ when percorso == $"/api/interventi/{InterventoChiuso}" => SchedaInterventoChiuso(),
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
        contatti = new object[]
        {
            new { contattoId = Guid.Parse("019ab000-0000-7000-8000-000000000050"), tipo = "cellulare", valore = "+39 333 000 0000", etichetta = "Personale", principale = true, versione = 1 },
        },
        consensi = new object[]
        {
            new { finalita = "promemoria_scadenze", concesso = true, fonte = "modulo_firmato", registratoIl = "2026-11-02T10:00:00Z" },
            new { finalita = "comunicazioni_commerciali", concesso = false, fonte = "email", registratoIl = "2026-11-05T10:00:00Z" },
        },
    };

    // Il calendario chiede un intervallo (da, a); l'elenco da pianificare chiede lo stato; la scheda impianto l'impianto.
    private static object ElencoInterventi(string query)
    {
        object[] elementi = query.Contains("stato=da_pianificare", StringComparison.Ordinal)
            ?
            [
                RiepilogoIntervento(Guid.Parse("019ab000-0000-7000-8000-000000000072"), 14, "sopralluogo", "da_pianificare", null, null, null, null, "Livorno"),
            ]
            :
            [
                RiepilogoIntervento(InterventoChiuso, 12, "manutenzione", "completato", "2027-01-11T08:00:00Z", "2027-01-11T10:00:00Z", TecnicoMarco, "Marco Bianchi", "Firenze"),
                RiepilogoIntervento(InterventoPianificato, 13, "riparazione", "pianificato", "2027-01-13T13:30:00Z", "2027-01-13T15:00:00Z", TecnicoSara, "Sara Neri", "Firenze"),
                RiepilogoIntervento(Guid.Parse("019ab000-0000-7000-8000-000000000073"), 15, "installazione", "in_corso", "2027-01-14T07:00:00Z", "2027-01-14T11:00:00Z", TecnicoMarco, "Marco Bianchi", "Livorno"),
                RiepilogoIntervento(Guid.Parse("019ab000-0000-7000-8000-000000000074"), 16, "manutenzione", "annullato", "2027-01-15T08:00:00Z", "2027-01-15T09:00:00Z", TecnicoSara, "Sara Neri", "Pisa"),
            ];
        return new { elementi, totale = elementi.Length, numeroPagina = 1, dimensione = 200 };
    }

    private static object RiepilogoIntervento(
        Guid id, int numero, string tipo, string stato, string? inizio, string? fine, Guid? tecnico, string? nomeTecnico, string comune) => new
        {
            interventoId = id,
            anno = 2027,
            numero,
            tipo,
            stato,
            inizio,
            fine,
            tecnicoUtenteId = tecnico,
            tecnico = nomeTecnico,
            impiantoId = Caldaia,
            tipoImpianto = "Riscaldamento (voce di prova)",
            cliente = "Rossi Mario",
            indirizzo = "Via dei Platani",
            civico = "12",
            comune,
            provincia = "FI",
            versione = 2,
        };

    private static object SchedaInterventoChiuso() => new
    {
        interventoId = InterventoChiuso,
        anno = 2027,
        numero = 12,
        tipo = "manutenzione",
        stato = "completato",
        esito = "risolto",
        inizio = "2027-01-11T08:00:00Z",
        fine = "2027-01-11T10:00:00Z",
        tecnicoUtenteId = TecnicoMarco,
        tecnico = "Marco Bianchi",
        impiantoId = Caldaia,
        tipoImpianto = "Riscaldamento (voce di prova)",
        clienteId = Cliente,
        cliente = "Rossi Mario",
        indirizzo = "Via dei Platani",
        civico = "12",
        cap = "50100",
        comune = "Firenze",
        provincia = "FI",
        descrizione = "Manutenzione annuale, il cliente segnala rumore all'accensione.",
        noteChiusura = "Pulito il bruciatore e sostituita la guarnizione.",
        chiusoIl = "2027-01-11T10:05:00Z",
        versione = 4,
        righe = new object[]
        {
            new { posizione = 1, tipo = "manodopera", descrizione = "Manutenzione ordinaria", unita = "h", quantita = 1.5m, prezzoUnitario = 40m, scontoPercentuale = 0m, aliquotaIva = 22m, importo = 60m },
            new { posizione = 2, tipo = "materiale", codice = "GUA-01", descrizione = "Guarnizione di prova", unita = "pz", quantita = 2m, prezzoUnitario = 4.99m, scontoPercentuale = 10m, aliquotaIva = 22m, importo = 8.98m },
        },
        totale = 68.98m,
    };
}
