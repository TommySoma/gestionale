using Avalonia;

namespace Gestionale.Desktop;

internal sealed class Program
{
    // Prima di AppMain non usare API di Avalonia o codice che dipende dal SynchronizationContext.
    [STAThread]
    public static void Main(string[] args)
    {
        // L'interfaccia è in italiano: date e numeri si leggono e si scrivono all'italiana
        // anche su un sistema in un'altra lingua.
        var italiano = System.Globalization.CultureInfo.GetCultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = italiano;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = italiano;
        System.Globalization.CultureInfo.CurrentCulture = italiano;
        System.Globalization.CultureInfo.CurrentUICulture = italiano;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Usato anche dal designer visuale: non rimuovere.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
