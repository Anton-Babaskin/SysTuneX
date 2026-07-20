using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

public class ServiceManager : IServiceManager
{
    private readonly ILogger<ServiceManager> _logger;

    private static readonly (string Name, string Display, string Desc, RiskLevel Risk)[] ManagedServiceDefs =
    [
        ("DiagTrack", "Connected User Experiences and Telemetry", "Microsoft telemetry collection service", RiskLevel.Safe),
        ("SysMain", "SysMain (Superfetch)", "Preloads apps into RAM — wastes memory for gaming", RiskLevel.Safe),
        ("WSearch", "Windows Search", "Background indexing — uses CPU and disk I/O", RiskLevel.Moderate),
        ("Fax", "Fax", "Fax service — unused on modern systems", RiskLevel.Safe),
        ("lfsvc", "Geolocation Service", "Location tracking service", RiskLevel.Safe),
        ("MapsBroker", "Downloaded Maps Manager", "Manages offline maps", RiskLevel.Safe),
        ("XblAuthManager", "Xbox Live Auth Manager", "Xbox Live authentication", RiskLevel.Safe),
        ("XblGameSave", "Xbox Live Game Save", "Xbox Live cloud saves sync", RiskLevel.Safe),
        ("XboxGipSvc", "Xbox Accessory Management", "Xbox controller management", RiskLevel.Moderate),
        ("XboxNetApiSvc", "Xbox Live Networking", "Xbox Live network service", RiskLevel.Safe),
        ("RetailDemo", "Retail Demo Service", "Store demo mode", RiskLevel.Safe),
        ("WMPNetworkSvc", "Windows Media Player Network Sharing", "Media streaming", RiskLevel.Safe),
        ("PhoneSvc", "Phone Service", "Telephony state management", RiskLevel.Safe),
        ("PrintNotify", "Printer Extensions and Notifications", "Printer notifications", RiskLevel.Moderate),
        ("RemoteRegistry", "Remote Registry", "Remote registry editing — security risk", RiskLevel.Safe),
        ("dmwappushservice", "WAP Push Message Routing", "Telemetry routing service", RiskLevel.Safe),
        ("TabletInputService", "Touch Keyboard and Handwriting Panel", "Tablet input — not needed on desktop", RiskLevel.Moderate),
        ("WerSvc", "Windows Error Reporting", "Crash reporting to Microsoft", RiskLevel.Safe),

        // Windows 11 specific
        ("Widgets", "Windows Widgets Service", "Win11 Widgets — fetches live news/weather in background, wastes CPU and network", RiskLevel.Safe),
        ("WpnService", "Windows Push Notification", "Push notification system — not needed when gaming", RiskLevel.Moderate),
        ("cbdhsvc", "Clipboard User Service", "Cloud clipboard sync with Microsoft servers", RiskLevel.Moderate),
        ("WbioSrvc", "Windows Biometric Service", "Fingerprint/face recognition — not needed on desktops without biometrics", RiskLevel.Moderate),
        ("OneSyncSvc", "Sync Host Service", "Syncs mail, contacts, calendar in background — OneDrive sync overhead", RiskLevel.Moderate),
        ("CDPSvc", "Connected Devices Platform", "Connects Windows devices and phone — background scanning", RiskLevel.Safe),
        ("CDPUserSvc", "Connected Devices Platform (User)", "User-level connected devices background service", RiskLevel.Safe),
        ("PushToInstall", "Windows PushToInstall", "Installs apps pushed from Microsoft Store remotely", RiskLevel.Safe),
        ("MicrosoftEdgeElevationService", "Microsoft Edge Elevation Service", "Edge auto-update service running in background", RiskLevel.Safe),
        ("edgeupdate", "Microsoft Edge Update", "Checks for Edge updates — runs periodically", RiskLevel.Safe),
    ];

    public ServiceManager(ILogger<ServiceManager> logger)
    {
        _logger = logger;
    }

    public List<ServiceItem> GetManagedServices()
    {
        var result = new List<ServiceItem>();
        foreach (var def in ManagedServiceDefs)
        {
            var item = new ServiceItem
            {
                ServiceName = def.Name,
                DisplayName = def.Display,
                Description = def.Desc,
                Risk = def.Risk,
                IsRunning = IsServiceRunning(def.Name)
            };
            result.Add(item);
        }
        return result;
    }

    public bool IsServiceRunning(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }

    public bool StopService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Stopped) return true;
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            _logger.LogInformation("Stopped service: {Name}", serviceName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop service: {Name}", serviceName);
            return false;
        }
    }

    public bool StartService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Running) return true;
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            _logger.LogInformation("Started service: {Name}", serviceName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start service: {Name}", serviceName);
            return false;
        }
    }

    public bool SetServiceStartType(string serviceName, bool disabled)
    {
        try
        {
            var startType = disabled ? "disabled" : "demand";
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"config \"{serviceName}\" start= {startType}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set start type for: {Name}", serviceName);
            return false;
        }
    }
}
