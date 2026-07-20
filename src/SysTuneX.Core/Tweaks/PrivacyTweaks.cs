using Microsoft.Win32;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;

namespace SysTuneX.Core.Tweaks;

public static class PrivacyTweaks
{
    public record TweakDefinition(
        string Id,
        string Name,
        string Description,
        RiskLevel Risk,
        string KeyPath,
        string ValueName,
        object EnabledValue,
        object DisabledValue,
        RegistryValueKind ValueKind
    );

    public static readonly TweakDefinition[] All =
    [
        new("telemetry", "Disable Telemetry", "Stops Windows from sending diagnostic data to Microsoft",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, 1, RegistryValueKind.DWord),

        new("advertising_id", "Disable Advertising ID", "Stops personalized ads across Windows apps",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1, RegistryValueKind.DWord),

        new("activity_history", "Disable Activity History", "Stops tracking your app and browsing history",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0, 1, RegistryValueKind.DWord),

        new("location", "Disable Location Tracking", "Turns off location services system-wide",
            RiskLevel.Moderate,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1, 0, RegistryValueKind.DWord),

        new("copilot", "Disable Copilot", "Removes Windows Copilot",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, 0, RegistryValueKind.DWord),

        new("recall", "Disable Recall", "Disables Windows Recall AI feature",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, 0, RegistryValueKind.DWord),

        new("feedback", "Disable Feedback Requests", "Stops Windows from asking for feedback",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, 1, RegistryValueKind.DWord),

        new("tailored_experiences", "Disable Tailored Experiences", "Stops Microsoft from using diagnostic data for suggestions",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1, RegistryValueKind.DWord),

        new("cloud_content", "Disable Cloud Content", "Disables Windows Spotlight and cloud-based suggestions",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, 0, RegistryValueKind.DWord),
    ];

    public static readonly string[] TelemetryHosts =
    [
        "vortex.data.microsoft.com",
        "vortex-win.data.microsoft.com",
        "telecommand.telemetry.microsoft.com",
        "telecommand.telemetry.microsoft.com.nsatc.net",
        "oca.telemetry.microsoft.com",
        "sqm.telemetry.microsoft.com",
        "watson.telemetry.microsoft.com",
        "redir.metaservices.microsoft.com",
        "choice.microsoft.com",
        "choice.microsoft.com.nsatc.net",
        "df.telemetry.microsoft.com",
        "reports.wes.df.telemetry.microsoft.com",
        "settings-sandbox.data.microsoft.com",
        "watson.microsoft.com",
    ];

    public static TweakItem ToTweakItem(TweakDefinition def) => new()
    {
        Id = def.Id,
        Name = def.Name,
        Description = def.Description,
        Category = TweakCategory.Privacy,
        Risk = def.Risk,
    };

    public static TweakStatus CheckStatus(TweakDefinition def, IRegistryService registry)
    {
        var value = registry.GetValue(def.KeyPath, def.ValueName);
        if (value == null) return TweakStatus.NotApplied;
        return value.ToString() == def.EnabledValue.ToString()
            ? TweakStatus.Applied
            : TweakStatus.NotApplied;
    }

    public static bool Apply(TweakDefinition def, IRegistryService registry) =>
        registry.SetValue(def.KeyPath, def.ValueName, def.EnabledValue, def.ValueKind);

    public static bool Revert(TweakDefinition def, IRegistryService registry) =>
        registry.SetValue(def.KeyPath, def.ValueName, def.DisabledValue, def.ValueKind);
}
