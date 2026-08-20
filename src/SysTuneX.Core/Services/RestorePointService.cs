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

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsElevated)
        {
            return false;
        }

        // Get-ComputerRestorePoint only works when System Protection is on for a volume.
        ProcessRunResult result = await ProcessRunner
            .RunPowerShellAsync(
                "(Get-CimInstance -Namespace root/default -ClassName SystemRestore -ErrorAction SilentlyContinue | Measure-Object).Count",
                TimeSpan.FromSeconds(30),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Success)
        {
            return true;
        }

        _logger.LogDebug("System Restore is not queryable: {Error}", result.Output.Trim());
        return false;
    }

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
