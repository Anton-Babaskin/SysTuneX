using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Native;

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
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _nvmlReady;
    private bool _nvmlAttempted;
    private IntPtr _nvmlDevice;

    /// <summary>Set once the thermal zone has refused, so a timer does not retry WMI every second.</summary>
    private bool _thermalZoneUnavailable;

    private bool _disposed;

    public SensorService(ILogger<SensorService> logger) => _logger = logger;

    public async Task<SensorReadings> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return SensorReadings.None;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TemperatureReading? gpu = ReadNvidia(out int? gpuUsage, out int? gpuFan);
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
    /// NVML, which ships with the NVIDIA driver. AMD's equivalent is ADL and Intel's is a
    /// different library again; neither is wired up yet, so those cards report no temperature
    /// rather than a made-up one.
    /// </summary>
    private TemperatureReading? ReadNvidia(out int? usage, out int? fan)
    {
        usage = null;
        fan = null;

        if (!EnsureNvml())
        {
            return null;
        }

        try
        {
            if (Nvml.GetUtilization(_nvmlDevice, out Nvml.Utilization utilization) == Nvml.Success)
            {
                usage = (int)Math.Min(utilization.Gpu, 100);
            }

            if (Nvml.GetFanSpeed(_nvmlDevice, out uint fanPercent) == Nvml.Success)
            {
                fan = (int)Math.Min(fanPercent, 100);
            }

            if (Nvml.GetTemperature(_nvmlDevice, Nvml.TemperatureSensorGpu, out uint celsius) != Nvml.Success)
            {
                return null;
            }

            return IsPlausible(celsius) ? new TemperatureReading(celsius, "NVIDIA NVML") : null;
        }
        catch (DllNotFoundException)
        {
            _nvmlReady = false;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NVML sample failed");
            return null;
        }
    }

    private bool EnsureNvml()
    {
        if (_nvmlReady)
        {
            return true;
        }

        if (_nvmlAttempted)
        {
            return false;
        }

        _nvmlAttempted = true;

        try
        {
            if (Nvml.Init() != Nvml.Success)
            {
                _logger.LogDebug("NVML is present but would not initialise; no NVIDIA GPU readings");
                return false;
            }

            if (Nvml.GetDeviceCount(out uint count) != Nvml.Success || count == 0)
            {
                Nvml.Shutdown();
                return false;
            }

            // Device 0. A machine with two NVIDIA cards is rare enough that picking the first
            // is better than guessing which one the game is on.
            if (Nvml.GetDeviceHandle(0, out IntPtr device) != Nvml.Success)
            {
                Nvml.Shutdown();
                return false;
            }

            _nvmlDevice = device;
            _nvmlReady = true;
            _logger.LogInformation("NVML initialised, reading GPU sensors from device 0 of {Count}", count);
            return true;
        }
        catch (DllNotFoundException)
        {
            // No NVIDIA driver on this machine. Expected on AMD and Intel systems.
            _logger.LogDebug("nvml.dll is not present; no NVIDIA GPU readings");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NVML could not be initialised");
            return false;
        }
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

        if (_nvmlReady)
        {
            try
            {
                Nvml.Shutdown();
            }
            catch
            {
                // Nothing useful to do if the driver library is already gone.
            }
        }

        _gate.Dispose();
    }
}
