namespace SysTuneX.Core.Models;

/// <summary>
/// Which readings are switched on, and the questions the rest of the app asks about that set.
///
/// Kept out of the view model deliberately. The last version of this lived there as a row of
/// booleans, and the one thing it got wrong - whether a reading being ticked actually means it can
/// be shown - was invisible to every test, because nothing in a view model is reachable from a
/// test that has to run without Windows.
/// </summary>
public sealed class MonitorSelection
{
    private readonly HashSet<MonitorMetric> _selected;

    public MonitorSelection(IEnumerable<MonitorMetric>? selected = null) =>
        _selected = [.. selected ?? MonitorMetrics.Defaults];

    public int Count => _selected.Count;

    public bool IsSelected(MonitorMetric metric) => _selected.Contains(metric);

    /// <summary>
    /// True when one more can be ticked. The cap exists so the readout stays scannable; a metric
    /// already on stays selectable so its tick can always be cleared.
    /// </summary>
    public bool CanSelectMore => _selected.Count < MonitorMetrics.MaxSelected;

    /// <summary>
    /// Ticks or clears a reading, and reports whether anything changed. Ticking past the cap is
    /// refused rather than silently dropping something else - quietly turning off a reading the
    /// user chose would be worse than declining the one they just asked for.
    /// </summary>
    public bool Set(MonitorMetric metric, bool selected)
    {
        if (selected)
        {
            if (_selected.Contains(metric))
            {
                return false;
            }

            if (!CanSelectMore)
            {
                return false;
            }

            _selected.Add(metric);
            return true;
        }

        return _selected.Remove(metric);
    }

    /// <summary>
    /// Whether any selected reading needs the slow sample - WMI, or a call into the vendor's
    /// driver library. When nothing does, the sample is skipped entirely rather than taken and
    /// thrown away.
    /// </summary>
    public bool NeedsSensors =>
        MonitorMetrics.All.Any(m => m.NeedsSensors && _selected.Contains(m.Metric));

    /// <summary>Whether any selected reading comes from the frame counter.</summary>
    public bool NeedsFrameCounter =>
        MonitorMetrics.All.Any(m => m.NeedsFrameCounter && _selected.Contains(m.Metric));

    /// <summary>Selected readings in a group, in catalogue order, for a card to draw.</summary>
    public IReadOnlyList<MonitorMetricDefinition> InGroup(MonitorGroup group) =>
        [.. MonitorMetrics.InGroup(group).Where(m => _selected.Contains(m.Metric))];

    /// <summary>True when a group has nothing selected, so its whole card can be left out.</summary>
    public bool IsGroupEmpty(MonitorGroup group) => InGroup(group).Count == 0;

    /// <summary>For the settings file. Ordered so the file does not churn between saves.</summary>
    public IReadOnlyList<MonitorMetric> ToList() =>
        [.. MonitorMetrics.All.Select(m => m.Metric).Where(_selected.Contains)];

    /// <summary>
    /// Reads a stored list back, dropping anything unrecognised.
    ///
    /// A settings file written by a newer build can name a reading this one has never heard of,
    /// and a file written by an older one can be missing the key entirely. Neither should stop the
    /// page loading, so an unreadable entry is skipped and an absent list falls back to defaults.
    /// </summary>
    public static MonitorSelection FromNames(IEnumerable<string>? names)
    {
        if (names is null)
        {
            return new MonitorSelection();
        }

        var parsed = new List<MonitorMetric>();
        foreach (string name in names)
        {
            if (Enum.TryParse(name, ignoreCase: true, out MonitorMetric metric) &&
                MonitorMetrics.All.Any(m => m.Metric == metric))
            {
                parsed.Add(metric);
            }
        }

        // An empty file is a deliberate "show nothing", which is different from having no file.
        return new MonitorSelection(parsed);
    }
}
