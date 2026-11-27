using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;

namespace Gestionale.Desktop.ViewModels;

public sealed record RigaOggiIntervento(Guid Id, string Orario, string Titolo, string Dettaglio, string Stato);

/// <summary>
/// La pagina iniziale: gli interventi di oggi, le scadenze scadute o della
/// settimana e i preventivi in attesa di risposta. Ognuno vede solo ciò che i
/// suoi permessi gli mostrano (il Tecnico i propri interventi e impianti).
/// Rapporti da trasmettere e bollini arrivano con M4.
/// </summary>
public partial class OggiViewModel(ShellViewModel shell) : ViewModelBase
{
    /// <summary>Le scadenze in evidenza: scadute e dei prossimi sette giorni.</summary>
    public const int GiorniScadenze = 7;

    private const int RigheScadenze = 5;

    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");

    public ObservableCollection<RigaOggiIntervento> Interventi { get; } = [];

    public ObservableCollection<RigaScadenza> Scadenze { get; } = [];

    public bool VedeInterventi => shell.VedeInterventi;

    public bool VedeScadenze => shell.VedeScadenze;

    public bool VedePreventivi => shell.VedePreventivi;

    public bool VedeRapporti => shell.VedeRapporti;

    /// <summary>Rapporti non ancora trasmessi al SIERT, dalla bozza allo scartato.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConteggioRapporti))]
    public partial int RapportiDaTrasmettere { get; set; }

    public string ConteggioRapporti => RapportiDaTrasmettere == 1
        ? Testi.OggiUnRapportoDaTrasmettere
        : string.Format(CultureInfo.CurrentCulture, Testi.OggiRapportiDaTrasmettere, RapportiDaTrasmettere);

    [RelayCommand]
    private Task ApriRapportiAsync() => shell.ApriRapportiAsync();

    public bool PianificaInterventi => shell.PianificaInterventi;

    public string Data => FormatoInterventi.Oggi(shell.Orologio).ToString("dddd d MMMM yyyy", s_italiano);

    public string Saluto => string.Format(CultureInfo.CurrentCulture, Testi.Buongiorno, shell.Profilo.Nome);

    [ObservableProperty]
    public partial bool Caricato { get; set; }

    [ObservableProperty]
    public partial int DaPianificare { get; set; }

    [ObservableProperty]
    public partial int ScaduteTotali { get; set; }

    [ObservableProperty]
    public partial int ScadenzeTotali { get; set; }

    [ObservableProperty]
    public partial int PreventiviInAttesa { get; set; }

    [ObservableProperty]
    public partial int PreventiviScaduti { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    public bool SenzaInterventi => Caricato && Interventi.Count == 0;

    public bool SenzaScadenze => Caricato && Scadenze.Count == 0;

    public bool AltreScadenze => ScadenzeTotali > Scadenze.Count;

    public string ConteggioScadenze => ScaduteTotali switch
    {
        0 => string.Format(CultureInfo.CurrentCulture, Testi.OggiScadenzeSettimana, ScadenzeTotali),
        _ => string.Format(CultureInfo.CurrentCulture, Testi.OggiScadenzeConScadute, ScadenzeTotali, ScaduteTotali),
    };

    public string ConteggioDaPianificare => DaPianificare == 1
        ? Testi.OggiUnoDaPianificare
        : string.Format(CultureInfo.CurrentCulture, Testi.OggiDaPianificare, DaPianificare);

    public string ConteggioPreventivi => string.Format(CultureInfo.CurrentCulture, Testi.OggiPreventiviInAttesa, PreventiviInAttesa, PreventiviScaduti);

    public async Task CaricaAsync()
    {
        Errore = null;
        var oggi = FormatoInterventi.Oggi(shell.Orologio);

        Interventi.Clear();
        if (VedeInterventi)
        {
            var risposta = await shell.Api.CercaInterventiAsync(
                FormatoInterventi.DaLocale(oggi, TimeSpan.Zero), FormatoInterventi.DaLocale(oggi.AddDays(1), TimeSpan.Zero), null, null, 1, 100);
            Errore ??= risposta.Valore is null ? risposta.Messaggio : null;
            foreach (var i in (risposta.Valore?.Elementi ?? []).Where(i => i.Stato != "annullato"))
            {
                Interventi.Add(new RigaOggiIntervento(
                    i.InterventoId,
                    FormatoInterventi.Orario(i.Inizio, i.Fine),
                    $"{FormatoInterventi.Tipo(i.Tipo)} {FormatoInterventi.Numero(i.Numero, i.Anno)}",
                    string.Join(" · ", new[] { i.Cliente, i.Comune, i.Tecnico }.Where(v => !string.IsNullOrEmpty(v))),
                    FormatoInterventi.Stato(i.Stato)));
            }

            if (PianificaInterventi)
            {
                DaPianificare = (await shell.Api.CercaInterventiAsync(null, null, null, "da_pianificare", 1, 1)).Valore?.Totale ?? 0;
            }
        }

        Scadenze.Clear();
        if (VedeScadenze)
        {
            var risposta = await shell.Api.ScadenzeInArrivoAsync(GiorniScadenze, null, null, 1, 200);
            Errore ??= risposta.Valore is null ? risposta.Messaggio : null;
            var elenco = risposta.Valore?.Elementi ?? [];
            foreach (var s in elenco.Take(RigheScadenze))
            {
                Scadenze.Add(RigaScadenza.Da(s));
            }

            ScadenzeTotali = risposta.Valore?.Totale ?? 0;
            ScaduteTotali = elenco.Count(s => s.GiorniMancanti < 0);
        }

        if (VedePreventivi)
        {
            PreventiviInAttesa = (await shell.Api.CercaPreventiviAsync(null, "emesso", null, 1, 1)).Valore?.Totale ?? 0;
            PreventiviScaduti = (await shell.Api.CercaPreventiviAsync(null, "scaduto", null, 1, 1)).Valore?.Totale ?? 0;
        }

        if (VedeRapporti)
        {
            RapportiDaTrasmettere = (await shell.Api.CercaRapportiAsync(null, true, null, null, 1, 1)).Valore?.Totale ?? 0;
        }

        Caricato = true;
        foreach (var nome in new[]
        {
            nameof(SenzaInterventi), nameof(SenzaScadenze), nameof(AltreScadenze),
            nameof(ConteggioScadenze), nameof(ConteggioDaPianificare), nameof(ConteggioPreventivi),
        })
        {
            OnPropertyChanged(nome);
        }
    }

    [RelayCommand]
    private Task ApriInterventoAsync(RigaOggiIntervento riga) => shell.ApriInterventoAsync(riga.Id, shell.ApriOggiAsync);

    [RelayCommand]
    private Task ApriImpiantoAsync(RigaScadenza riga) => shell.ApriImpiantoAsync(riga.Dati.ImpiantoId);

    [RelayCommand]
    private Task ApriCalendarioAsync() => shell.ApriCalendarioAsync(null);

    [RelayCommand]
    private Task ApriScadenzeAsync() => shell.ApriScadenzeAsync();

    [RelayCommand]
    private Task ApriPreventiviAsync() => shell.ApriPreventiviAsync();
}
