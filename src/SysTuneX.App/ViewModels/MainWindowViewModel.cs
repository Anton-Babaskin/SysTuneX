using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>Window chrome state: the title, the banners along the top, and the restart prompt.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IEnvironmentService _environment;
    private readonly ILocalizationService _localization;
    private readonly IUserInteraction _interaction;
    private readonly IAppLifetime _lifetime;

    [ObservableProperty]
    private string _windowsDescription = string.Empty;

    [ObservableProperty]
    private bool _isRestartPending;

    public MainWindowViewModel(
        IEnvironmentService environment,
        ILocalizationService localization,
        IUserInteraction interaction,
        IAppLifetime lifetime)
    {
        _environment = environment;
        _localization = localization;
        _interaction = interaction;
        _lifetime = lifetime;

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
            _lifetime.Shutdown();
        }
    }

    /// <summary>
    /// Reboots the machine.
    ///
    /// The command lives here because the button does; the reboot itself does not. It used to be a
    /// static call to a process runner from inside this view model - the most consequential thing
    /// the app can do, in the layer whose job is to describe a screen, and unreachable from any
    /// test. It is <see cref="IEnvironmentService"/>'s business now, alongside relaunching elevated
    /// and restarting the shell.
    /// </summary>
    [RelayCommand]
    private async Task RestartWindowsAsync()
    {
        OperationResult result = await _environment.RestartWindowsAsync().ConfigureAwait(true);

        if (!result.Success)
        {
            _interaction.ShowError(result.Describe(_localization));
        }
    }

    [RelayCommand]
    private void DismissRestartBanner() => IsRestartPending = false;
}
