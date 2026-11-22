using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;

namespace Gestionale.Desktop.ViewModels;

public sealed record RigaCliente(Guid Id, string Nome, string Tipo, string Codice, string Comune);

public static class TipiCliente
{
    public static string Nome(string tipo) => tipo switch
    {
        "privato" => Testi.TipoPrivato,
        "condominio" => Testi.TipoCondominio,
        "azienda" => Testi.TipoAzienda,
        _ => tipo,
    };
}

/// <summary>Elenco dei clienti con ricerca, filtro per tipo e pagine.</summary>
public partial class ClientiViewModel(ShellViewModel shell) : ViewModelBase
{
    public const int DimensionePagina = 25;
    private static readonly TimeSpan s_attesaRicerca = TimeSpan.FromMilliseconds(300);
    private CancellationTokenSource? _ricerca;

    public ObservableCollection<RigaCliente> Righe { get; } = [];

    public bool PuoCreare => shell.ModificaClienti;

    [ObservableProperty]
    public partial string Testo { get; set; } = "";

    [ObservableProperty]
    public partial string? Tipo { get; set; }

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

    // Il Tecnico vede solo i propri clienti, che arrivano con gli interventi.
    public string MessaggioVuoto => shell.Profilo.Permessi.Any(p => p.Codice == "clienti.lettura" && p.Ambito == "propri")
        ? Testi.NessunClienteTecnico
        : Testi.NessunCliente;

    public string Conteggio => Totale == 1
        ? Testi.ConteggioUnCliente
        : string.Format(CultureInfo.CurrentCulture, Testi.ConteggioClienti, Totale);

    public string Intervallo => Totale == 0
        ? ""
        : string.Format(CultureInfo.CurrentCulture, Testi.IntervalloPagina,
            ((NumeroPagina - 1) * DimensionePagina) + 1, Math.Min(NumeroPagina * DimensionePagina, Totale), Totale);

    public bool PuoPrecedente => NumeroPagina > 1;

    public bool PuoSuccessiva => NumeroPagina * DimensionePagina < Totale;

    public bool FiltroTutti { get => Tipo is null; set { if (value) { ImpostaTipo(null); } } }

    public bool FiltroPrivati { get => Tipo == "privato"; set { if (value) { ImpostaTipo("privato"); } } }

    public bool FiltroCondomini { get => Tipo == "condominio"; set { if (value) { ImpostaTipo("condominio"); } } }

    public bool FiltroAziende { get => Tipo == "azienda"; set { if (value) { ImpostaTipo("azienda"); } } }

    public async Task CaricaAsync(CancellationToken ct = default)
    {
        Caricamento = true;
        Errore = null;
        try
        {
            var risposta = await shell.Api.CercaClientiAsync(Testo, Tipo, NumeroPagina, DimensionePagina, ct);
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
            foreach (var c in pagina.Elementi)
            {
                Righe.Add(new RigaCliente(c.ClienteId, c.NomeVisualizzato, TipiCliente.Nome(c.Tipo),
                    c.CodiceFiscale ?? c.PartitaIva ?? "—", c.ComunePrincipale ?? "—"));
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

    private void ImpostaTipo(string? tipo)
    {
        if (Tipo == tipo)
        {
            return;
        }

        Tipo = tipo;
        NumeroPagina = 1;
        OnPropertyChanged(nameof(FiltroTutti));
        OnPropertyChanged(nameof(FiltroPrivati));
        OnPropertyChanged(nameof(FiltroCondomini));
        OnPropertyChanged(nameof(FiltroAziende));
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
    private Task ApriAsync(RigaCliente riga) => shell.ApriClienteAsync(riga.Id);

    [RelayCommand]
    private void Nuovo() => shell.MostraDialogo(new EditorClienteViewModel(shell, null, id => _ = shell.ApriClienteAsync(id)));
}
