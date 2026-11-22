using Avalonia;

namespace Gestionale.Desktop;

internal sealed class Program
{
    // Prima di AppMain non usare API di Avalonia o codice che dipende dal SynchronizationContext.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Usato anche dal designer visuale: non rimuovere.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
