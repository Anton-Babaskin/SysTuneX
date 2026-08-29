using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tests.Fakes;

/// <summary>
/// Shared ordering log. Record-before-write is the line the whole rollback story depends on, and
/// the only way to prove it is to watch the two calls happen in order.
/// </summary>
public sealed class CallTrace
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public void Add(string call)
    {
        lock (_calls)
        {
            _calls.Add(call);
        }
    }
}

/// <summary>A journal held in memory, with the recorded entries visible to the test.</summary>
public sealed class FakeBackupService : IBackupService
{
    private readonly List<BackupEntry> _entries = [];

    public FakeBackupService(CallTrace? trace = null) => Trace = trace;

    public CallTrace? Trace { get; }

    public List<string> RevertedIds { get; } = [];

    /// <summary>Pre-loads a journal entry, standing in for a change made in an earlier session.</summary>
    public FakeBackupService Record(string keyPath, string valueName, string? originalValue, RegistryValueKind kind = RegistryValueKind.DWord)
    {
        _entries.Add(new BackupEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = BackupKind.RegistryValue,
            Target = keyPath,
            ValueName = valueName,
            OriginalValue = originalValue,
            OriginalValueKind = kind,
        });

        return this;
    }

    public Task RecordRegistryAsync(
        string ownerId,
        string keyPath,
        string valueName,
        object? currentValue,
        RegistryValueKind kind,
        CancellationToken cancellationToken = default)
    {
        Trace?.Add($"record {keyPath}\\{valueName}={currentValue?.ToString() ?? "<absent>"}");

        _entries.Add(new BackupEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = BackupKind.RegistryValue,
            OwnerId = ownerId,
            Target = keyPath,
            ValueName = valueName,
            OriginalValue = currentValue is null ? null : RegistryValueComparerAccess.Stringify(currentValue),
            OriginalValueKind = kind,
        });

        return Task.CompletedTask;
    }

    public Task RecordServiceAsync(string ownerId, string serviceName, ServiceStartMode startMode, bool wasRunning, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RecordPowerSchemeAsync(string ownerId, Guid activeScheme, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RecordDnsAsync(string ownerId, string adapterId, bool usedDhcp, IReadOnlyList<string> servers, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RecordRawAsync(BackupEntry entry, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public BackupEntry? FindActive(BackupKind kind, string target, string valueName = "") =>
        _entries.LastOrDefault(e =>
            e.Kind == kind &&
            e.IsActive &&
            string.Equals(e.Target, target, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.ValueName, valueName, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<BackupEntry> GetAll() => [.. _entries];

    public IReadOnlyList<BackupEntry> GetActive() => [.. _entries.Where(e => e.IsActive)];

    public Task MarkRevertedAsync(IEnumerable<string> entryIds, CancellationToken cancellationToken = default)
    {
        foreach (string id in entryIds)
        {
            RevertedIds.Add(id);

            int index = _entries.FindIndex(e => e.Id == id);
            if (index >= 0)
            {
                _entries[index] = _entries[index] with { RevertedAt = DateTimeOffset.Now };
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkRevertedAsync(BackupKind kind, string target, string valueName = "", CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<OperationResult> ExportAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Ok());
}

/// <summary>A registry that logs its writes into the shared trace and can refuse them.</summary>
public sealed class TracingRegistryService : IRegistryService
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

    public TracingRegistryService(CallTrace? trace = null) => Trace = trace;

    public CallTrace? Trace { get; }

    /// <summary>Paths that fail to write, standing in for a key an administrator token cannot reach.</summary>
    public HashSet<string> ReadOnlyPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Deleted { get; } = [];

    public TracingRegistryService Set(string keyPath, string valueName, object value)
    {
        _values[Key(keyPath, valueName)] = value;
        return this;
    }

    public object? GetValue(string keyPath, string valueName) =>
        _values.TryGetValue(Key(keyPath, valueName), out object? value) ? value : null;

    public (object? Value, RegistryValueKind Kind) GetValueWithKind(string keyPath, string valueName) =>
        (GetValue(keyPath, valueName), RegistryValueKind.DWord);

    public OperationResult SetValue(string keyPath, string valueName, object value, RegistryValueKind kind)
    {
        string key = Key(keyPath, valueName);
        Trace?.Add($"write {key}={value}");

        if (ReadOnlyPaths.Contains(keyPath))
        {
            return OperationResult.Fail(CoreMessages.RegistryAccessDeniedWrite, keyPath, valueName);
        }

        bool changed = !_values.TryGetValue(key, out object? existing) || !Equals(existing, value);
        _values[key] = value;

        return changed ? OperationResult.Ok() : OperationResult.NoChange();
    }

    public OperationResult DeleteValue(string keyPath, string valueName)
    {
        string key = Key(keyPath, valueName);
        Trace?.Add($"delete {key}");
        Deleted.Add(key);

        return _values.Remove(key) ? OperationResult.Ok() : OperationResult.NoChange();
    }

    public bool KeyExists(string keyPath) =>
        _values.Keys.Any(k => k.StartsWith(keyPath + "\\", StringComparison.OrdinalIgnoreCase));

    public bool ValueExists(string keyPath, string valueName) => GetValue(keyPath, valueName) is not null;

    public IReadOnlyList<string> GetSubKeyNames(string keyPath) => [];

    private static string Key(string keyPath, string valueName) => $"{keyPath}\\{valueName}";
}

/// <summary>A handler whose calls and outcome the test decides.</summary>
public sealed class FakeTweakHandler(string key, OperationResult? apply = null, OperationResult? revert = null)
    : ISpecialTweakHandler
{
    public string Key { get; } = key;

    public int Applied { get; private set; }

    public int Reverted { get; private set; }

    public TweakStatus Status { get; set; } = TweakStatus.NotApplied;

    public Task<TweakStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);

    public Task<OperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        Applied++;
        return Task.FromResult(apply ?? OperationResult.Ok());
    }

    public Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        Reverted++;
        return Task.FromResult(revert ?? OperationResult.Ok());
    }
}

/// <summary>
/// The journal stores values as strings through an internal helper; the fake has to agree with
/// it or a round trip through the fake would prove nothing about the real one.
/// </summary>
internal static class RegistryValueComparerAccess
{
    public static string Stringify(object value) => SysTuneX.Core.Services.RegistryValueComparer.Stringify(value);
}
