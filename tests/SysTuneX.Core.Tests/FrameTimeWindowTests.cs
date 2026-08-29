using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Services.Sensors;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The ETW session cannot be started anywhere but a live elevated Windows box, so the frame
/// counter's correctness has to be pinned down here instead - in the arithmetic that turns
/// presentation timestamps into the numbers a player reads.
/// </summary>
public class FrameTimeWindowTests
{
    private const int Game = 4242;
    private const string GameName = "shooter";

    private static FrameTimeWindow Window(double seconds = 2) =>
        new(TimeSpan.FromSeconds(seconds));

    /// <summary>Feeds evenly spaced frames and returns the timestamp of the last one.</summary>
    private static double Feed(FrameTimeWindow window, int frames, double intervalMs, double startMs = 0, int processId = Game)
    {
        double at = startMs;

        for (int i = 0; i < frames; i++)
        {
            window.Add(processId, GameName, at);
            at += intervalMs;
        }

        return at - intervalMs;
    }

    /// <summary>Asserts a reading was produced and hands it back, since Assert.NotNull returns void.</summary>
    private static FrameRateReading Reading(FrameRateReading? reading)
    {
        Assert.NotNull(reading);
        return reading;
    }

    [Fact]
    public void Nothing_presented_reports_no_reading()
    {
        Assert.Null(Window().Compute(0));
    }

    [Fact]
    public void A_single_frame_reports_no_reading()
    {
        // One timestamp is no interval, and a frame rate is made of intervals. Reporting anything
        // here would mean inventing a number.
        FrameTimeWindow window = Window();
        window.Add(Game, GameName, 0);

        Assert.Null(window.Compute(0));
    }

    [Fact]
    public void A_steady_frame_rate_is_reported_as_that_rate()
    {
        FrameTimeWindow window = Window();
        double last = Feed(window, frames: 60, intervalMs: 1000.0 / 60);

        FrameRateReading reading = Reading(window.Compute(last));

        Assert.Equal(60, reading.RoundedFps);
        Assert.Equal(1000.0 / 60, reading.FrameTimeMs, precision: 6);
        Assert.Equal(Game, reading.ProcessId);
        Assert.Equal(GameName, reading.ProcessName);
    }

    [Fact]
    public void The_one_percent_low_reports_the_stutter_the_average_hides()
    {
        // Ninety-nine frames at 10 ms and one at 100 ms: a smooth-looking average over a hitch
        // any player would see. This is the whole reason the number is on the panel.
        FrameTimeWindow window = Window();

        double at = 0;
        for (int i = 0; i < 100; i++)
        {
            window.Add(Game, GameName, at);
            at += i == 50 ? 100 : 10;
        }

        window.Add(Game, GameName, at);

        FrameRateReading reading = Reading(window.Compute(at));

        Assert.Equal(92, reading.RoundedFps);
        Assert.Equal(10, reading.RoundedOnePercentLowFps);
    }

    [Fact]
    public void A_perfectly_even_rate_has_a_one_percent_low_equal_to_it()
    {
        FrameTimeWindow window = Window();
        double last = Feed(window, frames: 200, intervalMs: 5);

        FrameRateReading reading = Reading(window.Compute(last));

        Assert.Equal(200, reading.RoundedFps);
        Assert.Equal(200, reading.RoundedOnePercentLowFps);
    }

    [Fact]
    public void Frames_that_fall_out_of_the_window_stop_counting()
    {
        // Half the window at 30 fps, then half at 120. Once the slow half has aged out the
        // reading must be 120, not an average of the two.
        FrameTimeWindow window = Window(seconds: 1);

        Feed(window, frames: 30, intervalMs: 1000.0 / 30, startMs: 0);
        double last = Feed(window, frames: 120, intervalMs: 1000.0 / 120, startMs: 1000);

        FrameRateReading reading = Reading(window.Compute(last));

        Assert.Equal(120, reading.RoundedFps);
    }

    [Fact]
    public void A_game_that_stops_presenting_stops_being_reported()
    {
        // Closing or minimising a game just stops the events. The number has to vanish rather
        // than freeze at whatever it last was - a stale frame rate is worse than none.
        FrameTimeWindow window = Window(seconds: 2);
        double last = Feed(window, frames: 60, intervalMs: 1000.0 / 60);

        Assert.NotNull(window.Compute(last));
        Assert.Null(window.Compute(last + 2001));
    }

    [Fact]
    public void Switching_game_starts_the_measurement_over()
    {
        // Alt-tabbing between two games must not average them together. The moment of the switch
        // is exactly when a blended number would look most plausible and be most wrong.
        FrameTimeWindow window = Window();

        Feed(window, frames: 60, intervalMs: 1000.0 / 30, processId: Game);
        double last = Feed(window, frames: 10, intervalMs: 1000.0 / 144, startMs: 2000, processId: 777);

        FrameRateReading reading = Reading(window.Compute(last));

        Assert.Equal(777, reading.ProcessId);
        Assert.Equal(144, reading.RoundedFps);
    }

    [Fact]
    public void Clearing_forgets_the_measurement()
    {
        FrameTimeWindow window = Window();
        double last = Feed(window, frames: 60, intervalMs: 1000.0 / 60);

        window.Clear();

        Assert.Null(window.Compute(last));
    }

    [Fact]
    public void Frames_sharing_one_timestamp_report_no_reading()
    {
        // Coarse timer resolution can stamp several presents identically. Zero elapsed time would
        // divide into an infinite frame rate, so it has to report nothing instead.
        FrameTimeWindow window = Window();

        for (int i = 0; i < 10; i++)
        {
            window.Add(Game, GameName, 500);
        }

        Assert.Null(window.Compute(500));
    }
}
