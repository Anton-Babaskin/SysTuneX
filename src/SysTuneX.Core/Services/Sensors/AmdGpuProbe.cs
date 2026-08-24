using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Native;

namespace SysTuneX.Core.Services.Sensors;

/// <summary>
/// Reads a Radeon through ADL, which ships with the AMD driver.
///
/// ADL enumerates one adapter per display output rather than per card, so the adapter that
/// answers is found by asking each active one in turn instead of assuming index 0. Which
/// temperature call works depends on the Overdrive generation the driver exposes, so both are
/// tried - a card that answers neither reports nothing rather than a guess.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AmdGpuProbe : IGpuSensorProbe
{
    private readonly ILogger<AmdGpuProbe> _logger;

    /// <summary>Held in a field so the GC cannot collect it while ADL still holds the pointer.</summary>
    private readonly Adl.MallocCallback _allocate = size => Marshal.AllocHGlobal(size);

    private IntPtr _context;
    private bool _attempted;
    private bool _ready;
    private bool _disposed;

    public AmdGpuProbe(ILogger<AmdGpuProbe> logger) => _logger = logger;

    public string Vendor => "AMD ADL";

    public GpuReading? Read()
    {
        if (_disposed || !Initialise())
        {
            return null;
        }

        try
        {
            if (Adl.NumberOfAdapters(_context, out int count) != Adl.Ok || count <= 0)
            {
                return null;
            }

            for (int adapter = 0; adapter < count; adapter++)
            {
                if (Adl.AdapterActive(_context, adapter, out int active) != Adl.Ok || active == 0)
                {
                    continue;
                }

                if (ReadAdapter(adapter) is { } reading)
                {
                    return reading;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ADL sample failed");
            return null;
        }
    }

    private GpuReading? ReadAdapter(int adapter)
    {
        double? celsius = null;

        // OverdriveN first: it is what current drivers expose, and it takes a sensor type so it
        // can name the edge temperature rather than whatever the card considers default.
        if (Adl.OverdriveNTemperature(_context, adapter, Adl.TemperatureTypeCore, out int milli) == Adl.Ok && milli > 0)
        {
            celsius = milli / Adl.MilliDegrees;
        }
        else if (Adl.Overdrive6Temperature(_context, adapter, out int legacyMilli) == Adl.Ok && legacyMilli > 0)
        {
            celsius = legacyMilli / Adl.MilliDegrees;
        }

        if (celsius is not { } temperature)
        {
            return null;
        }

        int? fan = Adl.Overdrive6FanSpeed(_context, adapter, out int fanSpeed) == Adl.Ok && fanSpeed is > 0 and <= 100
            ? fanSpeed
            : null;

        // ADL's utilisation call is one of the struct-taking ones this deliberately avoids, so
        // an AMD card reports temperature and fan but no load.
        return new GpuReading(temperature, UsagePercent: null, fan);
    }

    private bool Initialise()
    {
        if (_ready)
        {
            return true;
        }

        if (_attempted)
        {
            return false;
        }

        _attempted = true;

        try
        {
            // 1: only adapters with something connected, which is what a display GPU is.
            if (Adl.MainControlCreate(_allocate, 1, out IntPtr context) != Adl.Ok || context == IntPtr.Zero)
            {
                _logger.LogDebug("ADL is present but would not initialise");
                return false;
            }

            _context = context;
            _ready = true;
            _logger.LogInformation("ADL initialised");
            return true;
        }
        catch (DllNotFoundException)
        {
            // Expected on NVIDIA and Intel systems.
            _logger.LogDebug("atiadlxx.dll is not present");
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            // An older driver branch without the ADL2 entry points.
            _logger.LogDebug(ex, "ADL is present but does not export the calls SysTuneX uses");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ADL could not be initialised");
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
            Adl.MainControlDestroy(_context);
        }
        catch
        {
            // Nothing useful to do if the driver library is already gone.
        }
        finally
        {
            _context = IntPtr.Zero;
        }
    }
}
