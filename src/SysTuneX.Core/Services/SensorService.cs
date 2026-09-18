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
    /// <summary>Absolute zero, for the deci-Kelvin the ACPI thermal zone reports in.</summary>

    /// <summary>
    /// A reading outside this range means the sensor is reporting a placeholder rather than a
    /// temperature - some firmware returns a constant, and 0 K or 200 C are not real numbers.
    /// </summary>
    private const double PlausibleMin = 5;
    private const double PlausibleMax = 125;

    private readonly ILogger<SensorService> _logger;
    private readonly IReadOnlyList<IGpuSensorProbe> _probes;
    private readonly ICpuTemperatureProbe _cpu;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Set once the thermal zone has refused, so a timer does not retry WMI every second.</summary>

    private bool _disposed;

    public SensorService(
        ILogger<SensorService> logger,
        IEnumerable<IGpuSensorProbe> probes,
        ICpuTemperatureProbe cpu)
    {
        _logger = logger;
        _probes = [.. probes];
        _cpu = cpu;
    }

    public async Task<SensorReadings> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return SensorReadings.None;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        finally
        {
            _gate.Release();
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

        _gate.Dispose();
    }
}
