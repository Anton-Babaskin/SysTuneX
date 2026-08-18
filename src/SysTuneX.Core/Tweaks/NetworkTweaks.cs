using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tweaks;

public static class NetworkTweaks
{
    private const string SystemProfile = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    public static IReadOnlyList<TweakDefinition> All { get; } =
    [
        new()
        {
            Id = "nagle_disable",
            Category = TweakCategory.Network,
            GroupKey = "Group_Latency",
            Name = "Disable Nagle's algorithm",
            Description =
                "Nagle batches small outbound packets to save bandwidth, which adds up to 200 ms to " +
                "the small, frequent updates a game client sends. Applied to every connected adapter, " +
                "because the setting lives per interface rather than system-wide.",
            Risk = RiskLevel.Moderate,
            HandlerKey = "nagle",
        },

        new()
        {
            Id = "network_throttling_disable",
            Category = TweakCategory.Network,
            GroupKey = "Group_Latency",
            Name = "Remove the network throttling limit",
            Description =
                "Windows caps non-multimedia network traffic at 10 packets per millisecond so audio " +
                "playback keeps its share. On a modern machine that cap only gets in the way.",
            Risk = RiskLevel.Safe,
            Changes =
            [
                // 0xFFFFFFFF is the documented "no throttling" value; as a REG_DWORD it is written as -1.
                new(SystemProfile, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), 10, RegistryValueKind.DWord),
            ],
        },

        new()
        {
            Id = "network_memory_reserve",
            Category = TweakCategory.Network,
            GroupKey = "Group_Throughput",
            Name = "Raise the network memory pool",
            Description =
                "Lets the TCP/IP stack allocate more non-paged pool for buffers, which helps on " +
                "high-bandwidth connections that would otherwise drop packets under load.",
            Risk = RiskLevel.Moderate,
            RequiresRestart = true,
            Changes =
            [
                new(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "MaxUserPort", 65534, null, RegistryValueKind.DWord),
                new(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "TcpTimedWaitDelay", 30, null, RegistryValueKind.DWord),
            ],
        },
    ];

    /// <summary>Public resolvers offered on the network page, all of them documented anycast addresses.</summary>
    public static IReadOnlyList<DnsPreset> DnsPresets { get; } =
    [
        new("cloudflare", "Cloudflare", "1.1.1.1", "1.0.0.1"),
        new("cloudflare_family", "Cloudflare (malware filtering)", "1.1.1.2", "1.0.0.2"),
        new("google", "Google Public DNS", "8.8.8.8", "8.8.4.4"),
        new("quad9", "Quad9", "9.9.9.9", "149.112.112.112"),
        new("opendns", "OpenDNS", "208.67.222.222", "208.67.220.220"),
        new("adguard", "AdGuard DNS", "94.140.14.14", "94.140.15.15"),
    ];
}
