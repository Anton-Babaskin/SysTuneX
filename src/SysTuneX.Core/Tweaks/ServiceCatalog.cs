using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tweaks;

/// <summary>
/// Services SysTuneX is willing to switch off, with the reason and the honest cost of doing so.
///
/// Two deliberate changes from the previous build: services are set to Manual rather than
/// Disabled wherever something else may legitimately start them on demand, and every entry
/// that breaks a visible Windows feature says so in its description instead of claiming it is
/// free performance.
/// </summary>
public static class ServiceCatalog
{
    private const int Windows11 = 22000;

    public static IReadOnlyList<ServiceDefinition> All { get; } =
    [
        // ── Telemetry and diagnostics ───────────────────────────────────────────

        new()
        {
            ServiceName = "DiagTrack",
            DisplayName = "Connected User Experiences and Telemetry",
            Description = "Collects and uploads diagnostic data. Nothing in Windows depends on it.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "dmwappushservice",
            DisplayName = "Device Management WAP Push",
            Description = "Routes telemetry for mobile device management. Unused outside managed fleets.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "WerSvc",
            DisplayName = "Windows Error Reporting",
            Description = "Uploads crash reports to Microsoft. Local crash dumps still get written without it.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "DPS",
            DisplayName = "Diagnostic Policy Service",
            Description =
                "Runs the built-in troubleshooters and network diagnostics. Turning it off means the " +
                "'Diagnose problems' buttons stop working.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
        },

        // ── Background indexing and prefetching ─────────────────────────────────

        new()
        {
            ServiceName = "SysMain",
            DisplayName = "SysMain (Superfetch)",
            Description =
                "Preloads frequently used apps into RAM. Designed for mechanical drives; on an NVMe " +
                "SSD it mostly generates background disk activity.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "WSearch",
            DisplayName = "Windows Search",
            Description =
                "Indexes files and mail so Start search is instant. Disabling it removes background " +
                "disk and CPU load but makes searching for files in Explorer and Start much slower.",
            Risk = RiskLevel.Advanced,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "TrkWks",
            DisplayName = "Distributed Link Tracking Client",
            Description = "Keeps shortcuts working when their target moves across NTFS volumes. Rarely needed.",
            Risk = RiskLevel.Safe,
        },

        // ── Delivery and updates ────────────────────────────────────────────────

        new()
        {
            ServiceName = "DoSvc",
            DisplayName = "Delivery Optimization",
            Description =
                "Shares downloaded updates with other PCs over your connection, including upload " +
                "bandwidth. Windows Update still works without it, just without peer caching.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "PushToInstall",
            DisplayName = "Windows PushToInstall",
            Description = "Lets the Store install apps remotely from another device. Unused by most people.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "edgeupdate",
            DisplayName = "Microsoft Edge Update",
            Description = "Polls for Edge updates on a timer. Edge still updates when you launch it.",
            Risk = RiskLevel.Safe,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "MicrosoftEdgeElevationService",
            DisplayName = "Microsoft Edge Elevation Service",
            Description = "Helper that applies Edge updates with elevated rights.",
            Risk = RiskLevel.Safe,
            DisabledStartMode = ServiceStartMode.Manual,
        },

        // ── Xbox ────────────────────────────────────────────────────────────────

        new()
        {
            ServiceName = "XblAuthManager",
            DisplayName = "Xbox Live Auth Manager",
            Description =
                "Signs you in to Xbox Live. Required for Game Pass titles and anything using the " +
                "Xbox identity; safe to disable if you only play on Steam, Epic or GOG.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "XblGameSave",
            DisplayName = "Xbox Live Game Save",
            Description = "Syncs Xbox cloud saves. Only matters for Microsoft Store and Game Pass games.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "XboxNetApiSvc",
            DisplayName = "Xbox Live Networking",
            Description = "Handles Xbox Live multiplayer networking for Store games.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "XboxGipSvc",
            DisplayName = "Xbox Accessory Management",
            Description =
                "Manages Xbox controller firmware and pairing. Controllers still work as generic " +
                "XInput devices without it, but firmware updates do not.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
        },

        // ── Connected devices and sync ──────────────────────────────────────────

        new()
        {
            ServiceName = "CDPSvc",
            DisplayName = "Connected Devices Platform",
            Description = "Discovers nearby devices for Nearby Sharing and Phone Link.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "CDPUserSvc",
            DisplayName = "Connected Devices Platform (per user)",
            Description = "Per-session half of the connected devices platform.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
            IsPerUserService = true,
        },
        new()
        {
            ServiceName = "OneSyncSvc",
            DisplayName = "Sync Host",
            Description =
                "Syncs Mail, People and Calendar in the background. Disabling it stops the built-in " +
                "Mail and Calendar apps updating.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
            IsPerUserService = true,
        },
        new()
        {
            ServiceName = "cbdhsvc",
            DisplayName = "Clipboard User Service",
            Description = "Backs clipboard history and cross-device clipboard. Win+V stops working without it.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
            IsPerUserService = true,
        },
        new()
        {
            ServiceName = "PhoneSvc",
            DisplayName = "Phone Service",
            Description = "Manages telephony state on devices with a cellular modem.",
            Risk = RiskLevel.Safe,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "WpnService",
            DisplayName = "Windows Push Notifications",
            Description =
                "Delivers toast notifications. Disabling it silences notifications from every app, " +
                "including Discord, Steam and Teams.",
            Risk = RiskLevel.Advanced,
            DisabledStartMode = ServiceStartMode.Manual,
        },

        // ── Hardware you may not have ───────────────────────────────────────────

        new()
        {
            ServiceName = "lfsvc",
            DisplayName = "Geolocation Service",
            Description = "Provides location to apps. Nothing else uses it on a desktop without GPS.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "MapsBroker",
            DisplayName = "Downloaded Maps Manager",
            Description = "Keeps offline maps updated for the Maps app.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "WbioSrvc",
            DisplayName = "Windows Biometric Service",
            Description =
                "Drives fingerprint and face sign-in. Only disable this if you sign in with a PIN " +
                "or password - Windows Hello stops working.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "TabletInputService",
            DisplayName = "Touch Keyboard and Handwriting Panel",
            Description = "Runs the on-screen keyboard and handwriting input. Unused on a desktop with a real keyboard.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "stisvc",
            DisplayName = "Windows Image Acquisition",
            Description = "Talks to scanners and cameras over WIA.",
            Risk = RiskLevel.Safe,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "Spooler",
            DisplayName = "Print Spooler",
            Description =
                "Required for any printing, including print-to-PDF. Only turn this off on a machine " +
                "with no printer at all.",
            Risk = RiskLevel.Advanced,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "PrintNotify",
            DisplayName = "Printer Extensions and Notifications",
            Description = "Shows printer status popups. Printing works without it.",
            Risk = RiskLevel.Safe,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "SEMgrSvc",
            DisplayName = "Payments and NFC",
            Description = "Handles NFC-based payments. Only present on machines with NFC hardware.",
            Risk = RiskLevel.Safe,
            DisabledStartMode = ServiceStartMode.Manual,
        },
        new()
        {
            ServiceName = "WalletService",
            DisplayName = "Wallet Service",
            Description = "Storage for payment cards and passes.",
            Risk = RiskLevel.Safe,
            DisabledStartMode = ServiceStartMode.Manual,
        },

        // ── Legacy and remote access ────────────────────────────────────────────

        new()
        {
            ServiceName = "RemoteRegistry",
            DisplayName = "Remote Registry",
            Description = "Lets other machines edit this registry over the network. Off by default and best left off.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "Fax",
            DisplayName = "Fax",
            Description = "Fax support. Present for compatibility; almost never used.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "RetailDemo",
            DisplayName = "Retail Demo Service",
            Description = "Shop-floor demo mode. Never needed on a machine you own.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "WMPNetworkSvc",
            DisplayName = "Windows Media Player Network Sharing",
            Description = "Streams the Media Player library to devices on the network.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "AJRouter",
            DisplayName = "AllJoyn Router",
            Description = "Routes AllJoyn IoT device traffic. Effectively dead technology.",
            Risk = RiskLevel.Safe,
        },
        new()
        {
            ServiceName = "SharedAccess",
            DisplayName = "Internet Connection Sharing",
            Description = "Shares this machine's internet connection with other devices. Also used by Mobile hotspot.",
            Risk = RiskLevel.Moderate,
            DisabledStartMode = ServiceStartMode.Manual,
        },

        // ── Windows 11 only ─────────────────────────────────────────────────────

        new()
        {
            ServiceName = "MessagingService",
            DisplayName = "Messaging Service",
            Description = "Per-user SMS and messaging support for devices with a cellular modem.",
            Risk = RiskLevel.Safe,
            DisabledStartMode = ServiceStartMode.Manual,
            IsPerUserService = true,
            MinBuild = Windows11,
        },
        new()
        {
            ServiceName = "PimIndexMaintenanceSvc",
            DisplayName = "Contact Data Indexing",
            Description = "Indexes contacts for the People app.",
            Risk = RiskLevel.Safe,
            DisabledStartMode = ServiceStartMode.Manual,
            IsPerUserService = true,
            MinBuild = Windows11,
        },
    ];

    public static ServiceDefinition? Find(string serviceName) =>
        All.FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
}
