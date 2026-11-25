using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Desktop.Views;

public partial class SchedaInterventoView : UserControl
{
    public SchedaInterventoView() => InitializeComponent();

    // Il PDF arriva dal server; dove salvarlo lo sceglie l'utente con il selettore del sistema.
    private async void ScaricaRapportino_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SchedaInterventoViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } archivio)
        {
            return;
        }

        if (await vm.RapportinoAsync() is not { } pdf)
        {
            return;
        }

        var file = await archivio.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Testi.ScaricaRapportino,
            SuggestedFileName = vm.NomeRapportino,
            DefaultExtension = "pdf",
            FileTypeChoices = [new FilePickerFileType(Testi.DocumentoPdf) { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"] }],
        });
        if (file is null)
        {
            return;
        }

        await using (var flusso = await file.OpenWriteAsync())
        {
            await flusso.WriteAsync(pdf);
        }

        vm.SegnalaRapportinoSalvato(file.TryGetLocalPath() ?? file.Name);
    }
}
