using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Gestionale.Desktop.Risorse;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Desktop.Views;

public partial class SchedaPreventivoView : UserControl
{
    public SchedaPreventivoView() => InitializeComponent();

    // Il PDF arriva dal server; dove salvarlo lo sceglie l'utente con il selettore del sistema.
    private async void ScaricaPdf_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SchedaPreventivoViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } archivio)
        {
            return;
        }

        if (await vm.PdfAsync() is not { } pdf)
        {
            return;
        }

        var file = await archivio.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Testi.ScaricaPdf,
            SuggestedFileName = vm.NomePdf,
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

        vm.SegnalaPdfSalvato(file.TryGetLocalPath() ?? file.Name);
    }
}
