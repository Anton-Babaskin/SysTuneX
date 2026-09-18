namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Where a CPU-side temperature comes from.
///
/// GPU sensors have been behind <see cref="IGpuSensorProbe"/> since the start, and the part that
/// picks between the vendors is tested. The CPU side was a WMI query inline in SensorService, so
/// the composition around it - which readings get shown, what happens when a probe answers with
/// something implausible - could only be tested by running a real WMI query on the machine doing
/// the testing. That is what SensorCompositionTests was reduced to doing.
/// </summary>
public interface ICpuTemperatureProbe
{
    /// <summary>
    /// The current reading, or <see langword="null"/> when this machine does not expose one.
    /// Never throws: a firmware that will not answer is a missing tile, not a broken page.
    /// </summary>
    TemperatureReading? Read();
}
