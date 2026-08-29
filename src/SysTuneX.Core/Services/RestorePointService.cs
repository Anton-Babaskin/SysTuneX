using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IRestorePointService"/>
[SupportedOSPlatform("windows")]
public sealed class RestorePointService : IRestorePointService
{
    private const string SystemRestoreKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    private readonly ILogger<RestorePointService> _logger;
    private readonly IRegistryService _registry;
    private readonly IEnvironmentService _environment;

    public RestorePointService(
        ILogger<RestorePointService> logger,
        IRegistryService registry,
        IEnvironmentService environment)
    {
        _logger = logger;
        _registry = registry;
        _environment = environment;
    }

    /// <summary>Policy key that turns System Restore off for the whole machine.</summary>
    private const string SystemRestorePolicyKey = @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsElevated)
        {
            return Task.FromResult(false);
        }

        // Read from the registry rather than asking PowerShell.
        //
        // The previous check ran a command that counted restore points and then ignored the count
        // entirely, deciding from the exit code alone - and with -ErrorAction SilentlyContinue
        // PowerShell exits 0 whether or not System Protection is on, so it answered "available"
        // every time. It also could not have been right had it read the number: a machine with
        // protection enabled and no restore points yet reports zero, and that is a machine where
        // creating one works perfectly well.
        //
        // These two values are what actually decide it, and reading them costs nothing - which
        // also takes a shell spawn and its thirty-second timeout off the profiles page.
        if (IsTurnedOff(SystemRestorePolicyKey) || IsTurnedOff(SystemRestoreKey))
        {
            _logger.LogDebug("System Restore is switched off for this machine");
            return Task.FromResult(false);
        }

        // Zero means System Protection is not enabled on any volume. Absent means the machine has
        // never had it configured either way, which is the stock state where it does work.
        if (_registry.GetValue(SystemRestoreKey, "RPSessionInterval") is int interval && interval == 0)
        {
            _logger.LogDebug("System Protection is not enabled on any volume");
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    private bool IsTurnedOff(string keyPath) =>
        _registry.GetValue(keyPath, "DisableSR") is int disabled && disabled == 1;

    public async Task<OperationResult> CreateAsync(string description, CancellationToken cancellationToken = default)
    {
        if (!_environment.IsElevated)
        {
            return OperationResult.Fail(CoreMessages.RestorePointNeedsAdministrator);
        }

        // Windows silently ignores restore points created within 24 hours of the previous one
        // unless this throttle is relaxed, which is why "create restore point" so often looks
        // like it did nothing. Temporarily set the interval to zero and put it back afterwards.
        object? previousFrequency = _registry.GetValue(SystemRestoreKey, "SystemRestorePointCreationFrequency");
        _registry.SetValue(SystemRestoreKey, "SystemRestorePointCreationFrequency", 0, Microsoft.Win32.RegistryValueKind.DWord);

        try
        {
            string safeDescription = description.Replace("'", "''");

            ProcessRunResult result = await ProcessRunner
                .RunPowerShellAsync(
                    $"Checkpoint-Computer -Description '{safeDescription}' -RestorePointType 'MODIFY_SETTINGS'",
                    TimeSpan.FromMinutes(4),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
            {
                _logger.LogInformation("Created restore point: {Description}", description);
                return OperationResult.Ok();
            }

            string error = result.Output.Trim();

            if (error.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("отключен", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Fail(CoreMessages.RestorePointProtectionOff);
            }

            return OperationResult.Fail(CoreMessages.RestorePointRefused, error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(CoreMessages.RestorePointFailed, ex, ex.Message);
        }
        finally
        {
            if (previousFrequency is null)
            {
                _registry.DeleteValue(SystemRestoreKey, "SystemRestorePointCreationFrequency");
            }
            else
            {
                _registry.SetValue(
                    SystemRestoreKey,
                    "SystemRestorePointCreationFrequency",
                    previousFrequency,
                    Microsoft.Win32.RegistryValueKind.DWord);
            }
        }
    }
}
