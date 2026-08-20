using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Diagnostics;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="ISnapshotService"/>
[SupportedOSPlatform("windows")]
public sealed class SnapshotService : ISnapshotService
{
    /// <summary>Enough to cover a testing session; older ones are dropped rather than kept forever.</summary>
    private const int MaxSnapshots = 40;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ILogger<SnapshotService> _logger;
    private readonly ITweakEngine _tweaks;
    private readonly IServiceManager _services;
    private readonly IPowerService _power;
    private readonly ISystemInfoService _systemInfo;
    private readonly string _file;
    private readonly List<SystemStateSnapshot> _snapshots = [];

    public SnapshotService(
        ILogger<SnapshotService> logger,
        ITweakEngine tweaks,
        IServiceManager services,
        IPowerService power,
        ISystemInfoService systemInfo,
        string? dataDirectory = null)
    {
        _logger = logger;
        _tweaks = tweaks;
        _services = services;
        _power = power;
        _systemInfo = systemInfo;
        _file = Path.Combine(dataDirectory ?? AppPaths.DataDirectory, "snapshots.json");
    }

    public IReadOnlyList<SystemStateSnapshot> Snapshots
    {
        get
        {
            lock (_snapshots)
            {
                return [.. _snapshots.OrderByDescending(s => s.CapturedAt)];
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_file))
            {
                return;
            }

            await using FileStream stream = File.OpenRead(_file);
            List<SystemStateSnapshot> loaded = await JsonSerializer
                .DeserializeAsync<List<SystemStateSnapshot>>(stream, Json, cancellationToken)
                .ConfigureAwait(false) ?? [];

            lock (_snapshots)
            {
                _snapshots.Clear();
                _snapshots.AddRange(loaded);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saved snapshots could not be read: {Path}", _file);
        }
    }

    public async Task<SystemStateSnapshot> CaptureAsync(string label, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TweakDefinition> supported = _tweaks.GetSupportedTweaks();

        var applied = new List<string>();
        foreach (TweakDefinition tweak in supported)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_tweaks.GetStatus(tweak) == TweakStatus.Applied)
            {
                applied.Add(tweak.Id);
            }
        }

        IReadOnlyList<(ServiceDefinition Definition, ServiceSnapshot State)> services =
            await _services.GetManagedServicesAsync(cancellationToken).ConfigureAwait(false);

        PowerScheme? scheme = await _power.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        SystemSnapshot live = _systemInfo.GetSnapshot();

        var snapshot = new SystemStateSnapshot
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = string.IsNullOrWhiteSpace(label) ? DateTime.Now.ToString("g", CultureInfo.CurrentCulture) : label.Trim(),
            AppliedTweakIds = applied,
            RunningServices = [.. services.Where(s => s.State.IsRunning).Select(s => s.Definition.ServiceName)],
            PowerScheme = scheme?.Guid ?? Guid.Empty,
            PowerSchemeName = scheme?.Name ?? string.Empty,
            RamUsedMb = live.RamUsedMb,
            RamTotalMb = live.RamTotalMb,
            StandbyMb = live.StandbyMemoryMb,
            ProcessCount = live.ProcessCount,
            CpuUsagePercent = live.CpuUsagePercent,
        };

        lock (_snapshots)
        {
            _snapshots.Add(snapshot);

            // Oldest first out, so a long testing session cannot grow the file without bound.
            while (_snapshots.Count > MaxSnapshots)
            {
                SystemStateSnapshot oldest = _snapshots.OrderBy(s => s.CapturedAt).First();
                _snapshots.Remove(oldest);
            }
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Snapshot '{Label}': {Tweaks} tweaks applied, {Services} services running, scheme {Scheme}",
            snapshot.Label,
            applied.Count,
            snapshot.RunningServices.Count,
            snapshot.PowerSchemeName);

        return snapshot;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_snapshots)
        {
            _snapshots.RemoveAll(s => string.Equals(s.Id, id, StringComparison.Ordinal));
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        lock (_snapshots)
        {
            _snapshots.Clear();
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public SnapshotComparison Compare(SystemStateSnapshot before, SystemStateSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        // Order by time rather than by argument, so picking them the wrong way round in the
        // interface still reads as "this became that".
        if (after.CapturedAt < before.CapturedAt)
        {
            (before, after) = (after, before);
        }

        var changes = new List<SnapshotChange>();

        AddSetDifferences(changes, "Tweak", before.AppliedTweakIds, after.AppliedTweakIds, "applied", "not applied");
        AddSetDifferences(changes, "Service", before.RunningServices, after.RunningServices, "running", "stopped");

        if (before.PowerScheme != after.PowerScheme)
        {
            changes.Add(new SnapshotChange("Power", "scheme", before.PowerSchemeName, after.PowerSchemeName));
        }

        AddNumericChange(changes, "Memory", "used", before.RamUsedMb, after.RamUsedMb, "MB");
        AddNumericChange(changes, "Memory", "standby", before.StandbyMb, after.StandbyMb, "MB");
        AddNumericChange(changes, "System", "processes", before.ProcessCount, after.ProcessCount, string.Empty);

        return new SnapshotComparison(before, after, changes);
    }

    /// <summary>Reports what entered the set and what left it, rather than the counts.</summary>
    private static void AddSetDifferences(
        List<SnapshotChange> changes,
        string category,
        IReadOnlyList<string> before,
        IReadOnlyList<string> after,
        string presentLabel,
        string absentLabel)
    {
        var wasThere = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
        var isThere = new HashSet<string>(after, StringComparer.OrdinalIgnoreCase);

        foreach (string added in isThere.Except(wasThere, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            changes.Add(new SnapshotChange(category, added, absentLabel, presentLabel));
        }

        foreach (string removed in wasThere.Except(isThere, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            changes.Add(new SnapshotChange(category, removed, presentLabel, absentLabel));
        }
    }

    /// <summary>
    /// Memory and process counts move on their own, so a change is only worth reporting once it
    /// is bigger than the noise. Listing a 3 MB difference as an effect of a tweak would be a lie
    /// dressed as data.
    /// </summary>
    private static void AddNumericChange(
        List<SnapshotChange> changes,
        string category,
        string item,
        long before,
        long after,
        string unit)
    {
        long delta = Math.Abs(after - before);
        long threshold = Math.Max(16, before / 20);

        if (delta < threshold)
        {
            return;
        }

        string suffix = string.IsNullOrEmpty(unit) ? string.Empty : " " + unit;
        changes.Add(new SnapshotChange(category, item, $"{before}{suffix}", $"{after}{suffix}"));
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);

            List<SystemStateSnapshot> copy;
            lock (_snapshots)
            {
                copy = [.. _snapshots];
            }

            await using FileStream stream = File.Create(_file);
            await JsonSerializer.SerializeAsync(stream, copy, Json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snapshots could not be saved");
        }
    }
}
