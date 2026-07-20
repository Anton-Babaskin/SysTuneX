using Microsoft.Win32;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;

namespace SysTuneX.Core.Tweaks;

/// <summary>
/// Windows 11-specific tweaks targeting AI features, VBS/HVCI security overhead,
/// and other Win11 exclusive features that cause FPS drops.
/// </summary>
public static class Windows11Tweaks
{
    public record TweakDefinition(
        string Id,
        string Name,
        string Description,
        string Category,
        RiskLevel Risk,
        string KeyPath,
        string ValueName,
        object EnabledValue,
        object DisabledValue,
        RegistryValueKind ValueKind,
        bool RequiresRestart = false
    );

    // ────────────────────────────────────────────────────────────────────────────
    //  SECURITY / VIRTUALIZATION (biggest FPS impact)
    // ────────────────────────────────────────────────────────────────────────────

    public static readonly TweakDefinition[] SecurityTweaks =
    [
        new("vbs_disable",
            "Disable VBS (Virtualization-Based Security)",
            "Removes the hypervisor security layer — recovers 5-15% FPS across all games. Requires restart.",
            "Security",
            RiskLevel.Advanced,
            @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard",
            "EnableVirtualizationBasedSecurity",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord,
            RequiresRestart: true),

        new("hvci_disable",
            "Disable HVCI / Memory Integrity",
            "Disables Hypervisor-Protected Code Integrity — removes kernel overhead, up to 10 FPS gain in CPU-heavy games. Requires restart.",
            "Security",
            RiskLevel.Advanced,
            @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity",
            "Enabled",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord,
            RequiresRestart: true),
    ];

    // ────────────────────────────────────────────────────────────────────────────
    //  AI FEATURES (GPU/CPU stolen by background ML inference)
    // ────────────────────────────────────────────────────────────────────────────

    public static readonly TweakDefinition[] AiTweaks =
    [
        new("recall_block_hklm",
            "Block Windows Recall (System-wide)",
            "Prevents Recall from ever being enabled — stops AI screenshot indexing that runs ML inference in background",
            "AI",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI",
            "AllowRecallEnablement",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord),

        new("recall_disable_user",
            "Disable Windows Recall (User)",
            "Stops saving Recall AI snapshots for current user — disables the background screenshot + ML analysis loop",
            "AI",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Policies\Microsoft\Windows\WindowsAI",
            "DisableAIDataAnalysis",
            EnabledValue: 1, DisabledValue: 0, RegistryValueKind.DWord),

        new("recall_disable_system",
            "Disable Windows Recall (System)",
            "Machine-wide Recall snapshot disable — no ML model inference in background",
            "AI",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI",
            "DisableAIDataAnalysis",
            EnabledValue: 1, DisabledValue: 0, RegistryValueKind.DWord),

        new("click_to_do",
            "Disable Click To Do",
            "Disables AI-powered screen action suggestions that process screen content in real time",
            "AI",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI",
            "DisableClickToDo",
            EnabledValue: 1, DisabledValue: 0, RegistryValueKind.DWord),

        new("ai_settings_agent",
            "Disable AI Settings Agent",
            "Disables the AI assistant inside Windows Settings",
            "AI",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI",
            "DisableSettingsAgent",
            EnabledValue: 1, DisabledValue: 0, RegistryValueKind.DWord),

        new("paint_cocreator",
            "Disable Paint AI (Cocreator / Generative Fill)",
            "Disables AI image generation in Paint — prevents background GPU loading",
            "AI",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint",
            "DisableCocreator",
            EnabledValue: 1, DisabledValue: 0, RegistryValueKind.DWord),

        new("copilot_machine",
            "Disable Copilot (System-wide)",
            "Machine-wide Copilot removal — eliminates background PWA process and AI connection",
            "AI",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot",
            "TurnOffWindowsCopilot",
            EnabledValue: 1, DisabledValue: 0, RegistryValueKind.DWord),
    ];

    // ────────────────────────────────────────────────────────────────────────────
    //  SEARCH & BING (background web queries eating CPU)
    // ────────────────────────────────────────────────────────────────────────────

    public static readonly TweakDefinition[] SearchTweaks =
    [
        new("bing_search_disable",
            "Disable Bing in Start Search",
            "Stops Start menu from sending every keystroke to Bing servers — eliminates network overhead and CPU spikes",
            "Search",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Search",
            "BingSearchEnabled",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord),

        new("web_search_disable",
            "Disable Web Search in Search Bar",
            "Prevents Windows Search from querying the internet in the background",
            "Search",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search",
            "DisableWebSearch",
            EnabledValue: 1, DisabledValue: 0, RegistryValueKind.DWord),

        new("connected_search_web",
            "Disable Connected Search (Web)",
            "Stops search bar from connecting to web for AI-powered results",
            "Search",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search",
            "ConnectedSearchUseWeb",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord),

        new("cortana_disable",
            "Disable Cortana",
            "Disables Cortana AI assistant — prevents background memory and CPU usage",
            "Search",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search",
            "AllowCortana",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord),

        new("cortana_consent",
            "Remove Cortana Consent",
            "Removes Cortana consent flag — ensures Cortana stays disabled",
            "Search",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Search",
            "CortanaConsent",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord),
    ];

    // ────────────────────────────────────────────────────────────────────────────
    //  WIDGETS & PHONE LINK (live content fetch, persistent background processes)
    // ────────────────────────────────────────────────────────────────────────────

    public static readonly TweakDefinition[] WidgetsTweaks =
    [
        new("widgets_taskbar_hide",
            "Hide Widgets from Taskbar",
            "Removes the Widgets button — the service still runs, but stops fetching live content",
            "Widgets",
            RiskLevel.Safe,
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "TaskbarDa",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord),

        new("widgets_policy_disable",
            "Disable Widgets Service (Policy)",
            "Full system-level Widgets disable via policy — stops background news/weather fetching entirely",
            "Widgets",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Dsh",
            "AllowNewsAndInterests",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord),

        new("phone_link_disable",
            "Disable Phone Link",
            "Disables Phone Link (Your Phone) — stops 150+ background COM servers and sync processes reported in some builds",
            "Widgets",
            RiskLevel.Safe,
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Mobility",
            "PhoneLinkEnabled",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord),

        new("startup_delay",
            "Disable Startup Delay",
            "Removes Windows 11's deliberate 10-second startup delay for background apps at boot",
            "Widgets",
            RiskLevel.Safe,
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize",
            "StartupDelayInMSec",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord),
    ];

    // ────────────────────────────────────────────────────────────────────────────
    //  SYSTEM RESPONSIVENESS (faster service kill, smart clipboard, etc.)
    // ────────────────────────────────────────────────────────────────────────────

    public static readonly TweakDefinition[] ResponsivenessTweaks =
    [
        new("service_kill_timeout",
            "Faster Service Kill Timeout",
            "Reduces the time Windows waits to kill hung services from 5s to 2s — faster game launches and shutdowns",
            "Responsiveness",
            RiskLevel.Safe,
            @"HKLM\SYSTEM\CurrentControlSet\Control",
            "WaitToKillServiceTimeout",
            EnabledValue: "2000", DisabledValue: "5000", RegistryValueKind.String),

        new("smart_clipboard",
            "Disable Smart Clipboard AI",
            "Disables AI-powered clipboard analysis (text/image recognition on every copy)",
            "Responsiveness",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\SmartActionPlatform\SmartClipboard",
            "Disabled",
            EnabledValue: 1, DisabledValue: 0, RegistryValueKind.DWord),

        new("activity_publish",
            "Disable Activity Publishing",
            "Stops Windows from uploading your activity history to Microsoft servers",
            "Responsiveness",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
            "PublishUserActivities",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord),

        new("activity_upload",
            "Disable Activity Upload",
            "Prevents activity timeline data from being synced to the cloud",
            "Responsiveness",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
            "UploadUserActivities",
            EnabledValue: 0, DisabledValue: 1, RegistryValueKind.DWord),

        new("sfio_priority",
            "SFIO High Priority for Games",
            "Sets Storage I/O priority to High for game processes — faster asset streaming",
            "Responsiveness",
            RiskLevel.Moderate,
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
            "SFIO Priority",
            EnabledValue: "High", DisabledValue: "Normal", RegistryValueKind.String),
    ];

    // ────────────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ────────────────────────────────────────────────────────────────────────────

    public static IEnumerable<TweakDefinition> All =>
        SecurityTweaks
        .Concat(AiTweaks)
        .Concat(SearchTweaks)
        .Concat(WidgetsTweaks)
        .Concat(ResponsivenessTweaks);

    public static TweakItem ToTweakItem(TweakDefinition def) => new()
    {
        Id = def.Id,
        Name = def.Name,
        Description = def.Description,
        Category = TweakCategory.Gaming, // reuse category for now; shown by sub-category string
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
