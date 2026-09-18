using System.Globalization;

namespace SysTuneX.Core.Models;

/// <summary>
/// Everything the monitor has measured this tick, as one value.
///
/// Nullable where the machine may simply not have the sensor, and not nullable where the reading
/// always exists. That distinction is the whole point of the type: a temperature of zero and a
/// temperature nobody can read are different facts, and the version of this code that kept them in
/// the same <c>int</c> drew "0 °C" on machines whose firmware exposes nothing.
/// </summary>
public sealed record MonitorValues
{
    public int? Fps { get; init; }

    public double? FrameTimeMs { get; init; }

    public int? OnePercentLowFps { get; init; }

    public double CpuLoadPercent { get; init; }

    public int? CpuTemperature { get; init; }

    public int? GpuLoadPercent { get; init; }

    public int? GpuTemperature { get; init; }

    public int? GpuFanPercent { get; init; }

    public long MemoryUsedMb { get; init; }

    public long StandbyMemoryMb { get; init; }

    public long DiskFreeMb { get; init; }

    public int ProcessCount { get; init; }

    public TimeSpan Uptime { get; init; }

    /// <summary>
    /// Whether the trace session is up. Separate from <see cref="Fps"/> being null, because
    /// "watching, nothing rendering yet" and "not watching at all" are different answers.
    /// </summary>
    public bool FrameCounterRunning { get; init; }
}

/// <param name="Metric">Which reading this is.</param>
/// <param name="NameKey">Resource key for the label; Core does not know about languages.</param>
/// <param name="Value">Already formatted, or "--" when the reading is on but has nothing to say.</param>
/// <param name="Unit">Shown after the value. Empty where the value carries its own unit.</param>
public sealed record MonitorReading(MonitorMetric Metric, string NameKey, string Value, string Unit);

/// <summary>
/// Turns a selection plus a tick of values into the rows to draw.
///
/// This is the half of the readout that can be wrong in ways worth testing, and it is shared:
/// the settings panel's one-line preview and the compact window both draw from it, so what the
/// preview promises is exactly what the window shows.
///
/// Two rules, and they are the ones that keep the numbers honest:
///
/// A reading the machine cannot supply is left out rather than drawn empty. A row reading "°C"
/// with nothing in front of it tells nobody that their firmware exposes no sensor.
///
/// A frame reading is left out when the counter is off, and shows "--" when the counter is on but
/// nothing is rendering. The row's presence is the message: it says the counter is watching.
/// </summary>
public static class MonitorReadout
{
    /// <summary>Stands in for a reading that is being taken but has nothing to report yet.</summary>
    public const string Pending = "--";

    public static IReadOnlyList<MonitorReading> Build(MonitorSelection selection, MonitorValues values)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(values);

        var rows = new List<MonitorReading>();

        // Catalogue order, not selection order, so the readout does not rearrange itself when a
        // tick is cleared and set again.
        foreach (MonitorMetricDefinition definition in MonitorMetrics.All)
        {
            if (!selection.IsSelected(definition.Metric))
            {
                continue;
            }

            if (definition.NeedsFrameCounter && !values.FrameCounterRunning)
            {
                continue;
            }

            if (Format(definition.Metric, values) is not { } text)
            {
                continue;
            }

            rows.Add(new MonitorReading(definition.Metric, definition.NameKey, text, definition.Unit));
        }

        return rows;
    }

    /// <summary>
    /// The value as text, or null when this machine cannot supply the reading at all.
    ///
    /// Frame readings never return null - <see cref="Build"/> has already decided whether the
    /// counter is running, and once it is, an idle moment is <see cref="Pending"/> rather than an
    /// absence.
    /// </summary>
    private static string? Format(MonitorMetric metric, MonitorValues values) => metric switch
    {
        MonitorMetric.Fps => values.Fps?.ToString(CultureInfo.CurrentCulture) ?? Pending,
        MonitorMetric.FrameTime => values.FrameTimeMs is > 0 and { } ms
            ? ms.ToString("F1", CultureInfo.CurrentCulture)
            : Pending,
        MonitorMetric.OnePercentLow => values.OnePercentLowFps is > 0 and { } low
            ? low.ToString(CultureInfo.CurrentCulture)
            : Pending,

        MonitorMetric.CpuLoad => values.CpuLoadPercent.ToString("F0", CultureInfo.CurrentCulture),
        MonitorMetric.CpuTemperature => values.CpuTemperature?.ToString(CultureInfo.CurrentCulture),

        MonitorMetric.GpuLoad => values.GpuLoadPercent?.ToString(CultureInfo.CurrentCulture),
        MonitorMetric.GpuTemperature => values.GpuTemperature?.ToString(CultureInfo.CurrentCulture),
        MonitorMetric.GpuFan => values.GpuFanPercent?.ToString(CultureInfo.CurrentCulture),

        MonitorMetric.MemoryUsed => Gigabytes(values.MemoryUsedMb),
        MonitorMetric.StandbyMemory => Gigabytes(values.StandbyMemoryMb),

        MonitorMetric.DiskFree => Gigabytes(values.DiskFreeMb),
        MonitorMetric.ProcessCount => values.ProcessCount.ToString(CultureInfo.CurrentCulture),
        MonitorMetric.Uptime => values.Uptime.ToString(@"d\.hh\:mm", CultureInfo.InvariantCulture),

        _ => null,
    };

    private static string Gigabytes(long megabytes) =>
        (megabytes / 1024.0).ToString("0.# GB", CultureInfo.CurrentCulture);
}
