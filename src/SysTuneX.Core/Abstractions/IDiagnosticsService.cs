using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Collects everything needed to explain a misbehaving machine into one file.
///
/// The point is that a tester should not have to know which of five folders holds the
/// interesting part - they press one button and send one file.
/// </summary>
public interface IDiagnosticsService
{
    /// <summary>Folder holding the daily log files.</summary>
    string LogDirectory { get; }

    /// <summary>The log file currently being written, or null when nothing has been logged yet.</summary>
    string? CurrentLogFile { get; }

    /// <summary>Verbose logging records every registry read and command line, not just the outcomes.</summary>
    bool IsVerbose { get; set; }

    /// <summary>Writes a report and returns its path.</summary>
    Task<DiagnosticsReport> WriteReportAsync(CancellationToken cancellationToken = default);
}

/// <summary>Where the report landed and what went into it.</summary>
public sealed record DiagnosticsReport(string FilePath, int LogLines, int JournalEntries, OperationResult Result);
