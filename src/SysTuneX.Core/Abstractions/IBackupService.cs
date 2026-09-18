using Microsoft.Win32;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Records what something was before SysTuneX changed it, and finds it again to put it back.
///
/// The half used by everything that touches the machine. "Record before write" is the line this
/// whole application is built on: a rollback restores the value this PC actually had, never an
/// invented default.
/// </summary>
public interface IChangeJournalWriter
{
    Task RecordRegistryAsync(string ownerId, string keyPath, string valueName, object? currentValue, RegistryValueKind kind, CancellationToken cancellationToken = default);

    Task RecordServiceAsync(string ownerId, string serviceName, ServiceStartMode startMode, bool wasRunning, CancellationToken cancellationToken = default);

    Task RecordPowerSchemeAsync(string ownerId, Guid activeScheme, CancellationToken cancellationToken = default);

    Task RecordDnsAsync(string ownerId, string adapterId, bool usedDhcp, IReadOnlyList<string> servers, CancellationToken cancellationToken = default);

    /// <summary>For a change whose shape none of the above fits.</summary>
    Task RecordRawAsync(BackupEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// The entry recording what this target was before, or null when SysTuneX never touched it.
    ///
    /// A read, but a writer's read: it is how a revert finds what to put back, and every caller is
    /// something that changes the machine.
    /// </summary>
    BackupEntry? FindActive(BackupKind kind, string target, string valueName = "");

    /// <summary>Retires entries once what they describe has been put back.</summary>
    Task MarkRevertedAsync(IEnumerable<string> entryIds, CancellationToken cancellationToken = default);

    Task MarkRevertedAsync(BackupKind kind, string target, string valueName = "", CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the journal without being able to add to it.
///
/// The half used by the history page and the diagnostics report, which show what was changed and
/// have no business recording anything. Split because a fake for them is three members rather than
/// eleven, of which the old fake left six empty.
/// </summary>
public interface IChangeJournalReader
{
    /// <summary>Everything ever recorded, reverted entries included.</summary>
    IReadOnlyList<BackupEntry> GetAll();

    /// <summary>What is still in force - recorded and not yet put back.</summary>
    IReadOnlyList<BackupEntry> GetActive();

    /// <summary>Writes the journal out as a file a bug report can carry.</summary>
    Task<OperationResult> ExportAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>Both halves plus the lifecycle, for the one implementation and for the container.</summary>
public interface IBackupService : IChangeJournalWriter, IChangeJournalReader
{
    /// <summary>Reads the journal from disk. Called once, at start-up.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);
}
