using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Desktop.Views;

/// <summary>
/// Scheda impianto. Qui solo ciò che tocca il sistema operativo: scegliere un
/// file da caricare, aprire un documento con il programma predefinito, salvarne
/// una copia. Il resto è nel ViewModel.
/// </summary>
public partial class SchedaImpiantoView : UserControl
{
    private static readonly FilePickerFileType s_ammessi = new(Testi.FotoEDocumenti)
    {
        Patterns = ["*.jpg", "*.jpeg", "*.png", "*.pdf"],
        MimeTypes = ["image/jpeg", "image/png", "application/pdf"],
        AppleUniformTypeIdentifiers = ["public.jpeg", "public.png", "com.adobe.pdf"],
    };

    public SchedaImpiantoView() => InitializeComponent();

    private SchedaImpiantoViewModel? Scheda => DataContext as SchedaImpiantoViewModel;

    private async void Carica_Click(object? sender, RoutedEventArgs e)
    {
        if (Scheda is not { } scheda || TopLevel.GetTopLevel(this)?.StorageProvider is not { } archivio)
        {
            return;
        }

        var scelti = await archivio.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Testi.CaricaDocumento,
            AllowMultiple = true,
            FileTypeFilter = [s_ammessi],
        });

        foreach (var file in scelti)
        {
            // Un file troppo grande non si legge nemmeno: il ViewModel lo dice all'utente.
            var proprieta = await file.GetBasicPropertiesAsync();
            if (proprieta.Size > (ulong)SchedaImpiantoViewModel.DimensioneMassimaDocumento)
            {
                scheda.SegnalaFileTroppoGrande(file.Name);
                continue;
            }

            await using var flusso = await file.OpenReadAsync();
            using var copia = new MemoryStream();
            await flusso.CopyToAsync(copia);
            await scheda.CaricaDocumentoAsync(copia.ToArray(), file.Name);
        }
    }

    // Si apre una copia in una cartella temporanea dell'applicazione, con il programma predefinito.
    private async void Apri_Click(object? sender, RoutedEventArgs e)
    {
        if (Scheda is not { } scheda || (sender as Control)?.DataContext is not RigaDocumento riga
            || TopLevel.GetTopLevel(this)?.Launcher is not { } avvio)
        {
            return;
        }

        if (await scheda.ContenutoDocumentoAsync(riga) is not { } dati)
        {
            return;
        }

        var cartella = Path.Combine(Path.GetTempPath(), "Gestionale", riga.Dati.DocumentoId.ToString("N"));
        Directory.CreateDirectory(cartella);
        var percorso = Path.Combine(cartella, Path.GetFileName(riga.Dati.NomeOriginale));
        await File.WriteAllBytesAsync(percorso, dati);
        await avvio.LaunchFileInfoAsync(new FileInfo(percorso));
    }

    private async void Salva_Click(object? sender, RoutedEventArgs e)
    {
        if (Scheda is not { } scheda || (sender as Control)?.DataContext is not RigaDocumento riga
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } archivio)
        {
            return;
        }

        if (await scheda.ContenutoDocumentoAsync(riga) is not { } dati)
        {
            return;
        }

        var file = await archivio.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Testi.SalvaCopia,
            SuggestedFileName = riga.Dati.NomeOriginale,
            FileTypeChoices = [s_ammessi],
        });
        if (file is null)
        {
            return;
        }

        await using (var flusso = await file.OpenWriteAsync())
        {
            await flusso.WriteAsync(dati);
        }

        scheda.SegnalaDocumentoSalvato(file.TryGetLocalPath() ?? file.Name);
    }
}
