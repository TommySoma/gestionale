using Avalonia;
using Avalonia.Controls;

namespace Gestionale.Desktop.Views;

public partial class PannelloMarca : UserControl
{
    public static readonly StyledProperty<string?> TitoloProperty =
        AvaloniaProperty.Register<PannelloMarca, string?>(nameof(Titolo));

    public static readonly StyledProperty<string?> SottotitoloProperty =
        AvaloniaProperty.Register<PannelloMarca, string?>(nameof(Sottotitolo));

    public PannelloMarca() => InitializeComponent();

    public string? Titolo
    {
        get => GetValue(TitoloProperty);
        set => SetValue(TitoloProperty, value);
    }

    public string? Sottotitolo
    {
        get => GetValue(SottotitoloProperty);
        set => SetValue(SottotitoloProperty, value);
    }
}
