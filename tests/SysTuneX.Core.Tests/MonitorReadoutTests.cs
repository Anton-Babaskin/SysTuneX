using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The rule this project keeps coming back to: never draw a number the machine did not give us.
/// The compact window is the worst place to break it - it is small, it is on top of a game, and
/// nobody reading it has the room to wonder whether a zero means zero.
/// </summary>
public sealed class MonitorReadoutTests
{
    private static MonitorSelection Only(params MonitorMetric[] metrics) => new(metrics);

    [Fact]
    public void A_sensor_this_machine_does_not_have_is_left_out_entirely()
    {
        IReadOnlyList<MonitorReading> rows = MonitorReadout.Build(
            Only(MonitorMetric.CpuTemperature, MonitorMetric.GpuTemperature, MonitorMetric.CpuLoad),
            new MonitorValues { CpuLoadPercent = 41, CpuTemperature = null, GpuTemperature = null });

        // Not "0", not "--", not "°C" with a hole in front of it. Gone.
        Assert.Equal([MonitorMetric.CpuLoad], rows.Select(r => r.Metric));
    }

    [Fact]
    public void A_sensor_that_answers_is_shown()
    {
        IReadOnlyList<MonitorReading> rows = MonitorReadout.Build(
            Only(MonitorMetric.CpuTemperature),
            new MonitorValues { CpuTemperature = 63 });

        MonitorReading row = Assert.Single(rows);
        Assert.Equal("63", row.Value);
        Assert.Equal("°C", row.Unit);
        Assert.Equal("Metric_CpuTemperature", row.NameKey);
    }

    /// <summary>
    /// With the counter switched off there is no frame rate to have an opinion about, so the row
    /// does not exist. Drawing "-- FPS" would suggest the app is trying and failing.
    /// </summary>
    [Fact]
    public void Frame_rows_are_absent_while_the_counter_is_off()
    {
        IReadOnlyList<MonitorReading> rows = MonitorReadout.Build(
            Only(MonitorMetric.Fps, MonitorMetric.FrameTime, MonitorMetric.OnePercentLow, MonitorMetric.CpuLoad),
            new MonitorValues { FrameCounterRunning = false, Fps = 144 });

        Assert.Equal([MonitorMetric.CpuLoad], rows.Select(r => r.Metric));
    }

    /// <summary>
    /// With the counter on and nothing rendering, the row stays and says "--". Its presence is
    /// the message: the counter is watching, the desktop simply is not a game.
    /// </summary>
    [Fact]
    public void A_running_counter_with_nothing_rendering_shows_a_placeholder()
    {
        IReadOnlyList<MonitorReading> rows = MonitorReadout.Build(
            Only(MonitorMetric.Fps),
            new MonitorValues { FrameCounterRunning = true, Fps = null });

        Assert.Equal(MonitorReadout.Pending, Assert.Single(rows).Value);
    }

    [Fact]
    public void A_running_counter_with_a_game_shows_the_frame_rate()
    {
        IReadOnlyList<MonitorReading> rows = MonitorReadout.Build(
            Only(MonitorMetric.Fps, MonitorMetric.FrameTime, MonitorMetric.OnePercentLow),
            new MonitorValues
            {
                FrameCounterRunning = true,
                Fps = 165,
                FrameTimeMs = 6.06,
                OnePercentLowFps = 121,
            });

        Assert.Equal(["165", "6.1", "121"], rows.Select(r => r.Value));
    }

    /// <summary>
    /// Clearing a tick and setting it again must not move the row. A readout that reorders itself
    /// is one the eye has to re-read every time it changes.
    /// </summary>
    [Fact]
    public void Rows_follow_the_catalogue_order_not_the_order_they_were_ticked()
    {
        IReadOnlyList<MonitorReading> rows = MonitorReadout.Build(
            Only(MonitorMetric.MemoryUsed, MonitorMetric.CpuLoad, MonitorMetric.GpuLoad),
            new MonitorValues { CpuLoadPercent = 12, GpuLoadPercent = 80, MemoryUsedMb = 8192 });

        Assert.Equal(
            [MonitorMetric.CpuLoad, MonitorMetric.GpuLoad, MonitorMetric.MemoryUsed],
            rows.Select(r => r.Metric));
    }

    [Fact]
    public void Nothing_ticked_draws_nothing()
    {
        Assert.Empty(MonitorReadout.Build(new MonitorSelection([]), new MonitorValues()));
    }

    /// <summary>
    /// Load is a reading that always exists, so zero is a real answer and has to survive. This is
    /// the mirror of the first test: absence is dropped, but a genuine zero is not.
    /// </summary>
    [Fact]
    public void A_genuine_zero_is_not_mistaken_for_a_missing_reading()
    {
        IReadOnlyList<MonitorReading> rows = MonitorReadout.Build(
            Only(MonitorMetric.CpuLoad, MonitorMetric.GpuLoad),
            new MonitorValues { CpuLoadPercent = 0, GpuLoadPercent = 0 });

        Assert.Equal(["0", "0"], rows.Select(r => r.Value));
    }

    [Fact]
    public void Memory_and_disk_are_shown_in_gigabytes()
    {
        IReadOnlyList<MonitorReading> rows = MonitorReadout.Build(
            Only(MonitorMetric.MemoryUsed, MonitorMetric.DiskFree),
            new MonitorValues { MemoryUsedMb = 12_288, DiskFreeMb = 512 });

        Assert.Equal(["12 GB", "0.5 GB"], rows.Select(r => r.Value));
    }

    /// <summary>
    /// Every reading in the catalogue has to be formattable. A metric added to the enum without a
    /// case here would silently vanish from the readout instead of failing anywhere visible.
    /// </summary>
    [Fact]
    public void Every_metric_in_the_catalogue_can_be_drawn()
    {
        var everything = new MonitorSelection(MonitorMetrics.All.Select(m => m.Metric));

        IReadOnlyList<MonitorReading> rows = MonitorReadout.Build(
            everything,
            new MonitorValues
            {
                FrameCounterRunning = true,
                Fps = 120,
                FrameTimeMs = 8.3,
                OnePercentLowFps = 95,
                CpuLoadPercent = 30,
                CpuTemperature = 55,
                GpuLoadPercent = 70,
                GpuTemperature = 61,
                GpuFanPercent = 44,
                MemoryUsedMb = 9000,
                StandbyMemoryMb = 2000,
                DiskFreeMb = 300_000,
                ProcessCount = 210,
                Uptime = TimeSpan.FromHours(30),
            });

        Assert.Equal(MonitorMetrics.All.Count, rows.Count);
        Assert.All(rows, row => Assert.NotEmpty(row.Value));
    }
}
