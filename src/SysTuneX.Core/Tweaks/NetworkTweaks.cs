using Microsoft.Win32;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;

namespace SysTuneX.Core.Tweaks;

public static class NetworkTweaks
{
    public record TweakDefinition(
        string Id,
        string Name,
        string Description,
        RiskLevel Risk,
        string KeyPath,
        string ValueName,
        object EnabledValue,
        object DisabledValue,
        RegistryValueKind ValueKind
    );

    public static readonly TweakDefinition[] All =
    [
        new("nagle_disable", "Disable Nagle's Algorithm", "Sends TCP packets immediately — reduces network latency in online games",
            RiskLevel.Safe,
            @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", "TcpNoDelay", 1, 0, RegistryValueKind.DWord),

        new("tcp_ack_frequency", "TCP ACK Frequency", "High-frequency ACKs — faster round-trip times",
            RiskLevel.Moderate,
            @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", "TcpAckFrequency", 1, 2, RegistryValueKind.DWord),

        new("network_throttling", "Disable Network Throttling", "Removes Windows network bandwidth throttling",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), 10, RegistryValueKind.DWord),

        new("ecn_capability", "Disable ECN", "Disables Explicit Congestion Notification — can improve some game connections",
            RiskLevel.Moderate,
            @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "EnableTCPChimney", 0, 1, RegistryValueKind.DWord),

        new("auto_tuning", "Disable Auto-Tuning", "Disables TCP receive window auto-tuning — can reduce latency on some networks",
            RiskLevel.Moderate,
            @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "EnableWsd", 0, 1, RegistryValueKind.DWord),

        new("wifi_sense", "Disable Wi-Fi Sense", "Stops auto-connecting and sharing Wi-Fi credentials",
            RiskLevel.Safe,
            @"HKLM\SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config", "AutoConnectAllowedOEM", 0, 1, RegistryValueKind.DWord),
    ];

    public static readonly (string Name, string Primary, string Secondary)[] DnsPresets =
    [
        ("Cloudflare", "1.1.1.1", "1.0.0.1"),
        ("Google", "8.8.8.8", "8.8.4.4"),
        ("Quad9", "9.9.9.9", "149.112.112.112"),
        ("OpenDNS", "208.67.222.222", "208.67.220.220"),
    ];

    public static TweakItem ToTweakItem(TweakDefinition def) => new()
    {
        Id = def.Id,
        Name = def.Name,
        Description = def.Description,
        Category = TweakCategory.Network,
        Risk = def.Risk,
    };

    public static TweakStatus CheckStatus(TweakDefinition def, IRegistryService registry)
    {
        var value = registry.GetValue(def.KeyPath, def.ValueName);
        if (value == null) return TweakStatus.NotApplied;
        return value.ToString() == def.EnabledValue.ToString()
            ? TweakStatus.Applied
            : TweakStatus.NotApplied;
    }

    public static bool Apply(TweakDefinition def, IRegistryService registry)
    {
        return registry.SetValue(def.KeyPath, def.ValueName, def.EnabledValue, def.ValueKind);
    }

    public static bool Revert(TweakDefinition def, IRegistryService registry)
    {
        return registry.SetValue(def.KeyPath, def.ValueName, def.DisabledValue, def.ValueKind);
    }
}
