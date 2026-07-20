using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SysTuneX.Core.Services;

public class CleanupService : ICleanupService
{
    private readonly ILogger<CleanupService> _logger;

    private static readonly string[] RemovableApps =
    [
        "Microsoft.549981C3F5F10",     // Cortana
        "Microsoft.BingNews",
        "Microsoft.BingWeather",
        "Microsoft.GamingApp",
        "Microsoft.GetHelp",
        "Microsoft.Getstarted",
        "Microsoft.MicrosoftOfficeHub",
        "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.MicrosoftStickyNotes",
        "Microsoft.People",
        "Microsoft.PowerAutomateDesktop",
        "Microsoft.Todos",
        "Microsoft.WindowsAlarms",
        "Microsoft.WindowsFeedbackHub",
        "Microsoft.WindowsMaps",
        "Microsoft.WindowsSoundRecorder",
        "Microsoft.Xbox.TCUI",
        "Microsoft.XboxGameOverlay",
        "Microsoft.XboxGamingOverlay",
        "Microsoft.XboxSpeechToTextOverlay",
        "Microsoft.YourPhone",
        "Microsoft.ZuneMusic",
        "Microsoft.ZuneVideo",
        "Clipchamp.Clipchamp",
        "king.com.CandyCrushSaga",
        "king.com.CandyCrushFriends",
    ];

    public CleanupService(ILogger<CleanupService> logger)
    {
        _logger = logger;
    }

    public long CleanTempFiles()
    {
        var tempPath = Path.GetTempPath();
        return CleanDirectory(tempPath);
    }

    public long CleanWindowsTemp()
    {
        var winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        return CleanDirectory(winTemp);
    }

    public long CleanPrefetch()
    {
        var prefetch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        return CleanDirectory(prefetch);
    }

    public long GetTotalCleanableSize()
    {
        long total = 0;
        total += GetDirectorySize(Path.GetTempPath());
        total += GetDirectorySize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
        total += GetDirectorySize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"));
        return total;
    }

    public List<string> GetRemovableApps()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"Get-AppxPackage | Select-Object -ExpandProperty Name\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd() ?? "";
            process?.WaitForExit(15000);

            var installed = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToHashSet();

            return RemovableApps.Where(a => installed.Contains(a)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list removable apps");
            return [];
        }
    }

    public bool RemoveApp(string packageName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command \"Get-AppxPackage '{packageName}' | Remove-AppxPackage\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            process?.WaitForExit(30000);
            _logger.LogInformation("Removed app: {Name}", packageName);
            return process?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove app: {Name}", packageName);
            return false;
        }
    }

    private long CleanDirectory(string path)
    {
        long freed = 0;
        try
        {
            if (!Directory.Exists(path)) return 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var size = new FileInfo(file).Length;
                    File.Delete(file);
                    freed += size;
                }
                catch { /* skip locked files */ }
            }
            _logger.LogInformation("Cleaned {Size:F1} MB from {Path}", freed / (1024.0 * 1024.0), path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean directory: {Path}", path);
        }
        return freed;
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }
}
