using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Net.Codecrete.QrCodeGenerator;

namespace Gestionale.Desktop.Controlli;

/// <summary>Disegna un codice QR dal testo, senza immagini intermedie.</summary>
public sealed class CodiceQr : Control
{
    public static readonly StyledProperty<string?> TestoProperty =
        AvaloniaProperty.Register<CodiceQr, string?>(nameof(Testo));

    // Margine di quiete richiesto dallo standard: 4 moduli per lato.
    private const int Margine = 4;

    private QrCode? _codice;

    static CodiceQr()
    {
        AffectsRender<CodiceQr>(TestoProperty);
    }

    public string? Testo
    {
        get => GetValue(TestoProperty);
        set => SetValue(TestoProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TestoProperty)
        {
            _codice = string.IsNullOrEmpty(Testo) ? null : QrCode.EncodeText(Testo, QrCode.Ecc.Medium);
        }
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var lato = Math.Min(Bounds.Width, Bounds.Height);
        context.FillRectangle(Brushes.White, new Rect(0, 0, lato, lato));
        if (_codice is null)
        {
            return;
        }

        var moduli = _codice.Size + (2 * Margine);
        var dimensione = lato / moduli;
        for (var y = 0; y < _codice.Size; y++)
        {
            for (var x = 0; x < _codice.Size; x++)
            {
                if (_codice.GetModule(x, y))
                {
                    context.FillRectangle(Brushes.Black,
                        new Rect((x + Margine) * dimensione, (y + Margine) * dimensione, Math.Ceiling(dimensione), Math.Ceiling(dimensione)));
                }
            }
        }
    }
}
