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
    private const double KelvinOffset = 273.15;

    /// <summary>
    /// A reading outside this range means the sensor is reporting a placeholder rather than a
    /// temperature - some firmware returns a constant, and 0 K or 200 C are not real numbers.
    /// </summary>
    private const double PlausibleMin = 5;
    private const double PlausibleMax = 125;

    private readonly ILogger<SensorService> _logger;
    private readonly IReadOnlyList<IGpuSensorProbe> _probes;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Set once the thermal zone has refused, so a timer does not retry WMI every second.</summary>
    private bool _thermalZoneUnavailable;

    private bool _disposed;

    public SensorService(ILogger<SensorService> logger, IEnumerable<IGpuSensorProbe> probes)
    {
        _logger = logger;
        _probes = [.. probes];
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
            TemperatureReading? cpu = ReadThermalZone();

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

    /// <summary>
    /// MSAcpi_ThermalZoneTemperature is the only CPU-side temperature Windows exposes without a
    /// kernel driver. Laptops usually populate it; a lot of desktop firmware does not, and where
    /// it exists it is a board thermal zone rather than the CPU package - hence the honest label.
    /// </summary>
    private TemperatureReading? ReadThermalZone()
    {
        if (_thermalZoneUnavailable)
        {
            return null;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"root\WMI"),
                new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"));

            using ManagementObjectCollection results = searcher.Get();

            double warmest = double.MinValue;
            foreach (ManagementBaseObject zone in results)
            {
                using (zone)
                {
                    if (zone["CurrentTemperature"] is not { } raw)
                    {
                        continue;
                    }

                    // Reported in tenths of a Kelvin.
                    double celsius = (Convert.ToDouble(raw, CultureInfo.InvariantCulture) / 10.0) - KelvinOffset;
                    if (IsPlausible(celsius))
                    {
                        warmest = Math.Max(warmest, celsius);
                    }
                }
            }

            if (warmest > double.MinValue)
            {
                return new TemperatureReading(warmest, "ACPI thermal zone");
            }

            // Firmware answered but had nothing usable. Stop asking.
            _thermalZoneUnavailable = true;
            _logger.LogInformation("No usable ACPI thermal zone on this machine; CPU temperature will not be shown");
            return null;
        }
        catch (Exception ex)
        {
            _thermalZoneUnavailable = true;
            _logger.LogInformation(
                "ACPI thermal zone is not available ({Reason}); CPU temperature will not be shown",
                ex.Message);
            return null;
        }
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
