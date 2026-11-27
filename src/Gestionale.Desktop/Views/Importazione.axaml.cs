using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Desktop.Views;

public partial class ImportazioneView : UserControl
{
    private static readonly FilePickerFileType s_csv = new(Testi.FileCsv) { Patterns = ["*.csv", "*.txt"], MimeTypes = ["text/csv", "text/plain"] };

    public ImportazioneView() => InitializeComponent();

    private async void ScegliFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportazioneViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } archivio)
        {
            return;
        }

        var scelti = await archivio.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Testi.ScegliFileCsv, FileTypeFilter = [s_csv] });
        if (scelti.Count == 0)
        {
            return;
        }

        var file = scelti[0];
        // Un file troppo grande non si legge nemmeno: il ViewModel lo dice all'utente.
        var proprieta = await file.GetBasicPropertiesAsync();
        if (proprieta.Size > (ulong)ImportazioneViewModel.DimensioneMassima)
        {
            vm.SegnalaFileTroppoGrande(file.Name);
            return;
        }

        await using var flusso = await file.OpenReadAsync();
        using var copia = new MemoryStream();
        await flusso.CopyToAsync(copia);
        await vm.AnalizzaAsync(copia.ToArray(), file.Name);
    }

    private async void ScaricaModello_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportazioneViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } archivio)
        {
            return;
        }

        if (await vm.ModelloAsync() is not { } contenuto)
        {
            return;
        }

        var file = await archivio.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Testi.ScaricaModelloCsv,
            SuggestedFileName = "modello-importazione.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [s_csv],
        });
        if (file is null)
        {
            return;
        }

        await using (var flusso = await file.OpenWriteAsync())
        {
            await flusso.WriteAsync(contenuto);
        }

        vm.SegnalaModelloSalvato(file.TryGetLocalPath() ?? file.Name);
    }
}
