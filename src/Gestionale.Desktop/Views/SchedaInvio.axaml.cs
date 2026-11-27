using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Desktop.Views;

public partial class SchedaInvioView : UserControl
{
    public SchedaInvioView() => InitializeComponent();

    // Il file arriva dal server così come è stato preparato; dove salvarlo lo sceglie l'utente.
    private async void ScaricaFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SchedaInvioViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } archivio)
        {
            return;
        }

        if (await vm.FileAsync() is not { } contenuto)
        {
            return;
        }

        var file = await archivio.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Testi.ScaricaFile,
            SuggestedFileName = vm.NomeFile,
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType(Testi.FileDiTesto) { Patterns = ["*.txt"], MimeTypes = ["text/plain"] }],
        });
        if (file is null)
        {
            return;
        }

        await using (var flusso = await file.OpenWriteAsync())
        {
            await flusso.WriteAsync(contenuto);
        }

        vm.SegnalaFileSalvato(file.TryGetLocalPath() ?? file.Name);
    }
}
