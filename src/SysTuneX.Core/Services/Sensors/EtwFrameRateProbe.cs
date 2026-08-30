using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;

namespace SysTuneX.Core.Services.Sensors;

/// <summary>
/// Counts frames by listening to the Present events the DirectX graphics stack already writes to
/// Event Tracing for Windows. Nothing is loaded into the game; see <see cref="IFrameRateProbe"/>
/// for why that matters more than the overlay it costs us.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EtwFrameRateProbe : IFrameRateProbe
{
    /// <summary>
    /// Microsoft-Windows-DXGI. Named by GUID rather than by string so starting the session needs
    /// no manifest lookup - this reads event headers only and never decodes a payload.
    /// </summary>
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");

    /// <summary>The DXGI analytic keyword. Without it the provider floods us with everything it has.</summary>
    private const ulong DxgiAnalyticKeyword = 0x1;

    /// <summary>IDXGISwapChain::Present, start. One per frame the application asks to show.</summary>
    private const int PresentStartEventId = 42;

    /// <summary>
    /// A fixed name, so a session orphaned by a crash can be found and stopped on the next run.
    /// ETW sessions outlive the process that made them; leaking one every crash would eventually
    /// exhaust the machine's session slots and need a reboot to clear.
    /// </summary>
    private const string SessionName = "SysTuneX-FrameRate";

    /// <summary>
    /// Two seconds rather than one. ETW hands over buffered events on a flush timer of about a
    /// second, so a one-second window would empty itself between flushes and blink.
    /// </summary>
    private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(2);

    /// <summary>How often to check what is in the foreground. Alt-tab does not need millisecond precision.</summary>
    private static readonly TimeSpan ForegroundPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<EtwFrameRateProbe> _logger;
    private readonly FrameTimeWindow _frames = new(SampleWindow);
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly Lock _gate = new();

    private TraceEventSession? _session;
    private Thread? _pump;
    private Timer? _foregroundWatch;
    private bool _disposed;

    /// <summary>
    /// Which process the frames are being attributed to. Written by the foreground watch and read
    /// by the trace thread on every event, hence volatile.
    /// </summary>
    private volatile int _targetProcessId;

    private volatile string _targetProcessName = string.Empty;

    public EtwFrameRateProbe(ILogger<EtwFrameRateProbe> logger) => _logger = logger;

    public bool IsRunning { get; private set; }

    public string UnavailableReason { get; private set; } = string.Empty;

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning || _disposed)
            {
                return;
            }

            try
            {
                StopStaleSession();

                _session = new TraceEventSession(SessionName) { StopOnDispose = true };
                _session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, DxgiAnalyticKeyword);
                _session.Source.AllEvents += OnEvent;

                // Process() blocks until the session stops, so it gets a thread of its own. Marked
                // background: a frame counter must never be the reason the app will not close.
                _pump = new Thread(Pump)
                {
                    IsBackground = true,
                    Name = "SysTuneX ETW frame counter",
                };
                _pump.Start();

                // The target is tracked on its own clock rather than inside Read(), so a game
                // launched while the monitor page is closed is still picked up. Two P/Invokes
                // twice a second; naming the process only happens when it actually changes.
                _foregroundWatch = new Timer(_ => RefreshTarget(), null, TimeSpan.Zero, ForegroundPollInterval);

                IsRunning = true;
                UnavailableReason = string.Empty;
                _logger.LogInformation("Frame rate tracing started");
            }
            catch (Exception ex)
            {
                // Almost always "not elevated": creating an ETW session needs administrator or
                // membership of Performance Log Users. Report it and carry on without a number,
                // rather than taking the dashboard down with us.
                UnavailableReason = ex.Message;
                _logger.LogInformation("Frame rate tracing unavailable ({Reason})", ex.Message);
                Cleanup();
            }
        }
    }

    public FrameRateReading? Read()
    {
        if (!IsRunning)
        {
            return null;
        }

        return _frames.Compute(DateTime.Now.Ticks / (double)TimeSpan.TicksPerMillisecond);
    }

    /// <summary>
    /// Points the counter at whatever is in the foreground - except ourselves.
    ///
    /// Skipping our own window is the whole reason this reads without an overlay and still works:
    /// the moment someone alt-tabs out of the game to look at the panel, we are the foreground. If
    /// that retargeted the counter it would show SysTuneX's own frame rate, which is worse than
    /// useless. Staying pointed at the game means the number survives being looked at, and it
    /// still goes blank on its own once the game genuinely stops presenting.
    /// </summary>
    private void RefreshTarget()
    {
        try
        {
            int foreground = ForegroundProcessId();

            if (foreground == 0 || foreground == _ownProcessId || foreground == _targetProcessId)
            {
                return;
            }

            // Name first: the trace thread reads both, and a frame attributed to the new process
            // before its name is set would be labelled with the old game's name.
            _targetProcessName = ProcessName(foreground);
            _targetProcessId = foreground;
        }
        catch (Exception ex)
        {
            // A timer callback that throws takes the process down with it.
            _logger.LogDebug(ex, "Could not identify the foreground process");
        }
    }

    private void OnEvent(TraceEvent data)
    {
        if ((int)data.ID != PresentStartEventId)
        {
            return;
        }

        int target = _targetProcessId;
        if (target == 0 || data.ProcessID != target)
        {
            return;
        }

        _frames.Add(target, _targetProcessName, data.TimeStamp.Ticks / (double)TimeSpan.TicksPerMillisecond);
    }

    private void Pump()
    {
        try
        {
            _session?.Source.Process();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Frame rate trace pump ended");
        }
    }

    /// <summary>
    /// Stops a session of ours left behind by an earlier crash. ETW would otherwise refuse to
    /// create a second session under the same name, and the feature would stay broken until reboot.
    /// </summary>
    private void StopStaleSession()
    {
        try
        {
            if (!TraceEventSession.GetActiveSessionNames().Contains(SessionName, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            using var stale = new TraceEventSession(SessionName, TraceEventSessionOptions.Attach);
            stale.Stop();
            _logger.LogInformation("Stopped an orphaned frame rate trace session from an earlier run");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not stop an orphaned trace session");
        }
    }

    private static int ForegroundProcessId()
    {
        nint window = Native.NativeMethods.GetForegroundWindow();
        if (window == 0)
        {
            return 0;
        }

        _ = Native.NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        return (int)processId;
    }

    private string ProcessName(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not name process {ProcessId}", processId);
            return string.Empty;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            Cleanup();

            // Not a failure, so no reason is recorded: the counter is off because it was asked to
            // be, and the interface says that rather than reporting something went wrong.
            UnavailableReason = string.Empty;
            _logger.LogInformation("Frame rate tracing stopped");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Cleanup();
        }
    }

    private void Cleanup()
    {
        IsRunning = false;

        try
        {
            _foregroundWatch?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Foreground watch did not stop cleanly");
        }

        _foregroundWatch = null;

        try
        {
            // Stopping the session is what makes Process() return and the pump thread exit.
            _session?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Frame rate trace session did not stop cleanly");
        }

        _session = null;
        _pump = null;
        _frames.Clear();

        // Forget which process was being watched. Without this a restart would keep attributing
        // frames to whatever was in the foreground when it was last switched off, and the target
        // watch only reassigns when the foreground *changes*.
        _targetProcessId = 0;
        _targetProcessName = string.Empty;
    }
}
