using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;

namespace SysTuneX.Core.Services.Sensors;

/// <summary>
/// MSAcpi_ThermalZoneTemperature is the only CPU-side temperature Windows exposes without a kernel
/// driver. Laptops usually populate it; a lot of desktop firmware does not, and where it exists it
/// is a board thermal zone rather than the CPU package - hence the honest label on the reading.
///
/// No kernel driver, deliberately, for the same reason there is no overlay: a tool people run
/// before playing cannot ship something an anti-cheat has to make a judgement about.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AcpiCpuTemperatureProbe : ICpuTemperatureProbe
{
    private const double KelvinOffset = 273.15;

    /// <summary>
    /// Outside this range the firmware is reporting something other than a temperature - a zone
    /// that is not populated usually answers with a constant. Showing it would be worse than
    /// showing nothing.
    /// </summary>
    private const double PlausibleMin = 5;
    private const double PlausibleMax = 125;

    private readonly ILogger<AcpiCpuTemperatureProbe> _logger;

    /// <summary>
    /// Asked once. A machine without a usable zone will not grow one, and a WMI query per sample
    /// for an answer that is always null is exactly the background cost this app exists to remove.
    /// </summary>
    private bool _unavailable;

    public AcpiCpuTemperatureProbe(ILogger<AcpiCpuTemperatureProbe> logger) => _logger = logger;

    public TemperatureReading? Read()
    {
        if (_unavailable)
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
                    if (celsius is >= PlausibleMin and <= PlausibleMax)
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
            _unavailable = true;
            _logger.LogInformation("No usable ACPI thermal zone on this machine; CPU temperature will not be shown");
            return null;
        }
        catch (Exception ex)
        {
            _unavailable = true;
            _logger.LogInformation(
                "ACPI thermal zone is not available ({Reason}); CPU temperature will not be shown",
                ex.Message);
            return null;
        }
    }
}
