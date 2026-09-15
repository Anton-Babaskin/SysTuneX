using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>One tile of the compact readout: a label, a number and its unit.</summary>
/// <param name="Label">Already translated; the window has no room for a second lookup.</param>
public sealed record CompactTile(MonitorMetric Metric, string Label, string Value, string Unit);

/// <summary>
/// The numbers behind the small always-on-top window.
///
/// Separate from <see cref="MonitorViewModel"/> on purpose, despite reading the same services. The
/// page's timer is tied to the page - it stops on navigation away, which is precisely the moment
/// the compact window becomes useful. This one runs while its window is open and not otherwise.
///
/// The ticks are the page's: whatever is chosen on the monitor page is what appears here, so there
/// is one list to configure rather than two that can disagree.
/// </summary>
public sealed partial class CompactMonitorViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Slower than the page's one second. This window sits over a game, and a sample costs a WMI
    /// query; two seconds is still faster than anyone can read a changing number.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>Temperatures move slowly and cost the most to read, so they get every other tick.</summary>
    private const int SensorTickInterval = 2;

    private readonly ISystemInfoService _systemInfo;
    private readonly ISensorService _sensors;
    private readonly IFrameRateProbe _frameRate;
    private readonly ILocalizationService _localization;
    private readonly IAppSettingsService _settings;
    private readonly DispatcherTimer _timer;

    private MonitorSelection _selection = new();
    private SensorReadings _readings = SensorReadings.None;
    private int _tickCount;
    private bool _disposed;

    /// <summary>The game whose frames are being counted, or empty when nothing is rendering.</summary>
    [ObservableProperty]
    private string _gameName = string.Empty;

    public CompactMonitorViewModel(
        ISystemInfoService systemInfo,
        ISensorService sensors,
        IFrameRateProbe frameRate,
        ILocalizationService localization,
        IAppSettingsService settings)
    {
        _systemInfo = systemInfo;
        _sensors = sensors;
        _frameRate = frameRate;
        _localization = localization;
        _settings = settings;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
        _timer.Tick += (_, _) => Sample();
    }

    /// <summary>What the window draws, rebuilt in place each tick.</summary>
    public ObservableCollection<CompactTile> Tiles { get; } = [];

    /// <summary>
    /// True when the ticks add up to nothing this machine can show - every reading switched off, or
    /// every one of them unavailable. The window says so rather than sitting there empty.
    /// </summary>
    public bool IsEmpty => Tiles.Count == 0;

    /// <summary>Starts sampling. Called when the window opens, not when it is constructed.</summary>
    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        _tickCount = 0;
        Sample();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }

    private void Sample()
    {
        // Re-read every tick rather than once when the window opens. The ticks are the monitor
        // page's, and someone changing them with this window open should see it change - two views
        // of one setting that disagree until a restart is worse than the cost of parsing thirteen
        // enum names every two seconds.
        _selection = MonitorSelection.FromNames(_settings.Current.Monitor.Metrics);

        if (_tickCount++ % SensorTickInterval == 0 && _selection.NeedsSensors)
        {
            _ = SampleSensorsAsync();
        }

        SystemSnapshot snapshot = _systemInfo.GetSnapshot();
        FrameRateReading? frames = _frameRate.Read();

        GameName = frames?.ProcessName ?? string.Empty;

        var values = new MonitorValues
        {
            FrameCounterRunning = _frameRate.IsRunning,
            Fps = frames?.RoundedFps,
            FrameTimeMs = frames?.FrameTimeMs,
            OnePercentLowFps = frames?.RoundedOnePercentLowFps,
            CpuLoadPercent = snapshot.CpuUsagePercent,
            CpuTemperature = _readings.Cpu?.Rounded,
            GpuLoadPercent = _readings.GpuUsagePercent,
            GpuTemperature = _readings.Gpu?.Rounded,
            GpuFanPercent = _readings.GpuFanPercent,
            MemoryUsedMb = snapshot.RamUsedMb,
            StandbyMemoryMb = snapshot.StandbyMemoryMb,
            DiskFreeMb = snapshot.SystemDriveFreeMb,
            ProcessCount = snapshot.ProcessCount,
            Uptime = snapshot.Uptime,
        };

        Apply(MonitorReadout.Build(_selection, values));
    }

    /// <summary>
    /// Writes the new readings over the old ones instead of clearing and refilling.
    ///
    /// A Clear followed by an Add per tile makes WPF tear down and rebuild every visual twice a
    /// second, which on a window sitting above a game is exactly the cost this app exists to
    /// avoid. Tiles come and go only when a sensor appears or disappears, so replacing in place is
    /// almost always a no-op for the layout.
    /// </summary>
    private void Apply(IReadOnlyList<MonitorReading> readings)
    {
        bool wasEmpty = Tiles.Count == 0;

        for (int i = 0; i < readings.Count; i++)
        {
            MonitorReading reading = readings[i];
            var tile = new CompactTile(
                reading.Metric,
                _localization[reading.NameKey],
                reading.Value,
                reading.Unit);

            if (i < Tiles.Count)
            {
                // Same reading in the same slot: only the number moved, so leave the visual alone.
                if (Tiles[i] != tile)
                {
                    Tiles[i] = tile;
                }
            }
            else
            {
                Tiles.Add(tile);
            }
        }

        while (Tiles.Count > readings.Count)
        {
            Tiles.RemoveAt(Tiles.Count - 1);
        }

        if (wasEmpty != (Tiles.Count == 0))
        {
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task SampleSensorsAsync()
    {
        try
        {
            _readings = await _sensors.ReadAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // A sensor that will not answer is a missing tile, never a broken window.
            _readings = SensorReadings.None;
        }
    }
}
