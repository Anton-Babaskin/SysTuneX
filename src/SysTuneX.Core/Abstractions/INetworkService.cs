using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

public interface INetworkService
{
    /// <summary>Adapters that are up, non-loopback and have a gateway — i.e. the ones worth tuning.</summary>
    IReadOnlyList<NetworkAdapterInfo> GetActiveAdapters();

    /// <summary>Reads the resolvers currently in use, straight from the IP configuration.</summary>
    IReadOnlyList<string> GetDnsServers(string adapterId);

    Task<OperationResult> SetDnsAsync(string adapterId, string primary, string? secondary, CancellationToken cancellationToken = default);

    /// <summary>Restores whatever DNS configuration was recorded before SysTuneX changed it.</summary>
    Task<OperationResult> RestoreDnsAsync(string adapterId, CancellationToken cancellationToken = default);

    Task<OperationResult> ResetDnsToDhcpAsync(string adapterId, CancellationToken cancellationToken = default);

    /// <summary>Clears the resolver cache. Cheap, safe, and usually what people actually want.</summary>
    Task<OperationResult> FlushDnsCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>Round-trip time to a resolver, used to show what a DNS change bought.</summary>
    Task<long?> MeasureLatencyAsync(string host, CancellationToken cancellationToken = default);
}

public sealed record NetworkAdapterInfo(
    string Id,
    string Name,
    string Description,
    string InterfaceType,
    bool UsesDhcpForDns,
    IReadOnlyList<string> DnsServers);

public sealed record DnsPreset(string Id, string Name, string Primary, string Secondary);
