using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SysTuneX.Core.Services;

public class PowerService : IPowerService
{
    private readonly ILogger<PowerService> _logger;
    private const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public PowerService(ILogger<PowerService> logger)
    {
        _logger = logger;
    }

    public bool ActivateUltimatePerformance()
    {
        try
        {
            // First, duplicate the Ultimate Performance plan to make it available
            RunPowerCfg($"duplicatescheme {UltimatePerformanceGuid}");
            // Set it as active
            return RunPowerCfg($"setactive {UltimatePerformanceGuid}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate Ultimate Performance plan");
            return false;
        }
    }

    public bool RestoreBalancedPlan()
    {
        return RunPowerCfg($"setactive {BalancedGuid}");
    }

    public bool DisableHibernation()
    {
        return RunPowerCfg("hibernate off");
    }

    public bool EnableHibernation()
    {
        return RunPowerCfg("hibernate on");
    }

    public string GetActivePowerPlan()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/getactivescheme",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd() ?? "";
            process?.WaitForExit(3000);
            return output.Trim();
        }
        catch
        {
            return "Unknown";
        }
    }

    public bool IsUltimatePerformanceActive()
    {
        return GetActivePowerPlan().Contains(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase);
    }

    private bool RunPowerCfg(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/{ arguments}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "powercfg command failed: {Args}", arguments);
            return false;
        }
    }
}
