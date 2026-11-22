using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

/// <summary>Passaggi tra le schermate di accesso e l'applicazione.</summary>
public interface INavigazione
{
    void VaiAdAccesso(string? messaggio = null);

    void VaiAVerificaMfa(string tokenPassaggio, string email);

    void VaiAConfiguraMfa(string tokenPassaggio, string email);

    void VaiACodiciRecupero(IReadOnlyList<string> codici);

    Task VaiAllApplicazioneAsync();
}

public partial class AccessoViewModel : ViewModelBase
{
    private readonly ClientApi _api;
    private readonly IArchivioImpostazioni _archivio;
    private readonly INavigazione _navigazione;

    public AccessoViewModel(ClientApi api, IArchivioImpostazioni archivio, INavigazione navigazione, string? messaggio = null)
    {
        _api = api;
        _archivio = archivio;
        _navigazione = navigazione;

        var impostazioni = archivio.Carica();
        IndirizzoServer = impostazioni.IndirizzoServer;
        Email = impostazioni.UltimaEmail ?? "";
        Errore = messaggio;
    }

    [ObservableProperty]
    public partial string Email { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial string IndirizzoServer { get; set; }

    [ObservableProperty]
    public partial bool ModificaServer { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestoPulsante))]
    public partial bool InCorso { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    public string TestoPulsante => InCorso ? Testi.AccessoInCorso : Testi.Accedi;

    [RelayCommand]
    private void CambiaServer() => ModificaServer = !ModificaServer;

    [RelayCommand]
    private async Task AccediAsync()
    {
        if (InCorso)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
        {
            Errore = Testi.CompilaEmailPassword;
            return;
        }

        if (!Uri.TryCreate(IndirizzoServer.Trim(), UriKind.Absolute, out var server) || server.Scheme is not ("http" or "https"))
        {
            Errore = Testi.IndirizzoServerNonValido;
            ModificaServer = true;
            return;
        }

        InCorso = true;
        Errore = null;
        try
        {
            _api.ImpostaServer(server);
            _archivio.Salva(new Impostazioni { IndirizzoServer = server.ToString(), UltimaEmail = Email.Trim() });

            var risposta = await _api.AccediAsync(Email.Trim(), Password);
            if (!risposta.Riuscita || risposta.Valore is not { } esito)
            {
                Errore = risposta.Messaggio;
                return;
            }

            Password = "";
            switch (esito.Esito)
            {
                case "mfa_richiesto":
                    _navigazione.VaiAVerificaMfa(esito.TokenPassaggio!, Email.Trim());
                    break;
                case "mfa_da_configurare":
                    _navigazione.VaiAConfiguraMfa(esito.TokenPassaggio!, Email.Trim());
                    break;
                default:
                    await _navigazione.VaiAllApplicazioneAsync();
                    break;
            }
        }
        finally
        {
            InCorso = false;
        }
    }
}

public partial class VerificaMfaViewModel(ClientApi api, INavigazione navigazione, string tokenPassaggio, string email) : ViewModelBase
{
    public string Email { get; } = email;

    [ObservableProperty]
    public partial string Codice { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    [ObservableProperty]
    public partial bool InCorso { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    [RelayCommand]
    private async Task VerificaAsync()
    {
        if (InCorso)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Codice))
        {
            Errore = Testi.ScriviCodice;
            return;
        }

        InCorso = true;
        Errore = null;
        try
        {
            var risposta = await api.VerificaMfaAsync(tokenPassaggio, Codice.Replace(" ", "", StringComparison.Ordinal));
            if (risposta.Riuscita && risposta.Valore?.Token is not null)
            {
                await navigazione.VaiAllApplicazioneAsync();
                return;
            }

            Codice = "";
            // Il token di passaggio dura pochi minuti: se è scaduto si ricomincia dall'accesso.
            if (risposta.Stato == System.Net.HttpStatusCode.Unauthorized && risposta.Messaggio == Testi.SessioneScaduta)
            {
                navigazione.VaiAdAccesso(risposta.Messaggio);
                return;
            }

            Errore = risposta.Messaggio;
        }
        finally
        {
            InCorso = false;
        }
    }

    [RelayCommand]
    private void AltroAccount() => navigazione.VaiAdAccesso();
}

public partial class ConfiguraMfaViewModel : ViewModelBase
{
    private readonly ClientApi _api;
    private readonly INavigazione _navigazione;
    private readonly string _tokenPassaggio;

    public ConfiguraMfaViewModel(ClientApi api, INavigazione navigazione, string tokenPassaggio, string email)
    {
        _api = api;
        _navigazione = navigazione;
        _tokenPassaggio = tokenPassaggio;
        Email = email;
    }

    public string Email { get; }

    [ObservableProperty]
    public partial string? UriConfigurazione { get; set; }

    [ObservableProperty]
    public partial string? Chiave { get; set; }

    [ObservableProperty]
    public partial string Codice { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    [ObservableProperty]
    public partial bool InCorso { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    /// <summary>Chiede al server il segreto; da chiamare quando la schermata compare.</summary>
    public async Task CaricaAsync()
    {
        var risposta = await _api.ConfiguraMfaAsync(_tokenPassaggio);
        if (risposta.Valore is { } configurazione)
        {
            UriConfigurazione = configurazione.UriConfigurazione;
            Chiave = string.Join(' ', configurazione.Segreto.Chunk(4).Select(g => new string(g)));
        }
        else
        {
            Errore = risposta.Messaggio;
        }
    }

    [RelayCommand]
    private async Task AttivaAsync()
    {
        if (InCorso)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Codice))
        {
            Errore = Testi.ScriviCodice;
            return;
        }

        InCorso = true;
        Errore = null;
        try
        {
            var risposta = await _api.AttivaMfaAsync(_tokenPassaggio, Codice.Replace(" ", "", StringComparison.Ordinal));
            if (risposta.Valore is { } attivazione)
            {
                _navigazione.VaiACodiciRecupero(attivazione.CodiciRecupero);
                return;
            }

            Codice = "";
            Errore = risposta.Messaggio;
        }
        finally
        {
            InCorso = false;
        }
    }
}

public partial class CodiciRecuperoViewModel(IReadOnlyList<string> codici, INavigazione navigazione) : ViewModelBase
{
    public IReadOnlyList<string> Codici { get; } = codici;

    public string TestoDaCopiare => string.Join(Environment.NewLine, Codici);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinuaCommand))]
    public partial bool Salvati { get; set; }

    [ObservableProperty]
    public partial string? Conferma { get; set; }

    public void SegnalaCopiati() => Conferma = Testi.Copiato;

    [RelayCommand(CanExecute = nameof(Salvati))]
    private Task ContinuaAsync() => navigazione.VaiAllApplicazioneAsync();
}
