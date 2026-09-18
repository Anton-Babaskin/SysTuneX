using System.Runtime.Versioning;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <summary>
/// Reverts every tweak that has a journal entry.
///
/// First, and that ordering matters: a registry value written by a tweak has to go back through
/// that tweak's own revert, which for a handler-backed tweak is more than one write and may not be
/// a registry write at all. Putting the values back one at a time would leave, say, PCI Express
/// power saving with its index restored and the scheme never re-activated.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TweakChangeRestorer : IChangeRestorer
{
    /// <summary>The prefix every tweak writes into its journal entries' owner id.</summary>
    public const string OwnerPrefix = "tweak:";

    private readonly ITweakEngine _tweaks;

    public TweakChangeRestorer(ITweakEngine tweaks) => _tweaks = tweaks;

    public string Id => "tweaks";

    public int Order => 0;

    public bool Handles(BackupEntry entry) =>
        entry.OwnerId?.StartsWith(OwnerPrefix, StringComparison.Ordinal) == true;

    public async Task<RestoreOutcome> RestoreAsync(
        IReadOnlyList<BackupEntry> entries,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<TweakDefinition> tweaks =
        [
            .. entries
                .Select(entry => entry.OwnerId![OwnerPrefix.Length..])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(_tweaks.Find)
                .OfType<TweakDefinition>(),
        ];

        if (tweaks.Count == 0)
        {
            // Journal entries naming tweaks this build no longer has. Nothing can be done with
            // them, and saying so beats reporting a clean restore.
            return new RestoreOutcome(0, entries.Count, [UnknownTweaks(entries)]);
        }

        BatchResult result = await _tweaks.RevertManyAsync(tweaks, progress, cancellationToken).ConfigureAwait(false);

        return new RestoreOutcome(result.Succeeded, result.Failed, result.Errors);
    }

    private static string UnknownTweaks(IReadOnlyList<BackupEntry> entries) =>
        "The change journal names tweaks this version does not have: " +
        string.Join(", ", entries.Select(e => e.OwnerId).Distinct(StringComparer.Ordinal));
}

/// <summary>Puts every service back to the start type and running state it was found in.</summary>
[SupportedOSPlatform("windows")]
public sealed class ServiceChangeRestorer : IChangeRestorer
{
    private readonly IServiceManager _services;

    public ServiceChangeRestorer(IServiceManager services) => _services = services;

    public string Id => "services";

    public int Order => 10;

    public bool Handles(BackupEntry entry) => entry.Kind == BackupKind.ServiceConfiguration;

    public async Task<RestoreOutcome> RestoreAsync(
        IReadOnlyList<BackupEntry> entries,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string[] names = [.. entries.Select(e => e.Target).Distinct(StringComparer.OrdinalIgnoreCase)];

        int changed = 0;
        int failed = 0;
        var errors = new List<string>();

        for (int i = 0; i < names.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new BatchProgress(names[i], i, names.Length));

            OperationResult result = await _services.RestoreAsync(names[i], cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                changed++;
            }
            else
            {
                failed++;
                errors.Add($"{names[i]}: {result.Message}");
            }
        }

        return new RestoreOutcome(changed, failed, errors);
    }
}

/// <summary>Switches back to the power scheme that was active before SysTuneX changed it.</summary>
[SupportedOSPlatform("windows")]
public sealed class PowerSchemeChangeRestorer : IChangeRestorer
{
    private readonly IPowerService _power;

    public PowerSchemeChangeRestorer(IPowerService power) => _power = power;

    public string Id => "power";

    public int Order => 20;

    public bool Handles(BackupEntry entry) => entry.Kind == BackupKind.PowerScheme;

    public async Task<RestoreOutcome> RestoreAsync(
        IReadOnlyList<BackupEntry> entries,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        OperationResult result = await _power.RestorePreviousSchemeAsync(cancellationToken).ConfigureAwait(false);

        return result.Success
            ? new RestoreOutcome(1, 0, [])
            : new RestoreOutcome(0, 1, [result.Message ?? "The power scheme could not be restored."]);
    }
}

/// <summary>Puts each adapter's resolvers back, DHCP included.</summary>
[SupportedOSPlatform("windows")]
public sealed class DnsChangeRestorer : IChangeRestorer
{
    private readonly INetworkService _network;

    public DnsChangeRestorer(INetworkService network) => _network = network;

    public string Id => "dns";

    public int Order => 30;

    public bool Handles(BackupEntry entry) => entry.Kind == BackupKind.DnsConfiguration;

    public async Task<RestoreOutcome> RestoreAsync(
        IReadOnlyList<BackupEntry> entries,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int changed = 0;
        int failed = 0;
        var errors = new List<string>();

        foreach (BackupEntry entry in entries)
        {
            OperationResult result = await _network.RestoreDnsAsync(entry.Target, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                changed++;
            }
            else
            {
                failed++;
                errors.Add(result.Message ?? "The DNS configuration could not be restored.");
            }
        }

        return new RestoreOutcome(changed, failed, errors);
    }
}

/// <summary>Takes the telemetry host entries back out of the hosts file.</summary>
[SupportedOSPlatform("windows")]
public sealed class HostsChangeRestorer : IChangeRestorer
{
    private readonly IPrivacyService _privacy;

    public HostsChangeRestorer(IPrivacyService privacy) => _privacy = privacy;

    public string Id => "hosts";

    public int Order => 40;

    public bool Handles(BackupEntry entry) => entry.Kind == BackupKind.HostsFile;

    public async Task<RestoreOutcome> RestoreAsync(
        IReadOnlyList<BackupEntry> entries,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        OperationResult result = await _privacy.UnblockTelemetryHostsAsync(cancellationToken).ConfigureAwait(false);

        return result.Success
            ? new RestoreOutcome(1, 0, [])
            : new RestoreOutcome(0, 1, [result.Message ?? "The hosts file could not be restored."]);
    }
}

/// <summary>
/// Registry values nothing else claimed.
///
/// Last on purpose. Most registry entries belong to a tweak and are reverted through it; this
/// catches the ones written by something that is not a tweak, and would otherwise be the kind of
/// entry that sat in the journal looking restorable and never was.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryChangeRestorer : IChangeRestorer
{
    private readonly IRegistryService _registry;
    private readonly IBackupService _backup;

    public RegistryChangeRestorer(IRegistryService registry, IBackupService backup)
    {
        _registry = registry;
        _backup = backup;
    }

    /// <summary>
    /// Puts one value back, or deletes it when the journal recorded something this build can no
    /// longer make sense of - writing a guess would leave the machine in a state it was never in.
    /// </summary>
    private OperationResult Write(BackupEntry entry)
    {
        object? value = RegistryValueComparer.Materialize(entry.OriginalValue!, entry.OriginalValueKind);

        return value is null
            ? _registry.DeleteValue(entry.Target, entry.ValueName)
            : _registry.SetValue(entry.Target, entry.ValueName, value, entry.OriginalValueKind);
    }

    public string Id => "registry";

    public int Order => 90;

    public bool Handles(BackupEntry entry) => entry.Kind == BackupKind.RegistryValue;

    public async Task<RestoreOutcome> RestoreAsync(
        IReadOnlyList<BackupEntry> entries,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int changed = 0;
        int failed = 0;
        var errors = new List<string>();
        var reverted = new List<string>();

        foreach (BackupEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // No recorded value means the value did not exist before, so deleting is the restore.
            // Writing a zero instead would leave the machine in a state Windows never shipped.
            OperationResult result = entry.OriginalValue is null
                ? _registry.DeleteValue(entry.Target, entry.ValueName)
                : Write(entry);

            if (result.Success)
            {
                changed++;
                reverted.Add(entry.Id);
            }
            else
            {
                failed++;
                errors.Add($"{entry.Target}\\{entry.ValueName}: {result.Message}");
            }
        }

        if (reverted.Count > 0)
        {
            await _backup.MarkRevertedAsync(reverted, cancellationToken).ConfigureAwait(false);
        }

        return new RestoreOutcome(changed, failed, errors);
    }
}

/// <summary>Boot configuration, which only <c>bcdedit</c> can put back.</summary>
[SupportedOSPlatform("windows")]
public sealed class BootChangeRestorer : IChangeRestorer
{
    private readonly IEnumerable<ISpecialTweakHandler> _handlers;

    public BootChangeRestorer(IEnumerable<ISpecialTweakHandler> handlers) => _handlers = handlers;

    public string Id => "boot";

    public int Order => 50;

    public bool Handles(BackupEntry entry) => entry.Kind == BackupKind.BootConfiguration;

    public async Task<RestoreOutcome> RestoreAsync(
        IReadOnlyList<BackupEntry> entries,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ISpecialTweakHandler? handler =
            _handlers.FirstOrDefault(h => string.Equals(h.Key, "hypervisor_launch", StringComparison.Ordinal));

        if (handler is null)
        {
            return new RestoreOutcome(0, entries.Count, ["No handler is registered for boot configuration."]);
        }

        OperationResult result = await handler.RevertAsync(cancellationToken).ConfigureAwait(false);

        return result.Success
            ? new RestoreOutcome(1, 0, [])
            : new RestoreOutcome(0, 1, [result.Message ?? "The boot configuration could not be restored."]);
    }
}

/// <summary>
/// One power setting index on the active scheme, as opposed to which scheme is active.
///
/// Every such entry today belongs to a tweak and is reverted through it, so this restorer normally
/// sees nothing. It exists because the alternative is a kind of change with nobody responsible for
/// it, which is exactly the hole this whole arrangement was built to close.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerSettingChangeRestorer : IChangeRestorer
{
    private readonly IPowerService _power;
    private readonly IBackupService _backup;

    public PowerSettingChangeRestorer(IPowerService power, IBackupService backup)
    {
        _power = power;
        _backup = backup;
    }

    public string Id => "power-setting";

    public int Order => 25;

    public bool Handles(BackupEntry entry) => entry.Kind == BackupKind.PowerSetting;

    public Task<RestoreOutcome> RestoreAsync(
        IReadOnlyList<BackupEntry> entries,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // The subgroup a setting belongs to is not in the journal entry, so these cannot be put
        // back from here alone. Reporting that is honest; silently dropping them would not be.
        return Task.FromResult(new RestoreOutcome(
            0,
            entries.Count,
            ["These power settings can only be restored from the tweak that changed them."]));
    }
}

/// <summary>Scheduled tasks, for entries not owned by a tweak.</summary>
[SupportedOSPlatform("windows")]
public sealed class ScheduledTaskChangeRestorer : IChangeRestorer
{
    private readonly IScheduledTaskService _tasks;
    private readonly IBackupService _backup;

    public ScheduledTaskChangeRestorer(IScheduledTaskService tasks, IBackupService backup)
    {
        _tasks = tasks;
        _backup = backup;
    }

    public string Id => "scheduled-tasks";

    public int Order => 60;

    public bool Handles(BackupEntry entry) => entry.Kind == BackupKind.ScheduledTask;

    public async Task<RestoreOutcome> RestoreAsync(
        IReadOnlyList<BackupEntry> entries,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Only what was recorded as on. A task the user disabled themselves has no entry here.
        string[] paths = [.. entries.Select(e => e.Target).Distinct(StringComparer.OrdinalIgnoreCase)];

        OperationResult result = await _tasks
            .SetEnabledAsync(paths, enabled: true, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return new RestoreOutcome(0, paths.Length, [result.Message ?? "The scheduled tasks could not be restored."]);
        }

        await _backup.MarkRevertedAsync([.. entries.Select(e => e.Id)], cancellationToken).ConfigureAwait(false);
        return new RestoreOutcome(paths.Length, 0, []);
    }
}
