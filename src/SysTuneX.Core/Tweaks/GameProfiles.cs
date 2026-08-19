using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tweaks;

/// <summary>
/// Preset bundles. Each one references tweak ids from <see cref="TweakCatalog"/> and service
/// names from <see cref="ServiceCatalog"/>; the engine skips anything that does not apply to
/// the running Windows build rather than failing the whole profile.
/// </summary>
public static class GameProfiles
{
    public static IReadOnlyList<GameProfile> BuiltIn { get; } =
    [
        new()
        {
            Id = "esports",
            Name = "Competitive FPS",
            Description =
                "Lowest input latency. Raw mouse input, no capture overlay, no compositor animations " +
                "and no background app wake-ups.",
            ExampleGames = ["CS2", "Valorant", "Apex Legends", "Overwatch 2"],
            Icon = "Target24",
            AccentColor = "#F97316",
            MaxRisk = RiskLevel.Moderate,
            TrimMemory = true,
            TweakIds =
            [
                "game_bar_disable",
                "game_dvr_policy_disable",
                "fullscreen_optimizations_disable",
                "mouse_acceleration_disable",
                "system_responsiveness",
                "games_cpu_priority",
                "power_throttling_disable",
                "core_parking_disable",
                "menu_show_delay",
                "visual_effects_performance",
                "transparency_disable",
                "background_apps_disable",
                "nagle_disable",
                "network_throttling_disable",
            ],
            ServiceNames = ["DiagTrack", "SysMain", "dmwappushservice", "WerSvc", "RetailDemo", "MapsBroker", "lfsvc"],
        },

        new()
        {
            Id = "battle_royale",
            Name = "Battle royale",
            Description =
                "Tuned for large lobbies: network latency first, then streaming performance for the " +
                "constant asset loading these maps do.",
            ExampleGames = ["Fortnite", "Warzone", "PUBG", "Apex Legends"],
            Icon = "Trophy24",
            AccentColor = "#22C55E",
            MaxRisk = RiskLevel.Moderate,
            TrimMemory = true,
            TweakIds =
            [
                "game_bar_disable",
                "fullscreen_optimizations_disable",
                "mouse_acceleration_disable",
                "system_responsiveness",
                "games_cpu_priority",
                "power_throttling_disable",
                "core_parking_disable",
                "background_apps_disable",
                "nagle_disable",
                "network_throttling_disable",
                "network_memory_reserve",
            ],
            ServiceNames = ["DiagTrack", "SysMain", "DoSvc", "dmwappushservice", "WerSvc", "PushToInstall"],
        },

        new()
        {
            Id = "open_world",
            Name = "Open world and RPG",
            Description =
                "Long sessions with heavy streaming. Keeps every core awake and stops background " +
                "indexing competing with the game for disk bandwidth.",
            ExampleGames = ["Baldur's Gate 3", "Cyberpunk 2077", "Final Fantasy XIV", "World of Warcraft"],
            Icon = "Globe24",
            AccentColor = "#8B5CF6",
            MaxRisk = RiskLevel.Moderate,
            TweakIds =
            [
                "game_bar_disable",
                "fullscreen_optimizations_disable",
                "hags_enable",
                "system_responsiveness",
                "games_cpu_priority",
                "power_throttling_disable",
                "core_parking_disable",
                "background_apps_disable",
                "visual_effects_performance",
            ],
            ServiceNames = ["DiagTrack", "SysMain", "WSearch", "DoSvc", "WerSvc"],
        },

        new()
        {
            Id = "simulation",
            Name = "Racing and simulation",
            Description =
                "Frame-time consistency over peak frame rate, which is what matters when you are " +
                "reading the road through a wheel.",
            ExampleGames = ["iRacing", "Assetto Corsa Competizione", "Forza", "MSFS"],
            Icon = "TopSpeed24",
            AccentColor = "#0EA5E9",
            MaxRisk = RiskLevel.Moderate,
            TweakIds =
            [
                "game_bar_disable",
                "fullscreen_optimizations_disable",
                "system_responsiveness",
                "games_cpu_priority",
                "power_throttling_disable",
                "core_parking_disable",
                "timer_resolution_global",
                "background_apps_disable",
                "nagle_disable",
            ],
            ServiceNames = ["DiagTrack", "SysMain", "WerSvc", "MapsBroker"],
        },

        new()
        {
            Id = "maximum",
            Name = "Maximum performance",
            Description =
                "Everything the other profiles do, plus the advanced changes: virtualisation " +
                "security off and the hypervisor removed from the boot path. Read the warnings " +
                "before applying this one - anti-cheat software and Hyper-V are affected.",
            ExampleGames = [],
            Icon = "Rocket24",
            AccentColor = "#EF4444",
            MaxRisk = RiskLevel.Advanced,
            TrimMemory = true,
            TweakIds =
            [
                "game_bar_disable",
                "game_dvr_policy_disable",
                "fullscreen_optimizations_disable",
                "hags_enable",
                "mouse_acceleration_disable",
                "system_responsiveness",
                "games_cpu_priority",
                "power_throttling_disable",
                "core_parking_disable",
                "timer_resolution_global",
                "menu_show_delay",
                "visual_effects_performance",
                "transparency_disable",
                "background_apps_disable",
                "wait_to_kill_service_timeout",
                "fast_startup_disable",
                "vbs_disable",
                "hvci_disable",
                "hypervisor_launch_off",
                "widgets_disable",
                "copilot_disable",
                "recall_disable",
                "bing_search_disable",
                "startup_delay_disable",
                "nagle_disable",
                "network_throttling_disable",
            ],
            ServiceNames =
            [
                "DiagTrack", "SysMain", "dmwappushservice", "WerSvc", "RetailDemo", "MapsBroker",
                "lfsvc", "DoSvc", "PushToInstall", "edgeupdate", "MicrosoftEdgeElevationService",
                "RemoteRegistry", "Fax", "WMPNetworkSvc", "AJRouter", "TrkWks",
            ],
        },

        new()
        {
            Id = "streaming",
            Name = "Streaming and recording",
            Description =
                "For playing while OBS is encoding. Deliberately leaves the capture stack and " +
                "background CPU reservation alone so the encoder is not starved.",
            ExampleGames = [],
            Icon = "Video24",
            AccentColor = "#EC4899",
            MaxRisk = RiskLevel.Safe,
            TweakIds =
            [
                "fullscreen_optimizations_disable",
                "mouse_acceleration_disable",
                "background_apps_disable",
                "transparency_disable",
                "network_throttling_disable",
                "widgets_disable",
                "startup_delay_disable",
            ],
            ServiceNames = ["DiagTrack", "SysMain", "WerSvc", "RetailDemo"],
            ActivateHighPerformancePower = true,
        },
    ];

    public static GameProfile? Find(string id) =>
        BuiltIn.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
