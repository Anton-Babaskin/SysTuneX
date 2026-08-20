using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <summary>
/// Core parking, applied through the power scheme.
///
/// The old build wrote <c>ValueMax</c> straight into the power settings key. The power manager
/// never reads that; it reads the per-scheme index, which only powercfg can set.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CoreParkingTweakHandler : ISpecialTweakHandler
{
    private readonly IPowerService _power;

    public CoreParkingTweakHandler(IPowerService power) => _power = power;

    public string Key => "core_parking";

    public async Task<TweakStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        await _power.IsCoreParkingDisabledAsync(cancellationToken).ConfigureAwait(false)
            ? TweakStatus.Applied
            : TweakStatus.NotApplied;

    public Task<OperationResult> ApplyAsync(CancellationToken cancellationToken = default) =>
        _power.SetCoreParkingAsync(enabled: false, cancellationToken);

    public Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default) =>
        _power.SetCoreParkingAsync(enabled: true, cancellationToken);
}

/// <summary>
/// Boot-level hypervisor switch. The DeviceGuard registry values stop Windows requesting VBS,
/// but the hypervisor still loads if the boot entry says so, which is why "VBS off" so often
/// shows as still on in msinfo32 after a reboot.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HypervisorLaunchTweakHandler : ISpecialTweakHandler
{
    private const string OwnerId = "tweak:hypervisor_launch_off";

    private readonly ILogger<HypervisorLaunchTweakHandler> _logger;
    private readonly IBackupService _backup;
    private readonly IEnvironmentService _environment;

    public HypervisorLaunchTweakHandler(
        ILogger<HypervisorLaunchTweakHandler> logger,
        IBackupService backup,
        IEnvironmentService environment)
    {
        _logger = logger;
        _backup = backup;
        _environment = environment;
    }

    public string Key => "hypervisor_launch";

    public async Task<TweakStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        string? current = await ReadLaunchTypeAsync(cancellationToken).ConfigureAwait(false);

        return current switch
        {
            null => TweakStatus.Unknown,
            "off" => TweakStatus.Applied,
            _ => TweakStatus.NotApplied,
        };
    }

    public async Task<OperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsElevated)
        {
            return OperationResult.Fail(CoreMessages.BootNeedsAdministrator);
        }

        string? current = await ReadLaunchTypeAsync(cancellationToken).ConfigureAwait(false);
        if (current == "off")
        {
            return OperationResult.NoChange();
        }

        await _backup.RecordRawAsync(
                new BackupEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = BackupKind.BootConfiguration,
                    OwnerId = OwnerId,
                    Target = "hypervisorlaunchtype",
                    // "auto" is the Windows default when the value has never been set explicitly.
                    OriginalValue = current ?? "auto",
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await SetLaunchTypeAsync("off", cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsElevated)
        {
            return OperationResult.Fail(CoreMessages.BootNeedsAdministrator);
        }

        BackupEntry? entry = _backup.FindActive(BackupKind.BootConfiguration, "hypervisorlaunchtype");
        string target = entry?.OriginalValue is { Length: > 0 } recorded ? recorded : "auto";

        OperationResult result = await SetLaunchTypeAsync(target, cancellationToken).ConfigureAwait(false);

        if (result.Success && entry is not null)
        {
            await _backup.MarkRevertedAsync([entry.Id], cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<string?> ReadLaunchTypeAsync(CancellationToken cancellationToken)
    {
        ProcessRunResult result = await ProcessRunner
            .RunAsync("bcdedit.exe", "/enum {current}", TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            _logger.LogDebug("bcdedit could not be read: {Error}", result.Output.Trim());
            return null;
        }

        foreach (string line in result.StandardOutput.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("hypervisorlaunchtype", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return parts[^1].ToLowerInvariant();
            }
        }

        // bcdedit omits the entry entirely when it has never been set, which means "auto".
        return "auto";
    }

    private async Task<OperationResult> SetLaunchTypeAsync(string value, CancellationToken cancellationToken)
    {
        ProcessRunResult result = await ProcessRunner
            .RunAsync("bcdedit.exe", $"/set hypervisorlaunchtype {value}", TimeSpan.FromSeconds(20), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return OperationResult.Fail(CoreMessages.BootBcdeditRefused, result.Output.Trim());
        }

        _logger.LogInformation("hypervisorlaunchtype set to {Value}", value);
        return OperationResult.Ok();
    }
}

/// <summary>
/// Nagle's algorithm, which lives per network interface rather than system-wide. The handler
/// walks the connected adapters so the tweak follows whichever NIC the machine is using.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NagleTweakHandler : ISpecialTweakHandler
{
    private const string InterfacesKey = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string OwnerId = "tweak:nagle_disable";

    private readonly IRegistryService _registry;
    private readonly IBackupService _backup;
    private readonly INetworkService _network;

    public NagleTweakHandler(IRegistryService registry, IBackupService backup, INetworkService network)
    {
        _registry = registry;
        _backup = backup;
        _network = network;
    }

    public string Key => "nagle";

    public Task<TweakStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> adapters = GetAdapterKeys();
        if (adapters.Count == 0)
        {
            return Task.FromResult(TweakStatus.Unknown);
        }

        int applied = adapters.Count(
            key => RegistryValueComparer.AreEqual(_registry.GetValue(key, "TcpAckFrequency"), 1) &&
                   RegistryValueComparer.AreEqual(_registry.GetValue(key, "TCPNoDelay"), 1));

        TweakStatus status = applied == adapters.Count
            ? TweakStatus.Applied
            : applied == 0
                ? TweakStatus.NotApplied
                : TweakStatus.Partial;

        return Task.FromResult(status);
    }

    public async Task<OperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> adapters = GetAdapterKeys();
        if (adapters.Count == 0)
        {
            return OperationResult.Fail(CoreMessages.NetworkNoConnectedAdapter);
        }

        var errors = new List<string>();

        foreach (string key in adapters)
        {
            foreach (string valueName in new[] { "TcpAckFrequency", "TCPNoDelay" })
            {
                (object? current, RegistryValueKind kind) = _registry.GetValueWithKind(key, valueName);
                await _backup.RecordRegistryAsync(OwnerId, key, valueName, current, kind, cancellationToken).ConfigureAwait(false);

                OperationResult result = _registry.SetValue(key, valueName, 1, RegistryValueKind.DWord);
                if (!result.Success && result.Message is not null)
                {
                    errors.Add(result.Message);
                }
            }
        }

        return errors.Count == 0 ? OperationResult.Ok() : OperationResult.Fail(string.Join(" ", errors));
    }

    public async Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var reverted = new List<string>();

        foreach (string key in GetAdapterKeys())
        {
            foreach (string valueName in new[] { "TcpAckFrequency", "TCPNoDelay" })
            {
                BackupEntry? entry = _backup.FindActive(BackupKind.RegistryValue, key, valueName);

                // Neither value exists on a stock install, so deleting is the correct revert
                // whenever nothing was recorded.
                OperationResult result = entry?.OriginalValue is { } original && int.TryParse(original, out int value)
                    ? _registry.SetValue(key, valueName, value, RegistryValueKind.DWord)
                    : _registry.DeleteValue(key, valueName);

                if (!result.Success && result.Message is not null)
                {
                    errors.Add(result.Message);
                }

                if (entry is not null)
                {
                    reverted.Add(entry.Id);
                }
            }
        }

        if (reverted.Count > 0)
        {
            await _backup.MarkRevertedAsync(reverted, cancellationToken).ConfigureAwait(false);
        }

        return errors.Count == 0 ? OperationResult.Ok() : OperationResult.Fail(string.Join(" ", errors));
    }

    private IReadOnlyList<string> GetAdapterKeys()
    {
        var keys = new List<string>();

        foreach (NetworkAdapterInfo adapter in _network.GetActiveAdapters())
        {
            string key = $@"{InterfacesKey}\{adapter.Id}";
            if (_registry.KeyExists(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }
}
