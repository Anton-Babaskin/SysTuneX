using Microsoft.Win32;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tweaks;

/// <summary>
/// Windows 11 specific features that cost frames: the virtualisation security stack, the
/// on-device AI features added in 24H2, and the shell components that fetch content in the
/// background. Every entry is gated on the build it actually exists in.
/// </summary>
public static class Windows11Tweaks
{
    private const string DeviceGuard = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard";
    private const string WindowsAi = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI";
    private const string WindowsSearchPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search";
    private const string ExplorerAdvanced = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    /// <summary>Windows 11 21H2.</summary>
    private const int Windows11 = 22000;

    /// <summary>Windows 11 22H2, where the Copilot and search policies landed.</summary>
    private const int Windows11_22H2 = 22621;

    /// <summary>Windows 11 24H2, where Recall and Click To Do were introduced.</summary>
    private const int Windows11_24H2 = 26100;

    public static IReadOnlyList<TweakDefinition> All { get; } =
    [
        // ── Virtualisation-based security ───────────────────────────────────────

        new()
        {
            Id = "vbs_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Virtualization",
            Name = "Disable virtualisation-based security",
            Description =
                "VBS runs Windows on top of a hypervisor, which costs roughly 5-15 percent of frame " +
                "rate in CPU-bound games. Turning it off removes a genuine security boundary.",
            Risk = RiskLevel.Advanced,
            RequiresRestart = true,
            MinBuild = Windows11,
            WarningKey = "Warning_Vbs",
            Changes =
            [
                new(DeviceGuard, "EnableVirtualizationBasedSecurity", 0, 1, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "hvci_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Virtualization",
            Name = "Disable memory integrity (HVCI)",
            Description =
                "Hypervisor-protected code integrity validates every kernel driver page. Disabling it " +
                "removes that check and its per-call overhead.",
            Risk = RiskLevel.Advanced,
            RequiresRestart = true,
            MinBuild = Windows11,
            WarningKey = "Warning_Vbs",
            Changes =
            [
                new($@"{DeviceGuard}\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, 1, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "hypervisor_launch_off",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Virtualization",
            Name = "Stop the hypervisor from launching at boot",
            Description =
                "The registry keys above stop VBS being requested, but the hypervisor still starts if " +
                "the boot entry says so. This writes hypervisorlaunchtype=off, which is what actually " +
                "removes the virtualisation layer. Hyper-V, WSL2, Docker Desktop, Windows Sandbox and " +
                "the Android subsystem stop working until it is turned back on.",
            Risk = RiskLevel.Advanced,
            RequiresRestart = true,
            MinBuild = Windows11,
            HandlerKey = "hypervisor_launch",
            WarningKey = "Warning_Hypervisor",
        },

        // ── On-device AI ────────────────────────────────────────────────────────

        new()
        {
            Id = "recall_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Ai",
            Name = "Disable Windows Recall",
            Description =
                "Blocks the snapshot service that screenshots the desktop every few seconds and runs " +
                "them through a local model. Both the machine-wide and per-user policies are written.",
            Risk = RiskLevel.Safe,
            MinBuild = Windows11_24H2,
            Changes =
            [
                new(WindowsAi, "AllowRecallEnablement", 0, null, RegistryValueKind.DWord),
                new(WindowsAi, "DisableAIDataAnalysis", 1, null, RegistryValueKind.DWord),
                new(@"HKCU\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "click_to_do_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Ai",
            Name = "Disable Click To Do",
            Description = "Turns off the overlay that analyses whatever is on screen to offer AI actions.",
            Risk = RiskLevel.Safe,
            MinBuild = Windows11_24H2,
            Changes =
            [
                new(WindowsAi, "DisableClickToDo", 1, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "copilot_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Ai",
            Name = "Disable Copilot",
            Description =
                "Removes the Copilot taskbar entry and blocks the policy that loads its web view in " +
                "the background.",
            Risk = RiskLevel.Safe,
            MinBuild = Windows11_22H2,
            RequiresExplorerRestart = true,
            Changes =
            [
                new(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null, RegistryValueKind.DWord),
                new(@"HKCU\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null, RegistryValueKind.DWord),
                new(ExplorerAdvanced, "ShowCopilotButton", 0, 1, RegistryValueKind.DWord),
            ],
        },

        // ── Search ──────────────────────────────────────────────────────────────

        new()
        {
            Id = "bing_search_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Search",
            Name = "Remove web results from Start search",
            Description =
                "Stops the Start menu sending what you type to Bing and rendering the answer in a web " +
                "view. Local file and app search is unaffected.",
            Risk = RiskLevel.Safe,
            MinBuild = Windows11,
            RequiresExplorerRestart = true,
            Changes =
            [
                new(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, 1, RegistryValueKind.DWord),
                new(@"HKCU\SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "web_search_policy_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Search",
            Name = "Block connected search by policy",
            Description = "Machine-wide policy that keeps Windows Search from reaching the internet at all.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(WindowsSearchPolicy, "DisableWebSearch", 1, null, RegistryValueKind.DWord),
                new(WindowsSearchPolicy, "ConnectedSearchUseWeb", 0, null, RegistryValueKind.DWord),
                new(WindowsSearchPolicy, "AllowCortana", 0, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "search_highlights_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Search",
            Name = "Turn off search highlights",
            Description = "Stops the search box downloading daily illustrations and trending content.",
            Risk = RiskLevel.Safe,
            MinBuild = Windows11,
            RequiresExplorerRestart = true,
            Changes =
            [
                new(WindowsSearchPolicy, "EnableDynamicContentInWSB", 0, null, RegistryValueKind.DWord),
                new(@"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0, 1, RegistryValueKind.DWord),
            ],
        },

        // ── Shell components with background work ───────────────────────────────

        new()
        {
            Id = "widgets_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Shell",
            Name = "Disable Widgets",
            Description =
                "Widgets keeps a web runtime alive to poll news and weather. The policy stops the " +
                "feed entirely rather than just hiding the taskbar button.",
            Risk = RiskLevel.Safe,
            MinBuild = Windows11,
            RequiresExplorerRestart = true,
            Changes =
            [
                new(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, null, RegistryValueKind.DWord),
                new(ExplorerAdvanced, "TaskbarDa", 0, 1, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "taskbar_chat_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Shell",
            Name = "Remove the Chat taskbar button",
            Description = "Hides the Teams personal button, which preloads its host process at sign-in.",
            Risk = RiskLevel.Safe,
            MinBuild = Windows11,
            RequiresExplorerRestart = true,
            Changes =
            [
                new(ExplorerAdvanced, "TaskbarMn", 0, 1, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "startup_delay_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Shell",
            Name = "Remove the startup app delay",
            Description =
                "Windows holds startup programs back for about ten seconds after sign-in so the shell " +
                "settles first. On an SSD that wait is no longer useful.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "lock_screen_spotlight_disable",
            Category = TweakCategory.Windows11,
            GroupKey = "Group_Shell",
            Name = "Turn off Windows Spotlight",
            Description = "Stops the lock screen and desktop downloading rotating wallpapers and their ad copy.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenEnabled", 0, 1, RegistryValueKind.DWord),
                new(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled", 0, 1, RegistryValueKind.DWord),
                new(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338387Enabled", 0, 1, RegistryValueKind.DWord),
            ],
        },
    ];
}
