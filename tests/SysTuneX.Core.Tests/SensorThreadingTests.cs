using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Services;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Every caller of the sensor service is a timer on the UI thread: the dashboard, the Monitor page,
/// and the compact readout that sits over a game. So where a read runs is not a detail. It ran on
/// the caller's thread - the method was async in signature only - and on a laptop, where the thermal
/// zone exists and WMI answers slowly, the window stopped on a fixed beat to wait for firmware.
///
/// The probes here block until released, which is what a slow WMI query looks like from outside.
/// </summary>
public sealed class SensorThreadingTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_read_does_not_hold_the_thread_that_asked_for_it()
    {
        var cpu = new GatedCpuProbe();
        var service = Service(cpu);

        // Asked from a thread of its own and waited on with a limit, so a read that runs inline
        // fails this test instead of hanging the run. A statement body, deliberately: as an
        // expression the lambda returns the read, and Task.Run would wait for that instead.
        Task<SensorReadings>? read = null;
        Task call = Task.Run(() => { read = service.ReadAsync(); });
        Task first = await Task.WhenAny(call, Task.Delay(Patience));
        cpu.Release();
        await call;

        Assert.True(first == call, "ReadAsync ran the probes on the thread that called it");
        Assert.Equal(50, (await read!).Cpu!.Celsius);
    }

    /// <summary>
    /// A read slower than the tick must not have the next tick's read queued behind it, and the one
    /// after that behind both: the numbers on screen would fall further behind with every tick.
    /// </summary>
    [Fact]
    public async Task Callers_that_arrive_during_a_read_are_handed_that_read_rather_than_queued()
    {
        var cpu = new GatedCpuProbe();
        var service = Service(cpu);

        Task<SensorReadings> first = service.ReadAsync();
        Assert.True(cpu.Entered.Wait(Patience));

        Task<SensorReadings> second = service.ReadAsync();
        Task<SensorReadings> third = service.ReadAsync();
        cpu.Release();

        SensorReadings[] all = await Task.WhenAll(first, second, third);

        Assert.Equal(1, cpu.Reads);
        Assert.Same(all[0], all[1]);
        Assert.Same(all[0], all[2]);
    }

    /// <summary>
    /// The shared read is replaced once it has finished. Getting this wrong - clearing it from
    /// inside the read, where that races with storing it - keeps one sample on screen for good.
    /// </summary>
    [Fact]
    public async Task A_read_after_the_last_one_finished_takes_a_fresh_sample()
    {
        var cpu = new GatedCpuProbe();
        cpu.Release();
        var service = Service(cpu);

        await service.ReadAsync();
        await service.ReadAsync();
        await service.ReadAsync();

        Assert.Equal(3, cpu.Reads);
    }

    /// <summary>
    /// The read is shared, so a caller leaving - a page navigated away from - ends only its own
    /// wait. Cancelling the read itself would take it away from the compact window as well.
    /// </summary>
    [Fact]
    public async Task A_caller_that_stops_waiting_does_not_cancel_the_read_for_the_others()
    {
        var cpu = new GatedCpuProbe();
        var service = Service(cpu);
        using var leaving = new CancellationTokenSource();

        Task<SensorReadings> abandoned = service.ReadAsync(leaving.Token);
        Assert.True(cpu.Entered.Wait(Patience));
        Task<SensorReadings> staying = service.ReadAsync();

        await leaving.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        cpu.Release();

        Assert.Equal(50, (await staying).Cpu!.Celsius);
        Assert.Equal(1, cpu.Reads);
    }

    /// <summary>
    /// Reads used to run on the UI thread, as disposal does at exit, so the two could not overlap.
    /// On the pool they can, and unloading a vendor's driver library underneath a call into it is
    /// a crash in native code.
    /// </summary>
    [Fact]
    public async Task Disposal_waits_for_a_read_under_way_before_unloading_the_probes()
    {
        var gpu = new GatedGpuProbe();
        var service = new SensorService(NullLogger<SensorService>.Instance, [gpu], new AnsweringCpuProbe());

        Task<SensorReadings> read = service.ReadAsync();
        Assert.True(gpu.Entered.Wait(Patience));

        Task disposing = Task.Run(service.Dispose);

        // Long enough for a disposal that does not wait to have unloaded the probe already.
        await Task.Delay(200);
        gpu.Release();

        await disposing;
        await read;

        Assert.True(gpu.Disposed);
        Assert.False(gpu.DisposedMidRead, "The probe was unloaded while it was being read");
    }

    [Fact]
    public async Task A_disposed_service_reports_nothing_rather_than_touching_the_probes()
    {
        var cpu = new GatedCpuProbe();
        cpu.Release();
        var service = Service(cpu);

        service.Dispose();
        SensorReadings readings = await service.ReadAsync();

        Assert.False(readings.HasAny);
        Assert.Equal(0, cpu.Reads);
    }

    private static SensorService Service(ICpuTemperatureProbe cpu) =>
        new(NullLogger<SensorService>.Instance, [], cpu);

    /// <summary>A thermal zone that answers only when the test lets it - a slow WMI query, in effect.</summary>
    private sealed class GatedCpuProbe : ICpuTemperatureProbe
    {
        private readonly ManualResetEventSlim _release = new(false);
        private int _reads;

        public ManualResetEventSlim Entered { get; } = new(false);

        public int Reads => Volatile.Read(ref _reads);

        public void Release() => _release.Set();

        public TemperatureReading? Read()
        {
            Interlocked.Increment(ref _reads);
            Entered.Set();
            _release.Wait(TimeSpan.FromSeconds(10));
            return new TemperatureReading(50, "test zone");
        }
    }

    private sealed class AnsweringCpuProbe : ICpuTemperatureProbe
    {
        public TemperatureReading? Read() => null;
    }

    /// <summary>A vendor library mid-call, which records whether it was unloaded underneath itself.</summary>
    private sealed class GatedGpuProbe : IGpuSensorProbe
    {
        private readonly ManualResetEventSlim _release = new(false);
        private volatile bool _reading;
        private volatile bool _disposed;
        private volatile bool _disposedMidRead;

        public ManualResetEventSlim Entered { get; } = new(false);

        public string Vendor => "Gated";

        public bool Disposed => _disposed;

        public bool DisposedMidRead => _disposedMidRead;

        public void Release() => _release.Set();

        public GpuReading? Read()
        {
            _reading = true;
            Entered.Set();
            _release.Wait(TimeSpan.FromSeconds(10));
            _reading = false;
            return new GpuReading(60, null, null);
        }

        public void Dispose()
        {
            _disposedMidRead = _reading;
            _disposed = true;
        }
    }
}
