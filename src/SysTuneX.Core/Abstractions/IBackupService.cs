using Microsoft.Win32;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// The journal of "what this machine looked like before SysTuneX touched it".
/// Every mutating service writes here first; revert reads from here.
/// </summary>
public interface IBackupService
{
    /// <summary>Records the current value of a registry entry, unless an active entry already exists for it.</summary>
    Task RecordRegistryAsync(string ownerId, string keyPath, string valueName, object? currentValue, RegistryValueKind kind, CancellationToken cancellationToken = default);

    Task RecordServiceAsync(string ownerId, string serviceName, ServiceStartMode startMode, bool wasRunning, CancellationToken cancellationToken = default);

    Task RecordPowerSchemeAsync(string ownerId, Guid activeScheme, CancellationToken cancellationToken = default);

    Task RecordDnsAsync(string ownerId, string adapterId, bool usedDhcp, IReadOnlyList<string> servers, CancellationToken cancellationToken = default);

    Task RecordRawAsync(BackupEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Newest entry still in effect for a target, or <see langword="null"/> when nothing was recorded.</summary>
    BackupEntry? FindActive(BackupKind kind, string target, string valueName = "");

    IReadOnlyList<BackupEntry> GetAll();

    IReadOnlyList<BackupEntry> GetActive();

    /// <summary>Marks entries as rolled back so history shows what is still applied.</summary>
    Task MarkRevertedAsync(IEnumerable<string> entryIds, CancellationToken cancellationToken = default);

    Task MarkRevertedAsync(BackupKind kind, string target, string valueName = "", CancellationToken cancellationToken = default);

    /// <summary>Loads the journal from disk. Safe to call more than once.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> ExportAsync(string filePath, CancellationToken cancellationToken = default);
}
