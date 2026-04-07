using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tweaks;

public static class GameProfiles
{
    public static readonly GameProfile[] BuiltIn =
    [
        new GameProfile
        {
            Id = "fps_competitive",
            Name = "FPS Competitive",
            IconGlyph = "\uE7FC",
            Description = "CS2, Valorant, Apex Legends — maximum FPS, minimum input lag",
            TweakIds = [
                "game_bar", "game_dvr", "game_dvr_policy", "hags",
                "gpu_priority", "cpu_priority", "scheduling_category",
                "timer_resolution", "multimedia_throttling",
                "fullscreen_optimizations", "fullscreen_optimizations2",
                "disable_mouse_accel", "disable_power_throttling",
                "background_apps", "visual_fx", "transparency", "animations",
                "sfio_priority", "service_kill_timeout",
                "nagle_disable", "tcp_ack_frequency", "network_throttling",
                // Win11-specific AI & search kills
                "recall_block_hklm", "recall_disable_user", "recall_disable_system",
                "click_to_do", "ai_settings_agent", "copilot_machine",
                "bing_search_disable", "web_search_disable", "connected_search_web", "cortana_disable",
                "widgets_taskbar_hide", "widgets_policy_disable", "phone_link_disable",
                "startup_delay", "smart_clipboard",
            ],
            ServiceNames = ["DiagTrack", "SysMain", "WSearch", "XblAuthManager", "XblGameSave", "XboxNetApiSvc",
                "Widgets", "CDPSvc", "CDPUserSvc", "PushToInstall"]
        },
        new GameProfile
        {
            Id = "battle_royale",
            Name = "Battle Royale",
            IconGlyph = "\uE709",
            Description = "Fortnite, Warzone, PUBG — balanced FPS and visual quality",
            TweakIds = [
                "game_bar", "game_dvr", "game_dvr_policy", "hags",
                "gpu_priority", "cpu_priority", "scheduling_category",
                "timer_resolution", "multimedia_throttling",
                "fullscreen_optimizations", "fullscreen_optimizations2",
                "disable_power_throttling", "background_apps",
                "nagle_disable", "tcp_ack_frequency", "network_throttling"
            ],
            ServiceNames = ["DiagTrack", "SysMain", "WSearch"]
        },
        new GameProfile
        {
            Id = "mmo_rpg",
            Name = "MMO / RPG",
            IconGlyph = "\uE8D6",
            Description = "WoW, FFXIV, Baldur's Gate 3 — stable frametime, keep visual quality",
            TweakIds = [
                "game_bar", "game_dvr", "game_dvr_policy",
                "gpu_priority", "cpu_priority", "scheduling_category",
                "multimedia_throttling", "fullscreen_optimizations",
                "disable_power_throttling", "background_apps",
                "nagle_disable", "network_throttling"
            ],
            ServiceNames = ["DiagTrack", "SysMain"]
        },
        new GameProfile
        {
            Id = "racing_sim",
            Name = "Racing / Sim",
            IconGlyph = "\uE804",
            Description = "Forza, iRacing, ACC — smooth frametime, low input lag for wheel",
            TweakIds = [
                "game_bar", "game_dvr", "game_dvr_policy", "hags",
                "gpu_priority", "cpu_priority", "scheduling_category",
                "timer_resolution", "multimedia_throttling",
                "fullscreen_optimizations", "fullscreen_optimizations2",
                "disable_power_throttling", "disable_core_parking",
                "background_apps"
            ],
            ServiceNames = ["DiagTrack", "SysMain", "WSearch"]
        },
        new GameProfile
        {
            Id = "maximum",
            Name = "Maximum Performance",
            IconGlyph = "\uE945",
            Description = "Apply ALL optimizations including Win11 AI kills — for maximum FPS at any cost",
            TweakIds = GamingTweaks.All.Select(t => t.Id)
                .Concat(NetworkTweaks.All.Select(t => t.Id))
                .Concat(Windows11Tweaks.AiTweaks.Select(t => t.Id))
                .Concat(Windows11Tweaks.SearchTweaks.Select(t => t.Id))
                .Concat(Windows11Tweaks.WidgetsTweaks.Select(t => t.Id))
                .Concat(Windows11Tweaks.ResponsivenessTweaks.Select(t => t.Id))
                .ToList(),
            ServiceNames = ["DiagTrack", "SysMain", "WSearch", "Fax", "lfsvc", "MapsBroker",
                "XblAuthManager", "XblGameSave", "XboxGipSvc", "XboxNetApiSvc",
                "RetailDemo", "WMPNetworkSvc", "PhoneSvc", "dmwappushservice", "WerSvc",
                "Widgets", "WpnService", "CDPSvc", "CDPUserSvc", "PushToInstall",
                "edgeupdate", "MicrosoftEdgeElevationService"]
        },
    ];
}
