namespace SysTuneX.Core.Diagnostics;

/// <summary>
/// Where SysTuneX keeps its state on disk.
///
/// The parameterless members are static because the log file has to exist before the container
/// does - the first thing worth logging is the container failing to build. Nothing else should use
/// them: a service that reads a static path cannot be pointed somewhere else, which is how every
/// test of the profile, snapshot and game-mode services ended up reading and writing the real
/// %ProgramData%\SysTuneX on whatever machine ran them. Those take the directory from
/// <c>IEnvironmentService</c> now, and the helpers below derive the rest from it.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// %ProgramData%\SysTuneX - machine-wide, because the journal describes the machine.
    ///
    /// The logger's copy. Everything that runs after the container is built reads the same value
    /// through <c>IEnvironmentService.DataDirectory</c>, which a test can redirect.
    /// </summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SysTuneX");

    /// <summary>For the file logger, which starts before there is anything to inject.</summary>
    public static string LogDirectory { get; } = LogDirectoryIn(DataDirectory);

    public static string LogDirectoryIn(string dataDirectory) => Path.Combine(dataDirectory, "logs");

    public static string ReportDirectoryIn(string dataDirectory) => Path.Combine(dataDirectory, "reports");

    /// <summary>The journal of pre-change values.</summary>
    public static string BackupFileIn(string dataDirectory) => Path.Combine(dataDirectory, "backup.json");

    /// <summary>Unhandled exceptions, written by the app's own crash handler.</summary>
    public static string ErrorLogFileIn(string dataDirectory) => Path.Combine(dataDirectory, "errors.log");

    /// <summary>One log file per day, so a session can be found by date without parsing anything.</summary>
    public static string LogFileFor(DateTime date, string? dataDirectory = null) =>
        Path.Combine(LogDirectoryIn(dataDirectory ?? DataDirectory), $"systunex-{date:yyyyMMdd}.log");
}
