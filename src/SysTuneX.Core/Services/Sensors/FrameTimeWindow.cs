using SysTuneX.Core.Abstractions;

namespace SysTuneX.Core.Services.Sensors;

/// <summary>
/// A sliding window of frame presentation times, and the arithmetic that turns them into the three
/// numbers a player cares about.
///
/// Kept free of ETW on purpose. The trace session cannot be exercised anywhere but a live elevated
/// Windows box, so everything that could be wrong about the numbers - the averaging, the percentile,
/// what happens when a game closes or the foreground changes - lives here, where a test can reach it.
/// The probe on top of this only has to deliver timestamps.
/// </summary>
public sealed class FrameTimeWindow(TimeSpan window)
{
    /// <summary>
    /// Fewer than this and the sample says nothing: a frame rate needs intervals between frames,
    /// and two timestamps are one interval.
    /// </summary>
    private const int MinimumSamples = 2;

    /// <summary>
    /// One frame, on two clocks.
    ///
    /// This is the whole point of the type, and it was the bug. A frame carries the time the
    /// graphics stack says it was presented, which is what the intervals between frames have to be
    /// measured from - and separately the time we heard about it, which is what "is this still
    /// happening?" has to be measured from. They are not the same clock and must never be compared
    /// to each other.
    /// </summary>
    /// <param name="PresentedAtMs">
    /// From the trace event. Only ever used as a difference against another presented time, so it
    /// does not matter what epoch or time zone it is in, only that it is monotonic.
    /// </param>
    /// <param name="ReceivedAtMs">
    /// From the caller's own monotonic clock, at the moment the event arrived. The only value
    /// compared against <c>now</c>.
    /// </param>
    private readonly record struct Frame(double PresentedAtMs, double ReceivedAtMs);

    private readonly TimeSpan _window = window;
    private readonly Queue<Frame> _frames = new();
    private readonly Lock _gate = new();

    private int _processId;
    private string _processName = string.Empty;

    /// <summary>
    /// Records a presented frame.
    ///
    /// A frame from a different process replaces the window rather than joining it. Alt-tabbing
    /// from one game to another must not average the two together, and the moment of the switch
    /// is exactly when a blended number would look most plausible and be most wrong.
    /// </summary>
    /// <param name="presentedAtMs">When the graphics stack says the frame was presented.</param>
    /// <param name="receivedAtMs">
    /// When this process heard about it, on the same clock that will be passed to
    /// <see cref="Compute"/>. Separate from <paramref name="presentedAtMs"/> on purpose - see
    /// <see cref="Frame"/>.
    /// </param>
    public void Add(int processId, string processName, double presentedAtMs, double receivedAtMs)
    {
        lock (_gate)
        {
            if (processId != _processId)
            {
                _frames.Clear();
                _processId = processId;
                _processName = processName;
            }

            _frames.Enqueue(new Frame(presentedAtMs, receivedAtMs));
            Trim(receivedAtMs);
        }
    }

    /// <summary>Drops everything, for when the foreground stops being a game at all.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _frames.Clear();
            _processId = 0;
            _processName = string.Empty;
        }
    }

    /// <summary>
    /// The current reading, or null when there is not enough recent history to state one.
    ///
    /// <paramref name="nowMs"/> is on the arrival clock - the one passed as <c>receivedAtMs</c> to
    /// <see cref="Add"/> - and is never compared against a presentation timestamp.
    /// </summary>
    public FrameRateReading? Compute(double nowMs)
    {
        lock (_gate)
        {
            // Frames stop arriving the instant a game is minimised or closed. Ageing the window
            // against the caller's clock, not against the last frame, is what makes the number
            // disappear then instead of freezing at whatever it last was.
            Trim(nowMs);

            if (_frames.Count < MinimumSamples)
            {
                return null;
            }

            double[] times = [.. _frames.Select(frame => frame.PresentedAtMs)];
            double span = times[^1] - times[0];

            if (span <= 0)
            {
                return null;
            }

            // Intervals, not frames: n timestamps bound n-1 frame times.
            int intervals = times.Length - 1;
            double fps = intervals * 1000.0 / span;
            double meanFrameTimeMs = span / intervals;

            return new FrameRateReading(
                _processName,
                _processId,
                fps,
                meanFrameTimeMs,
                OnePercentLowFps(times));
        }
    }

    /// <summary>
    /// The average of the slowest one percent of frames, expressed as a frame rate.
    ///
    /// This is the reviewers' definition, and it is the one worth showing: an average of 144 with
    /// a 1% low of 40 stutters, and a steady 90 does not. Reporting only the average would hide
    /// exactly the problem this tool exists to fix.
    /// </summary>
    private static double OnePercentLowFps(double[] times)
    {
        double[] frameTimes = new double[times.Length - 1];
        for (int i = 0; i < frameTimes.Length; i++)
        {
            frameTimes[i] = times[i + 1] - times[i];
        }

        Array.Sort(frameTimes);

        // Round up, so a short window still reports its single worst frame rather than none.
        int worstCount = Math.Max(1, (int)Math.Ceiling(frameTimes.Length / 100.0));

        double total = 0;
        for (int i = frameTimes.Length - worstCount; i < frameTimes.Length; i++)
        {
            total += frameTimes[i];
        }

        double meanWorstMs = total / worstCount;
        return meanWorstMs > 0 ? 1000.0 / meanWorstMs : 0;
    }

    /// <summary>
    /// Drops every frame we heard about longer ago than the window, measured back from
    /// <paramref name="nowMs"/> on the arrival clock.
    /// </summary>
    private void Trim(double nowMs)
    {
        double oldest = nowMs - _window.TotalMilliseconds;

        while (_frames.Count > 0 && _frames.Peek().ReceivedAtMs < oldest)
        {
            _frames.Dequeue();
        }
    }
}
