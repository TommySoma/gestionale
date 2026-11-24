using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Gestionale.Desktop.ViewModels;

namespace Gestionale.Desktop.Views;

/// <summary>
/// Trascinamento nel calendario: un intervento pianificato (o uno da pianificare)
/// si prende con il mouse e si lascia sul giorno di un tecnico. Qui c'è solo il
/// gesto; cosa succede lo decide <see cref="CalendarioViewModel.SpostaAsync"/>.
/// Chi usa la tastiera ottiene lo stesso risultato con "Pianifica" nella scheda.
/// </summary>
public partial class CalendarioView : UserControl
{
    // Il dato resta nell'applicazione: non passa dagli appunti né da altri programmi.
    private static readonly DataFormat<string> s_formatoIntervento = DataFormat.CreateInProcessFormat<string>("gestionale-intervento");

    // Sotto questa distanza è un clic, non un trascinamento.
    private const double SogliaTrascinamento = 6;

    private PointerPressedEventArgs? _pressione;
    private Point _puntoPressione;
    private Guid _interventoPremuto;
    private Border? _cellaEvidenziata;

    public CalendarioView()
    {
        InitializeComponent();

        // Tunnel: i pulsanti delle voci gestiscono la pressione prima che arrivi qui.
        AddHandler(PointerPressedEvent, Premuto, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, Mosso, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, Rilasciato, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, SopraCella);
        AddHandler(DragDrop.DragLeaveEvent, FuoriCella);
        AddHandler(DragDrop.DropEvent, Lasciato);
    }

    private CalendarioViewModel? Calendario => DataContext as CalendarioViewModel;

    private void Premuto(object? sender, PointerPressedEventArgs e)
    {
        _pressione = null;
        if (Calendario is not { PuoPianificare: true } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var id = Contenitore(e.Source)?.DataContext switch
        {
            VoceCalendario { Trascinabile: true } voce => voce.Id,
            VoceDaPianificare voce => voce.Id,
            _ => (Guid?)null,
        };

        if (id is { } interventoId)
        {
            (_pressione, _puntoPressione, _interventoPremuto) = (e, e.GetPosition(this), interventoId);
        }
    }

    private async void Mosso(object? sender, PointerEventArgs e)
    {
        if (_pressione is not { } pressione)
        {
            return;
        }

        var spostamento = e.GetPosition(this) - _puntoPressione;
        if (Math.Abs(spostamento.X) < SogliaTrascinamento && Math.Abs(spostamento.Y) < SogliaTrascinamento)
        {
            return;
        }

        _pressione = null;
        var dati = new DataTransfer();
        dati.Add(DataTransferItem.Create(s_formatoIntervento, _interventoPremuto.ToString()));
        await DragDrop.DoDragDropAsync(pressione, dati, DragDropEffects.Move);
        Evidenzia(null);
    }

    private void Rilasciato(object? sender, PointerReleasedEventArgs e) => _pressione = null;

    private void SopraCella(object? sender, DragEventArgs e)
    {
        var cella = Cella(e.Source);
        var valido = cella is not null && e.DataTransfer.Contains(s_formatoIntervento);
        e.DragEffects = valido ? DragDropEffects.Move : DragDropEffects.None;
        Evidenzia(valido ? cella : null);
        e.Handled = true;
    }

    private void FuoriCella(object? sender, DragEventArgs e) => Evidenzia(null);

    private async void Lasciato(object? sender, DragEventArgs e)
    {
        Evidenzia(null);
        if (Cella(e.Source)?.DataContext is not CellaCalendario cella
            || e.DataTransfer.TryGetValue(s_formatoIntervento) is not { } testo
            || !Guid.TryParse(testo, out var interventoId)
            || Calendario is not { } calendario)
        {
            return;
        }

        e.Handled = true;
        await calendario.SpostaAsync(interventoId, cella.TecnicoUtenteId, cella.Giorno);
    }

    private void Evidenzia(Border? cella)
    {
        if (ReferenceEquals(cella, _cellaEvidenziata))
        {
            return;
        }

        _cellaEvidenziata?.Classes.Remove("bersaglio");
        cella?.Classes.Add("bersaglio");
        _cellaEvidenziata = cella;
    }

    private static Control? Contenitore(object? origine) =>
        (origine as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .FirstOrDefault(c => c.DataContext is VoceCalendario or VoceDaPianificare);

    private static Border? Cella(object? origine) =>
        (origine as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("cella"));
}
