using Microsoft.Extensions.Logging;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.Core.Services;

public class PrivacyService : IPrivacyService
{
    private readonly ILogger<PrivacyService> _logger;
    private static readonly string HostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
    private const string Marker = "# SysTuneX Telemetry Block";

    public PrivacyService(ILogger<PrivacyService> logger)
    {
        _logger = logger;
    }

    public bool BlockTelemetryHosts()
    {
        try
        {
            var lines = File.Exists(HostsPath) ? File.ReadAllLines(HostsPath).ToList() : [];
            if (lines.Any(l => l.Contains(Marker))) return true; // already blocked

            lines.Add("");
            lines.Add(Marker);
            foreach (var host in PrivacyTweaks.TelemetryHosts)
            {
                lines.Add($"0.0.0.0 {host}");
            }
            lines.Add($"{Marker} End");

            File.WriteAllLines(HostsPath, lines);
            _logger.LogInformation("Blocked {Count} telemetry hosts", PrivacyTweaks.TelemetryHosts.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to block telemetry hosts");
            return false;
        }
    }

    public bool UnblockTelemetryHosts()
    {
        try
        {
            if (!File.Exists(HostsPath)) return true;
            var lines = File.ReadAllLines(HostsPath).ToList();
            var startIdx = lines.FindIndex(l => l.Contains(Marker) && !l.Contains("End"));
            var endIdx = lines.FindIndex(l => l.Contains($"{Marker} End"));

            if (startIdx < 0) return true; // nothing to remove
            if (endIdx < 0) endIdx = lines.Count - 1;

            lines.RemoveRange(startIdx, endIdx - startIdx + 1);
            File.WriteAllLines(HostsPath, lines);
            _logger.LogInformation("Unblocked telemetry hosts");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unblock telemetry hosts");
            return false;
        }
    }

    public bool AreHostsBlocked()
    {
        try
        {
            if (!File.Exists(HostsPath)) return false;
            return File.ReadAllText(HostsPath).Contains(Marker);
        }
        catch { return false; }
    }
}
