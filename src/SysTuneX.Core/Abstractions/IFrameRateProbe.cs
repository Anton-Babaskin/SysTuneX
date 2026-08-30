namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Frame rate for whatever is in the foreground, measured without touching the game.
///
/// Deliberately no injection. RTSS, Afterburner and Fraps hook the graphics API from inside the
/// game process; that is how they draw an overlay, and it is also what anti-cheat looks for. A
/// tool whose whole point is to be run before playing cannot ship something that risks a ban, so
/// this reads the Present events the graphics stack already writes to Event Tracing for Windows -
/// the same source PresentMon and the Xbox Game Bar counter use. Nothing is loaded into the game.
///
/// The cost of that choice is honest: no overlay drawn over a fullscreen game, and no frame rate
/// at all when the session cannot be started (ETW needs elevation). Both are reported as
/// unavailable rather than guessed at.
/// </summary>
public interface IFrameRateProbe : IDisposable
{
    /// <summary>
    /// Whether the trace session is running. False when ETW refused - not elevated, or another
    /// tool already owns a session with our name and would not release it.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Why the probe is not running, for the interface to show instead of an empty number.
    /// Empty while it is running.
    /// </summary>
    string UnavailableReason { get; }

    /// <summary>Starts tracing. Safe to call twice; the second call does nothing.</summary>
    void Start();

    /// <summary>
    /// Stops tracing and releases the trace session, leaving the probe able to start again.
    ///
    /// An ETW session is a machine-wide resource that keeps consuming kernel buffers for as long
    /// as it is open, so it is switched off rather than left running for a counter nobody is
    /// reading. Safe to call when it is not running.
    /// </summary>
    void Stop();

    /// <summary>
    /// The current reading, or null when no frames have been presented recently - a desktop with
    /// no game running is the normal case, not an error.
    /// </summary>
    FrameRateReading? Read();
}

/// <param name="ProcessName">The foreground process the frames belong to, for the interface to name.</param>
/// <param name="ProcessId">Which instance, so two copies of a game are not conflated.</param>
/// <param name="Fps">Frames presented per second over the sample window.</param>
/// <param name="FrameTimeMs">Mean milliseconds per frame. The reciprocal of Fps, shown because it is what stutter is measured in.</param>
/// <param name="OnePercentLowFps">
/// The frame rate of the worst 1% of frames. This is the number that corresponds to what stutter
/// feels like: an average of 144 with a 1% low of 40 plays worse than a steady 90.
/// </param>
public sealed record FrameRateReading(
    string ProcessName,
    int ProcessId,
    double Fps,
    double FrameTimeMs,
    double OnePercentLowFps)
{
    /// <summary>Rounded for display; a frame rate is not meaningful to a tenth.</summary>
    public int RoundedFps => (int)Math.Round(Fps, MidpointRounding.AwayFromZero);

    public int RoundedOnePercentLowFps => (int)Math.Round(OnePercentLowFps, MidpointRounding.AwayFromZero);
}
