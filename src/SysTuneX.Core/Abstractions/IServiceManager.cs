using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

public interface IServiceManager
{
    /// <summary>Definitions that exist on this Windows build, with their live state attached.</summary>
    Task<IReadOnlyList<(ServiceDefinition Definition, ServiceSnapshot State)>> GetManagedServicesAsync(
        CancellationToken cancellationToken = default);

    ServiceSnapshot GetState(string serviceName);

    /// <summary>Stops the service (and its dependents) and sets it to the definition's disabled start mode.</summary>
    Task<OperationResult> DisableAsync(ServiceDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Puts the service back to the start mode recorded in the backup journal, then starts it.</summary>
    Task<OperationResult> RestoreAsync(string serviceName, CancellationToken cancellationToken = default);

    Task<OperationResult> StartAsync(string serviceName, CancellationToken cancellationToken = default);

    Task<OperationResult> StopAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>Writes the service's start type through the service control manager.</summary>
    OperationResult SetStartMode(string serviceName, ServiceStartMode startMode);
}
