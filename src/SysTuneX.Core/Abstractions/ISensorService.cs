namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Temperatures and GPU load, read through documented user-mode APIs only.
///
/// Deliberately no kernel driver. Reading a CPU package temperature properly means talking to
/// model-specific registers, which needs a ring-0 helper — and every off-the-shelf one is on
/// Microsoft's vulnerable driver blocklist and trips anti-cheat. A tool for gamers cannot ship
/// that, so CPU temperature comes from the ACPI thermal zone where the firmware exposes one,
/// and is reported as unavailable where it does not.
/// </summary>
public interface ISensorService
{
    /// <summary>
    /// Samples every sensor that answered. Safe to call on a timer: the expensive discovery
    /// happens once and unavailable sensors are not retried on every tick.
    /// </summary>
    Task<SensorReadings> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>One sample. A null reading means "no sensor answered", never "zero degrees".</summary>
public sealed record SensorReadings
{
    public static readonly SensorReadings None = new();

    public TemperatureReading? Cpu { get; init; }

    public TemperatureReading? Gpu { get; init; }

    /// <summary>GPU busy percentage, when the vendor library reports one.</summary>
    public int? GpuUsagePercent { get; init; }

    /// <summary>Fan duty cycle in percent, when the card has a controllable fan.</summary>
    public int? GpuFanPercent { get; init; }

    public bool HasAny => Cpu is not null || Gpu is not null;
}

/// <param name="Celsius">The reading.</param>
/// <param name="Source">
/// Where it came from, shown in the UI. A thermal zone is not the CPU package, and saying so is
/// the difference between a number someone can act on and a number that quietly misleads.
/// </param>
public sealed record TemperatureReading(double Celsius, string Source)
{
    /// <summary>Rounded for display; sensors are not accurate to a tenth of a degree.</summary>
    public int Rounded => (int)Math.Round(Celsius, MidpointRounding.AwayFromZero);
}
