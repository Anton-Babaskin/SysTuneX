using Microsoft.Win32;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tweaks;

/// <summary>
/// Telemetry and data-collection controls. These are documented Windows policies, not
/// undocumented switches, so they survive feature updates and can be inspected with gpedit.
/// </summary>
public static class PrivacyTweaks
{
    private const string DataCollectionPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    private const string SystemPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System";
    private const string ContentDelivery = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public static IReadOnlyList<TweakDefinition> All { get; } =
    [
        new()
        {
            Id = "telemetry_minimize",
            Category = TweakCategory.Privacy,
            GroupKey = "Group_Telemetry",
            Name = "Reduce diagnostic data to the minimum",
            Description =
                "Sets the diagnostic data policy to its lowest level. On Home and Pro, Windows treats " +
                "0 as Required rather than Off - only Enterprise and Education honour a full zero. " +
                "SysTuneX writes the policy either way and does not pretend it disables telemetry.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(DataCollectionPolicy, "AllowTelemetry", 0, null, RegistryValueKind.DWord),
                new(DataCollectionPolicy, "MaxTelemetryAllowed", 1, null, RegistryValueKind.DWord),
                new(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "advertising_id_disable",
            Category = TweakCategory.Privacy,
            GroupKey = "Group_Telemetry",
            Name = "Disable the advertising ID",
            Description = "Stops apps reading the per-user identifier Windows hands out for ad targeting.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1, RegistryValueKind.DWord),
                new(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "activity_history_disable",
            Category = TweakCategory.Privacy,
            GroupKey = "Group_Telemetry",
            Name = "Disable activity history",
            Description = "Stops Windows recording which apps and files you open and uploading that timeline.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(SystemPolicy, "EnableActivityFeed", 0, null, RegistryValueKind.DWord),
                new(SystemPolicy, "PublishUserActivities", 0, null, RegistryValueKind.DWord),
                new(SystemPolicy, "UploadUserActivities", 0, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "feedback_requests_disable",
            Category = TweakCategory.Privacy,
            GroupKey = "Group_Telemetry",
            Name = "Stop feedback prompts",
            Description = "Turns off the periodic 'how likely are you to recommend Windows' dialogs.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(@"HKCU\SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, null, RegistryValueKind.DWord),
                new(DataCollectionPolicy, "DoNotShowFeedbackNotifications", 1, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "tailored_experiences_disable",
            Category = TweakCategory.Privacy,
            GroupKey = "Group_Telemetry",
            Name = "Disable tailored experiences",
            Description = "Stops Windows using diagnostic data to personalise tips, ads and Start menu suggestions.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1, RegistryValueKind.DWord),
                new(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "start_suggestions_disable",
            Category = TweakCategory.Privacy,
            GroupKey = "Group_Suggestions",
            Name = "Remove Start menu suggestions and auto-installed apps",
            Description =
                "Stops Windows silently installing promoted Store apps and showing sponsored tiles in Start.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(ContentDelivery, "SystemPaneSuggestionsEnabled", 0, 1, RegistryValueKind.DWord),
                new(ContentDelivery, "SilentInstalledAppsEnabled", 0, 1, RegistryValueKind.DWord),
                new(ContentDelivery, "PreInstalledAppsEnabled", 0, 1, RegistryValueKind.DWord),
                new(ContentDelivery, "SubscribedContent-338388Enabled", 0, 1, RegistryValueKind.DWord),
                new(ContentDelivery, "SubscribedContent-338389Enabled", 0, 1, RegistryValueKind.DWord),
                new(ContentDelivery, "SubscribedContent-353698Enabled", 0, 1, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "location_disable",
            Category = TweakCategory.Privacy,
            GroupKey = "Group_Sensors",
            Name = "Deny location access",
            Description =
                "Sets the system-wide location consent to Deny. Turn this back on before using Maps, " +
                "Find my device or weather widgets that need a real position.",
            Risk = RiskLevel.Moderate,
            Changes =
            [
                new(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny", "Allow", RegistryValueKind.String),
            ],
        },

        new()
        {
            Id = "error_reporting_disable",
            Category = TweakCategory.Privacy,
            GroupKey = "Group_Sensors",
            Name = "Disable Windows Error Reporting",
            Description =
                "Stops crash dumps being uploaded to Microsoft. Local dumps still get written, so " +
                "you can still debug a crashing game.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, 0, RegistryValueKind.DWord),
                new(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "cloud_clipboard_disable",
            Category = TweakCategory.Privacy,
            GroupKey = "Group_Sensors",
            Name = "Disable clipboard history and sync",
            Description =
                "Stops the clipboard being kept in memory across copies and synced to your Microsoft " +
                "account. Win+V stops working.",
            Risk = RiskLevel.Moderate,
            Changes =
            [
                new(SystemPolicy, "AllowClipboardHistory", 0, null, RegistryValueKind.DWord),
                new(SystemPolicy, "AllowCrossDeviceClipboard", 0, null, RegistryValueKind.DWord),
            ],
        },
    ];
}

/// <summary>
/// Hosts pointed at 0.0.0.0 when the user turns the hosts block on.
///
/// Deliberately narrow: only endpoints whose sole purpose is telemetry ingestion. Update,
/// activation, Store and Defender endpoints are not on this list, because blocking those
/// breaks Windows in ways that are hard to diagnose later.
/// </summary>
public static class TelemetryHosts
{
    public static IReadOnlyList<string> All { get; } =
    [
        "vortex.data.microsoft.com",
        "vortex-win.data.microsoft.com",
        "telecommand.telemetry.microsoft.com",
        "telecommand.telemetry.microsoft.com.nsatc.net",
        "oca.telemetry.microsoft.com",
        "oca.telemetry.microsoft.com.nsatc.net",
        "sqm.telemetry.microsoft.com",
        "sqm.telemetry.microsoft.com.nsatc.net",
        "watson.telemetry.microsoft.com",
        "watson.telemetry.microsoft.com.nsatc.net",
        "redir.metaservices.microsoft.com",
        "choice.microsoft.com",
        "choice.microsoft.com.nsatc.net",
        "df.telemetry.microsoft.com",
        "reports.wes.df.telemetry.microsoft.com",
        "services.wes.df.telemetry.microsoft.com",
        "sqm.df.telemetry.microsoft.com",
        "telemetry.microsoft.com",
        "watson.ppe.telemetry.microsoft.com",
        "telemetry.appex.bing.net",
        "telemetry.urs.microsoft.com",
        "settings-sandbox.data.microsoft.com",
        "vortex-sandbox.data.microsoft.com",
        "survey.watson.microsoft.com",
        "watson.live.com",
        "statsfe2.ws.microsoft.com",
        "statsfe1.ws.microsoft.com",
        "corpext.msitadfs.glbdns2.microsoft.com",
        "compatexchange.cloudapp.net",
        "cs1.wpc.v0cdn.net",
        "a-0001.a-msedge.net",
        "fe2.update.microsoft.com.akadns.net",
        "diagnostics.support.microsoft.com",
        "feedback.windows.com",
        "feedback.microsoft-hohm.com",
        "feedback.search.microsoft.com",
    ];
}
