using SysTuneX.Core.Models;

namespace SysTuneX.App.Localization;

/// <summary>
/// Resolves display text for catalog entries defined in SysTuneX.Core.
///
/// Core carries neutral English text so it stays useful and testable on its own; this looks for
/// a translated resource first and falls back to that text, which means a missing translation
/// degrades to English instead of showing a raw resource key.
/// </summary>
public sealed class CatalogText
{
    private readonly ILocalizationService _localization;

    public CatalogText(ILocalizationService localization) => _localization = localization;

    public string Name(TweakDefinition tweak) => _localization.Get($"Tweak_{tweak.Id}_Name", tweak.Name);

    public string Description(TweakDefinition tweak) => _localization.Get($"Tweak_{tweak.Id}_Desc", tweak.Description);

    public string Warning(TweakDefinition tweak) =>
        tweak.WarningKey is null ? string.Empty : _localization.Get(tweak.WarningKey, string.Empty);

    public string Name(GameProfile profile) => _localization.Get($"Profile_{profile.Id}_Name", profile.Name);

    public string Description(GameProfile profile) => _localization.Get($"Profile_{profile.Id}_Desc", profile.Description);

    public string Name(ServiceDefinition service) =>
        _localization.Get($"Service_{service.ServiceName}_Name", service.DisplayName);

    public string Description(ServiceDefinition service) =>
        _localization.Get($"Service_{service.ServiceName}_Desc", service.Description);

    public string Name(CleanupTarget target) => _localization.Get($"Cleanup_{target.Id}_Name", target.Name);

    public string Description(CleanupTarget target) => _localization.Get($"Cleanup_{target.Id}_Desc", target.Description);

    public string Group(string groupKey) => _localization.Get(groupKey, groupKey);

    public string Risk(RiskLevel risk) => risk switch
    {
        RiskLevel.Safe => _localization["Risk_Safe"],
        RiskLevel.Moderate => _localization["Risk_Moderate"],
        _ => _localization["Risk_Advanced"],
    };

    public string Status(TweakStatus status) => status switch
    {
        TweakStatus.Applied => _localization["Common_Applied"],
        TweakStatus.NotApplied => _localization["Common_NotApplied"],
        TweakStatus.Partial => _localization["Common_Partial"],
        TweakStatus.Unsupported => _localization["Common_Unsupported"],
        _ => _localization["Common_Unknown"],
    };

    public string StartMode(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Automatic => _localization["Services_StartMode_Automatic"],
        ServiceStartMode.Manual => _localization["Services_StartMode_Manual"],
        ServiceStartMode.Disabled => _localization["Services_StartMode_Disabled"],
        ServiceStartMode.Boot => _localization["Services_StartMode_Boot"],
        ServiceStartMode.System => _localization["Services_StartMode_System"],
        _ => _localization["Common_Unknown"],
    };

    public string RunningState(bool isRunning) =>
        isRunning ? _localization["Common_Running"] : _localization["Common_Stopped"];

    public string BackupKind(BackupKind kind) => _localization.Get($"History_Kind_{kind}", kind.ToString());
}
