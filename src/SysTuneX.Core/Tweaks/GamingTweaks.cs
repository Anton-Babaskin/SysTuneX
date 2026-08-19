using Microsoft.Win32;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tweaks;

/// <summary>
/// Latency and frame-pacing oriented tweaks.
///
/// Every entry records the documented stock-Windows value in <see cref="RegistryChange.WindowsDefaultValue"/>.
/// Where a value does not exist on a clean install the default is <see langword="null"/>, so reverting
/// deletes it instead of inventing a number - the previous build guessed here and could leave a
/// machine in a state Windows never shipped.
/// </summary>
public static class GamingTweaks
{
    private const string GameConfigStore = @"HKCU\System\GameConfigStore";
    private const string GameDvr = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string SystemProfile = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GamesTask = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
    private const string GraphicsDrivers = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string Mouse = @"HKCU\Control Panel\Mouse";
    private const string Desktop = @"HKCU\Control Panel\Desktop";

    public static IReadOnlyList<TweakDefinition> All { get; } =
    [
        new()
        {
            Id = "game_bar_disable",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_GameCapture",
            Name = "Turn off Game Bar and Game DVR",
            Description =
                "Stops the Xbox overlay and the background recorder that keeps the last 30 seconds of " +
                "gameplay buffered. Frees a CPU thread and removes a present-path hook.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(GameDvr, "AppCaptureEnabled", 0, 1, RegistryValueKind.DWord),
                new(GameConfigStore, "GameDVR_Enabled", 0, 1, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "game_dvr_policy_disable",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_GameCapture",
            Name = "Block Game DVR by policy",
            Description =
                "Machine-wide policy block so Windows cannot re-enable capture after a feature update. " +
                "Applies to every user account on the PC.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "fullscreen_optimizations_disable",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_GameCapture",
            Name = "Disable fullscreen optimizations",
            Description =
                "Makes exclusive fullscreen behave as exclusive fullscreen instead of a borderless " +
                "composited window. Removes one frame of presentation latency in most engines.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(GameConfigStore, "GameDVR_FSEBehavior", 2, null, RegistryValueKind.DWord),
                new(GameConfigStore, "GameDVR_FSEBehaviorMode", 2, 0, RegistryValueKind.DWord),
                new(GameConfigStore, "GameDVR_HonorUserFSEBehaviorMode", 1, 0, RegistryValueKind.DWord),
                new(GameConfigStore, "GameDVR_DXGIHonorFSEWindowsCompatible", 1, 0, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "hags_enable",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Gpu",
            Name = "Enable hardware-accelerated GPU scheduling",
            Description =
                "Hands frame queue management to the GPU instead of the Windows scheduler. Needs a " +
                "GPU and driver that support it (GTX 10-series / RX 5000 and newer); on older hardware " +
                "Windows ignores the value.",
            Risk = RiskLevel.Moderate,
            RequiresRestart = true,
            Changes =
            [
                new(GraphicsDrivers, "HwSchMode", 2, 1, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "gpu_preemption_disable",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Gpu",
            Name = "Disable GPU preemption",
            Description =
                "Stops the driver from interrupting long draw calls. Can smooth frame times on some " +
                "systems and cause driver timeouts (black screen, TDR recovery) on others.",
            Risk = RiskLevel.Advanced,
            RequiresRestart = true,
            WarningKey = "Warning_GpuPreemption",
            Changes =
            [
                new($@"{GraphicsDrivers}\Scheduler", "EnablePreemption", 0, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "games_cpu_priority",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Scheduling",
            Name = "Raise the multimedia priority of games",
            Description =
                "Lifts the MMCSS Games task from priority 2 to 6, so game threads win against " +
                "background work under load. GPU priority and the scheduling category are already " +
                "at their best values on stock Windows and are left alone.",
            Risk = RiskLevel.Moderate,
            Changes =
            [
                new(GamesTask, "Priority", 6, 2, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "system_responsiveness",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Scheduling",
            Name = "Reduce the background CPU reservation",
            Description =
                "Windows reserves 20 percent of CPU time for background tasks. Microsoft's documented " +
                "value for multimedia machines is 10, which is what SysTuneX writes. Setting it to 0 " +
                "is a common guide recommendation but starves the audio engine under load.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(SystemProfile, "SystemResponsiveness", 10, 20, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "power_throttling_disable",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Scheduling",
            Name = "Disable power throttling",
            Description =
                "Stops Windows from parking background threads on efficiency cores to save power. " +
                "Recommended on desktops; on a laptop it measurably shortens battery life.",
            Risk = RiskLevel.Moderate,
            Changes =
            [
                new(@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "core_parking_disable",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Scheduling",
            Name = "Disable CPU core parking",
            Description =
                "Keeps every core awake so a thread waking up does not wait for a core to be unparked. " +
                "Applied through the power scheme, which is the only way the power manager honours it.",
            Risk = RiskLevel.Moderate,
            HandlerKey = "core_parking",
        },

        new()
        {
            Id = "timer_resolution_global",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Scheduling",
            Name = "Restore global timer resolution requests",
            Description =
                "Windows 11 22H2 made a timer resolution request apply only to the process that asked " +
                "for it. This restores the pre-22H2 system-wide behaviour, which most frame pacing " +
                "tools and older games expect.",
            Risk = RiskLevel.Moderate,
            RequiresRestart = true,
            MinBuild = 22621,
            Changes =
            [
                new(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "mouse_acceleration_disable",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Input",
            Name = "Disable mouse acceleration",
            Description =
                "Turns off Enhance pointer precision so cursor movement maps 1:1 to mouse counts. " +
                "SysTuneX also clears both acceleration thresholds and pushes the change into the " +
                "running session, which a registry write alone does not do.",
            Risk = RiskLevel.Safe,
            PostApply = PostApplyAction.RefreshMouseSettings,
            Changes =
            [
                new(Mouse, "MouseSpeed", "0", "1", RegistryValueKind.String),
                new(Mouse, "MouseThreshold1", "0", "6", RegistryValueKind.String),
                new(Mouse, "MouseThreshold2", "0", "10", RegistryValueKind.String),
            ],
        },

        new()
        {
            Id = "menu_show_delay",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Responsiveness",
            Name = "Remove the menu open delay",
            Description = "Drops the 400 ms delay before a submenu appears, which makes the whole shell feel immediate.",
            Risk = RiskLevel.Safe,
            PostApply = PostApplyAction.BroadcastSettingChange,
            Changes =
            [
                new(Desktop, "MenuShowDelay", "0", "400", RegistryValueKind.String),
            ],
        },

        new()
        {
            Id = "visual_effects_performance",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Responsiveness",
            Name = "Set visual effects to best performance",
            Description =
                "Turns off window animations and the fade effects the desktop compositor draws. " +
                "Frees a little GPU time and removes animation latency from alt-tab.",
            Risk = RiskLevel.Safe,
            PostApply = PostApplyAction.BroadcastSettingChange | PostApplyAction.RefreshVisualEffects,
            Changes =
            [
                new(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2, 0, RegistryValueKind.DWord),
                new($@"{Desktop}\WindowMetrics", "MinAnimate", "0", "1", RegistryValueKind.String),
            ],
        },

        new()
        {
            Id = "transparency_disable",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Responsiveness",
            Name = "Disable transparency effects",
            Description = "Removes the acrylic blur behind the taskbar and Start menu, which the GPU redraws continuously.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, 1, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "background_apps_disable",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Responsiveness",
            Name = "Stop Store apps running in the background",
            Description =
                "Blocks packaged apps from waking up to sync, poll or show notifications while a game " +
                "has focus. Desktop programs and their tray icons are unaffected.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, 0, RegistryValueKind.DWord),
                new(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2, null, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "wait_to_kill_service_timeout",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Responsiveness",
            Name = "Shorten the service shutdown timeout",
            Description = "Cuts the wait for a hung service at shutdown from 5 seconds to 2, so restarts finish sooner.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                new(@"HKLM\SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000", "5000", RegistryValueKind.String),
            ],
        },

        new()
        {
            Id = "fast_startup_disable",
            Category = TweakCategory.Gaming,
            GroupKey = "Group_Responsiveness",
            Name = "Disable Fast Startup",
            Description =
                "Fast Startup hibernates the kernel instead of shutting down, so driver state and " +
                "timer resolution carry over between sessions. Turning it off makes shutdown a real " +
                "shutdown, at the cost of a few seconds of boot time.",
            Risk = RiskLevel.Moderate,
            RequiresRestart = true,
            Changes =
            [
                new(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0, 1, RegistryValueKind.DWord),
            ],
        },
    ];
}
