using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Desktop.Views;

public partial class ConfiguraMfaView : UserControl
{
    public ConfiguraMfaView() => InitializeComponent();

    // Gli appunti sono un servizio della finestra: si raggiungono dalla vista.
    private async void CopiaChiave_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConfiguraMfaViewModel { Chiave: { } chiave } && TopLevel.GetTopLevel(this)?.Clipboard is { } appunti)
        {
            await appunti.SetTextAsync(chiave.Replace(" ", "", System.StringComparison.Ordinal));
        }
    }
}
