using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Diagnostics;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IDiagnosticsService"/>
[SupportedOSPlatform("windows")]
public sealed class DiagnosticsService : IDiagnosticsService
{
    /// <summary>Enough log to cover a testing session without producing a file nobody will read.</summary>
    private const int LogTailLines = 3000;

    private const int ErrorTailLines = 400;

    private readonly ILogger<DiagnosticsService> _logger;
    private readonly IEnvironmentService _environment;
    private readonly ISystemInfoService _systemInfo;
    private readonly IBackupService _backup;
    private readonly LogLevelSwitch _level;

    public DiagnosticsService(
        ILogger<DiagnosticsService> logger,
        IEnvironmentService environment,
        ISystemInfoService systemInfo,
        IBackupService backup,
        LogLevelSwitch level)
    {
        _logger = logger;
        _environment = environment;
        _systemInfo = systemInfo;
        _backup = backup;
        _level = level;
    }

    public string LogDirectory => AppPaths.LogDirectory;

    public string? CurrentLogFile
    {
        get
        {
            string path = AppPaths.LogFileFor(DateTime.Now);
            return File.Exists(path) ? path : null;
        }
    }

    public bool IsVerbose
    {
        get => _level.IsVerbose;
        set => _level.IsVerbose = value;
    }

    public async Task<DiagnosticsReport> WriteReportAsync(CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(
            AppPaths.ReportDirectory,
            $"systunex-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        var report = new StringBuilder();
        int logLines = 0;
        IReadOnlyList<BackupEntry> journal = [];

        try
        {
            WriteHeader(report);
            await WriteEnvironmentAsync(report, cancellationToken).ConfigureAwait(false);

            journal = _backup.GetAll();
            WriteJournal(report, journal);

            logLines = WriteLog(report);
            WriteErrors(report);

            Directory.CreateDirectory(AppPaths.ReportDirectory);
            await File.WriteAllTextAsync(path, report.ToString(), Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Diagnostics report written to {Path}", path);
            return new DiagnosticsReport(path, logLines, journal.Count, OperationResult.Ok());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write the diagnostics report");
            return new DiagnosticsReport(
                path,
                logLines,
                journal.Count,
                OperationResult.Fail(CoreMessages.DiagnosticsReportFailed, ex, ex.Message));
        }
    }

    private static void WriteHeader(StringBuilder report)
    {
        Version? version = Assembly.GetEntryAssembly()?.GetName().Version;

        Section(report, "SysTuneX diagnostics");
        Field(report, "Generated", DateTimeOffset.Now.ToString("u", CultureInfo.InvariantCulture));
        Field(report, "Version", version?.ToString(3) ?? "unknown");
        Field(report, "Executable", Environment.ProcessPath ?? "unknown");
        Field(report, "Culture", CultureInfo.CurrentUICulture.Name);
    }

    private async Task WriteEnvironmentAsync(StringBuilder report, CancellationToken cancellationToken)
    {
        Section(report, "Environment");
        Field(report, "Elevated", _environment.IsElevated ? "yes" : "NO - every system change will fail");

        WindowsVersionInfo windows = _environment.Windows;
        Field(report, "Windows", windows.ToString());
        Field(report, "Version", windows.FullVersion);
        Field(report, "Edition", string.IsNullOrEmpty(windows.Edition) ? "unknown" : windows.Edition);
        Field(report, "Supported", windows.IsSupported ? "yes" : "NO - below Windows 10 1809");
        Field(report, "Architecture", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString());
        Field(report, "Data directory", _environment.DataDirectory);

        try
        {
            HardwareInfo hardware = await _systemInfo.GetHardwareInfoAsync(cancellationToken).ConfigureAwait(false);

            Section(report, "Hardware");
            Field(report, "CPU", $"{hardware.CpuName} ({hardware.CpuCores}C/{hardware.CpuThreads}T)");
            Field(report, "GPU", $"{hardware.GpuName}, {hardware.GpuVramMb} MB, driver {hardware.GpuDriverVersion}");
            Field(report, "RAM", $"{hardware.RamTotalMb} MB - {hardware.RamSummary}");
            Field(report, "Motherboard", hardware.MotherboardName);
            Field(report, "System drive", hardware.SystemDriveModel);
        }
        catch (Exception ex)
        {
            Section(report, "Hardware");
            report.Append("  Could not be read: ").AppendLine(ex.Message);
        }

        try
        {
            SystemSnapshot snapshot = _systemInfo.GetSnapshot();

            Section(report, "Live counters at report time");
            Field(report, "CPU load", $"{snapshot.CpuUsagePercent:F1} %");
            Field(report, "RAM used", $"{snapshot.RamUsedMb} / {snapshot.RamTotalMb} MB");
            Field(report, "Processes", snapshot.ProcessCount.ToString(CultureInfo.InvariantCulture));
            Field(report, "Uptime", snapshot.Uptime.ToString(@"d\.hh\:mm"));
        }
        catch (Exception ex)
        {
            report.Append("  Live counters could not be read: ").AppendLine(ex.Message);
        }
    }

    private static void WriteJournal(StringBuilder report, IReadOnlyList<BackupEntry> journal)
    {
        Section(report, $"Change journal ({journal.Count} entries, {journal.Count(e => e.IsActive)} still applied)");

        if (journal.Count == 0)
        {
            report.AppendLine("  Nothing recorded - no change has been applied on this machine yet.");
            return;
        }

        foreach (BackupEntry entry in journal.OrderByDescending(e => e.CreatedAt))
        {
            report
                .Append("  ")
                .Append(entry.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Append(entry.IsActive ? "  [applied]  " : "  [reverted] ")
                .Append(entry.Kind)
                .Append(' ')
                .Append(entry.Target);

            if (!string.IsNullOrEmpty(entry.ValueName))
            {
                report.Append('\\').Append(entry.ValueName);
            }

            report
                .Append("  owner=")
                .Append(entry.OwnerId ?? "-")
                .Append("  previous=")
                .AppendLine(Describe(entry));
        }
    }

    /// <summary>
    /// A null original value is the interesting case: it means Windows shipped without the value,
    /// so reverting deletes it. Printing it as an empty string would hide exactly that.
    /// </summary>
    private static string Describe(BackupEntry entry) => entry.Kind switch
    {
        BackupKind.ServiceConfiguration => $"{entry.OriginalStartMode}{(entry.OriginalWasRunning ? ", running" : ", stopped")}",
        _ => entry.OriginalValue is null
            ? "<value did not exist>"
            : $"{entry.OriginalValue} ({entry.OriginalValueKind})",
    };

    private static int WriteLog(StringBuilder report)
    {
        string path = AppPaths.LogFileFor(DateTime.Now);
        Section(report, $"Log tail (last {LogTailLines} lines of {Path.GetFileName(path)})");

        string[] lines = ReadTail(path, LogTailLines);
        if (lines.Length == 0)
        {
            report.AppendLine("  The log is empty or could not be read.");
            return 0;
        }

        foreach (string line in lines)
        {
            report.AppendLine(line);
        }

        return lines.Length;
    }

    private static void WriteErrors(StringBuilder report)
    {
        if (!File.Exists(AppPaths.ErrorLogFile))
        {
            return;
        }

        Section(report, "Unhandled exceptions (errors.log)");
        foreach (string line in ReadTail(AppPaths.ErrorLogFile, ErrorTailLines))
        {
            report.AppendLine(line);
        }
    }

    /// <summary>
    /// Reads the last <paramref name="count"/> lines. Opened shared, because the logger holds
    /// the same file open while this runs.
    /// </summary>
    private static string[] ReadTail(string path, int count)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var tail = new Queue<string>(count);
            while (reader.ReadLine() is { } line)
            {
                if (tail.Count == count)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(line);
            }

            return [.. tail];
        }
        catch
        {
            return [];
        }
    }

    private static void Section(StringBuilder report, string title) =>
        report
            .AppendLine()
            .AppendLine(new string('=', 78))
            .AppendLine(title)
            .AppendLine(new string('=', 78));

    private static void Field(StringBuilder report, string name, string value) =>
        report.Append("  ").Append(name.PadRight(18)).Append(": ").AppendLine(value);
}
