using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Gestionale.Desktop.Servizi;
using Gestionale.Desktop.ViewModels;
using Gestionale.Desktop.Views;

namespace Gestionale.Desktop;

public partial class App : Application
{
    private ClientApi? _api;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _api = new ClientApi(Portachiavi.DelSistema(), TimeProvider.System);
            var principale = new MainViewModel(_api, ArchivioImpostazioniSuFile.Predefinito());

            desktop.MainWindow = new MainWindow { DataContext = principale };
            desktop.Exit += (_, _) => _api.Dispose();
            _ = principale.AvviaAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
