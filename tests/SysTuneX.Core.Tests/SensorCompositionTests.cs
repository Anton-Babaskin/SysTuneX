using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Services;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The vendor libraries cannot be tested from here - there is no GPU and no driver. What can be
/// tested, and is where the mistakes live, is the part that picks between them: which probe
/// wins, what happens when one throws, and what counts as a believable number.
/// </summary>
public sealed class SensorCompositionTests
{
    [Fact]
    public async Task With_no_probes_nothing_is_reported()
    {
        SensorReadings readings = await Service().ReadAsync();

        Assert.Null(readings.Gpu);
        Assert.False(readings.HasAny);
    }

    [Fact]
    public async Task A_probe_that_answers_is_reported_with_the_vendor_it_came_from()
    {
        SensorReadings readings = await Service(Probe("NVIDIA NVML", new GpuReading(61, 74, 45))).ReadAsync();

        Assert.Equal(61, readings.Gpu!.Rounded);
        Assert.Equal("NVIDIA NVML", readings.Gpu.Source);
        Assert.Equal(74, readings.GpuUsagePercent);
        Assert.Equal(45, readings.GpuFanPercent);
    }

    [Fact]
    public async Task The_first_probe_that_answers_wins()
    {
        SensorReadings readings = await Service(
            Probe("NVIDIA NVML", new GpuReading(61, null, null)),
            Probe("AMD ADL", new GpuReading(70, null, null))).ReadAsync();

        Assert.Equal("NVIDIA NVML", readings.Gpu!.Source);
    }

    /// <summary>The usual case: one vendor's driver is installed, the other's probe finds nothing.</summary>
    [Fact]
    public async Task A_probe_with_no_card_is_skipped_and_the_next_one_answers()
    {
        SensorReadings readings = await Service(
            Probe("NVIDIA NVML", reading: null),
            Probe("AMD ADL", new GpuReading(70, null, 55))).ReadAsync();

        Assert.Equal("AMD ADL", readings.Gpu!.Source);
        Assert.Equal(70, readings.Gpu.Rounded);
        Assert.Equal(55, readings.GpuFanPercent);
    }

    [Fact]
    public async Task A_probe_that_throws_does_not_take_the_sample_with_it()
    {
        SensorReadings readings = await Service(
            new ThrowingProbe(),
            Probe("AMD ADL", new GpuReading(70, null, null))).ReadAsync();

        Assert.Equal("AMD ADL", readings.Gpu!.Source);
    }

    /// <summary>
    /// Some firmware and some drivers return a placeholder. A number outside anything a running
    /// GPU produces is worse than a blank tile, because it looks like data.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    [InlineData(4)]
    [InlineData(126)]
    [InlineData(511)]
    public async Task An_implausible_reading_is_dropped(double celsius)
    {
        SensorReadings readings = await Service(Probe("AMD ADL", new GpuReading(celsius, null, null))).ReadAsync();

        Assert.Null(readings.Gpu);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(35)]
    [InlineData(95)]
    [InlineData(125)]
    public async Task A_believable_reading_is_kept(double celsius)
    {
        SensorReadings readings = await Service(Probe("AMD ADL", new GpuReading(celsius, null, null))).ReadAsync();

        Assert.NotNull(readings.Gpu);
    }

    [Fact]
    public async Task An_implausible_first_probe_does_not_hide_a_good_second_one()
    {
        SensorReadings readings = await Service(
            Probe("NVIDIA NVML", new GpuReading(0, null, null)),
            Probe("AMD ADL", new GpuReading(68, null, null))).ReadAsync();

        Assert.Equal("AMD ADL", readings.Gpu!.Source);
    }

    [Fact]
    public void Disposing_the_service_disposes_every_probe()
    {
        var first = new StubProbe("A", new GpuReading(60, null, null));
        var second = new StubProbe("B", null);

        var service = new SensorService(NullLogger<SensorService>.Instance, [first, second], new StubCpuProbe());
        service.Dispose();

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public void One_probe_failing_to_dispose_does_not_leak_the_others()
    {
        var throwing = new ThrowingProbe();
        var normal = new StubProbe("B", null);

        var service = new SensorService(NullLogger<SensorService>.Instance, [throwing, normal], new StubCpuProbe());
        service.Dispose();

        Assert.True(normal.Disposed);
    }

    private static SensorService Service(params IGpuSensorProbe[] probes) =>
        Service(null, probes);

    private static SensorService Service(TemperatureReading? cpu, params IGpuSensorProbe[] probes) =>
        new(NullLogger<SensorService>.Instance, probes, new StubCpuProbe(cpu));

    /// <summary>
    /// A CPU probe that answers whatever the test says.
    ///
    /// These tests used to run a real WMI query against root\WMI on whatever machine they ran on,
    /// because the thermal zone read was inline in the service. So they were slow, they depended
    /// on the firmware of the test machine, and the one thing they could not do was pin what the
    /// service does with a CPU reading.
    /// </summary>
    private sealed class StubCpuProbe(TemperatureReading? reading = null) : ICpuTemperatureProbe
    {
        public TemperatureReading? Read() => reading;
    }

    private static IGpuSensorProbe Probe(string vendor, GpuReading? reading) => new StubProbe(vendor, reading);

    private sealed class StubProbe(string vendor, GpuReading? reading) : IGpuSensorProbe
    {
        public string Vendor { get; } = vendor;

        public bool Disposed { get; private set; }

        public GpuReading? Read() => reading;

        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingProbe : IGpuSensorProbe
    {
        public string Vendor => "Broken";

        public GpuReading? Read() => throw new InvalidOperationException("driver library exploded");

        public void Dispose() => throw new InvalidOperationException("and again on the way out");
    }

    /// <summary>
    /// A CPU reading reaches the readings object. Trivially true, and it could not be checked at
    /// all until the probe became something a test could supply - which is the whole argument for
    /// the seam.
    /// </summary>
    [Fact]
    public async Task A_cpu_reading_is_passed_through()
    {
        SensorReadings readings = await Service(new TemperatureReading(58, "ACPI thermal zone")).ReadAsync();

        Assert.Equal(58, readings.Cpu!.Celsius);
        Assert.Equal("ACPI thermal zone", readings.Cpu.Source);
    }

    /// <summary>
    /// A machine whose firmware exposes nothing reports nothing, rather than a zero that would be
    /// drawn on the dashboard as a real temperature.
    /// </summary>
    [Fact]
    public async Task A_machine_with_no_thermal_zone_reports_no_cpu_temperature()
    {
        Assert.Null((await Service(cpu: null).ReadAsync()).Cpu);
    }
}
