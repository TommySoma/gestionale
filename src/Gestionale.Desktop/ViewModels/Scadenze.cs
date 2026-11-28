using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

/// <summary>Quanto è vicina una scadenza: rosso scaduta, ambra in scadenza, verde in regola. Sempre con icona e testo.</summary>
public enum LivelloScadenza
{
    Scaduta,
    InScadenza,
    InRegola,
}

/// <summary>Testi e livelli delle scadenze, in italiano.</summary>
public static class FormatoScadenze
{
    /// <summary>Entro questi giorni una scadenza è "in scadenza"; oltre è in regola.</summary>
    public const int GiorniInScadenza = 30;

    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");

    public static string Tipo(string tipo) => tipo switch
    {
        "manutenzione" => Testi.TipoScadenzaManutenzione,
        "controllo_efficienza" => Testi.TipoScadenzaControllo,
        "controllo_perdite" => Testi.TipoScadenzaControlloPerdite,
        _ => tipo,
    };

    public static LivelloScadenza Livello(int giorniMancanti) => giorniMancanti switch
    {
        < 0 => LivelloScadenza.Scaduta,
        <= GiorniInScadenza => LivelloScadenza.InScadenza,
        _ => LivelloScadenza.InRegola,
    };

    public static string Urgenza(int giorniMancanti) => giorniMancanti switch
    {
        -1 => Testi.ScadutaIeri,
        < 0 => string.Format(s_italiano, Testi.ScadutaDaGiorni, -giorniMancanti),
        0 => Testi.ScadeOggi,
        1 => Testi.ScadeDomani,
        <= GiorniInScadenza => string.Format(s_italiano, Testi.ScadeTraGiorni, giorniMancanti),
        _ => Testi.InRegola,
    };

    public static string StatoRichiamo(string stato) => stato switch
    {
        "non_contattato" => Testi.RichiamoNonContattato,
        "in_attesa" => Testi.RichiamoInAttesa,
        "fissato" => Testi.RichiamoFissato,
        "rifiutato" => Testi.RichiamoRifiutato,
        _ => stato,
    };

    public static string Canale(string canale) => canale switch
    {
        "telefono" => Testi.CanaleTelefono,
        "email" => Testi.CanaleEmail,
        "sms" => Testi.CanaleSms,
        "whatsapp" => Testi.CanaleWhatsapp,
        "di_persona" => Testi.CanaleDiPersona,
        _ => Testi.CanaleAltro,
    };

    public static string EsitoRichiamo(string esito) => esito switch
    {
        "nessuna_risposta" => Testi.EsitoNessunaRisposta,
        "da_richiamare" => Testi.EsitoDaRichiamare,
        "appuntamento" => Testi.EsitoAppuntamento,
        "rifiutato" => Testi.EsitoRifiutato,
        "promemoria_inviato" => Testi.EsitoPromemoriaInviato,
        _ => esito,
    };

    public static string Periodicita(int mesi) => mesi switch
    {
        1 => Testi.OgniMese,
        12 => Testi.OgniAnno,
        _ when mesi % 12 == 0 => string.Format(s_italiano, Testi.OgniAnni, mesi / 12),
        _ => string.Format(s_italiano, Testi.OgniMesi, mesi),
    };

    /// <summary>"Ultimo: nessuna risposta, 25 novembre · risentire il 27 novembre 2026"; null se non ci sono richiami.</summary>
    public static string? UltimoRichiamo(string? esito, DateTime? quando, DateOnly? richiamareIl)
    {
        if (esito is null)
        {
            return null;
        }

        var testo = string.Format(s_italiano, Testi.UltimoRichiamo, EsitoRichiamo(esito).ToLower(s_italiano),
            FormatoInterventi.DataOra(quando));
        return richiamareIl is { } data
            ? $"{testo} · {string.Format(s_italiano, Testi.RisentireIl, FormatoImpianti.Data(data))}"
            : testo;
    }
}

/// <summary>Riga della lista da richiamare.</summary>
public sealed record RigaScadenza(
    ScadenzaInArrivo Dati,
    string Cliente,
    string Indirizzo,
    string Tipo,
    string Data,
    string Urgenza,
    LivelloScadenza Livello,
    string Richiamo,
    string? UltimoRichiamo,
    string? Telefono)
{
    public bool Scaduta => Livello == LivelloScadenza.Scaduta;

    public bool InScadenza => Livello == LivelloScadenza.InScadenza;

    public bool InRegola => Livello == LivelloScadenza.InRegola;

    public bool HaUltimoRichiamo => UltimoRichiamo is not null;

    /// <summary>Scelta per il promemoria email.</summary>
    public SelezioneRiga Selezione { get; } = new();

    public static RigaScadenza Da(ScadenzaInArrivo s) => new(
        s,
        s.Cliente,
        $"{s.Indirizzo}, {s.Comune} ({s.Provincia})",
        FormatoScadenze.Tipo(s.Tipo),
        FormatoImpianti.Data(s.DataScadenza),
        FormatoScadenze.Urgenza(s.GiorniMancanti),
        FormatoScadenze.Livello(s.GiorniMancanti),
        FormatoScadenze.StatoRichiamo(s.StatoRichiamo),
        FormatoScadenze.UltimoRichiamo(s.UltimoEsito, s.UltimoRichiamoIl, s.RichiamareIl),
        s.Telefono);
}

/// <summary>
/// Lista "da richiamare": scadenze scadute e in arrivo, dalla più urgente. Le
/// calcola il server; qui si filtrano, si registrano i richiami e si corregge la
/// data. Il Tecnico vede solo quelle dei propri impianti, senza telefono né richiami.
/// </summary>
public partial class ScadenzeViewModel(ShellViewModel shell) : ViewModelBase
{
    public const int DimensionePagina = 50;

    public ObservableCollection<RigaScadenza> Righe { get; } = [];

    public bool GestisceScadenze => shell.GestisceScadenze;

    public bool PuoInviarePromemoria => shell.InviaPromemoria;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestoInviaPromemoria))]
    [NotifyCanExecuteChangedFor(nameof(InviaPromemoriaCommand))]
    public partial int Scelte { get; set; }

    public string TestoInviaPromemoria => Scelte == 0
        ? Testi.InviaPromemoria
        : string.Format(CultureInfo.CurrentCulture, Testi.InviaPromemoriaScelte, Scelte);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaAvviso))]
    public partial string? Avviso { get; set; }

    public bool HaAvviso => !string.IsNullOrEmpty(Avviso);

    [ObservableProperty]
    public partial int Giorni { get; set; } = 30;

    [ObservableProperty]
    public partial string? Tipo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intervallo), nameof(PuoPrecedente), nameof(PuoSuccessiva))]
    public partial int NumeroPagina { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Conteggio), nameof(Intervallo), nameof(PuoSuccessiva), nameof(Vuoto))]
    public partial int Totale { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaScadute))]
    public partial int Scadute { get; set; }

    [ObservableProperty]
    public partial bool Caricamento { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    public bool Vuoto => !Caricamento && Totale == 0 && !HaErrore;

    public bool HaScadute => Scadute > 0;

    public string AvvisoScadute => Scadute == 1
        ? Testi.AvvisoUnaScaduta
        : string.Format(CultureInfo.CurrentCulture, Testi.AvvisoScadute, Scadute);

    public string Conteggio => Totale == 1
        ? string.Format(CultureInfo.CurrentCulture, Testi.ConteggioUnaScadenza, Giorni)
        : string.Format(CultureInfo.CurrentCulture, Testi.ConteggioScadenze, Totale, Giorni);

    public string Intervallo => Totale == 0
        ? ""
        : string.Format(CultureInfo.CurrentCulture, Testi.IntervalloPagina,
            ((NumeroPagina - 1) * DimensionePagina) + 1, Math.Min(NumeroPagina * DimensionePagina, Totale), Totale);

    public bool PuoPrecedente => NumeroPagina > 1;

    public bool PuoSuccessiva => NumeroPagina * DimensionePagina < Totale;

    public bool Entro30 { get => Giorni == 30; set { if (value) { ImpostaGiorni(30); } } }

    public bool Entro60 { get => Giorni == 60; set { if (value) { ImpostaGiorni(60); } } }

    public bool Entro90 { get => Giorni == 90; set { if (value) { ImpostaGiorni(90); } } }

    public bool TipoTutti { get => Tipo is null; set { if (value) { ImpostaTipo(null); } } }

    public bool TipoManutenzioni { get => Tipo == "manutenzione"; set { if (value) { ImpostaTipo("manutenzione"); } } }

    public bool TipoControlli { get => Tipo == "controllo_efficienza"; set { if (value) { ImpostaTipo("controllo_efficienza"); } } }

    public async Task CaricaAsync()
    {
        Caricamento = true;
        Errore = null;
        try
        {
            var risposta = await shell.Api.ScadenzeInArrivoAsync(Giorni, Tipo, null, NumeroPagina, DimensionePagina);
            if (risposta.Valore is not { } pagina)
            {
                Errore = risposta.Messaggio;
                return;
            }

            Righe.Clear();
            Scelte = 0;
            foreach (var s in pagina.Elementi)
            {
                var riga = RigaScadenza.Da(s);
                riga.Selezione.PropertyChanged += (_, _) => Scelte = Righe.Count(r => r.Selezione.Scelta);
                Righe.Add(riga);
            }

            Totale = pagina.Totale;
            // Le scadute vengono per prime: bastano quelle della pagina per l'avviso in cima.
            Scadute = Righe.Count(r => r.Scaduta);
            OnPropertyChanged(nameof(AvvisoScadute));
        }
        finally
        {
            Caricamento = false;
            OnPropertyChanged(nameof(Vuoto));
        }
    }

    private void ImpostaGiorni(int giorni)
    {
        if (Giorni == giorni)
        {
            return;
        }

        Giorni = giorni;
        NumeroPagina = 1;
        OnPropertyChanged(nameof(Entro30));
        OnPropertyChanged(nameof(Entro60));
        OnPropertyChanged(nameof(Entro90));
        OnPropertyChanged(nameof(Conteggio));
        _ = CaricaAsync();
    }

    private void ImpostaTipo(string? tipo)
    {
        if (Tipo == tipo)
        {
            return;
        }

        Tipo = tipo;
        NumeroPagina = 1;
        OnPropertyChanged(nameof(TipoTutti));
        OnPropertyChanged(nameof(TipoManutenzioni));
        OnPropertyChanged(nameof(TipoControlli));
        _ = CaricaAsync();
    }

    [RelayCommand]
    private async Task PrecedenteAsync()
    {
        NumeroPagina--;
        await CaricaAsync();
    }

    [RelayCommand]
    private async Task SuccessivaAsync()
    {
        NumeroPagina++;
        await CaricaAsync();
    }

    [RelayCommand]
    private Task ApriAsync(RigaScadenza riga) => shell.ApriImpiantoAsync(riga.Dati.ImpiantoId);

    /// <summary>Anteprima e invio dei promemoria email per le scadenze scelte; il risultato resta in un avviso.</summary>
    [RelayCommand(CanExecute = nameof(HaScelte))]
    private async Task InviaPromemoriaAsync()
    {
        var scelte = Righe.Where(r => r.Selezione.Scelta).Select(r => r.Dati.ScadenzaId).ToList();
        var dialogo = new PromemoriaViewModel(shell, scelte, risultati =>
        {
            var inviati = risultati.Count(r => r.Inviato);
            var falliti = risultati.Where(r => r.Errore is not null).ToList();
            Avviso = string.Format(CultureInfo.CurrentCulture, Testi.PromemoriaInviati, inviati)
                + (falliti.Count > 0 ? " " + string.Format(CultureInfo.CurrentCulture, Testi.PromemoriaNonInviati, falliti.Count, falliti[0].Errore) : "");
            _ = CaricaAsync();
        });
        shell.MostraDialogo(dialogo);
        await dialogo.CaricaAsync();
    }

    private bool HaScelte() => Scelte > 0;

    [RelayCommand]
    private void RegistraRichiamo(RigaScadenza riga) =>
        shell.MostraDialogo(new RichiamoViewModel(
            shell, riga.Dati.ScadenzaId, riga.Dati.ImpiantoId, riga.Dati.Versione, $"{riga.Tipo} · {riga.Cliente}", riga.Telefono, () => _ = CaricaAsync()));

    [RelayCommand]
    private void CorreggiData(RigaScadenza riga) =>
        shell.MostraDialogo(new DataScadenzaViewModel(
            shell, riga.Dati.ScadenzaId, riga.Dati.Versione, riga.Dati.DataScadenza, $"{riga.Tipo} · {riga.Cliente}", () => _ = CaricaAsync()));
}

/// <summary>
/// Un tentativo di contatto per una scadenza: canale, esito e, se va risentito,
/// quando. Con l'appuntamento si collega uno degli interventi aperti dell'impianto.
/// </summary>
public partial class RichiamoViewModel : DialogoViewModel
{
    private readonly Guid _scadenzaId;
    private readonly Guid _impiantoId;
    private readonly int _versione;
    private readonly Action _salvato;
    private Task? _caricamentoInterventi;
    private bool _interventiCaricati;

    public RichiamoViewModel(ShellViewModel shell, Guid scadenzaId, Guid impiantoId, int versione, string descrizione, string? telefono, Action salvato) : base(shell)
    {
        _scadenzaId = scadenzaId;
        _impiantoId = impiantoId;
        _versione = versione;
        _salvato = salvato;
        Descrizione = descrizione;
        Telefono = telefono;
        Canale = Canali[0];
        Esito = Esiti[0];
    }

    public string Descrizione { get; }

    public string? Telefono { get; }

    public bool HaTelefono => !string.IsNullOrEmpty(Telefono);

    public IReadOnlyList<Opzione> Canali { get; } =
        [.. new[] { "telefono", "email", "sms", "whatsapp", "di_persona", "altro" }.Select(c => new Opzione(c, FormatoScadenze.Canale(c)))];

    public IReadOnlyList<Opzione> Esiti { get; } =
        [.. new[] { "nessuna_risposta", "da_richiamare", "appuntamento", "rifiutato" }.Select(c => new Opzione(c, FormatoScadenze.EsitoRichiamo(c)))];

    [ObservableProperty]
    public partial Opzione Canale { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConData), nameof(ConAppuntamento))]
    public partial Opzione Esito { get; set; }

    /// <summary>La data per risentirsi vale solo se il cliente non ha risposto o va richiamato (vincolo anche sul server).</summary>
    public bool ConData => Esito.Codice is "nessuna_risposta" or "da_richiamare";

    public bool ConAppuntamento => Esito.Codice == "appuntamento";

    /// <summary>Interventi aperti dell'impianto a cui legare l'appuntamento; il primo è "nessuno".</summary>
    public ObservableCollection<OpzioneVoce> Interventi { get; } = [];

    [ObservableProperty]
    public partial OpzioneVoce? Intervento { get; set; }

    public bool SenzaInterventiAperti => _interventiCaricati && Interventi.Count <= 1;

    /// <summary>Carica gli interventi aperti la prima volta che serve l'appuntamento; chi richiama aspetta lo stesso caricamento.</summary>
    public Task CaricaInterventiAsync() => _caricamentoInterventi ??= CaricaInterventiApertiAsync();

    private async Task CaricaInterventiApertiAsync()
    {
        var nessuno = new OpzioneVoce(null, Testi.NessunInterventoCollegato);
        Interventi.Add(nessuno);
        Intervento = nessuno;
        if (!Shell.VedeInterventi)
        {
            _interventiCaricati = true;
            OnPropertyChanged(nameof(SenzaInterventiAperti));
            return;
        }

        var risposta = await Shell.Api.CercaInterventiAsync(null, null, _impiantoId, null, 1, 50);
        foreach (var i in (risposta.Valore?.Elementi ?? []).Where(i => i.Stato is "da_pianificare" or "pianificato" or "in_corso"))
        {
            var quando = i.Inizio is null ? FormatoInterventi.Stato(i.Stato) : FormatoInterventi.Finestra(i.Inizio, i.Fine);
            Interventi.Add(new OpzioneVoce(i.InterventoId, $"{FormatoInterventi.Tipo(i.Tipo)} {FormatoInterventi.Numero(i.Numero, i.Anno)} · {quando}"));
        }

        // Se c'è un solo intervento aperto, è quasi sempre quello dell'appuntamento.
        if (Interventi.Count == 2)
        {
            Intervento = Interventi[1];
        }

        _interventiCaricati = true;
        OnPropertyChanged(nameof(SenzaInterventiAperti));
    }

    partial void OnEsitoChanged(Opzione value)
    {
        if (value.Codice == "appuntamento")
        {
            _ = CaricaInterventiAsync();
        }
    }

    [ObservableProperty]
    public partial DateTime? RichiamareIl { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; } = "";

    protected override async Task<string?> EseguiAsync()
    {
        var risposta = await Shell.Api.RegistraRichiamoAsync(_scadenzaId, new DatiRichiamo(
            Canale.Codice,
            Esito.Codice,
            ConData ? FormatoImpianti.DaSelettore(RichiamareIl) : null,
            ConAppuntamento ? Intervento?.Id : null,
            Vuoto(Note),
            _versione));
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato();
}

/// <summary>Data corretta a mano dall'ufficio: da quel momento il ricalcolo non la sposta più.</summary>
public partial class DataScadenzaViewModel : DialogoViewModel
{
    private readonly Guid _scadenzaId;
    private readonly int _versione;
    private readonly Action _salvato;

    public DataScadenzaViewModel(ShellViewModel shell, Guid scadenzaId, int versione, DateOnly attuale, string descrizione, Action salvato) : base(shell)
    {
        _scadenzaId = scadenzaId;
        _versione = versione;
        _salvato = salvato;
        Descrizione = descrizione;
        Data = FormatoImpianti.PerSelettore(attuale);
    }

    public string Descrizione { get; }

    [ObservableProperty]
    public partial DateTime? Data { get; set; }

    protected override async Task<string?> EseguiAsync()
    {
        if (FormatoImpianti.DaSelettore(Data) is not { } data)
        {
            return Testi.ScegliDataScadenza;
        }

        var risposta = await Shell.Api.CorreggiDataScadenzaAsync(_scadenzaId, data, _versione);
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato();
}
