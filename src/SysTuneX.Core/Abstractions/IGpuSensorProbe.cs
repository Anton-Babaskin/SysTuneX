namespace SysTuneX.Core.Abstractions;

/// <summary>
/// One vendor's way of reporting GPU sensors.
///
/// Each vendor ships its own user-mode library with the driver, and none of them knows about the
/// others' cards. Keeping them behind one interface means the machine decides which one answers,
/// and means the part that picks between them can be tested without any of the native calls.
/// </summary>
public interface IGpuSensorProbe : IDisposable
{
    /// <summary>Shown beside the reading, so a number is never presented without saying where it came from.</summary>
    string Vendor { get; }

    /// <summary>
    /// The current reading, or <see langword="null"/> when this vendor's library is absent, the
    /// machine has no card of theirs, or the card declined to answer. Never throws.
    /// </summary>
    GpuReading? Read();
}

/// <param name="Celsius">GPU temperature.</param>
/// <param name="UsagePercent">Busy percentage, when the library reports one.</param>
/// <param name="FanPercent">Fan duty cycle, when the card has a controllable fan.</param>
public sealed record GpuReading(double Celsius, int? UsagePercent, int? FanPercent);
