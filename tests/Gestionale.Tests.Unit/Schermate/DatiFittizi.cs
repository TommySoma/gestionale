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
    public static readonly Guid InterventoDaPianificare = Guid.Parse("019ab000-0000-7000-8000-000000000072");
    public static readonly Guid ScadenzaScaduta = Guid.Parse("019ab000-0000-7000-8000-000000000090");
    public static readonly Guid ScadenzaOggi = Guid.Parse("019ab000-0000-7000-8000-000000000091");
    public static readonly Guid ScadenzaVicina = Guid.Parse("019ab000-0000-7000-8000-000000000092");
    public static readonly Guid PreventivoBozza = Guid.Parse("019ab000-0000-7000-8000-0000000000a0");
    public static readonly Guid PreventivoEmesso = Guid.Parse("019ab000-0000-7000-8000-0000000000a1");
    public static readonly Guid PreventivoAccettato = Guid.Parse("019ab000-0000-7000-8000-0000000000a2");
    public static readonly Guid VoceManodopera = Guid.Parse("019ab000-0000-7000-8000-0000000000b0");
    public static readonly Guid VoceVaso = Guid.Parse("019ab000-0000-7000-8000-0000000000b1");

    /// <summary>Lunedì della settimana mostrata nelle schermate del calendario.</summary>
    public static readonly DateOnly Settimana = new(2027, 1, 11);

    /// <summary>Corpo dell'ultima modifica inviata (PUT): i test dello spostamento lo controllano.</summary>
    public static string? UltimaModifica { get; private set; }

    public static object? Rispondi(HttpRequestMessage richiesta)
    {
        var percorso = richiesta.RequestUri!.AbsolutePath;
        if (richiesta.Method == HttpMethod.Post && percorso.EndsWith("/richiami", StringComparison.Ordinal))
        {
            return new { id = Guid.Parse(percorso.Split('/')[3]), versione = 5 };
        }

        // Passaggi di stato dei preventivi: il server risponde con numero e, dopo la conversione, l'intervento.
        if (richiesta.Method == HttpMethod.Post && percorso.StartsWith("/api/preventivi/", StringComparison.Ordinal))
        {
            return new { id = Guid.Parse(percorso.Split('/')[3]), versione = 5, anno = 2027, numero = 8, interventoId = InterventoDaPianificare };
        }

        if (richiesta.Method == HttpMethod.Put)
        {
            UltimaModifica = richiesta.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new { id = Guid.Parse(percorso[(percorso.LastIndexOf('/') + 1)..]), versione = 3, anno = 2027, numero = 13 };
        }

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
            "/api/scadenze" => ElencoScadenze(),
            "/api/listino" => new { elementi = Listino(), totale = 3, numeroPagina = 1, dimensione = 50 },
            "/api/preventivi" => new { elementi = ElencoPreventivi(), totale = 3, numeroPagina = 1, dimensione = 50 },
            _ when percorso == $"/api/preventivi/{PreventivoEmesso}" => SchedaPreventivo(PreventivoEmesso, "emesso", 7, scaduto: true),
            _ when percorso == $"/api/preventivi/{PreventivoBozza}" => SchedaPreventivo(PreventivoBozza, "bozza", null, scaduto: false),
            _ when percorso == $"/api/preventivi/{PreventivoAccettato}" => SchedaPreventivo(PreventivoAccettato, "accettato", 6, scaduto: false),
            _ when percorso == $"/api/impianti/{Caldaia}/scadenze" => ScadenzeCaldaia(),
            _ when percorso == $"/api/interventi/{InterventoChiuso}" => SchedaInterventoChiuso(),
            _ when percorso == $"/api/interventi/{InterventoPianificato}" => SchedaIntervento(InterventoPianificato, 13, "riparazione", "pianificato", "2027-01-13T13:30:00Z", "2027-01-13T15:00:00Z", TecnicoSara, "Sara Neri"),
            _ when percorso == $"/api/interventi/{InterventoDaPianificare}" => SchedaIntervento(InterventoDaPianificare, 14, "sopralluogo", "da_pianificare", null, null, null, null),
            _ when percorso == $"/api/impianti/{Caldaia}/documenti" => new object[]
            {
                new { documentoId = Guid.Parse("019ab000-0000-7000-8000-000000000080"), categoria = "foto", nomeOriginale = "caldaia-frontale.jpg", tipoMime = "image/jpeg", dimensione = 2_456_000L, descrizione = "Vista frontale", creatoIl = "2027-01-11T09:10:00Z", creatoDa = "Marco Bianchi", versione = 1 },
                new { documentoId = Guid.Parse("019ab000-0000-7000-8000-000000000081"), categoria = "documento", nomeOriginale = "libretto-di-impianto.pdf", tipoMime = "application/pdf", dimensione = 845_000L, descrizione = (string?)null, creatoIl = "2026-11-20T15:00:00Z", creatoDa = "Anna Verdi", versione = 1 },
            },
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
        manutenzioneMesi = 12,
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
                RiepilogoIntervento(InterventoDaPianificare, 14, "sopralluogo", "da_pianificare", null, null, null, null, "Livorno"),
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

    private static object SchedaIntervento(
        Guid id, int numero, string tipo, string stato, string? inizio, string? fine, Guid? tecnico, string? nomeTecnico) => new
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
            clienteId = Cliente,
            cliente = "Rossi Mario",
            indirizzo = "Via dei Platani",
            civico = "12",
            cap = "50100",
            comune = "Firenze",
            provincia = "FI",
            descrizione = "Perdita d'acqua sotto la caldaia.",
            versione = 2,
            righe = Array.Empty<object>(),
            totale = 0m,
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

    // La lista da richiamare: il server manda già i giorni mancanti (negativi per le scadute), dalla più urgente.
    private static object ElencoScadenze()
    {
        object[] elementi =
        [
            InArrivo(ScadenzaScaduta, Caldaia, "manutenzione", "2027-01-10", -3, "in_attesa", "Rossi Mario", "Via dei Platani 12", "Firenze", "FI",
                "+39 333 000 0000", "2027-01-12T09:30:00Z", "nessuna_risposta", "2027-01-14"),
            InArrivo(ScadenzaOggi, Climatizzatore, "controllo_efficienza", "2027-01-13", 0, "non_contattato", "Rossi Mario", "Via dei Platani 12", "Firenze", "FI",
                "+39 333 000 0000", null, null, null),
            InArrivo(ScadenzaVicina, Dismesso, "manutenzione", "2027-01-25", 12, "fissato", "Condominio Le Querce", "Via del Mare 3", "Livorno", "LI",
                null, "2027-01-08T15:00:00Z", "appuntamento", null),
        ];
        return new { elementi, totale = elementi.Length, numeroPagina = 1, dimensione = 50 };
    }

    private static object InArrivo(
        Guid id, Guid impianto, string tipo, string data, int giorni, string richiamo, string cliente, string indirizzo, string comune, string provincia,
        string? telefono, string? ultimoIl, string? ultimoEsito, string? richiamareIl) => new
        {
            scadenzaId = id,
            impiantoId = impianto,
            tipo,
            dataScadenza = data,
            giorniMancanti = giorni,
            statoRichiamo = richiamo,
            versione = 2,
            clienteId = Cliente,
            cliente,
            indirizzo,
            comune,
            provincia,
            telefono,
            ultimoRichiamoIl = ultimoIl,
            ultimoEsito,
            richiamareIl,
        };

    // Le scadenze della caldaia: due aperte (una scaduta, il controllo con la regola di prova) e una chiusa nello storico.
    private static object[] ScadenzeCaldaia() =>
    [
        new
        {
            scadenzaId = ScadenzaScaduta, impiantoId = Caldaia, tipo = "manutenzione", dataScadenza = "2027-01-10", stato = "aperta", periodicitaMesi = 12,
            regolaDaVerificare = false, dataManuale = true, statoRichiamo = "in_attesa", versione = 2, richiami = Array.Empty<object>(),
        },
        new
        {
            scadenzaId = Guid.Parse("019ab000-0000-7000-8000-000000000093"), impiantoId = Caldaia, tipo = "controllo_efficienza", dataScadenza = "2028-03-01",
            stato = "aperta", periodicitaMesi = 48, regola = "Riscaldamento fino a 100 kW (voce di prova)", regolaDaVerificare = true, dataManuale = false,
            statoRichiamo = "non_contattato", versione = 1, richiami = Array.Empty<object>(),
        },
        new
        {
            scadenzaId = Guid.Parse("019ab000-0000-7000-8000-000000000094"), impiantoId = Caldaia, tipo = "manutenzione", dataScadenza = "2026-01-10", stato = "chiusa",
            periodicitaMesi = 12, regolaDaVerificare = false, dataManuale = false, interventoChiusuraId = InterventoChiuso, chiusaIl = "2026-01-11T10:05:00Z",
            statoRichiamo = "fissato", versione = 4, richiami = Array.Empty<object>(),
        },
    ];

    private static object[] Listino() =>
    [
        new
        {
            voceId = VoceManodopera, codice = "MAN-01", descrizione = "Manodopera tecnico (prova)", tipo = "manodopera", unita = "h", aliquotaIva = 22m,
            attiva = true, prezzo = 45m, prezzoDal = "2027-01-01", versione = 2,
        },
        new
        {
            voceId = VoceVaso, codice = "VAS-08", descrizione = "Vaso di espansione 8 l (prova)", tipo = "materiale", unita = "pz", aliquotaIva = 22m,
            attiva = true, prezzo = 90m, prezzoDal = "2026-06-01", versione = 1,
        },
        new
        {
            voceId = Guid.Parse("019ab000-0000-7000-8000-0000000000b2"), codice = "TER-02", descrizione = "Termostato di prova", tipo = "materiale", unita = "pz",
            aliquotaIva = (decimal?)null, attiva = true, prezzo = (decimal?)null, prezzoDal = (string?)null, versione = 1,
        },
    ];

    // Dal più recente: una bozza senza numero, un emesso oltre la validità, un accettato che ha già generato l'intervento.
    private static object[] ElencoPreventivi() =>
    [
        new
        {
            preventivoId = PreventivoBozza, stato = "bozza", scaduto = false, clienteId = Cliente, cliente = "Rossi Mario",
            oggetto = "Sostituzione del termostato (prova)", totale = 61.00m, creatoIl = "2027-01-12T15:20:00Z", versione = 3,
        },
        new
        {
            preventivoId = PreventivoEmesso, anno = 2027, numero = 7, stato = "emesso", scaduto = true, clienteId = Cliente, cliente = "Rossi Mario",
            oggetto = "Sostituzione del vaso di espansione (prova)", emessoIl = "2026-12-01", scadeIl = "2026-12-31", totale = 192.15m,
            creatoIl = "2026-12-01T09:00:00Z", versione = 4,
        },
        new
        {
            preventivoId = PreventivoAccettato, anno = 2027, numero = 6, stato = "accettato", scaduto = false, clienteId = Cliente,
            cliente = "Condominio Le Querce", oggetto = "Nuova caldaia condominiale (prova)", emessoIl = "2026-11-20", scadeIl = "2027-01-19",
            totale = 18300.00m, interventoId = InterventoDaPianificare, creatoIl = "2026-11-20T10:00:00Z", versione = 6,
        },
    ];

    private static object SchedaPreventivo(Guid id, string stato, int? numero, bool scaduto) => new
    {
        preventivoId = id,
        anno = numero is null ? (int?)null : 2027,
        numero,
        stato,
        scaduto,
        clienteId = Cliente,
        cliente = "Rossi Mario",
        codiceFiscale = "RSSMRA80A01H501U",
        sedeId = SedeFirenze,
        indirizzo = "Via dei Platani",
        civico = "12",
        cap = "50100",
        comune = "Firenze",
        provincia = "FI",
        impiantoId = Caldaia,
        tipoImpianto = "Riscaldamento (voce di prova)",
        oggetto = "Sostituzione del vaso di espansione (prova)",
        note = "Prezzi validi per lavori in orario ordinario.",
        validitaGiorni = 30,
        emessoIl = numero is null ? null : "2026-12-01",
        scadeIl = numero is null ? null : "2026-12-31",
        imponibile = 157.50m,
        iva = 34.65m,
        totale = 192.15m,
        accettatoIl = stato == "accettato" ? "2026-12-05T10:00:00Z" : null,
        interventoId = stato == "accettato" ? InterventoDaPianificare : (Guid?)null,
        creatoIl = "2026-12-01T09:00:00Z",
        versione = 4,
        righe = new object[]
        {
            new { posizione = 1, voceId = VoceManodopera, tipo = "manodopera", codice = "MAN-01", descrizione = "Manodopera tecnico (prova)", unita = "h", quantita = 1.5m, prezzoUnitario = 45m, scontoPercentuale = 0m, aliquotaIva = 22m, importo = 67.50m },
            new { posizione = 2, voceId = VoceVaso, tipo = "materiale", codice = "VAS-08", descrizione = "Vaso di espansione 8 l (prova)", unita = "pz", quantita = 1m, prezzoUnitario = 90m, scontoPercentuale = 0m, aliquotaIva = 22m, importo = 90m },
        },
    };
}
