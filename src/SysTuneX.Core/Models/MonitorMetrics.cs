namespace SysTuneX.Core.Models;

/// <summary>Everything the monitor can put on screen.</summary>
public enum MonitorMetric
{
    Fps,
    FrameTime,
    OnePercentLow,
    CpuLoad,
    CpuTemperature,
    GpuLoad,
    GpuTemperature,
    GpuFan,
    MemoryUsed,
    StandbyMemory,
    DiskFree,
    ProcessCount,
    Uptime,
}

/// <summary>Which card a metric belongs to, and therefore where its tick appears.</summary>
public enum MonitorGroup
{
    Frames,
    Cpu,
    Gpu,
    Memory,
    System,
}

/// <param name="Metric">The reading itself.</param>
/// <param name="Group">Which card carries it.</param>
/// <param name="NameKey">Resource key for the label, so both languages stay in step.</param>
/// <param name="Unit">Shown after the number. Empty where the number speaks for itself.</param>
/// <param name="NeedsFrameCounter">True when the reading only exists while the trace session runs.</param>
/// <param name="NeedsSensors">
/// True when taking it costs a WMI query or a call into the vendor's driver library, which is what
/// makes it worth switching off rather than merely hiding.
/// </param>
public sealed record MonitorMetricDefinition(
    MonitorMetric Metric,
    MonitorGroup Group,
    string NameKey,
    string Unit,
    bool NeedsFrameCounter = false,
    bool NeedsSensors = false);

/// <summary>
/// The catalogue of readings, as data rather than as a field per reading.
///
/// This replaces nine hand-written booleans, nine change handlers and two nine-line blocks copying
/// them to and from the settings file. Adding a reading is one entry here plus its two resource
/// strings; the interface, the persistence and the "how many are selected" count all follow from
/// the list. The tweak catalogue is built the same way and for the same reason.
/// </summary>
public static class MonitorMetrics
{
    /// <summary>
    /// A cap, so the readout stays a glance rather than a spreadsheet. Twelve is what fits across
    /// the cards at a readable size; past that the numbers stop being scannable, which is the only
    /// thing they are for.
    /// </summary>
    public const int MaxSelected = 12;

    public static IReadOnlyList<MonitorMetricDefinition> All { get; } =
    [
        new(MonitorMetric.Fps, MonitorGroup.Frames, "Metric_Fps", "FPS", NeedsFrameCounter: true),
        new(MonitorMetric.FrameTime, MonitorGroup.Frames, "Metric_FrameTime", "ms", NeedsFrameCounter: true),
        new(MonitorMetric.OnePercentLow, MonitorGroup.Frames, "Metric_OnePercentLow", "FPS", NeedsFrameCounter: true),

        new(MonitorMetric.CpuLoad, MonitorGroup.Cpu, "Metric_CpuLoad", "%"),
        new(MonitorMetric.CpuTemperature, MonitorGroup.Cpu, "Metric_CpuTemperature", "°C", NeedsSensors: true),

        new(MonitorMetric.GpuLoad, MonitorGroup.Gpu, "Metric_GpuLoad", "%", NeedsSensors: true),
        new(MonitorMetric.GpuTemperature, MonitorGroup.Gpu, "Metric_GpuTemperature", "°C", NeedsSensors: true),
        new(MonitorMetric.GpuFan, MonitorGroup.Gpu, "Metric_GpuFan", "%", NeedsSensors: true),

        new(MonitorMetric.MemoryUsed, MonitorGroup.Memory, "Metric_MemoryUsed", ""),
        new(MonitorMetric.StandbyMemory, MonitorGroup.Memory, "Metric_StandbyMemory", ""),

        new(MonitorMetric.DiskFree, MonitorGroup.System, "Metric_DiskFree", ""),
        new(MonitorMetric.ProcessCount, MonitorGroup.System, "Metric_ProcessCount", ""),
        new(MonitorMetric.Uptime, MonitorGroup.System, "Metric_Uptime", ""),
    ];

    /// <summary>What a fresh install shows: the frame rate story, load, temperature and memory.</summary>
    public static IReadOnlyList<MonitorMetric> Defaults { get; } =
    [
        MonitorMetric.Fps,
        MonitorMetric.FrameTime,
        MonitorMetric.OnePercentLow,
        MonitorMetric.CpuLoad,
        MonitorMetric.CpuTemperature,
        MonitorMetric.GpuLoad,
        MonitorMetric.GpuTemperature,
        MonitorMetric.MemoryUsed,
    ];

    public static MonitorMetricDefinition Find(MonitorMetric metric) =>
        All.First(m => m.Metric == metric);

    public static IEnumerable<MonitorMetricDefinition> InGroup(MonitorGroup group) =>
        All.Where(m => m.Group == group);
}
