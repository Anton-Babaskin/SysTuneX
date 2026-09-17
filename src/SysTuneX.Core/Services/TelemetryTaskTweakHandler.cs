using System.Runtime.Versioning;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <summary>
/// The scheduled tasks that exist to collect data about how the PC is used.
///
/// These are worth switching off for a reason that is not ideological: the Compatibility Appraiser
/// runs CompatTelRunner.exe, which walks the installed software and can hold a core busy for
/// minutes at a time, and it is not choosy about when. The rest are small, but they are the same
/// kind of thing and they belong in the same switch.
///
/// Nothing here is needed for Windows to work, for updates to install, or for a game to run. What
/// is lost is Microsoft's view of this machine's telemetry - which is the point.
///
/// One tweak rather than seven, because seven switches that people would all set the same way is a
/// list to scroll past, not a choice. Each task is recorded individually so the revert puts back
/// exactly the ones that were on.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TelemetryTaskTweakHandler : ISpecialTweakHandler
{
    private const string OwnerId = "tweak:telemetry_tasks_disable";

    /// <summary>
    /// The tasks, by full path. Every one is documented as telemetry or as part of the Customer
    /// Experience Improvement Program; nothing here touches updates, security or repair. A task
    /// missing on this edition of Windows is skipped rather than reported as a failure.
    /// </summary>
    public static IReadOnlyList<string> Tasks { get; } =
    [
        // Walks installed software and reports compatibility data. The expensive one.
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Application Experience\StartupAppTask",

        // Customer Experience Improvement Program.
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\Autochk\Proxy",

        // Collects disk diagnostic data for the same programme. Disk *health* checks are a
        // different task and are deliberately left alone.
        @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
    ];

    private readonly IScheduledTaskService _tasks;
    private readonly IBackupService _backup;

    public TelemetryTaskTweakHandler(IScheduledTaskService tasks, IBackupService backup)
    {
        _tasks = tasks;
        _backup = backup;
    }

    public string Key => "telemetry_tasks";

    public async Task<TweakStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ScheduledTaskInfo> found = await _tasks
            .GetStateAsync(Tasks, cancellationToken)
            .ConfigureAwait(false);

        List<ScheduledTaskInfo> known =
            [.. found.Where(task => task.State is not ScheduledTaskState.Unknown)];

        // Nothing came back at all: the query failed, or this is not a build that has them. Either
        // way, saying "not applied" would offer a switch that can do nothing.
        if (known.Count == 0)
        {
            return TweakStatus.Unknown;
        }

        int disabled = known.Count(task => task.State is ScheduledTaskState.Disabled);

        return disabled == known.Count
            ? TweakStatus.Applied
            : disabled == 0
                ? TweakStatus.NotApplied
                : TweakStatus.Partial;
    }

    public async Task<OperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ScheduledTaskInfo> found = await _tasks
            .GetStateAsync(Tasks, cancellationToken)
            .ConfigureAwait(false);

        // Only the ones that are actually on. Recording a task that was already off would make the
        // revert switch on something the user had disabled themselves.
        List<ScheduledTaskInfo> toDisable =
            [.. found.Where(task => task.State is ScheduledTaskState.Ready or ScheduledTaskState.Running)];

        if (toDisable.Count == 0)
        {
            return found.Count == 0
                ? OperationResult.Fail(CoreMessages.ScheduledTaskQueryFailed)
                : OperationResult.NoChange();
        }

        foreach (ScheduledTaskInfo task in toDisable)
        {
            await _backup.RecordRawAsync(
                    new BackupEntry
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Kind = BackupKind.ScheduledTask,
                        OwnerId = OwnerId,
                        Target = task.Path,
                        OriginalValue = nameof(ScheduledTaskState.Ready),
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await _tasks
            .SetEnabledAsync([.. toDisable.Select(task => task.Path)], enabled: false, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        // Only what this app switched off. A task the user disabled before ever running SysTuneX
        // has no journal entry and stays off, which is the same rule the service tweaks follow.
        List<BackupEntry> entries =
            [.. Tasks.Select(path => _backup.FindActive(BackupKind.ScheduledTask, path)).OfType<BackupEntry>()];

        if (entries.Count == 0)
        {
            return OperationResult.NoChange();
        }

        OperationResult result = await _tasks
            .SetEnabledAsync([.. entries.Select(entry => entry.Target)], enabled: true, cancellationToken)
            .ConfigureAwait(false);

        if (result.Success)
        {
            await _backup
                .MarkRevertedAsync([.. entries.Select(entry => entry.Id)], cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }
}
