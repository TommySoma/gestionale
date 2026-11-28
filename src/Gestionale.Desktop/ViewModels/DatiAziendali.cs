using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

/// <summary>
/// Dati dell'impresa stampati sui documenti e codice ditta del catasto, che entra
/// in ogni riga del file di import SIERT. La regione si legge soltanto.
/// </summary>
public partial class DatiAziendaliViewModel(ShellViewModel shell) : ViewModelBase
{
    private int _versione;

    public bool PuoModificare => shell.GestisceAzienda;

    public bool SolaLettura => !PuoModificare;

    [ObservableProperty]
    public partial string RagioneSociale { get; set; } = "";

    [ObservableProperty]
    public partial string PartitaIva { get; set; } = "";

    [ObservableProperty]
    public partial string CodiceFiscale { get; set; } = "";

    [ObservableProperty]
    public partial string Indirizzo { get; set; } = "";

    [ObservableProperty]
    public partial string Cap { get; set; } = "";

    [ObservableProperty]
    public partial string Comune { get; set; } = "";

    [ObservableProperty]
    public partial string Provincia { get; set; } = "";

    [ObservableProperty]
    public partial string Regione { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SenzaCodiceDitta))]
    public partial string CodiceDitta { get; set; } = "";

    public bool SenzaCodiceDitta => string.IsNullOrWhiteSpace(CodiceDitta);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    [ObservableProperty]
    public partial bool Salvati { get; set; }

    [ObservableProperty]
    public partial bool InCorso { get; set; }

    public async Task CaricaAsync()
    {
        var risposta = await shell.Api.AziendaAsync();
        if (risposta.Valore is not { } azienda)
        {
            Errore = risposta.Messaggio;
            return;
        }

        Mostra(azienda);
        await CaricaPostaAsync();
    }

    [RelayCommand]
    private async Task SalvaAsync()
    {
        if (InCorso || !PuoModificare)
        {
            return;
        }

        Errore = null;
        Salvati = false;
        if (!FormatoCommerciale.ProvaIntero(CodiceDitta, out var codiceDitta))
        {
            Errore = Testi.CodiceDittaNumerico;
            return;
        }

        InCorso = true;
        try
        {
            var risposta = await shell.Api.AggiornaAziendaAsync(new DatiAzienda(
                RagioneSociale.Trim(), Vuoto(PartitaIva), Vuoto(CodiceFiscale), Vuoto(Indirizzo), Vuoto(Cap), Vuoto(Comune),
                Vuoto(Provincia), codiceDitta, _versione));
            if (!risposta.Riuscita)
            {
                Errore = risposta.Messaggio;
                return;
            }

            await CaricaAsync();
            Salvati = Errore is null;
        }
        finally
        {
            InCorso = false;
        }
    }

    // --- Posta in uscita, per i promemoria email ---------------------------

    private int? _versionePosta;

    public IReadOnlyList<Opzione> Sicurezze { get; } =
    [
        new("starttls", Testi.SicurezzaStartTls),
        new("ssl", Testi.SicurezzaSsl),
        new("nessuna", Testi.SicurezzaNessuna),
    ];

    [ObservableProperty]
    public partial string HostPosta { get; set; } = "";

    [ObservableProperty]
    public partial string PortaPosta { get; set; } = "587";

    [ObservableProperty]
    public partial Opzione? SicurezzaPosta { get; set; }

    [ObservableProperty]
    public partial string UtentePosta { get; set; } = "";

    [ObservableProperty]
    public partial string PasswordPosta { get; set; } = "";

    [ObservableProperty]
    public partial string MittenteEmail { get; set; } = "";

    [ObservableProperty]
    public partial string MittenteNome { get; set; } = "";

    [ObservableProperty]
    public partial string SuggerimentoPassword { get; set; } = "";

    [ObservableProperty]
    public partial bool PostaConfigurata { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrorePosta))]
    public partial string? ErrorePosta { get; set; }

    public bool HaErrorePosta => !string.IsNullOrEmpty(ErrorePosta);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaAvvisoPosta))]
    public partial string? AvvisoPosta { get; set; }

    public bool HaAvvisoPosta => !string.IsNullOrEmpty(AvvisoPosta);

    private async Task CaricaPostaAsync()
    {
        var risposta = await shell.Api.ParametriPostaAsync();
        if (!risposta.Riuscita)
        {
            ErrorePosta = risposta.Messaggio;
            return;
        }

        var p = risposta.Valore;
        _versionePosta = p?.Versione;
        PostaConfigurata = p is not null;
        HostPosta = p?.Host ?? "";
        PortaPosta = (p?.Porta ?? 587).ToString(CultureInfo.InvariantCulture);
        SicurezzaPosta = Sicurezze.FirstOrDefault(s => s.Codice == p?.Sicurezza) ?? Sicurezze[0];
        UtentePosta = p?.Utente ?? "";
        PasswordPosta = "";
        SuggerimentoPassword = p?.ConPassword == true ? Testi.PasswordSalvata : Testi.PasswordNonSalvata;
        MittenteEmail = p?.MittenteEmail ?? "";
        MittenteNome = p?.MittenteNome ?? "";
    }

    [RelayCommand]
    private async Task SalvaPostaAsync()
    {
        ErrorePosta = null;
        AvvisoPosta = null;
        if (!int.TryParse(PortaPosta.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var porta))
        {
            ErrorePosta = Testi.PortaNonNumerica;
            return;
        }

        var risposta = await shell.Api.SalvaPostaAsync(new DatiPosta(
            HostPosta.Trim(), porta, SicurezzaPosta?.Codice ?? "starttls", Vuoto(UtentePosta), string.IsNullOrEmpty(PasswordPosta) ? null : PasswordPosta,
            MittenteEmail.Trim(), Vuoto(MittenteNome), _versionePosta));
        if (!risposta.Riuscita)
        {
            ErrorePosta = risposta.Messaggio;
            return;
        }

        await CaricaPostaAsync();
        AvvisoPosta = Testi.PostaSalvata;
    }

    /// <summary>Un'email di prova all'utente collegato, con i parametri salvati.</summary>
    [RelayCommand]
    private async Task ProvaPostaAsync()
    {
        ErrorePosta = null;
        AvvisoPosta = null;
        var risposta = await shell.Api.ProvaPostaAsync();
        if (risposta.Riuscita)
        {
            AvvisoPosta = string.Format(CultureInfo.CurrentCulture, Testi.ProvaPostaInviata, shell.Profilo.Email);
        }
        else
        {
            ErrorePosta = risposta.Messaggio;
        }
    }

    private void Mostra(Azienda azienda)
    {
        _versione = azienda.Versione;
        RagioneSociale = azienda.RagioneSociale;
        PartitaIva = azienda.PartitaIva ?? "";
        CodiceFiscale = azienda.CodiceFiscale ?? "";
        Indirizzo = azienda.Indirizzo ?? "";
        Cap = azienda.Cap ?? "";
        Comune = azienda.Comune ?? "";
        Provincia = azienda.Provincia ?? "";
        Regione = CultureInfo.GetCultureInfo("it-IT").TextInfo.ToTitleCase(azienda.Regione);
        CodiceDitta = azienda.CodiceDittaSiert?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    private static string? Vuoto(string testo) => string.IsNullOrWhiteSpace(testo) ? null : testo.Trim();
}
