using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>Window chrome state: the title, the banners along the top, and the restart prompt.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IEnvironmentService _environment;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private string _windowsDescription = string.Empty;

    [ObservableProperty]
    private bool _isRestartPending;

    public MainWindowViewModel(IEnvironmentService environment, ILocalizationService localization)
    {
        _environment = environment;
        _localization = localization;

        WindowsVersionInfo windows = environment.Windows;
        IsElevated = environment.IsElevated;
        IsOsSupported = windows.IsSupported;
        WindowsDescription = windows.ToString();

        _localization.LanguageChanged += (_, _) => OnPropertyChanged(nameof(ApplicationTitle));
    }

    public bool IsElevated { get; }

    public bool IsOsSupported { get; }

    public string ApplicationTitle => $"SysTuneX — {_localization["App_Tagline"]}";

    public string Version =>
        typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";

    /// <summary>Raised by any page that applied a change needing a reboot.</summary>
    public void NotifyRestartRequired() => IsRestartPending = true;

    [RelayCommand]
    private void RestartElevated()
    {
        OperationResult result = _environment.RestartElevated();

        if (result.Success)
        {
            System.Windows.Application.Current.Shutdown();
        }
    }

    [RelayCommand]
    private static void RestartWindows() =>
        _ = SysTuneX.Core.Services.ProcessRunner.RunAsync("shutdown.exe", "/r /t 5");

    [RelayCommand]
    private void DismissRestartBanner() => IsRestartPending = false;
}
