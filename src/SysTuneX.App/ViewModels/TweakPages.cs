using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

public sealed class GamingViewModel : TweakPageViewModel
{
    public GamingViewModel(
        ITweakEngine tweaks,
        IEnvironmentService environment,
        IUserInteraction interaction,
        ILocalizationService localization,
        CatalogText text,
        IAppSettingsService settings)
        : base(tweaks, environment, interaction, localization, text, settings)
    {
    }

    protected override TweakCategory Category => TweakCategory.Gaming;
}

public sealed class Windows11ViewModel : TweakPageViewModel
{
    public Windows11ViewModel(
        ITweakEngine tweaks,
        IEnvironmentService environment,
        IUserInteraction interaction,
        ILocalizationService localization,
        CatalogText text,
        IAppSettingsService settings)
        : base(tweaks, environment, interaction, localization, text, settings)
    {
        WindowsVersionInfo windows = environment.Windows;
        IsWindows11 = windows.IsWindows11;
        WindowsDescription = windows.ToString();
    }

    protected override TweakCategory Category => TweakCategory.Windows11;

    /// <summary>Windows 10 machines see a note explaining why most of this page is empty.</summary>
    public bool IsWindows11 { get; }

    public string WindowsDescription { get; }
}

public sealed class PrivacyViewModel : TweakPageViewModel
{
    public PrivacyViewModel(
        ITweakEngine tweaks,
        IEnvironmentService environment,
        IUserInteraction interaction,
        ILocalizationService localization,
        CatalogText text,
        IAppSettingsService settings,
        IPrivacyService privacy)
        : base(tweaks, environment, interaction, localization, text, settings)
    {
        Hosts = new HostsBlockViewModel(privacy, interaction, localization);
    }

    protected override TweakCategory Category => TweakCategory.Privacy;

    public HostsBlockViewModel Hosts { get; }

    protected override async Task OnEnterAsync()
    {
        await base.OnEnterAsync().ConfigureAwait(true);
        Hosts.Refresh();
    }
}
