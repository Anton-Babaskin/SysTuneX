using CommunityToolkit.Mvvm.ComponentModel;

namespace SysTuneX.Core.Models;

public partial class StartupItem : ObservableObject
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Location { get; set; } = ""; // "HKCU" or "HKLM"

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isBusy;
}
