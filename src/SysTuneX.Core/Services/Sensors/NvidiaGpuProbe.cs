using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Native;

namespace SysTuneX.Core.Services.Sensors;

/// <summary>
/// Reads a GeForce through NVML, which ships with the NVIDIA driver and lives in System32 - no
/// install, and no kernel driver.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NvidiaGpuProbe : IGpuSensorProbe
{
    private readonly ILogger<NvidiaGpuProbe> _logger;

    private bool _ready;
    private bool _attempted;
    private IntPtr _device;
    private bool _disposed;

    public NvidiaGpuProbe(ILogger<NvidiaGpuProbe> logger) => _logger = logger;

    public string Vendor => "NVIDIA NVML";

    public GpuReading? Read()
    {
        if (_disposed || !Initialise())
        {
            return null;
        }

        try
        {
            int? usage = Nvml.GetUtilization(_device, out Nvml.Utilization utilization) == Nvml.Success
                ? (int)Math.Min(utilization.Gpu, 100)
                : null;

            int? fan = Nvml.GetFanSpeed(_device, out uint fanPercent) == Nvml.Success
                ? (int)Math.Min(fanPercent, 100)
                : null;

            return Nvml.GetTemperature(_device, Nvml.TemperatureSensorGpu, out uint celsius) == Nvml.Success
                ? new GpuReading(celsius, usage, fan)
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NVML sample failed");
            return null;
        }
    }

    private bool Initialise()
    {
        if (_ready)
        {
            return true;
        }

        // One attempt. A machine without an NVIDIA driver would otherwise pay for a failed
        // library load on every tick of the dashboard timer.
        if (_attempted)
        {
            return false;
        }

        _attempted = true;

        try
        {
            if (Nvml.Init() != Nvml.Success)
            {
                _logger.LogDebug("NVML is present but would not initialise");
                return false;
            }

            if (Nvml.GetDeviceCount(out uint count) != Nvml.Success || count == 0)
            {
                Nvml.Shutdown();
                return false;
            }

            // Device 0. Two NVIDIA cards in one machine is rare enough that picking the first
            // beats guessing which one the game is on.
            if (Nvml.GetDeviceHandle(0, out IntPtr device) != Nvml.Success)
            {
                Nvml.Shutdown();
                return false;
            }

            _device = device;
            _ready = true;
            _logger.LogInformation("NVML initialised, reading device 0 of {Count}", count);
            return true;
        }
        catch (DllNotFoundException)
        {
            // Expected on AMD and Intel systems.
            _logger.LogDebug("nvml.dll is not present");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NVML could not be initialised");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_ready)
        {
            return;
        }

        try
        {
            Nvml.Shutdown();
        }
        catch
        {
            // Nothing useful to do if the driver library is already gone.
        }
    }
}
