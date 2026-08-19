using Microsoft.Extensions.Logging;

namespace SysTuneX.Core.Diagnostics;

/// <summary>
/// The file log's level, changeable while the app runs.
///
/// Verbose logging is worth having on hand when something misbehaves on a machine that is not
/// the developer's, and worth being off the rest of the time — so it is a setting rather than
/// a rebuild.
/// </summary>
public sealed class LogLevelSwitch
{
    public LogLevel Minimum { get; set; } = LogLevel.Information;

    public bool IsVerbose
    {
        get => Minimum <= LogLevel.Debug;
        set => Minimum = value ? LogLevel.Debug : LogLevel.Information;
    }
}
