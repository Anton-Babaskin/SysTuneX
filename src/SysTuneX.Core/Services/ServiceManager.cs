using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Native;
using SysTuneX.Core.Tweaks;

// Both System.ServiceProcess and the Core models declare a ServiceStartMode; the model type
// is the one this file speaks in, and the framework enum is referenced fully qualified.
using ServiceStartMode = SysTuneX.Core.Models.ServiceStartMode;
using Win32ServiceStartMode = System.ServiceProcess.ServiceStartMode;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IServiceManager"/>
[SupportedOSPlatform("windows")]
public sealed class ServiceManager : IServiceManager
{
    private static readonly TimeSpan StateChangeTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger<ServiceManager> _logger;
    private readonly IBackupService _backup;
    private readonly IEnvironmentService _environment;

    public ServiceManager(ILogger<ServiceManager> logger, IBackupService backup, IEnvironmentService environment)
    {
        _logger = logger;
        _backup = backup;
        _environment = environment;
    }

    public Task<IReadOnlyList<(ServiceDefinition Definition, ServiceSnapshot State)>> GetManagedServicesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<(ServiceDefinition, ServiceSnapshot)>>(
            () =>
            {
                int build = _environment.Windows.Build;
                var results = new List<(ServiceDefinition, ServiceSnapshot)>();

                foreach (ServiceDefinition definition in ServiceCatalog.All)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (build < definition.MinBuild)
                    {
                        continue;
                    }

                    ServiceSnapshot state = GetState(definition.ServiceName);

                    // A definition that matches nothing on this machine is noise, not information.
                    if (!state.IsInstalled)
                    {
                        continue;
                    }

                    results.Add((definition, state));
                }

                return results;
            },
            cancellationToken);
    }

    public ServiceSnapshot GetState(string serviceName)
    {
        string? resolved = ResolveServiceName(serviceName);
        if (resolved is null)
        {
            return new ServiceSnapshot { ServiceName = serviceName, State = ServiceState.NotInstalled };
        }

        try
        {
            using var controller = new ServiceController(resolved);
            return new ServiceSnapshot
            {
                ServiceName = resolved,
                State = MapState(controller.Status),
                StartMode = MapStartMode(controller.StartType),
            };
        }
        catch (InvalidOperationException)
        {
            return new ServiceSnapshot { ServiceName = serviceName, State = ServiceState.NotInstalled };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the state of {Service}", serviceName);
            return new ServiceSnapshot { ServiceName = serviceName, State = ServiceState.Unknown };
        }
    }

    public async Task<OperationResult> DisableAsync(ServiceDefinition definition, CancellationToken cancellationToken = default)
    {
        string? resolved = ResolveServiceName(definition.ServiceName);
        if (resolved is null)
        {
            return OperationResult.NoChange(CoreMessages.ServiceNotInstalled, definition.ServiceName);
        }

        ServiceSnapshot before = GetState(resolved);

        // Record the real previous configuration before touching anything, so restore is exact.
        await _backup
            .RecordServiceAsync($"service:{definition.ServiceName}", resolved, before.StartMode, before.IsRunning, cancellationToken)
            .ConfigureAwait(false);

        var errors = new List<string>();

        if (before.IsRunning)
        {
            OperationResult stop = await StopAsync(resolved, cancellationToken).ConfigureAwait(false);
            if (!stop.Success && stop.Message is not null)
            {
                errors.Add(stop.Message);
            }
        }

        OperationResult startMode = SetStartMode(resolved, definition.DisabledStartMode);
        if (!startMode.Success && startMode.Message is not null)
        {
            errors.Add(startMode.Message);
        }

        // A service that refuses to stop but will not start again next boot is still a win,
        // so a failed stop alone is reported rather than treated as a total failure.
        if (startMode.Success)
        {
            return errors.Count == 0
                ? OperationResult.Ok()
                : OperationResult.Ok(string.Join(" ", errors));
        }

        return OperationResult.Fail(string.Join(" ", errors));
    }

    public async Task<OperationResult> RestoreAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        string? resolved = ResolveServiceName(serviceName);
        if (resolved is null)
        {
            return OperationResult.NoChange(CoreMessages.ServiceNotInstalled, serviceName);
        }

        BackupEntry? entry = _backup.FindActive(BackupKind.ServiceConfiguration, resolved)
                             ?? _backup.FindActive(BackupKind.ServiceConfiguration, serviceName);

        // No journal entry means SysTuneX never touched this service, so there is nothing here to
        // restore and no way to know what "restored" would even mean.
        //
        // This used to guess: Manual for a handful of services and Automatic for everything else,
        // then start it. Several services in the catalog ship Disabled on stock Windows 11 -
        // RemoteRegistry among them - so they read as "disabled" in the interface even on a
        // machine SysTuneX had never been run on. Pressing Restore All then enabled and started
        // them, and because nothing was recorded first, that change could not be undone. Turning
        // on RemoteRegistry behind someone's back is the opposite of what this tool is for.
        if (entry is null)
        {
            return OperationResult.NoChange(CoreMessages.ServiceNotChangedByUs, serviceName);
        }

        ServiceStartMode targetMode = entry.OriginalStartMode;

        // A recording that failed to read the start type cannot be acted on either.
        if (targetMode == ServiceStartMode.Unknown)
        {
            return OperationResult.NoChange(CoreMessages.ServiceNotChangedByUs, serviceName);
        }

        bool shouldRun = entry.OriginalWasRunning;

        OperationResult startMode = SetStartMode(resolved, targetMode);
        if (!startMode.Success)
        {
            return startMode;
        }

        if (shouldRun)
        {
            OperationResult start = await StartAsync(resolved, cancellationToken).ConfigureAwait(false);
            if (!start.Success)
            {
                // The start type is restored, so the service comes back at the next boot regardless.
                _logger.LogWarning("Restored the start type of {Service} but could not start it now", resolved);
            }
        }

        await _backup.MarkRevertedAsync([entry.Id], cancellationToken).ConfigureAwait(false);

        return OperationResult.Ok();
    }

    public async Task<OperationResult> StartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        string? resolved = ResolveServiceName(serviceName);
        if (resolved is null)
        {
            return OperationResult.NoChange(CoreMessages.ServiceNotInstalled, serviceName);
        }

        ServiceSnapshot before = GetState(resolved);

        // Starting a Disabled service means changing its start type, and that is a change to the
        // machine like any other - so it is journalled before it happens, not after. Without this
        // the lift to Manual was invisible to the change log and could not be undone.
        if (before.StartMode == ServiceStartMode.Disabled && !before.IsRunning)
        {
            await _backup
                .RecordServiceAsync($"service:{resolved}", resolved, before.StartMode, wasRunning: false, cancellationToken)
                .ConfigureAwait(false);
        }

        return await Task.Run(
            () =>
            {
                try
                {
                    using var controller = new ServiceController(resolved);
                    if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                    {
                        return OperationResult.NoChange();
                    }

                    // A disabled service cannot start; lift it to Manual first.
                    if (controller.StartType == Win32ServiceStartMode.Disabled)
                    {
                        OperationResult lift = SetStartMode(resolved, ServiceStartMode.Manual);
                        if (!lift.Success)
                        {
                            return lift;
                        }
                    }

                    controller.Start();
                    controller.WaitForStatus(ServiceControllerStatus.Running, StateChangeTimeout);
                    _logger.LogInformation("Started service {Service}", resolved);
                    return OperationResult.Ok();
                }
                catch (System.ServiceProcess.TimeoutException)
                {
                    return OperationResult.Fail(CoreMessages.ServiceStartTimedOut, resolved);
                }
                catch (Exception ex)
                {
                    return OperationResult.Fail(CoreMessages.ServiceStartFailed, ex, resolved, Describe(ex));
                }
            },
            cancellationToken);
    }

    public Task<OperationResult> StopAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                string? resolved = ResolveServiceName(serviceName);
                if (resolved is null)
                {
                    return OperationResult.NoChange(CoreMessages.ServiceNotInstalled, serviceName);
                }

                try
                {
                    using var controller = new ServiceController(resolved);
                    if (controller.Status == ServiceControllerStatus.Stopped)
                    {
                        return OperationResult.NoChange();
                    }

                    if (!controller.CanStop)
                    {
                        return OperationResult.Fail(CoreMessages.ServiceCannotBeStopped, resolved);
                    }

                    // Dependents hold the service open, so they have to go down first.
                    foreach (ServiceController dependent in controller.DependentServices)
                    {
                        using (dependent)
                        {
                            try
                            {
                                if (dependent.Status != ServiceControllerStatus.Stopped && dependent.CanStop)
                                {
                                    dependent.Stop();
                                    dependent.WaitForStatus(ServiceControllerStatus.Stopped, StateChangeTimeout);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Could not stop dependent service {Service}", dependent.ServiceName);
                            }
                        }
                    }

                    controller.Stop();
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, StateChangeTimeout);
                    _logger.LogInformation("Stopped service {Service}", resolved);
                    return OperationResult.Ok();
                }
                catch (System.ServiceProcess.TimeoutException)
                {
                    return OperationResult.Fail(CoreMessages.ServiceStopTimedOut, resolved);
                }
                catch (Exception ex)
                {
                    return OperationResult.Fail(CoreMessages.ServiceStopFailed, ex, resolved, Describe(ex));
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Writes the start type through the service control manager.
    ///
    /// The old code shelled out to <c>sc.exe config</c> and only looked at the exit code, so a
    /// blocked write looked identical to a successful one. This reports the actual Win32 error.
    /// </summary>
    public OperationResult SetStartMode(string serviceName, ServiceStartMode startMode)
    {
        string? resolved = ResolveServiceName(serviceName);
        if (resolved is null)
        {
            return OperationResult.NoChange(CoreMessages.ServiceNotInstalled, serviceName);
        }

        if (startMode == ServiceStartMode.Unknown)
        {
            return OperationResult.Fail(CoreMessages.ServiceUnknownStartType);
        }

        IntPtr manager = IntPtr.Zero;
        IntPtr service = IntPtr.Zero;

        try
        {
            manager = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
            if (manager == IntPtr.Zero)
            {
                return OperationResult.Fail(LastError("Could not connect to the service control manager"));
            }

            service = NativeMethods.OpenService(
                manager,
                resolved,
                NativeMethods.SERVICE_CHANGE_CONFIG | NativeMethods.SERVICE_QUERY_CONFIG);

            if (service == IntPtr.Zero)
            {
                return OperationResult.Fail(LastError($"Could not open {resolved}"));
            }

            bool changed = NativeMethods.ChangeServiceConfig(
                service,
                NativeMethods.SERVICE_NO_CHANGE,
                (uint)startMode,
                NativeMethods.SERVICE_NO_CHANGE,
                null, null, IntPtr.Zero, null, null, null, null);

            if (!changed)
            {
                return OperationResult.Fail(LastError($"Could not set the start type of {resolved}"));
            }

            _logger.LogInformation("Start type of {Service} set to {Mode}", resolved, startMode);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(CoreMessages.ServiceSetStartTypeFailed, ex, resolved, ex.Message);
        }
        finally
        {
            if (service != IntPtr.Zero)
            {
                NativeMethods.CloseServiceHandle(service);
            }

            if (manager != IntPtr.Zero)
            {
                NativeMethods.CloseServiceHandle(manager);
            }
        }
    }

    /// <summary>
    /// Per-user services are registered as "CDPUserSvc_4f2a1" and the suffix differs per logon
    /// session, so a literal name lookup misses them. Match the stem when the exact name fails.
    /// </summary>
    private string? ResolveServiceName(string serviceName)
    {
        try
        {
            using var direct = new ServiceController(serviceName);
            _ = direct.Status;
            return serviceName;
        }
        catch
        {
            // Fall through to the prefix scan.
        }

        try
        {
            string prefix = serviceName + "_";
            ServiceController[] all = ServiceController.GetServices();
            try
            {
                ServiceController? match = all.FirstOrDefault(
                    s => s.ServiceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                return match?.ServiceName;
            }
            finally
            {
                foreach (ServiceController controller in all)
                {
                    controller.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not enumerate services while resolving {Service}", serviceName);
            return null;
        }
    }

    private static string LastError(string prefix)
    {
        int code = Marshal.GetLastWin32Error();
        string detail = code == 5
            ? "access denied - SysTuneX needs to run as administrator"
            : new Win32Exception(code).Message;
        return $"{prefix}: {detail} (0x{code:X}).";
    }

    private static string Describe(Exception ex) =>
        ex.InnerException is Win32Exception win32 ? win32.Message : ex.Message;

    private static ServiceState MapState(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => ServiceState.Running,
        ServiceControllerStatus.Stopped => ServiceState.Stopped,
        ServiceControllerStatus.StartPending => ServiceState.StartPending,
        ServiceControllerStatus.StopPending => ServiceState.StopPending,
        _ => ServiceState.Unknown,
    };

    private static ServiceStartMode MapStartMode(Win32ServiceStartMode mode) => mode switch
    {
        Win32ServiceStartMode.Boot => ServiceStartMode.Boot,
        Win32ServiceStartMode.System => ServiceStartMode.System,
        Win32ServiceStartMode.Automatic => ServiceStartMode.Automatic,
        Win32ServiceStartMode.Manual => ServiceStartMode.Manual,
        Win32ServiceStartMode.Disabled => ServiceStartMode.Disabled,
        _ => ServiceStartMode.Unknown,
    };
}
