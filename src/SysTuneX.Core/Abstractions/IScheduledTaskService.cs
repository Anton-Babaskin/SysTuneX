using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Reads and changes Windows scheduled tasks.
///
/// Batched on purpose: every call starts PowerShell, which costs the best part of a second, so
/// asking about six tasks is one call rather than six.
/// </summary>
public interface IScheduledTaskService
{
    /// <summary>
    /// The state of each task that exists. Tasks absent from this build are simply absent from the
    /// result - an edition of Windows that never shipped a task is not an error.
    /// </summary>
    Task<IReadOnlyList<ScheduledTaskInfo>> GetStateAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);

    /// <summary>Enables or disables every task named, reporting the ones that refused.</summary>
    Task<OperationResult> SetEnabledAsync(
        IReadOnlyList<string> paths,
        bool enabled,
        CancellationToken cancellationToken = default);
}
