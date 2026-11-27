using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.Servizi;

namespace Gestionale.Desktop.ViewModels;

public sealed record RigaSegnalazione(string Riga, string Messaggio);

/// <summary>
/// Importazione di clienti, sedi e impianti da CSV (ADR 0007). Si sceglie il file,
/// l'anteprima dice cosa succederà e quali righe correggere, e si importa solo un
/// file senza errori: tutto o niente.
/// </summary>
public partial class ImportazioneViewModel(ShellViewModel shell) : ViewModelBase
{
    public const long DimensioneMassima = 5 * 1024 * 1024;

    private ModuloFile? _file;

    public ObservableCollection<RigaSegnalazione> Errori { get; } = [];

    public ObservableCollection<RigaSegnalazione> Avvisi { get; } = [];

    public ObservableCollection<CampoScheda> Riepilogo { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaFile))]
    public partial string? NomeFile { get; set; }

    public bool HaFile => NomeFile is not null;

    [ObservableProperty]
    public partial bool HaAnteprima { get; set; }

    [ObservableProperty]
    public partial bool PuoImportare { get; set; }

    [ObservableProperty]
    public partial bool Importata { get; set; }

    [ObservableProperty]
    public partial bool InCorso { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaErrore))]
    public partial string? Errore { get; set; }

    public bool HaErrore => !string.IsNullOrEmpty(Errore);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaAvviso))]
    public partial string? Avviso { get; set; }

    public bool HaAvviso => !string.IsNullOrEmpty(Avviso);

    public bool HaErroriRighe => Errori.Count > 0;

    public bool HaAvvisiRighe => Avvisi.Count > 0;

    /// <summary>Un file scelto dall'utente: parte subito l'anteprima.</summary>
    public async Task AnalizzaAsync(byte[] dati, string nome)
    {
        ArgumentNullException.ThrowIfNull(dati);

        _file = new ModuloFile(dati, nome, null);
        NomeFile = nome;
        Importata = false;
        Avviso = null;
        await EseguiAsync(conferma: false);
    }

    public void SegnalaFileTroppoGrande(string nome) =>
        Errore = string.Format(CultureInfo.CurrentCulture, Testi.FileImportazioneTroppoGrande, nome);

    /// <summary>Il file di esempio con le colonne riconosciute; il salvataggio lo fa la vista.</summary>
    public async Task<byte[]?> ModelloAsync()
    {
        var risposta = await shell.Api.ModelloImportazioneAsync();
        Errore = risposta.Valore is null ? risposta.Messaggio : null;
        return risposta.Valore;
    }

    public void SegnalaModelloSalvato(string percorso) =>
        Avviso = string.Format(CultureInfo.CurrentCulture, Testi.ModelloImportazioneSalvato, percorso);

    [RelayCommand]
    private Task ImportaAsync() => EseguiAsync(conferma: true);

    [RelayCommand]
    private Task IndietroAsync() => shell.ApriClientiAsync();

    private async Task EseguiAsync(bool conferma)
    {
        if (_file is not { } file || InCorso)
        {
            return;
        }

        InCorso = true;
        Errore = null;
        try
        {
            var risposta = conferma ? await shell.Api.ImportaAsync(file) : await shell.Api.AnteprimaImportazioneAsync(file);
            if (risposta.Valore is not { } esito)
            {
                Errore = risposta.Messaggio;
                HaAnteprima = false;
                PuoImportare = false;
                return;
            }

            Mostra(esito);
            if (conferma && esito.Importata)
            {
                Importata = true;
                PuoImportare = false;
                Avviso = Testi.ImportazioneCompletata;
            }
        }
        finally
        {
            InCorso = false;
        }
    }

    private void Mostra(EsitoImportazione esito)
    {
        Riepilogo.Clear();
        Riepilogo.Add(new(Testi.RigheLette, Numero(esito.Righe)));
        Riepilogo.Add(new(Testi.ClientiNuovi, Numero(esito.ClientiNuovi)));
        Riepilogo.Add(new(Testi.ClientiGiaPresenti, Numero(esito.ClientiEsistenti)));
        Riepilogo.Add(new(Testi.SediNuove, Numero(esito.SediNuove)));
        Riepilogo.Add(new(Testi.ImpiantiNuovi, Numero(esito.ImpiantiNuovi)));
        Riepilogo.Add(new(Testi.ImpiantiSaltati, Numero(esito.ImpiantiSaltati)));
        Riepilogo.Add(new(Testi.GeneratoriNuovi, Numero(esito.Generatori)));

        Errori.Clear();
        foreach (var e in esito.Errori)
        {
            Errori.Add(new(Riga(e.Riga), e.Messaggio));
        }

        Avvisi.Clear();
        foreach (var a in esito.Avvisi)
        {
            Avvisi.Add(new(Riga(a.Riga), a.Messaggio));
        }

        HaAnteprima = true;
        PuoImportare = esito.Errori.Count == 0 && !esito.Importata;
        OnPropertyChanged(nameof(HaErroriRighe));
        OnPropertyChanged(nameof(HaAvvisiRighe));
    }

    private static string Numero(int n) => n.ToString(CultureInfo.CurrentCulture);

    // La riga 0 è l'intestazione del file.
    private static string Riga(int riga) => riga == 0
        ? Testi.Intestazione
        : string.Format(CultureInfo.CurrentCulture, Testi.RigaNumero, riga);
}
