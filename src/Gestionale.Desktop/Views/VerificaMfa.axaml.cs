using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Gestionale.Desktop.Views;

public partial class VerificaMfaView : UserControl
{
    public VerificaMfaView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        CampoCodice.Focus();
    }
}
