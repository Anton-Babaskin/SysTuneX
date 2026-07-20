using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SysTuneX.Core.Services;

public class NetworkService : INetworkService
{
    private readonly ILogger<NetworkService> _logger;

    public NetworkService(ILogger<NetworkService> logger)
    {
        _logger = logger;
    }

    public bool SetDns(string primary, string secondary)
    {
        try
        {
            var adapter = GetActiveAdapterName();
            if (adapter == null) return false;

            RunNetsh($"interface ip set dns name=\"{adapter}\" static {primary}");
            RunNetsh($"interface ip add dns name=\"{adapter}\" {secondary} index=2");
            RunNetsh("interface ip set dns name=\"Loopback Pseudo-Interface 1\" static 127.0.0.1");

            _logger.LogInformation("Set DNS to {Primary}/{Secondary} on {Adapter}", primary, secondary, adapter);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set DNS");
            return false;
        }
    }

    public bool ResetDns()
    {
        try
        {
            var adapter = GetActiveAdapterName();
            if (adapter == null) return false;

            RunNetsh($"interface ip set dns name=\"{adapter}\" dhcp");
            _logger.LogInformation("Reset DNS to DHCP on {Adapter}", adapter);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset DNS");
            return false;
        }
    }

    public (string Primary, string Secondary) GetCurrentDns()
    {
        try
        {
            var output = RunNetshWithOutput("interface ip show dnsservers");
            var lines = output.Split('\n')
                .Where(l => l.Trim().StartsWith("DNS") || char.IsDigit(l.Trim().FirstOrDefault()))
                .Select(l => l.Trim())
                .ToList();

            // Parse DNS IPs from output
            var ips = output.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Split('.').Length == 4 && l.All(c => char.IsDigit(c) || c == '.' || c == ' '))
                .Select(l => l.Trim())
                .Take(2)
                .ToList();

            return (ips.ElementAtOrDefault(0) ?? "Auto", ips.ElementAtOrDefault(1) ?? "Auto");
        }
        catch
        {
            return ("Auto", "Auto");
        }
    }

    private string? GetActiveAdapterName()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"(Get-NetAdapter | Where-Object {$_.Status -eq 'Up'} | Select-Object -First 1).Name\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            var process = Process.Start(psi);
            var name = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit(5000);
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch { return null; }
    }

    private static void RunNetsh(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var process = Process.Start(psi);
        process?.WaitForExit(5000);
    }

    private static string RunNetshWithOutput(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        var process = Process.Start(psi);
        var output = process?.StandardOutput.ReadToEnd() ?? "";
        process?.WaitForExit(5000);
        return output;
    }
}
