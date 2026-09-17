using SysTuneX.Core.Services;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Runs an external console tool and hands back what it printed.
///
/// The seam that makes half of Core testable. Everything SysTuneX cannot do through an API it does
/// by running powercfg, netsh, bcdedit or PowerShell and reading the result - and the reading is
/// what goes wrong, because those tools print in the user's language and their output shape is not
/// a contract. With a static runner none of that could be reached from a test; a comment in
/// PowerService used to say so out loud.
/// </summary>
public interface IProcessRunner
{
    /// <param name="timeout">Thirty seconds when not given. The process is killed when it expires.</param>
    Task<ProcessRunResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a PowerShell command with the user's profile skipped, so a profile that prints a banner
    /// or changes the output encoding cannot break parsing.
    /// </summary>
    Task<ProcessRunResult> RunPowerShellAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
