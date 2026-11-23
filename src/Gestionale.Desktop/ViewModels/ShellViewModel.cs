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

    public string EtichettaM2 => string.Format(System.Globalization.CultureInfo.CurrentCulture, Testi.InArrivo, "M2");

    public string EtichettaM3 => string.Format(System.Globalization.CultureInfo.CurrentCulture, Testi.InArrivo, "M3");

    [ObservableProperty]
    public partial ViewModelBase? Contenuto { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaDialogo))]
    public partial ViewModelBase? Dialogo { get; set; }

    public bool HaDialogo => Dialogo is not null;

    [ObservableProperty]
    public partial bool ClientiAttivo { get; set; }

    [RelayCommand]
    public async Task ApriClientiAsync()
    {
        ClientiAttivo = true;
        var clienti = new ClientiViewModel(this);
        Contenuto = clienti;
        await clienti.CaricaAsync();
    }

    public async Task ApriClienteAsync(Guid clienteId)
    {
        var scheda = new SchedaClienteViewModel(this, clienteId);
        Contenuto = scheda;
        await scheda.CaricaAsync();
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
