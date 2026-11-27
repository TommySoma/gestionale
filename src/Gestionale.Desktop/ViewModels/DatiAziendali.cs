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
