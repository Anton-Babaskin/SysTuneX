using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Which readings are on, and what follows from that.
///
/// This lived in the monitor's view model as a row of booleans, and shipped with the one thing it
/// could get wrong left untested: whether a reading being ticked means it can actually be shown.
/// A machine with no readable temperature drew a bare "°C" with no number in front of it. Nothing
/// could catch it, because nothing in a view model is reachable from a test that runs without
/// Windows. It is here now, and so is the coverage.
/// </summary>
public sealed class MonitorSelectionTests
{
    [Fact]
    public void A_fresh_install_shows_the_defaults()
    {
        var selection = new MonitorSelection();

        Assert.True(selection.IsSelected(MonitorMetric.Fps));
        Assert.True(selection.IsSelected(MonitorMetric.CpuLoad));
        Assert.Equal(MonitorMetrics.Defaults.Count, selection.Count);
    }

    [Fact]
    public void The_defaults_fit_under_the_cap() =>
        Assert.True(MonitorMetrics.Defaults.Count <= MonitorMetrics.MaxSelected);

    [Fact]
    public void Every_catalogue_entry_is_unique()
    {
        // Two entries for one reading would draw it twice and toggle as one.
        Assert.Equal(
            MonitorMetrics.All.Count,
            MonitorMetrics.All.Select(m => m.Metric).Distinct().Count());
    }

    [Fact]
    public void Every_metric_in_the_enum_is_in_the_catalogue()
    {
        // A metric with no entry can never be ticked, which is a silent hole rather than an error.
        foreach (MonitorMetric metric in Enum.GetValues<MonitorMetric>())
        {
            Assert.Contains(MonitorMetrics.All, m => m.Metric == metric);
        }
    }

    [Fact]
    public void Ticking_past_the_cap_is_refused_rather_than_dropping_something()
    {
        // Quietly turning off a reading the user chose, to make room for the one they just asked
        // for, is worse than declining the new one.
        var selection = new MonitorSelection([]);

        foreach (MonitorMetricDefinition definition in MonitorMetrics.All.Take(MonitorMetrics.MaxSelected))
        {
            Assert.True(selection.Set(definition.Metric, true));
        }

        MonitorMetric overflow = MonitorMetrics.All[MonitorMetrics.MaxSelected].Metric;

        Assert.False(selection.CanSelectMore);
        Assert.False(selection.Set(overflow, true));
        Assert.Equal(MonitorMetrics.MaxSelected, selection.Count);
        Assert.False(selection.IsSelected(overflow));
    }

    [Fact]
    public void A_reading_already_on_can_always_be_cleared_even_at_the_cap()
    {
        var selection = new MonitorSelection(
            MonitorMetrics.All.Take(MonitorMetrics.MaxSelected).Select(m => m.Metric));

        Assert.False(selection.CanSelectMore);
        Assert.True(selection.Set(MonitorMetric.Fps, false));
        Assert.True(selection.CanSelectMore);
    }

    [Fact]
    public void Ticking_something_already_on_changes_nothing()
    {
        var selection = new MonitorSelection([MonitorMetric.Fps]);

        Assert.False(selection.Set(MonitorMetric.Fps, true));
        Assert.Equal(1, selection.Count);
    }

    [Fact]
    public void The_slow_sample_is_only_needed_when_something_selected_needs_it()
    {
        Assert.False(new MonitorSelection([MonitorMetric.CpuLoad, MonitorMetric.MemoryUsed]).NeedsSensors);
        Assert.True(new MonitorSelection([MonitorMetric.GpuTemperature]).NeedsSensors);
        Assert.True(new MonitorSelection([MonitorMetric.CpuTemperature]).NeedsSensors);
        Assert.False(new MonitorSelection([]).NeedsSensors);
    }

    [Fact]
    public void The_frame_counter_is_only_needed_for_readings_that_come_from_it()
    {
        Assert.True(new MonitorSelection([MonitorMetric.Fps]).NeedsFrameCounter);
        Assert.True(new MonitorSelection([MonitorMetric.OnePercentLow]).NeedsFrameCounter);
        Assert.False(new MonitorSelection([MonitorMetric.CpuLoad]).NeedsFrameCounter);
    }

    [Fact]
    public void A_group_with_nothing_ticked_is_reported_empty_so_its_card_can_go()
    {
        var selection = new MonitorSelection([MonitorMetric.CpuLoad]);

        Assert.False(selection.IsGroupEmpty(MonitorGroup.Cpu));
        Assert.True(selection.IsGroupEmpty(MonitorGroup.Gpu));
        Assert.True(selection.IsGroupEmpty(MonitorGroup.Memory));
    }

    [Fact]
    public void A_group_lists_its_selected_readings_in_catalogue_order()
    {
        var selection = new MonitorSelection([MonitorMetric.GpuFan, MonitorMetric.GpuLoad]);

        Assert.Equal(
            [MonitorMetric.GpuLoad, MonitorMetric.GpuFan],
            selection.InGroup(MonitorGroup.Gpu).Select(m => m.Metric));
    }

    [Fact]
    public void A_round_trip_through_the_settings_file_keeps_the_selection()
    {
        var original = new MonitorSelection([MonitorMetric.Fps, MonitorMetric.GpuTemperature]);

        MonitorSelection restored = MonitorSelection.FromNames(
            original.ToList().Select(m => m.ToString()));

        Assert.Equal(original.ToList(), restored.ToList());
    }

    [Fact]
    public void A_name_this_build_does_not_know_is_skipped_rather_than_fatal()
    {
        // A settings file written by a newer build can name a reading this one has never heard of.
        MonitorSelection selection = MonitorSelection.FromNames(["Fps", "WarpCoreTemperature", "CpuLoad"]);

        Assert.Equal([MonitorMetric.Fps, MonitorMetric.CpuLoad], selection.ToList());
    }

    [Fact]
    public void No_stored_list_at_all_falls_back_to_the_defaults()
    {
        // Absent is "this file predates the feature". Empty is "show nothing", which is a choice.
        Assert.Equal(MonitorMetrics.Defaults.Count, MonitorSelection.FromNames(null).Count);
        Assert.Equal(0, MonitorSelection.FromNames([]).Count);
    }
}
