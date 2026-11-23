using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;

namespace Gestionale.Desktop.ViewModels;

public sealed record RigaImpianto(Guid Id, string Cliente, string Tipo, string Indirizzo, string Codice, string Stato, bool Dismesso);

/// <summary>Formati comuni per impianti e generatori, in italiano.</summary>
public static class FormatoImpianti
{
    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");

    public static string Indirizzo(string indirizzo, string? civico, string? cap, string comune, string provincia) =>
        $"{indirizzo}{(civico is null ? "" : " " + civico)}, {(cap is null ? "" : cap + " ")}{comune} ({provincia})";

    public static string Stato(string stato) => stato == "dismesso" ? Testi.StatoDismesso : Testi.StatoAttivo;

    public static string Potenza(decimal? kw) =>
        kw is { } valore ? string.Format(s_italiano, Testi.PotenzaKw, valore.ToString("0.##", s_italiano)) : Testi.NonIndicato;

    public static string Data(DateOnly? data) => data?.ToString("d MMMM yyyy", s_italiano) ?? Testi.NonIndicato;

    public static string Regione(string regione) => regione switch
    {
        "toscana" => Testi.CatastoToscana,
        "lombardia" => Testi.CatastoLombardia,
        _ => regione,
    };

    /// <summary>
    /// Numero con la virgola o con il punto come separatore decimale; vuoto significa
    /// nessun valore. Niente separatore delle migliaia: "1.234" sarebbe ambiguo.
    /// </summary>
    public static bool ProvaDecimale(string? testo, out decimal? valore)
    {
        valore = null;
        if (string.IsNullOrWhiteSpace(testo))
        {
            return true;
        }

        var normalizzato = testo.Trim().Replace(',', '.');
        if (!decimal.TryParse(normalizzato, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var numero))
        {
            return false;
        }

        valore = numero;
        return true;
    }

    // Il selettore di date di Avalonia lavora con DateTime; l'API con date senza ora.
    public static DateOnly? DaSelettore(DateTime? data) => data is { } d ? DateOnly.FromDateTime(d) : null;

    public static DateTime? PerSelettore(DateOnly? data) => data?.ToDateTime(TimeOnly.MinValue);
}

/// <summary>Elenco degli impianti con ricerca, filtro per stato e pagine.</summary>
public partial class ImpiantiViewModel(ShellViewModel shell) : ViewModelBase
{
    public const int DimensionePagina = 25;
    private static readonly TimeSpan s_attesaRicerca = TimeSpan.FromMilliseconds(300);
    private CancellationTokenSource? _ricerca;

    public ObservableCollection<RigaImpianto> Righe { get; } = [];

    [ObservableProperty]
    public partial string Testo { get; set; } = "";

    [ObservableProperty]
    public partial string? Stato { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intervallo), nameof(PuoPrecedente), nameof(PuoSuccessiva))]
    public partial int NumeroPagina { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Conteggio), nameof(Intervallo), nameof(PuoSuccessiva), nameof(Vuoto))]
    public partial int Totale { get; set; }

    [ObservableProperty]
    public partial bool Caricamento { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    public bool Vuoto => !Caricamento && Totale == 0 && !HaErrore;

    // Il Tecnico vede solo gli impianti dei propri interventi.
    public string MessaggioVuoto => shell.Profilo.Permessi.Any(p => p.Codice == "impianti.lettura" && p.Ambito == "propri")
        ? Testi.NessunImpiantoTecnico
        : Testi.NessunImpianto;

    public string Conteggio => Totale == 1
        ? Testi.ConteggioUnImpianto
        : string.Format(CultureInfo.CurrentCulture, Testi.ConteggioImpianti, Totale);

    public string Intervallo => Totale == 0
        ? ""
        : string.Format(CultureInfo.CurrentCulture, Testi.IntervalloPagina,
            ((NumeroPagina - 1) * DimensionePagina) + 1, Math.Min(NumeroPagina * DimensionePagina, Totale), Totale);

    public bool PuoPrecedente => NumeroPagina > 1;

    public bool PuoSuccessiva => NumeroPagina * DimensionePagina < Totale;

    public bool FiltroTutti { get => Stato is null; set { if (value) { ImpostaStato(null); } } }

    public bool FiltroAttivi { get => Stato == "attivo"; set { if (value) { ImpostaStato("attivo"); } } }

    public bool FiltroDismessi { get => Stato == "dismesso"; set { if (value) { ImpostaStato("dismesso"); } } }

    public async Task CaricaAsync(CancellationToken ct = default)
    {
        Caricamento = true;
        Errore = null;
        try
        {
            var risposta = await shell.Api.CercaImpiantiAsync(Testo, null, Stato, NumeroPagina, DimensionePagina, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (risposta.Valore is not { } pagina)
            {
                Errore = risposta.Messaggio;
                return;
            }

            Righe.Clear();
            foreach (var i in pagina.Elementi)
            {
                Righe.Add(new RigaImpianto(
                    i.ImpiantoId,
                    i.Cliente,
                    i.TipoImpianto,
                    FormatoImpianti.Indirizzo(i.Indirizzo, i.Civico, null, i.Comune, i.Provincia),
                    i.CodiceCatasto ?? i.Pdr ?? "—",
                    FormatoImpianti.Stato(i.Stato),
                    i.Stato == "dismesso"));
            }

            Totale = pagina.Totale;
        }
        finally
        {
            Caricamento = false;
            OnPropertyChanged(nameof(Vuoto));
        }
    }

    // La ricerca parte quando l'utente smette di scrivere per un attimo.
    partial void OnTestoChanged(string value)
    {
        _ricerca?.Cancel();
        _ricerca?.Dispose();
        _ricerca = new CancellationTokenSource();
        _ = CercaDopoUnaPausaAsync(_ricerca.Token);
    }

    private async Task CercaDopoUnaPausaAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(s_attesaRicerca, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        NumeroPagina = 1;
        await CaricaAsync(ct);
    }

    private void ImpostaStato(string? stato)
    {
        if (Stato == stato)
        {
            return;
        }

        Stato = stato;
        NumeroPagina = 1;
        OnPropertyChanged(nameof(FiltroTutti));
        OnPropertyChanged(nameof(FiltroAttivi));
        OnPropertyChanged(nameof(FiltroDismessi));
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
    private Task ApriAsync(RigaImpianto riga) => shell.ApriImpiantoAsync(riga.Id);
}
