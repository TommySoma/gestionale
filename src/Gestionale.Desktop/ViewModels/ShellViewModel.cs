using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

/// <summary>
/// Struttura dell'applicazione: barra laterale, barra superiore con l'utente,
/// contenuto al centro e un eventuale dialogo sopra.
/// </summary>
public partial class ShellViewModel(ClientApi api, Profilo profilo, INavigazione navigazione, PreferenzaTema tema) : ViewModelBase
{
    private static readonly Dictionary<string, string> s_nomiRuolo = new(StringComparer.Ordinal)
    {
        ["titolare"] = "Titolare",
        ["amministrativo"] = "Amministrativo",
        ["responsabile_tecnico"] = "Responsabile tecnico",
        ["tecnico"] = "Tecnico",
        ["sola_lettura"] = "Sola lettura",
        ["amministratore_sistema"] = "Amministratore di sistema",
    };

    public Profilo Profilo { get; } = profilo;

    public ClientApi Api { get; } = api;

    public string NomeUtente => $"{Profilo.Nome} {Profilo.Cognome}";

    // Ruoli nell'ordine del piano, dal Titolare alla Sola lettura.
    public string Ruolo => string.Join(", ", s_nomiRuolo.Where(r => Profilo.Ruoli.Contains(r.Key)).Select(r => r.Value));

    public string Iniziali => $"{Profilo.Nome.FirstOrDefault()}{Profilo.Cognome.FirstOrDefault()}".ToUpperInvariant();

    public bool VedeClienti => Profilo.Ha("clienti.lettura");

    public bool ModificaClienti => Profilo.Ha("clienti.scrittura");

    public bool AnonimizzaClienti => Profilo.Ha("clienti.anonimizzazione");

    public bool VedeImpianti => Profilo.Ha("impianti.lettura");

    // Con l'ambito "propri" (il Tecnico) non si creano impianti nuovi.
    public bool ModificaImpianti => Profilo.Permessi.Any(p => p.Codice == "impianti.scrittura" && p.Ambito == "tutti");

    public bool VedeInterventi => Profilo.Ha("interventi.lettura");

    // Creare, pianificare, riassegnare e annullare spetta a chi ha l'ambito su tutto.
    public bool PianificaInterventi => Profilo.Permessi.Any(p => p.Codice == "interventi.scrittura" && p.Ambito == "tutti");

    // Avviare e chiudere: anche il Tecnico, sui propri interventi.
    public bool ScriveInterventi => Profilo.Ha("interventi.scrittura");

    public bool VedeScadenze => Profilo.Ha("scadenze.lettura");

    // Correggere le date e registrare i richiami spetta all'ufficio (il Tecnico ha solo la lettura).
    public bool GestisceScadenze => Profilo.Ha("scadenze.scrittura");

    /// <summary>Data e ora correnti; nei test si fissa per avere schermate ripetibili.</summary>
    public TimeProvider Orologio { get; init; } = TimeProvider.System;

    public string EtichettaM3 => string.Format(System.Globalization.CultureInfo.CurrentCulture, Testi.InArrivo, "M3");

    [ObservableProperty]
    public partial ViewModelBase? Contenuto { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaDialogo))]
    public partial ViewModelBase? Dialogo { get; set; }

    public bool HaDialogo => Dialogo is not null;

    [ObservableProperty]
    public partial bool ClientiAttivo { get; set; }

    [ObservableProperty]
    public partial bool ImpiantiAttivo { get; set; }

    [ObservableProperty]
    public partial bool InterventiAttivo { get; set; }

    [ObservableProperty]
    public partial bool ScadenzeAttivo { get; set; }

    [ObservableProperty]
    public partial bool PreventiviAttivo { get; set; }

    [ObservableProperty]
    public partial bool ListinoAttivo { get; set; }

    public bool VedePreventivi => Profilo.Ha("preventivi.lettura");

    public bool GestiscePreventivi => Profilo.Ha("preventivi.scrittura");

    public bool VedeListino => Profilo.Ha("listino.lettura");

    public bool GestisceListino => Profilo.Ha("listino.scrittura");

    [RelayCommand]
    public async Task ApriPreventiviAsync()
    {
        Evidenzia(nameof(PreventiviAttivo));
        var preventivi = new PreventiviViewModel(this);
        Contenuto = preventivi;
        await preventivi.CaricaAsync();
    }

    public async Task ApriPreventivoAsync(Guid preventivoId)
    {
        Evidenzia(nameof(PreventiviAttivo));
        var scheda = new SchedaPreventivoViewModel(this, preventivoId);
        Contenuto = scheda;
        await scheda.CaricaAsync();
    }

    [RelayCommand]
    public async Task ApriListinoAsync()
    {
        Evidenzia(nameof(ListinoAttivo));
        var listino = new ListinoViewModel(this);
        Contenuto = listino;
        await listino.CaricaAsync();
    }

    // Una sola voce della barra laterale è evidenziata: quella dell'area aperta.
    private void Evidenzia(string area)
    {
        ClientiAttivo = area == nameof(ClientiAttivo);
        ImpiantiAttivo = area == nameof(ImpiantiAttivo);
        InterventiAttivo = area == nameof(InterventiAttivo);
        ScadenzeAttivo = area == nameof(ScadenzeAttivo);
        PreventiviAttivo = area == nameof(PreventiviAttivo);
        ListinoAttivo = area == nameof(ListinoAttivo);
    }

    private VociImpianti? _vociImpianti;

    [RelayCommand]
    public async Task ApriClientiAsync()
    {
        Evidenzia(nameof(ClientiAttivo));
        var clienti = new ClientiViewModel(this);
        Contenuto = clienti;
        await clienti.CaricaAsync();
    }

    public async Task ApriClienteAsync(Guid clienteId)
    {
        Evidenzia(nameof(ClientiAttivo));
        var scheda = new SchedaClienteViewModel(this, clienteId);
        Contenuto = scheda;
        await scheda.CaricaAsync();
    }

    [RelayCommand]
    public async Task ApriImpiantiAsync()
    {
        Evidenzia(nameof(ImpiantiAttivo));
        var impianti = new ImpiantiViewModel(this);
        Contenuto = impianti;
        await impianti.CaricaAsync();
    }

    public async Task ApriImpiantoAsync(Guid impiantoId)
    {
        Evidenzia(nameof(ImpiantiAttivo));
        var scheda = new SchedaImpiantoViewModel(this, impiantoId);
        Contenuto = scheda;
        await scheda.CaricaAsync();
    }

    [RelayCommand]
    public async Task ApriScadenzeAsync()
    {
        Evidenzia(nameof(ScadenzeAttivo));
        var scadenze = new ScadenzeViewModel(this);
        Contenuto = scadenze;
        await scadenze.CaricaAsync();
    }

    [RelayCommand]
    public Task ApriInterventiAsync() => ApriCalendarioAsync(null);

    /// <summary>Calendario della settimana che contiene il giorno indicato; senza giorno, oggi.</summary>
    public async Task ApriCalendarioAsync(DateOnly? giorno)
    {
        Evidenzia(nameof(InterventiAttivo));
        var calendario = new CalendarioViewModel(this, giorno ?? FormatoInterventi.Oggi(Orologio));
        Contenuto = calendario;
        await calendario.CaricaAsync();
    }

    /// <summary>Scheda di un intervento; indietro dice dove tornare (di norma il calendario).</summary>
    public async Task ApriInterventoAsync(Guid interventoId, Func<Task>? indietro = null)
    {
        Evidenzia(nameof(InterventiAttivo));
        var scheda = new SchedaInterventoViewModel(this, interventoId, indietro);
        Contenuto = scheda;
        await scheda.CaricaAsync();
    }

    /// <summary>Tecnici da proporre nei dialoghi; se il server non risponde, l'elenco è vuoto e l'intervento resta da assegnare.</summary>
    public async Task<IReadOnlyList<TecnicoAssegnabile>> TecniciPerDialogoAsync() =>
        (await Api.TecniciAsync()).Valore ?? [];

    /// <summary>Tipi e combustibili cambiano solo con una migrazione: basta leggerli una volta per sessione.</summary>
    public async Task<Risposta<VociImpianti>> VociImpiantiAsync()
    {
        if (_vociImpianti is { } voci)
        {
            return new Risposta<VociImpianti>(System.Net.HttpStatusCode.OK, voci, null);
        }

        var risposta = await Api.VociImpiantiAsync();
        _vociImpianti = risposta.Valore;
        return risposta;
    }

    public bool TemaSistema => tema.Corrente == CodiciTema.Sistema;

    public bool TemaChiaro => tema.Corrente == CodiciTema.Chiaro;

    public bool TemaScuro => tema.Corrente == CodiciTema.Scuro;

    [RelayCommand]
    private void ScegliTema(string codice)
    {
        tema.Imposta(codice);
        OnPropertyChanged(nameof(TemaSistema));
        OnPropertyChanged(nameof(TemaChiaro));
        OnPropertyChanged(nameof(TemaScuro));
    }

    public void MostraDialogo(ViewModelBase dialogo) => Dialogo = dialogo;

    public void ChiudiDialogo() => Dialogo = null;

    [RelayCommand]
    private async Task EsciAsync()
    {
        await Api.EsciAsync();
        navigazione.VaiAdAccesso();
    }
}
