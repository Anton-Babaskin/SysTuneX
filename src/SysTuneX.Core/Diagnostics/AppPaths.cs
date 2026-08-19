namespace SysTuneX.Core.Diagnostics;

/// <summary>
/// Where SysTuneX keeps its state on disk.
///
/// These are static because the log file has to exist before the container does — the first
/// thing worth logging is the container failing to build.
/// </summary>
public static class AppPaths
{
    /// <summary>%ProgramData%\SysTuneX — machine-wide, because the journal describes the machine.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SysTuneX");

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    public static string ReportDirectory { get; } = Path.Combine(DataDirectory, "reports");

    /// <summary>The journal of pre-change values.</summary>
    public static string BackupFile { get; } = Path.Combine(DataDirectory, "backup.json");

    /// <summary>Unhandled exceptions, written by the app's own crash handler.</summary>
    public static string ErrorLogFile { get; } = Path.Combine(DataDirectory, "errors.log");

    /// <summary>One log file per day, so a session can be found by date without parsing anything.</summary>
    public static string LogFileFor(DateTime date) =>
        Path.Combine(LogDirectory, $"systunex-{date:yyyyMMdd}.log");
}
