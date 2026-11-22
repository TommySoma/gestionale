using CommunityToolkit.Mvvm.ComponentModel;
using Gestionale.Desktop.Risorse;

namespace Gestionale.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Messaggio { get; set; } = Testi.MessaggioBenvenuto;
}
