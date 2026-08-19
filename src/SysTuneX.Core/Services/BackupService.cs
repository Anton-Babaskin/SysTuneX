using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <summary>
/// Append-only journal of pre-change state, persisted as JSON next to the app data.
///
/// The old build reverted tweaks by writing a hard-coded "default" value, which is exactly
/// what the project's own safety rules forbid: if a machine had HAGS on before SysTuneX ran,
/// reverting used to turn it off. Now every write is preceded by a record of what was there,
/// and revert restores that.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<BackupService> _logger;
    private readonly IEnvironmentService _environment;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<BackupEntry> _entries = [];

    private bool _loaded;

    public BackupService(ILogger<BackupService> logger, IEnvironmentService environment)
    {
        _logger = logger;
        _environment = environment;
    }

    private string JournalPath => Path.Combine(_environment.DataDirectory, "backup.json");

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;

            if (!File.Exists(JournalPath))
            {
                return;
            }

            await using FileStream stream = File.OpenRead(JournalPath);
            List<BackupEntry>? loaded = await JsonSerializer
                .DeserializeAsync<List<BackupEntry>>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (loaded is not null)
            {
                _entries.AddRange(loaded);
            }

            _logger.LogInformation("Loaded {Count} backup entries", _entries.Count);
        }
        catch (Exception ex)
        {
            // A corrupt journal must not stop the app from starting; it is kept aside for inspection.
            _logger.LogError(ex, "Backup journal could not be read, starting a fresh one");
            TryQuarantineJournal();
            _entries.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RecordRegistryAsync(
        string ownerId,
        string keyPath,
        string valueName,
        object? currentValue,
        RegistryValueKind kind,
        CancellationToken cancellationToken = default)
    {
        var entry = new BackupEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = BackupKind.RegistryValue,
            OwnerId = ownerId,
            Target = keyPath,
            ValueName = valueName,
            OriginalValue = currentValue is null ? null : RegistryValueComparer.Stringify(currentValue),
            OriginalValueKind = currentValue is null ? RegistryValueKind.Unknown : kind,
        };

        return AddIfFirstAsync(entry, cancellationToken);
    }

    public Task RecordServiceAsync(
        string ownerId,
        string serviceName,
        ServiceStartMode startMode,
        bool wasRunning,
        CancellationToken cancellationToken = default)
    {
        var entry = new BackupEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = BackupKind.ServiceConfiguration,
            OwnerId = ownerId,
            Target = serviceName,
            OriginalStartMode = startMode,
            OriginalWasRunning = wasRunning,
        };

        return AddIfFirstAsync(entry, cancellationToken);
    }

    public Task RecordPowerSchemeAsync(string ownerId, Guid activeScheme, CancellationToken cancellationToken = default)
    {
        var entry = new BackupEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = BackupKind.PowerScheme,
            OwnerId = ownerId,
            Target = "ActiveScheme",
            OriginalValue = activeScheme.ToString(),
        };

        return AddIfFirstAsync(entry, cancellationToken);
    }

    public Task RecordDnsAsync(
        string ownerId,
        string adapterId,
        bool usedDhcp,
        IReadOnlyList<string> servers,
        CancellationToken cancellationToken = default)
    {
        var entry = new BackupEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = BackupKind.DnsConfiguration,
            OwnerId = ownerId,
            Target = adapterId,
            // "dhcp" and an explicit server list are different states, so both are encoded.
            OriginalValue = usedDhcp ? "dhcp" : string.Join(',', servers),
        };

        return AddIfFirstAsync(entry, cancellationToken);
    }

    public Task RecordRawAsync(BackupEntry entry, CancellationToken cancellationToken = default) =>
        AddIfFirstAsync(entry, cancellationToken);

    public BackupEntry? FindActive(BackupKind kind, string target, string valueName = "")
    {
        string key = $"{kind}|{target}|{valueName}".ToUpperInvariant();

        lock (_entries)
        {
            return _entries
                .Where(e => e.IsActive && e.TargetKey == key)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<BackupEntry> GetAll()
    {
        lock (_entries)
        {
            return _entries.OrderByDescending(e => e.CreatedAt).ToList();
        }
    }

    public IReadOnlyList<BackupEntry> GetActive()
    {
        lock (_entries)
        {
            return _entries.Where(e => e.IsActive).OrderByDescending(e => e.CreatedAt).ToList();
        }
    }

    public async Task MarkRevertedAsync(IEnumerable<string> entryIds, CancellationToken cancellationToken = default)
    {
        var ids = entryIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
        {
            return;
        }

        lock (_entries)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (ids.Contains(_entries[i].Id) && _entries[i].IsActive)
                {
                    _entries[i] = _entries[i] with { RevertedAt = DateTimeOffset.Now };
                }
            }
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task MarkRevertedAsync(BackupKind kind, string target, string valueName = "", CancellationToken cancellationToken = default)
    {
        string key = $"{kind}|{target}|{valueName}".ToUpperInvariant();
        List<string> ids;

        lock (_entries)
        {
            ids = _entries.Where(e => e.IsActive && e.TargetKey == key).Select(e => e.Id).ToList();
        }

        return MarkRevertedAsync(ids, cancellationToken);
    }

    public async Task<OperationResult> ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            List<BackupEntry> snapshot;
            lock (_entries)
            {
                snapshot = [.. _entries];
            }

            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using FileStream stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(filePath);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Export failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Records an entry only when nothing is already on file for that target. Re-applying a
    /// tweak must not overwrite the machine's genuine original value with SysTuneX's own.
    /// </summary>
    private async Task AddIfFirstAsync(BackupEntry entry, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(false);

        lock (_entries)
        {
            if (_entries.Any(e => e.IsActive && e.TargetKey == entry.TargetKey))
            {
                return;
            }

            _entries.Add(entry);
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_environment.DataDirectory);

            List<BackupEntry> snapshot;
            lock (_entries)
            {
                snapshot = [.. _entries];
            }

            // Write to a temp file and swap, so a crash mid-write cannot leave a truncated journal.
            string tempPath = JournalPath + ".tmp";
            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, JournalPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not persist the backup journal");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void TryQuarantineJournal()
    {
        try
        {
            if (File.Exists(JournalPath))
            {
                File.Move(JournalPath, $"{JournalPath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not move the corrupt journal aside");
        }
    }
}
