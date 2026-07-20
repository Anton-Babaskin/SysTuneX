using Microsoft.Win32;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;

namespace SysTuneX.Core.Tweaks;

public static class GamingTweaks
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
        new("game_bar", "Disable Game Bar", "Removes Xbox Game Bar overlay — frees CPU and reduces input lag",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, 1, RegistryValueKind.DWord),

        new("game_dvr", "Disable Game DVR", "Stops background game recording — saves CPU and disk I/O",
            RiskLevel.Safe,
            @"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0, 1, RegistryValueKind.DWord),

        new("game_dvr_policy", "Disable Game DVR (Policy)", "Group policy level Game DVR block",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, 1, RegistryValueKind.DWord),

        new("hags", "Enable HAGS", "Hardware-Accelerated GPU Scheduling — reduces input lag 5-15% on modern GPUs",
            RiskLevel.Moderate,
            @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1, RegistryValueKind.DWord),

        new("gpu_priority", "GPU Priority", "Gives games higher GPU priority",
            RiskLevel.Moderate,
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority", 8, 2, RegistryValueKind.DWord),

        new("cpu_priority", "CPU Priority for Games", "Sets high CPU priority for game processes",
            RiskLevel.Moderate,
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority", 6, 2, RegistryValueKind.DWord),

        new("scheduling_category", "Scheduling Category", "Sets games to High scheduling category",
            RiskLevel.Moderate,
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category", "High", "Medium", RegistryValueKind.String),

        new("timer_resolution", "High Timer Resolution", "Improves frame timing precision on 144Hz+ monitors",
            RiskLevel.Moderate,
            @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1, 0, RegistryValueKind.DWord),

        new("multimedia_throttling", "Disable Multimedia Throttling", "Prevents Windows from throttling multimedia tasks",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 0, 20, RegistryValueKind.DWord),

        new("fullscreen_optimizations", "Disable Fullscreen Optimizations", "Prevents Windows from adding latency to fullscreen games",
            RiskLevel.Safe,
            @"HKCU\System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2, 0, RegistryValueKind.DWord),

        new("fullscreen_optimizations2", "Disable FSO (DX)", "Disables DX fullscreen optimizations globally",
            RiskLevel.Safe,
            @"HKCU\System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1, 0, RegistryValueKind.DWord),

        new("disable_mouse_accel", "Disable Mouse Acceleration", "Raw mouse input — essential for FPS games",
            RiskLevel.Safe,
            @"HKCU\Control Panel\Mouse", "MouseSpeed", "0", "1", RegistryValueKind.String),

        new("preemption_gpu", "Disable GPU Preemption", "May reduce micro-stuttering in games",
            RiskLevel.Advanced,
            @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler", "EnablePreemption", 0, 1, RegistryValueKind.DWord),

        new("disable_power_throttling", "Disable Power Throttling", "Prevents CPU throttling during gaming",
            RiskLevel.Moderate,
            @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, 0, RegistryValueKind.DWord),

        new("disable_core_parking", "Disable Core Parking", "Keeps all CPU cores active — better for multi-threaded games",
            RiskLevel.Moderate,
            @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583", "ValueMax", 0, 64, RegistryValueKind.DWord),

        new("background_apps", "Disable Background Apps", "Stops UWP apps from running in the background",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, 0, RegistryValueKind.DWord),

        new("visual_fx", "Optimize Visual Effects", "Sets visual effects to performance mode",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2, 1, RegistryValueKind.DWord),

        new("transparency", "Disable Transparency Effects", "Reduces GPU load from Aero effects",
            RiskLevel.Safe,
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, 1, RegistryValueKind.DWord),

        new("animations", "Disable Animations", "Removes window animations for snappier feel",
            RiskLevel.Safe,
            @"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", "1", RegistryValueKind.String),

        new("menu_show_delay", "Reduce Menu Show Delay", "Makes menus appear instantly",
            RiskLevel.Safe,
            @"HKCU\Control Panel\Desktop", "MenuShowDelay", "0", "400", RegistryValueKind.String),

        new("sfio_priority", "SFIO High Priority for Games", "Prioritizes Storage I/O for game processes — faster texture/asset streaming",
            RiskLevel.Moderate,
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
            "SFIO Priority", "High", "Normal", RegistryValueKind.String),

        new("service_kill_timeout", "Faster Service Kill Timeout",
            "Reduces time Windows waits for hanging services from 5s to 2s — faster game launch after boot",
            RiskLevel.Safe,
            @"HKLM\SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000", "5000", RegistryValueKind.String),
    ];

    public static TweakItem ToTweakItem(TweakDefinition def) => new()
    {
        Id = def.Id,
        Name = def.Name,
        Description = def.Description,
        Category = TweakCategory.Gaming,
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

    public static bool Apply(TweakDefinition def, IRegistryService registry)
    {
        return registry.SetValue(def.KeyPath, def.ValueName, def.EnabledValue, def.ValueKind);
    }

    public static bool Revert(TweakDefinition def, IRegistryService registry)
    {
        return registry.SetValue(def.KeyPath, def.ValueName, def.DisabledValue, def.ValueKind);
    }
}
