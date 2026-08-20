using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="INetworkService"/>
[SupportedOSPlatform("windows")]
public sealed class NetworkService : INetworkService
{
    private const string InterfacesKey = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string OwnerId = "network:dns";

    private readonly ILogger<NetworkService> _logger;
    private readonly IRegistryService _registry;
    private readonly IBackupService _backup;

    public NetworkService(ILogger<NetworkService> logger, IRegistryService registry, IBackupService backup)
    {
        _logger = logger;
        _registry = registry;
        _backup = backup;
    }

    /// <summary>
    /// Enumerates real, connected adapters through the IP helper API.
    ///
    /// The old code spawned PowerShell and parsed its output to find one adapter name, which was
    /// slow, flashed a console window, and broke on any localised or renamed adapter.
    /// </summary>
    public IReadOnlyList<NetworkAdapterInfo> GetActiveAdapters()
    {
        var adapters = new List<NetworkAdapterInfo>();

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                IPInterfaceProperties properties = nic.GetIPProperties();

                // No gateway means it is a virtual or host-only adapter, not the machine's route to the internet.
                if (properties.GatewayAddresses.Count == 0)
                {
                    continue;
                }

                List<string> dns = properties.DnsAddresses
                    .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToList();

                adapters.Add(new NetworkAdapterInfo(
                    nic.Id,
                    nic.Name,
                    nic.Description,
                    nic.NetworkInterfaceType.ToString(),
                    !IsDnsStaticallyConfigured(nic.Id),
                    dns));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enumerate network adapters");
        }

        return adapters;
    }

    public IReadOnlyList<string> GetDnsServers(string adapterId)
    {
        try
        {
            NetworkInterface? nic = FindAdapter(adapterId);
            if (nic is null)
            {
                return [];
            }

            return nic.GetIPProperties().DnsAddresses
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read DNS servers for {Adapter}", adapterId);
            return [];
        }
    }

    public async Task<OperationResult> SetDnsAsync(
        string adapterId,
        string primary,
        string? secondary,
        CancellationToken cancellationToken = default)
    {
        NetworkInterface? nic = FindAdapter(adapterId);
        if (nic is null)
        {
            return OperationResult.Fail(CoreMessages.NetworkAdapterGone);
        }

        if (!System.Net.IPAddress.TryParse(primary, out _))
        {
            return OperationResult.Fail(CoreMessages.NetworkInvalidAddress, primary);
        }

        int? index = GetInterfaceIndex(nic);
        if (index is null)
        {
            return OperationResult.Fail(CoreMessages.NetworkNoIpv4Interface);
        }

        await _backup
            .RecordDnsAsync(OwnerId, adapterId, !IsDnsStaticallyConfigured(adapterId), GetDnsServers(adapterId), cancellationToken)
            .ConfigureAwait(false);

        // validate=no keeps netsh from blocking for several seconds probing the new resolver.
        ProcessRunResult setPrimary = await ProcessRunner.RunAsync(
                "netsh.exe",
                $"interface ipv4 set dnsservers name={index} source=static address={primary} register=primary validate=no",
                TimeSpan.FromSeconds(20),
                cancellationToken)
            .ConfigureAwait(false);

        if (!setPrimary.Success)
        {
            return OperationResult.Fail(CoreMessages.NetworkSetResolverFailed, setPrimary.Output.Trim());
        }

        if (!string.IsNullOrWhiteSpace(secondary) && System.Net.IPAddress.TryParse(secondary, out _))
        {
            ProcessRunResult setSecondary = await ProcessRunner.RunAsync(
                    "netsh.exe",
                    $"interface ipv4 add dnsservers name={index} address={secondary} index=2 validate=no",
                    TimeSpan.FromSeconds(20),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!setSecondary.Success)
            {
                _logger.LogWarning("Secondary resolver was rejected: {Error}", setSecondary.Output.Trim());
            }
        }

        await FlushDnsCacheAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("DNS on {Adapter} set to {Primary}/{Secondary}", nic.Name, primary, secondary);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> RestoreDnsAsync(string adapterId, CancellationToken cancellationToken = default)
    {
        BackupEntry? entry = _backup.FindActive(BackupKind.DnsConfiguration, adapterId);

        // No record means SysTuneX never changed this adapter; DHCP is the Windows default.
        if (entry?.OriginalValue is null || entry.OriginalValue == "dhcp")
        {
            OperationResult dhcp = await ResetDnsToDhcpAsync(adapterId, cancellationToken).ConfigureAwait(false);
            if (dhcp.Success && entry is not null)
            {
                await _backup.MarkRevertedAsync([entry.Id], cancellationToken).ConfigureAwait(false);
            }

            return dhcp;
        }

        string[] servers = entry.OriginalValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (servers.Length == 0)
        {
            return await ResetDnsToDhcpAsync(adapterId, cancellationToken).ConfigureAwait(false);
        }

        OperationResult restore = await SetDnsAsync(
                adapterId,
                servers[0],
                servers.Length > 1 ? servers[1] : null,
                cancellationToken)
            .ConfigureAwait(false);

        if (restore.Success)
        {
            await _backup.MarkRevertedAsync([entry.Id], cancellationToken).ConfigureAwait(false);
        }

        return restore;
    }

    public async Task<OperationResult> ResetDnsToDhcpAsync(string adapterId, CancellationToken cancellationToken = default)
    {
        NetworkInterface? nic = FindAdapter(adapterId);
        if (nic is null)
        {
            return OperationResult.Fail(CoreMessages.NetworkAdapterGone);
        }

        int? index = GetInterfaceIndex(nic);
        if (index is null)
        {
            return OperationResult.Fail(CoreMessages.NetworkNoIpv4Interface);
        }

        ProcessRunResult result = await ProcessRunner.RunAsync(
                "netsh.exe",
                $"interface ipv4 set dnsservers name={index} source=dhcp",
                TimeSpan.FromSeconds(20),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return OperationResult.Fail(CoreMessages.NetworkRestoreDhcpFailed, result.Output.Trim());
        }

        await FlushDnsCacheAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> FlushDnsCacheAsync(CancellationToken cancellationToken = default)
    {
        ProcessRunResult result = await ProcessRunner
            .RunAsync("ipconfig.exe", "/flushdns", TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);

        return result.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(CoreMessages.NetworkFlushCacheFailed, result.Output.Trim());
    }

    public async Task<long?> MeasureLatencyAsync(string host, CancellationToken cancellationToken = default)
    {
        try
        {
            using var ping = new Ping();
            var samples = new List<long>();

            for (int i = 0; i < 3; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                PingReply reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(2), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (reply.Status == IPStatus.Success)
                {
                    samples.Add(reply.RoundtripTime);
                }
            }

            return samples.Count > 0 ? (long)samples.Average() : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not measure latency to {Host}", host);
            return null;
        }
    }

    /// <summary>
    /// A non-empty NameServer value under the adapter's TCP/IP key means the resolvers were set
    /// by hand; DHCP-assigned ones live in DhcpNameServer instead.
    /// </summary>
    private bool IsDnsStaticallyConfigured(string adapterId)
    {
        string value = _registry.GetValue($@"{InterfacesKey}\{adapterId}", "NameServer") as string ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static NetworkInterface? FindAdapter(string adapterId)
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Id, adapterId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static int? GetInterfaceIndex(NetworkInterface nic)
    {
        try
        {
            return nic.GetIPProperties().GetIPv4Properties()?.Index;
        }
        catch
        {
            return null;
        }
    }
}
