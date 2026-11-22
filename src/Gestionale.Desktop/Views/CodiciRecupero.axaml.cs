using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Desktop.Views;

public partial class CodiciRecuperoView : UserControl
{
    public CodiciRecuperoView() => InitializeComponent();

    private async void Copia_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodiciRecuperoViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } appunti)
        {
            await appunti.SetTextAsync(vm.TestoDaCopiare);
            vm.SegnalaCopiati();
        }
    }
}
