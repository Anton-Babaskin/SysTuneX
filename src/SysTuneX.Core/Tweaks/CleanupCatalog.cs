using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tweaks;

/// <summary>
/// Locations the cleanup page offers to sweep. Paths are stored unexpanded so the same entry
/// works whatever drive Windows is installed on.
/// </summary>
public static class CleanupCatalog
{
    public static IReadOnlyList<CleanupTarget> All { get; } =
    [
        new()
        {
            Id = "user_temp",
            Name = "Your temporary files",
            Description = "Everything under %TEMP%. Files touched in the last hour are left alone in case something is still using them.",
            Category = CleanupCategory.Temporary,
            Risk = RiskLevel.Safe,
            Paths = ["%TEMP%"],
            MinimumAge = TimeSpan.FromHours(1),
        },

        new()
        {
            Id = "windows_temp",
            Name = "Windows temporary files",
            Description = "Scratch files installers and Windows components leave in the system temp folder.",
            Category = CleanupCategory.Temporary,
            Risk = RiskLevel.Safe,
            Paths = [@"%SystemRoot%\Temp"],
            MinimumAge = TimeSpan.FromHours(1),
        },

        new()
        {
            Id = "crash_dumps",
            Name = "Crash dumps",
            Description = "Memory dumps written when an app or the kernel crashed. Often hundreds of megabytes each.",
            Category = CleanupCategory.Logs,
            Risk = RiskLevel.Safe,
            Paths = [@"%LOCALAPPDATA%\CrashDumps", @"%SystemRoot%\Minidump"],
        },

        new()
        {
            Id = "error_reports",
            Name = "Error report queue",
            Description = "Reports Windows Error Reporting queued for upload but has not sent.",
            Category = CleanupCategory.Logs,
            Risk = RiskLevel.Safe,
            Paths =
            [
                @"%LOCALAPPDATA%\Microsoft\Windows\WER",
                @"%ProgramData%\Microsoft\Windows\WER\ReportQueue",
                @"%ProgramData%\Microsoft\Windows\WER\ReportArchive",
            ],
        },

        new()
        {
            Id = "windows_logs",
            Name = "Servicing logs",
            Description = "Component store and DISM logs kept from past Windows updates.",
            Category = CleanupCategory.Logs,
            Risk = RiskLevel.Safe,
            Paths = [@"%SystemRoot%\Logs\CBS", @"%SystemRoot%\Logs\DISM", @"%SystemRoot%\Logs\MoSetup"],
        },

        new()
        {
            Id = "windows_update_cache",
            Name = "Windows Update download cache",
            Description =
                "Installers Windows Update already applied. Safe to remove; the only cost is that " +
                "uninstalling a recent update may need to download it again.",
            Category = CleanupCategory.WindowsUpdate,
            Risk = RiskLevel.Moderate,
            Paths = [@"%SystemRoot%\SoftwareDistribution\Download"],
            EnabledByDefault = false,
        },

        new()
        {
            Id = "delivery_optimization_cache",
            Name = "Delivery Optimization cache",
            Description = "Update chunks kept to share with other PCs on your network.",
            Category = CleanupCategory.WindowsUpdate,
            Risk = RiskLevel.Moderate,
            Paths = [@"%SystemRoot%\SoftwareDistribution\DeliveryOptimization"],
            EnabledByDefault = false,
        },

        new()
        {
            Id = "thumbnail_cache",
            Name = "Thumbnail cache",
            Description = "Explorer's picture previews. Rebuilt automatically the next time you browse a folder.",
            Category = CleanupCategory.Thumbnails,
            Risk = RiskLevel.Safe,
            Paths = [@"%LOCALAPPDATA%\Microsoft\Windows\Explorer"],
            SearchPattern = "thumbcache_*.db",
            RemoveEmptyDirectories = false,
        },

        new()
        {
            Id = "directx_shader_cache",
            Name = "DirectX shader cache",
            Description =
                "Compiled shaders Windows caches for D3D12 titles. Clearing it can fix stutter after " +
                "a driver update, at the cost of one recompilation pass per game.",
            Category = CleanupCategory.ShaderCache,
            Risk = RiskLevel.Safe,
            Paths = [@"%LOCALAPPDATA%\D3DSCache", @"%LOCALAPPDATA%\Microsoft\DirectX Shader Cache"],
            MinimumAge = TimeSpan.FromHours(2),
        },

        new()
        {
            Id = "nvidia_shader_cache",
            Name = "NVIDIA shader cache",
            Description = "The driver's own DirectX and OpenGL shader caches. Rebuilt on the next launch of each game.",
            Category = CleanupCategory.ShaderCache,
            Risk = RiskLevel.Safe,
            Paths =
            [
                @"%LOCALAPPDATA%\NVIDIA\DXCache",
                @"%LOCALAPPDATA%\NVIDIA\GLCache",
                @"%ProgramData%\NVIDIA Corporation\NV_Cache",
            ],
            MinimumAge = TimeSpan.FromHours(2),
        },

        new()
        {
            Id = "amd_shader_cache",
            Name = "AMD shader cache",
            Description = "Radeon driver shader caches. Rebuilt on the next launch of each game.",
            Category = CleanupCategory.ShaderCache,
            Risk = RiskLevel.Safe,
            Paths =
            [
                @"%LOCALAPPDATA%\AMD\DxCache",
                @"%LOCALAPPDATA%\AMD\DxcCache",
                @"%LOCALAPPDATA%\AMD\GLCache",
                @"%LOCALAPPDATA%\AMD\VkCache",
            ],
            MinimumAge = TimeSpan.FromHours(2),
        },

        new()
        {
            Id = "prefetch",
            Name = "Prefetch data",
            Description =
                "Windows uses these files to predict what to load at boot and at app start. Deleting " +
                "them makes the next few boots and launches slower until Windows rebuilds the data. " +
                "There is no lasting benefit on an SSD - included only because people ask for it.",
            Category = CleanupCategory.Temporary,
            Risk = RiskLevel.Advanced,
            Paths = [@"%SystemRoot%\Prefetch"],
            SearchPattern = "*.pf",
            EnabledByDefault = false,
            RemoveEmptyDirectories = false,
        },
    ];
}

/// <param name="PackageName">Value of the package's <c>Name</c> property, as reported by Get-AppxPackage.</param>
/// <param name="IsSystemRelevant">Removing it breaks a visible part of Windows.</param>
public sealed record BloatwarePackage(string PackageName, string DisplayName, bool IsSystemRelevant = false);

/// <summary>Store packages the cleanup page offers to remove.</summary>
public static class BloatwareCatalog
{
    public static IReadOnlyList<BloatwarePackage> All { get; } =
    [
        new("Microsoft.BingNews", "News"),
        new("Microsoft.BingWeather", "Weather"),
        new("Microsoft.BingSearch", "Bing Search in Start", IsSystemRelevant: true),
        new("Microsoft.GetHelp", "Get Help"),
        new("Microsoft.Getstarted", "Tips"),
        new("Microsoft.MicrosoftOfficeHub", "Office Hub"),
        new("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection"),
        new("Microsoft.MicrosoftStickyNotes", "Sticky Notes"),
        new("Microsoft.People", "People"),
        new("Microsoft.PowerAutomateDesktop", "Power Automate"),
        new("Microsoft.Todos", "To Do"),
        new("Microsoft.WindowsAlarms", "Clock"),
        new("Microsoft.WindowsFeedbackHub", "Feedback Hub"),
        new("Microsoft.WindowsMaps", "Maps"),
        new("Microsoft.WindowsSoundRecorder", "Sound Recorder"),
        new("Microsoft.YourPhone", "Phone Link"),
        new("Microsoft.ZuneMusic", "Media Player", IsSystemRelevant: true),
        new("Microsoft.ZuneVideo", "Films and TV"),
        new("Microsoft.549981C3F5F10", "Cortana"),
        new("Microsoft.Xbox.TCUI", "Xbox TCUI", IsSystemRelevant: true),
        new("Microsoft.XboxGameOverlay", "Xbox Game Overlay"),
        new("Microsoft.XboxGamingOverlay", "Xbox Game Bar"),
        new("Microsoft.XboxSpeechToTextOverlay", "Xbox Speech To Text"),
        new("Microsoft.GamingApp", "Xbox app", IsSystemRelevant: true),
        new("Clipchamp.Clipchamp", "Clipchamp"),
        new("MicrosoftTeams", "Teams (personal)"),
        new("MicrosoftCorporationII.MicrosoftFamily", "Family Safety"),
        new("Microsoft.OutlookForWindows", "New Outlook"),
        new("king.com.CandyCrushSaga", "Candy Crush Saga"),
        new("king.com.CandyCrushFriends", "Candy Crush Friends"),
        new("SpotifyAB.SpotifyMusic", "Spotify (preinstalled)"),
    ];
}
