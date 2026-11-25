using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

/// <summary>Formati comuni di listino e preventivi, in italiano.</summary>
public static class FormatoCommerciale
{
    private static readonly CultureInfo s_italiano = CultureInfo.GetCultureInfo("it-IT");

    public static string Tipo(string tipo) => tipo == "manodopera" ? Testi.RigaManodopera : Testi.RigaMateriale;

    public static string Iva(decimal? aliquota) => aliquota is { } a ? $"{a.ToString("0.##", s_italiano)}%" : Testi.NonIndicato;

    /// <summary>"45,00 € dal 1 gennaio 2027", oppure "Senza prezzo".</summary>
    public static string Prezzo(decimal? prezzo, DateOnly? dal) =>
        prezzo is { } p
            ? dal is { } d ? string.Format(s_italiano, Testi.PrezzoDal, FormatoInterventi.Importo(p), FormatoImpianti.Data(d)) : FormatoInterventi.Importo(p)
            : Testi.SenzaPrezzo;

    public static string StatoPreventivo(string stato, bool scaduto) => scaduto ? Testi.StatoPreventivoScaduto : stato switch
    {
        "bozza" => Testi.StatoPreventivoBozza,
        "emesso" => Testi.StatoPreventivoEmesso,
        "accettato" => Testi.StatoPreventivoAccettato,
        "rifiutato" => Testi.StatoPreventivoRifiutato,
        "annullato" => Testi.StatoPreventivoAnnullato,
        _ => stato,
    };

    /// <summary>"Preventivo 7/2027", oppure "Bozza" quando non ha ancora un numero.</summary>
    public static string Numero(int? numero, int? anno) =>
        numero is { } n ? string.Format(s_italiano, Testi.NumeroPreventivo, n, anno) : Testi.StatoPreventivoBozza;

    /// <summary>Numero intero senza segno; vuoto significa nessun valore.</summary>
    public static bool ProvaIntero(string? testo, out int? valore)
    {
        valore = null;
        if (string.IsNullOrWhiteSpace(testo))
        {
            return true;
        }

        if (!int.TryParse(testo.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var numero))
        {
            return false;
        }

        valore = numero;
        return true;
    }
}

public sealed record RigaVoce(VoceListino Dati, string Codice, string Descrizione, string Tipo, string Unita, string Prezzo, string Iva, bool Disattivata);

/// <summary>Listino prezzi con ricerca; chi ha il permesso di scrittura crea voci, le modifica e aggiunge prezzi.</summary>
public partial class ListinoViewModel(ShellViewModel shell) : ViewModelBase
{
    public const int DimensionePagina = 50;
    private static readonly TimeSpan s_attesaRicerca = TimeSpan.FromMilliseconds(300);
    private CancellationTokenSource? _ricerca;

    public ObservableCollection<RigaVoce> Righe { get; } = [];

    public bool GestisceListino => shell.GestisceListino;

    [ObservableProperty]
    public partial string Testo { get; set; } = "";

    [ObservableProperty]
    public partial bool AncheDisattivate { get; set; }

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

    public string Conteggio => Totale == 1
        ? Testi.ConteggioUnaVoce
        : string.Format(CultureInfo.CurrentCulture, Testi.ConteggioVoci, Totale);

    public string Intervallo => Totale == 0
        ? ""
        : string.Format(CultureInfo.CurrentCulture, Testi.IntervalloPagina,
            ((NumeroPagina - 1) * DimensionePagina) + 1, Math.Min(NumeroPagina * DimensionePagina, Totale), Totale);

    public bool PuoPrecedente => NumeroPagina > 1;

    public bool PuoSuccessiva => NumeroPagina * DimensionePagina < Totale;

    public async Task CaricaAsync(CancellationToken ct = default)
    {
        Caricamento = true;
        Errore = null;
        try
        {
            var risposta = await shell.Api.CercaListinoAsync(Testo, AncheDisattivate, NumeroPagina, DimensionePagina, ct);
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
            foreach (var v in pagina.Elementi)
            {
                Righe.Add(new RigaVoce(
                    v, v.Codice, v.Descrizione, FormatoCommerciale.Tipo(v.Tipo), v.Unita,
                    FormatoCommerciale.Prezzo(v.Prezzo, v.PrezzoDal), FormatoCommerciale.Iva(v.AliquotaIva), !v.Attiva));
            }

            Totale = pagina.Totale;
        }
        finally
        {
            Caricamento = false;
            OnPropertyChanged(nameof(Vuoto));
        }
    }

    partial void OnTestoChanged(string value)
    {
        _ricerca?.Cancel();
        _ricerca?.Dispose();
        _ricerca = new CancellationTokenSource();
        _ = CercaDopoUnaPausaAsync(_ricerca.Token);
    }

    partial void OnAncheDisattivateChanged(bool value)
    {
        NumeroPagina = 1;
        _ = CaricaAsync();
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
    private void NuovaVoce() => shell.MostraDialogo(new EditorVoceViewModel(shell, null, () => _ = CaricaAsync()));

    [RelayCommand]
    private void ModificaVoce(RigaVoce riga) => shell.MostraDialogo(new EditorVoceViewModel(shell, riga.Dati, () => _ = CaricaAsync()));

    [RelayCommand]
    private void NuovoPrezzo(RigaVoce riga) => shell.MostraDialogo(new PrezzoVoceViewModel(shell, riga.Dati, () => _ = CaricaAsync()));
}

/// <summary>Nuova voce del listino, con il primo prezzo, oppure modifica (anche per disattivarla).</summary>
public partial class EditorVoceViewModel : DialogoViewModel
{
    private readonly VoceListino? _esistente;
    private readonly Action _salvato;

    public EditorVoceViewModel(ShellViewModel shell, VoceListino? esistente, Action salvato) : base(shell)
    {
        _esistente = esistente;
        _salvato = salvato;
        Tipo = Tipi.First(t => t.Codice == (esistente?.Tipo ?? "materiale"));
        Codice = esistente?.Codice ?? "";
        Descrizione = esistente?.Descrizione ?? "";
        Unita = esistente?.Unita ?? "pz";
        Iva = esistente?.AliquotaIva?.ToString("0.##", CultureInfo.GetCultureInfo("it-IT")) ?? "";
        Attiva = esistente?.Attiva ?? true;
        Note = esistente?.Note ?? "";
    }

    public string Titolo => _esistente is null ? Testi.TitoloNuovaVoce : Testi.TitoloModificaVoce;

    /// <summary>Il primo prezzo si indica alla creazione; dopo, i cambi di prezzo hanno la loro data.</summary>
    public bool ConPrezzoIniziale => _esistente is null;

    public bool InModifica => _esistente is not null;

    public IReadOnlyList<Opzione> Tipi { get; } = [new("materiale", Testi.RigaMateriale), new("manodopera", Testi.RigaManodopera)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Manodopera))]
    public partial Opzione Tipo { get; set; }

    public bool Manodopera => Tipo.Codice == "manodopera";

    [ObservableProperty]
    public partial string Codice { get; set; }

    [ObservableProperty]
    public partial string Descrizione { get; set; }

    [ObservableProperty]
    public partial string Unita { get; set; }

    [ObservableProperty]
    public partial string Iva { get; set; }

    [ObservableProperty]
    public partial bool Attiva { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial string PrezzoIniziale { get; set; } = "";

    // La manodopera si conta in ore: l'unità si fissa da sola.
    partial void OnTipoChanged(Opzione value)
    {
        if (value.Codice == "manodopera")
        {
            Unita = "h";
        }
    }

    protected override async Task<string?> EseguiAsync()
    {
        if (!FormatoImpianti.ProvaDecimale(Iva, out var iva))
        {
            return Testi.IvaNonNumerica;
        }

        if (!FormatoImpianti.ProvaDecimale(PrezzoIniziale, out var prezzo))
        {
            return Testi.PrezzoNonNumerico;
        }

        var dati = new DatiVoce(
            Codice.Trim(), Descrizione.Trim(), Tipo.Codice, Manodopera ? "h" : Unita.Trim(), iva, Attiva, Vuoto(Note),
            ConPrezzoIniziale ? prezzo : null, null, _esistente?.Versione);

        var risposta = _esistente is null
            ? await Shell.Api.CreaVoceAsync(dati)
            : await Shell.Api.AggiornaVoceAsync(_esistente.VoceId, dati);
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => _salvato();
}

/// <summary>Nuovo prezzo di una voce, valido da una data: preventivi e interventi già fatti non cambiano.</summary>
public partial class PrezzoVoceViewModel(ShellViewModel shell, VoceListino voce, Action salvato) : DialogoViewModel(shell)
{
    public string Descrizione { get; } = $"{voce.Codice} · {voce.Descrizione}";

    public string PrezzoAttuale { get; } = FormatoCommerciale.Prezzo(voce.Prezzo, voce.PrezzoDal);

    [ObservableProperty]
    public partial string Prezzo { get; set; } = "";

    [ObservableProperty]
    public partial DateTime? ValidoDal { get; set; } = FormatoImpianti.PerSelettore(FormatoInterventi.Oggi(shell.Orologio));

    protected override async Task<string?> EseguiAsync()
    {
        if (!FormatoImpianti.ProvaDecimale(Prezzo, out var prezzo) || prezzo is null)
        {
            return Testi.PrezzoNonNumerico;
        }

        var risposta = await Shell.Api.AggiungiPrezzoAsync(voce.VoceId, new DatiPrezzo(prezzo.Value, FormatoImpianti.DaSelettore(ValidoDal)));
        return risposta.Riuscita ? null : risposta.Messaggio;
    }

    protected override void DopoIlSalvataggio() => salvato();
}
