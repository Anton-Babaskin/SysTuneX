using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="ISensorService"/>
[SupportedOSPlatform("windows")]
public sealed class SensorService : ISensorService, IDisposable
{
    /// <summary>
    /// A reading outside this range means the sensor is reporting a placeholder rather than a
    /// temperature - some firmware returns a constant, and 0 K or 200 C are not real numbers.
    /// </summary>
    private const double PlausibleMin = 5;
    private const double PlausibleMax = 125;

    /// <summary>How long disposal waits for a read already under way before unloading the libraries.</summary>
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(2);

    private readonly ILogger<SensorService> _logger;
    private readonly IReadOnlyList<IGpuSensorProbe> _probes;
    private readonly ICpuTemperatureProbe _cpu;
    private readonly Lock _flight = new();

    /// <summary>The read under way, which every caller that arrives while it runs is handed.</summary>
    private Task<SensorReadings>? _inFlight;

    private volatile bool _disposed;

    public SensorService(
        ILogger<SensorService> logger,
        IEnumerable<IGpuSensorProbe> probes,
        ICpuTemperatureProbe cpu)
    {
        _logger = logger;
        _probes = [.. probes];
        _cpu = cpu;
    }

    /// <summary>
    /// Reads on the thread pool, and joins a read already under way rather than starting another.
    ///
    /// This used to be async in signature only. Its lock completed synchronously whenever nobody
    /// else held it - which was always - so the WMI query for the thermal zone and the calls into
    /// the vendor's driver library ran on whichever thread asked. Every caller is a UI timer: the
    /// dashboard every five seconds, the Monitor page every two, and the compact readout that sits
    /// over a game every four. On a laptop, where the thermal zone exists and WMI answers slowly,
    /// that was the whole window stopping to wait for firmware on a fixed beat.
    ///
    /// Joined rather than queued, because the callers are timers: a read slower than the tick would
    /// otherwise have a second one waiting behind it, then a third, and the numbers on screen would
    /// fall further behind the machine with every tick. The read itself ignores the caller's token -
    /// it is shared, and one caller leaving must not cancel it for the others - so the token only
    /// ends that caller's wait.
    /// </summary>
    public Task<SensorReadings> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.FromResult(SensorReadings.None);
        }

        Task<SensorReadings> read;

        lock (_flight)
        {
            // A finished read is replaced, not cleared when it ends. Clearing from inside the read
            // races with storing it here, and losing that race keeps one stale sample forever.
            if (_inFlight is not { IsCompleted: false })
            {
                _inFlight = Task.Run(Sample, CancellationToken.None);
            }

            read = _inFlight;
        }

        return read.WaitAsync(cancellationToken);
    }

    /// <summary>One sample of every probe. Only ever runs one at a time: the probes are not thread-safe.</summary>
    private SensorReadings Sample()
    {
        if (_disposed)
        {
            return SensorReadings.None;
        }

        try
        {
            (TemperatureReading? gpu, int? gpuUsage, int? gpuFan) = ReadGpu();
            TemperatureReading? cpu = _cpu.Read();

            return new SensorReadings
            {
                Cpu = cpu,
                Gpu = gpu,
                GpuUsagePercent = gpuUsage,
                GpuFanPercent = gpuFan,
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Sensor sample failed");
            return SensorReadings.None;
        }
    }

    /// <summary>
    /// The first probe that answers wins. A machine has one vendor's card in it, so asking them
    /// all and taking the first reading needs no guessing about which is installed - and a probe
    /// that throws is skipped rather than taking the whole sample with it.
    /// </summary>
    private (TemperatureReading? Temperature, int? Usage, int? Fan) ReadGpu()
    {
        foreach (IGpuSensorProbe probe in _probes)
        {
            GpuReading? reading;

            try
            {
                reading = probe.Read();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GPU probe {Vendor} failed", probe.Vendor);
                continue;
            }

            if (reading is null || !IsPlausible(reading.Celsius))
            {
                continue;
            }

            return (new TemperatureReading(reading.Celsius, probe.Vendor), reading.UsagePercent, reading.FanPercent);
        }

        return (null, null, null);
    }


    private static bool IsPlausible(double celsius) => celsius is >= PlausibleMin and <= PlausibleMax;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Reads used to run on the UI thread, as disposal does at exit, so the two could never
        // overlap. On the pool they can, and unloading NVML underneath a call into it is not
        // something to find out about from a crash report. Bounded: a firmware that never answers
        // must not hold the application open.
        Task<SensorReadings>? inFlight;
        lock (_flight)
        {
            inFlight = _inFlight;
        }

        try
        {
            inFlight?.Wait(DisposeWait);
        }
        catch
        {
            // Sample catches its own failures; anything here is not worth failing shutdown over.
        }

        foreach (IGpuSensorProbe probe in _probes)
        {
            try
            {
                probe.Dispose();
            }
            catch
            {
                // One vendor library failing to unload must not stop the others from doing so.
            }
        }
    }
}
