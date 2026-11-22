using CommunityToolkit.Mvvm.ComponentModel;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

/// <summary>La finestra mostra una pagina alla volta: accesso, secondo fattore oppure l'applicazione.</summary>
public partial class MainViewModel : ViewModelBase, INavigazione
{
    private readonly ClientApi _api;
    private readonly IArchivioImpostazioni _archivio;

    public MainViewModel(ClientApi api, IArchivioImpostazioni archivio)
    {
        _api = api;
        _archivio = archivio;
        _api.SessioneTerminata += (_, _) => VaiAdAccesso(Testi.SessioneScaduta);
        Pagina = new AccessoViewModel(api, archivio, this);
    }

    [ObservableProperty]
    public partial ViewModelBase Pagina { get; set; }

    /// <summary>All'avvio prova a riprendere la sessione salvata nel portachiavi.</summary>
    public async Task AvviaAsync()
    {
        if (Uri.TryCreate(_archivio.Carica().IndirizzoServer, UriKind.Absolute, out var server))
        {
            _api.ImpostaServer(server);
            if (await _api.RiprendiSessioneAsync())
            {
                await VaiAllApplicazioneAsync();
            }
        }
    }

    public void VaiAdAccesso(string? messaggio = null) => Pagina = new AccessoViewModel(_api, _archivio, this, messaggio);

    public void VaiAVerificaMfa(string tokenPassaggio, string email) => Pagina = new VerificaMfaViewModel(_api, this, tokenPassaggio, email);

    public void VaiAConfiguraMfa(string tokenPassaggio, string email)
    {
        var configura = new ConfiguraMfaViewModel(_api, this, tokenPassaggio, email);
        Pagina = configura;
        _ = configura.CaricaAsync();
    }

    public void VaiACodiciRecupero(IReadOnlyList<string> codici) => Pagina = new CodiciRecuperoViewModel(codici, this);

    public async Task VaiAllApplicazioneAsync()
    {
        var profilo = await _api.ProfiloAsync();
        if (profilo.Valore is null)
        {
            VaiAdAccesso(profilo.Messaggio);
            return;
        }

        var shell = new ShellViewModel(_api, profilo.Valore, this);
        Pagina = shell;
        await shell.ApriClientiAsync();
    }
}
