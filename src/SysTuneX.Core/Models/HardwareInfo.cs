namespace SysTuneX.Core.Models;

public sealed record HardwareInfo
{
    public string CpuName { get; init; } = "Unknown";
    public int CpuCores { get; init; }
    public int CpuThreads { get; init; }
    public string GpuName { get; init; } = "Unknown";

    /// <summary>Dedicated video memory in MB. Read from the driver key, which — unlike WMI — is not capped at 4 GB.</summary>
    public long GpuVramMb { get; init; }

    public string GpuDriverVersion { get; init; } = string.Empty;

    /// <summary>Physical RAM in MB, from GlobalMemoryStatusEx (not the GC heap limit).</summary>
    public long RamTotalMb { get; init; }

    public string RamSummary { get; init; } = string.Empty;
    public string MotherboardName { get; init; } = string.Empty;
    public string SystemDriveModel { get; init; } = string.Empty;
    public WindowsVersionInfo Windows { get; init; } = WindowsVersionInfo.Unknown;
}

/// <summary>Windows version, resolved from RtlGetVersion plus the CurrentVersion registry key.</summary>
public sealed record WindowsVersionInfo
{
    public static readonly WindowsVersionInfo Unknown = new();

    public int Major { get; init; }
    public int Minor { get; init; }
    public int Build { get; init; }

    /// <summary>Update Build Revision — the fourth part of "10.0.26100.2314".</summary>
    public int Revision { get; init; }

    /// <summary>Marketing name, e.g. "Windows 11 Pro".</summary>
    public string ProductName { get; init; } = "Unknown";

    /// <summary>Feature update, e.g. "24H2".</summary>
    public string DisplayVersion { get; init; } = string.Empty;

    public string Edition { get; init; } = string.Empty;

    /// <summary>Windows 11 shipped as build 22000. Anything below that is Windows 10 or older.</summary>
    public bool IsWindows11 => Major >= 10 && Build >= 22000;

    public bool IsWindows10 => Major >= 10 && Build is >= 10240 and < 22000;

    /// <summary>The app targets Windows 10 1809+ and Windows 11.</summary>
    public bool IsSupported => Major >= 10 && Build >= 17763;

    public string FullVersion => Revision > 0
        ? $"{Major}.{Minor}.{Build}.{Revision}"
        : $"{Major}.{Minor}.{Build}";

    public override string ToString() =>
        string.IsNullOrEmpty(DisplayVersion)
            ? $"{ProductName} (build {Build})"
            : $"{ProductName} {DisplayVersion} (build {Build})";
}
