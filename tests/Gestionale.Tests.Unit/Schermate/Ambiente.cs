using System.Net;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Gestionale.Desktop;
using Gestionale.Desktop.Servizi;
using Gestionale.Desktop.ViewModels;
using Gestionale.Desktop.Views;

[assembly: AvaloniaTestApplication(typeof(Gestionale.Tests.Unit.Schermate.AppDiProva))]

namespace Gestionale.Tests.Unit.Schermate;

/// <summary>L'applicazione vera (stili, temi, viste), disegnata con Skia ma senza finestre sullo schermo.</summary>
public static class AppDiProva
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}

/// <summary>Server finto con dati fittizi: risponde come l'API, senza rete né database.</summary>
internal sealed class ServerDatiFittizi(Func<HttpRequestMessage, object?> rispondi) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(rispondi(request) is { } corpo
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(corpo) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
}

internal sealed class NavigazioneMuta : INavigazione
{
    public void VaiAdAccesso(string? messaggio = null)
    {
    }

    public void VaiAVerificaMfa(string tokenPassaggio, string email)
    {
    }

    public void VaiAConfiguraMfa(string tokenPassaggio, string email)
    {
    }

    public void VaiACodiciRecupero(IReadOnlyList<string> codici)
    {
    }

    public Task VaiAllApplicazioneAsync() => Task.CompletedTask;
}

/// <summary>Crea la shell con un utente e il server finto, la mostra in una finestra e ne salva le immagini.</summary>
internal static class Schermate
{
    public static string Cartella { get; } = Path.Combine(AppContext.BaseDirectory, "schermate");

    /// <summary>
    /// Esegue la prova sul thread di Avalonia della sessione headless, una per tutto
    /// l'assembly, con l'applicazione indicata da AvaloniaTestApplication.
    /// </summary>
    public static Task Esegui(Func<Task> prova) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AppDiProva).Assembly).Dispatch(async () =>
        {
            await prova();
            return true;
        }, TestContext.Current.CancellationToken);

    public static Task<(ShellViewModel Shell, Window Finestra)> ApriAsync(params Permesso[] permessi) =>
        ApriComeAsync(Guid.NewGuid(), "Anna", "Verdi", "responsabile_tecnico", permessi);

    /// <summary>Come <see cref="ApriAsync"/>, con un utente preciso: serve per vedere ciò che vede un Tecnico.</summary>
    public static async Task<(ShellViewModel Shell, Window Finestra)> ApriComeAsync(Guid utenteId, string nome, string cognome, string ruolo, params Permesso[] permessi)
    {
        var api = new ClientApi(new PortachiaviInMemoria(), TimeProvider.System, indirizzo => new HttpClient(new ServerDatiFittizi(DatiFittizi.Rispondi)) { BaseAddress = indirizzo });
        api.ImpostaServer(new Uri("http://gestionale.locale:5225"));
        await api.AccediAsync("anna.verdi@prova.example", "password-di-prova-lunga");

        var profilo = new Profilo(utenteId, Guid.NewGuid(), "anna.verdi@prova.example", nome, cognome, [ruolo], permessi);
        var shell = new ShellViewModel(api, profilo, new NavigazioneMuta(), new PreferenzaTema(new ArchivioImpostazioniInMemoria(), _ => { }))
        {
            Orologio = new OrologioFisso(new DateTimeOffset(2027, 1, 13, 9, 0, 0, TimeSpan.Zero)),
        };
        var finestra = new Window { Width = 1440, Height = 960, Content = new ShellView(), DataContext = shell };
        finestra.Show();
        return (shell, finestra);
    }

    public static Permesso[] ResponsabileTecnico { get; } =
    [
        new("clienti.lettura", "tutti"),
        new("impianti.lettura", "tutti"),
        new("impianti.scrittura", "tutti"),
        new("interventi.lettura", "tutti"),
        new("documenti.lettura", "tutti"),
        new("documenti.scrittura", "tutti"),
        new("interventi.scrittura", "tutti"),
        new("scadenze.lettura", "tutti"),
        new("scadenze.scrittura", "tutti"),
    ];

    public static Permesso[] Tecnico { get; } =
    [
        new("impianti.lettura", "propri"),
        new("interventi.lettura", "propri"),
        new("interventi.scrittura", "propri"),
        new("scadenze.lettura", "propri"),
    ];

    /// <summary>Salva la finestra in chiaro e in scuro; restituisce i testi visibili per i controlli.</summary>
    public static IReadOnlyList<string> Salva(Window finestra, string nome)
    {
        Directory.CreateDirectory(Cartella);
        foreach (var (tema, suffisso) in new[] { (ThemeVariant.Light, "chiaro"), (ThemeVariant.Dark, "scuro") })
        {
            Avalonia.Application.Current!.RequestedThemeVariant = tema;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var immagine = finestra.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nessun fotogramma disegnato.");
            immagine.Save(Path.Combine(Cartella, $"{nome}-{suffisso}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        return [.. finestra.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "")];
    }

    public static void Attendi()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}

/// <summary>Un orologio fermo: "oggi" è sempre lo stesso giorno nelle schermate.</summary>
internal sealed class OrologioFisso(DateTimeOffset adesso) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => adesso;
}
