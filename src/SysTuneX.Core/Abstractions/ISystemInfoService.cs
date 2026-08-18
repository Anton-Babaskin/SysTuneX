using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

public interface ISystemInfoService
{
    /// <summary>Static hardware description. Cached after the first call — the WMI queries are slow.</summary>
    Task<HardwareInfo> GetHardwareInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One sample of the live counters. Cheap enough to call on a timer; the CPU figure is a
    /// delta against the previous call, so the first sample after start-up reads zero.
    /// </summary>
    SystemSnapshot GetSnapshot();
}
