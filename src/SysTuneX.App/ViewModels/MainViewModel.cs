using CommunityToolkit.Mvvm.ComponentModel;

namespace SysTuneX.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _appVersion = "v1.0.0";
}
