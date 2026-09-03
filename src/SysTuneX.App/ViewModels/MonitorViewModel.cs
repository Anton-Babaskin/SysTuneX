using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// The one screen to leave open on a second monitor while playing: frame rate, load and
/// temperature, and nothing else.
///
/// Every tile here answers a question a player actually asks - is the game running slower than it
/// should, and if so what is the machine doing about it. Anything that does not answer one of
/// those belongs on the dashboard instead.
/// </summary>
public sealed partial class MonitorViewModel : PageViewModel
{
    private const int HistoryLength = 60;

    /// <summary>Temperatures move slowly and cost far more to read, so they get every other tick.</summary>
    private const int SensorTickInterval = 2;

    private readonly ISystemInfoService _systemInfo;
    private readonly ISensorService _sensors;
    private readonly IFrameRateProbe _frameRate;
    private readonly ILocalizationService _localization;
    private readonly IAppSettingsService _settings;

    /// <summary>Which readings are on. The single source of truth the whole page reads from.</summary>
    private MonitorSelection _selection = new();

    /// <summary>Serialises starting the trace session against stopping it.</summary>
    private readonly SemaphoreSlim _frameCounterGate = new(1, 1);
    private readonly DispatcherTimer _timer;

    private int _tickCount;
    private bool _sensorsChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFrameRate))]
    private int? _fps;

    [ObservableProperty]
    private int _onePercentLowFps;

    [ObservableProperty]
    private double _frameTimeMs;

    [ObservableProperty]
    private string _gameName = string.Empty;

    /// <summary>
    /// Why there is no frame rate, in words. "Nothing is being rendered" and "the trace session
    /// could not start" are different problems and a blank tile tells them apart for nobody.
    /// </summary>
    [ObservableProperty]
    private string _frameRateHint = string.Empty;

    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCpuTemperature))]
    [NotifyPropertyChangedFor(nameof(HasNoTemperature))]
    private int? _cpuTemperature;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGpuUsage))]
    private int? _gpuUsage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGpuTemperature))]
    [NotifyPropertyChangedFor(nameof(HasNoTemperature))]
    private int? _gpuTemperature;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGpuFan))]
    private int? _gpuFan;

    [ObservableProperty]
    private string _gpuName = string.Empty;

    [ObservableProperty]
    private string _cpuName = string.Empty;

    [ObservableProperty]
    private long _ramUsedMb;

    [ObservableProperty]
    private long _ramTotalMb;

    [ObservableProperty]
    private double _ramUsagePercent;

    [ObservableProperty]
    private int _processCount;

    [ObservableProperty]
    private long _standbyMb;

    [ObservableProperty]
    private long _driveFreeMb;

    [ObservableProperty]
    private long _driveTotalMb;

    [ObservableProperty]
    private TimeSpan _uptime;

    [ObservableProperty]
    private bool _frameCounterEnabled;

    [ObservableProperty]
    private bool _keepFrameCounterInBackground = true;

    /// <summary>True while the trace session is being created, so the switch can show it is working.</summary>
    [ObservableProperty]
    private bool _frameCounterStarting;

    public MonitorViewModel(
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

        GroupedOptions = CollectionViewSource.GetDefaultView(Options);
        GroupedOptions.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MonitorMetricOption.GroupName)));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    /// <summary>The tick list, built from the catalogue so a new reading needs no code here.</summary>
    public ObservableCollection<MonitorMetricOption> Options { get; } = [];

    /// <summary>
    /// The same list, grouped for the panel.
    ///
    /// Built here rather than as a CollectionViewSource in the page's resources. A Binding inside a
    /// ResourceDictionary has no DataContext to resolve against - it can appear to work through
    /// WPF's inheritance context and then quietly resolve to nothing, which would leave the panel
    /// with no ticks at all and no error to explain why.
    /// </summary>
    public ICollectionView GroupedOptions { get; }

    /// <summary>"9 / 12", so the cap is visible before it is hit rather than only when it refuses.</summary>
    public string SelectionCount => $"{_selection.Count} / {MonitorMetrics.MaxSelected}";

    /// <summary>
    /// A sample of the readout with the current ticks applied, so the effect of a tick is visible
    /// without closing the panel and looking at the cards behind it.
    /// </summary>
    public string Preview => string.Join("   ", _selection.ToList().Select(PreviewPart));

    public ObservableCollection<double> FpsHistory { get; } = [];

    public ObservableCollection<double> CpuHistory { get; } = [];

    public ObservableCollection<double> GpuHistory { get; } = [];

    public ObservableCollection<double> RamHistory { get; } = [];

    public bool HasFrameRate => Fps is not null;

    /// <summary>
    /// The top of the frame rate graph, snapped to a refresh rate people recognise. A graph that
    /// rescaled itself to whatever the peak happened to be would make every session look the same
    /// shape, which is the opposite of what a graph is for.
    /// </summary>
    public double FpsScale
    {
        get
        {
            double peak = FpsHistory.Count == 0 ? 0 : FpsHistory.Max();

            return peak switch
            {
                <= 60 => 60,
                <= 120 => 120,
                <= 144 => 144,
                <= 240 => 240,
                _ => 360,
            };
        }
    }

    public bool HasCpuTemperature => CpuTemperature is not null;

    public bool HasGpuTemperature => GpuTemperature is not null;

    public bool HasGpuUsage => GpuUsage is not null;

    public bool HasGpuFan => GpuFan is not null;

    // Asked for *and* obtainable. Binding a tile to the tick alone was the audit's first finding:
    // a machine whose firmware exposes no temperature drew a bare "°C" with no number in front of
    // it, which is exactly the plausible-looking wrong reading this project refuses everywhere
    // else. Every one of these is "the user wants it" AND "the machine can supply it".

    public bool DisplayFps => Selected(MonitorMetric.Fps) && HasFrameRate;

    public bool DisplayFrameTime => Selected(MonitorMetric.FrameTime) && HasFrameRate;

    public bool DisplayOnePercentLow => Selected(MonitorMetric.OnePercentLow) && HasFrameRate;

    public bool DisplayCpuLoad => Selected(MonitorMetric.CpuLoad);

    public bool DisplayCpuTemperature => Selected(MonitorMetric.CpuTemperature) && HasCpuTemperature;

    public bool DisplayGpuLoad => Selected(MonitorMetric.GpuLoad) && HasGpuUsage;

    public bool DisplayGpuTemperature => Selected(MonitorMetric.GpuTemperature) && HasGpuTemperature;

    public bool DisplayGpuFan => Selected(MonitorMetric.GpuFan) && HasGpuFan;

    public bool DisplayMemory => Selected(MonitorMetric.MemoryUsed);

    public bool DisplayStandby => Selected(MonitorMetric.StandbyMemory);

    public bool DisplayDiskFree => Selected(MonitorMetric.DiskFree);

    public bool DisplayProcessCount => Selected(MonitorMetric.ProcessCount);

    public bool DisplayUptime => Selected(MonitorMetric.Uptime);

    /// <summary>A card is drawn when its group has anything ticked at all.</summary>
    public bool ShowFrameCard => FrameCounterEnabled && !_selection.IsGroupEmpty(MonitorGroup.Frames);

    public bool ShowCpuCard => !_selection.IsGroupEmpty(MonitorGroup.Cpu);

    public bool ShowGpuCard => !_selection.IsGroupEmpty(MonitorGroup.Gpu);

    public bool ShowMemoryCard =>
        !_selection.IsGroupEmpty(MonitorGroup.Memory) || !_selection.IsGroupEmpty(MonitorGroup.System);

    /// <summary>
    /// How many of the lower cards are on screen, so the row divides itself between exactly those.
    /// A UniformGrid ignores collapsed children, so giving it the visible count makes them fill the
    /// width - the fixed three columns this replaces left a hole, despite a comment claiming the
    /// opposite. The comment was the bug; this is the fix.
    /// </summary>
    public int VisibleCardCount =>
        Math.Max(1, (ShowCpuCard ? 1 : 0) + (ShowGpuCard ? 1 : 0) + (ShowMemoryCard ? 1 : 0));

    public bool ShowNothing => !ShowFrameCard && !ShowCpuCard && !ShowGpuCard && !ShowMemoryCard;

    private bool Selected(MonitorMetric metric) => _selection.IsSelected(metric);

    /// <summary>
    /// False only once a sample has actually come back empty. Before that the tiles say nothing
    /// rather than announcing an absence that has not been established yet.
    /// </summary>
    /// <summary>
    /// Every temperature that was asked for came back empty. Asking whether *both* were null was
    /// wrong: with GPU temperature ticked off and readable, a missing CPU temperature that was
    /// ticked on had its explanation suppressed by a number nobody had asked to see.
    /// </summary>
    public bool HasNoTemperature =>
        _sensorsChecked &&
        (Selected(MonitorMetric.CpuTemperature) || Selected(MonitorMetric.GpuTemperature)) &&
        !DisplayCpuTemperature &&
        !DisplayGpuTemperature;


    private bool _loading;

    private void LoadSettings()
    {
        MonitorSettings monitor = _settings.Current.Monitor;

        _loading = true;
        try
        {
            _selection = MonitorSelection.FromNames(monitor.Metrics);
            FrameCounterEnabled = monitor.FrameCounter;
            KeepFrameCounterInBackground = monitor.KeepFrameCounterInBackground;
        }
        finally
        {
            _loading = false;
        }

        BuildOptions();
    }

    /// <summary>
    /// One tickable row per catalogue entry, grouped for the panel. Built once: the catalogue does
    /// not change at run time, so only the ticks and the enabled state move afterwards.
    /// </summary>
    private void BuildOptions()
    {
        Options.Clear();

        foreach (MonitorMetricDefinition definition in MonitorMetrics.All)
        {
            Options.Add(new MonitorMetricOption(
                definition,
                _localization[definition.NameKey],
                _localization[$"MetricGroup_{definition.Group}"],
                _selection.IsSelected(definition.Metric),
                OnOptionToggled));
        }

        RefreshOptionAvailability();
    }

    /// <summary>
    /// A tick the cap would refuse is disabled rather than silently doing nothing when clicked.
    /// Anything already on stays clickable so it can always be turned back off.
    /// </summary>
    private void RefreshOptionAvailability()
    {
        bool room = _selection.CanSelectMore;

        foreach (MonitorMetricOption option in Options)
        {
            option.CanToggle = room || option.IsSelected;
        }
    }

    private void OnOptionToggled(MonitorMetricOption option, bool wanted)
    {
        if (!_selection.Set(option.Definition.Metric, wanted))
        {
            // Refused by the cap. Put the tick back so the interface matches the state.
            option.RestoreTo(_selection.IsSelected(option.Definition.Metric));
            return;
        }

        RefreshOptionAvailability();
        RefreshVisibility();
        SaveSettings();
    }

    /// <summary>Every derived flag the page draws from. Raised together because a tick moves several.</summary>
    private void RefreshVisibility()
    {
        foreach (string name in (string[])
        [
            nameof(SelectionCount), nameof(Preview), nameof(ShowNothing), nameof(VisibleCardCount),
            nameof(ShowFrameCard), nameof(ShowCpuCard), nameof(ShowGpuCard), nameof(ShowMemoryCard),
            nameof(DisplayFps), nameof(DisplayFrameTime), nameof(DisplayOnePercentLow),
            nameof(DisplayCpuLoad), nameof(DisplayCpuTemperature),
            nameof(DisplayGpuLoad), nameof(DisplayGpuTemperature), nameof(DisplayGpuFan),
            nameof(DisplayMemory), nameof(DisplayStandby), nameof(DisplayDiskFree),
            nameof(DisplayProcessCount), nameof(DisplayUptime), nameof(HasNoTemperature),
        ])
        {
            OnPropertyChanged(name);
        }
    }

    private void SaveSettings()
    {
        if (_loading)
        {
            return;
        }

        MonitorSettings monitor = _settings.Current.Monitor;
        monitor.FrameCounter = FrameCounterEnabled;
        monitor.KeepFrameCounterInBackground = KeepFrameCounterInBackground;
        monitor.Metrics = [.. _selection.ToList().Select(m => m.ToString())];

        _ = _settings.SaveAsync();
    }

    private string PreviewPart(MonitorMetric metric)
    {
        MonitorMetricDefinition definition = MonitorMetrics.Find(metric);
        string label = _localization[definition.NameKey];

        string value = metric switch
        {
            MonitorMetric.Fps => Fps?.ToString() ?? "--",
            MonitorMetric.FrameTime => FrameTimeMs > 0 ? FrameTimeMs.ToString("F1") : "--",
            MonitorMetric.OnePercentLow => OnePercentLowFps > 0 ? OnePercentLowFps.ToString() : "--",
            MonitorMetric.CpuLoad => CpuUsage.ToString("F0"),
            MonitorMetric.CpuTemperature => CpuTemperature?.ToString() ?? "--",
            MonitorMetric.GpuLoad => GpuUsage?.ToString() ?? "--",
            MonitorMetric.GpuTemperature => GpuTemperature?.ToString() ?? "--",
            MonitorMetric.GpuFan => GpuFan?.ToString() ?? "--",
            MonitorMetric.MemoryUsed => $"{RamUsedMb / 1024.0:0.#} GB",
            MonitorMetric.StandbyMemory => $"{StandbyMb / 1024.0:0.#} GB",
            MonitorMetric.DiskFree => $"{DriveFreeMb / 1024.0:0.#} GB",
            MonitorMetric.ProcessCount => ProcessCount.ToString(),
            MonitorMetric.Uptime => Uptime.ToString(@"d\.hh\:mm"),
            _ => "--",
        };

        return definition.Unit.Length > 0 ? $"{label} {value}{definition.Unit}" : $"{label} {value}";
    }

    private async Task StartFrameCounterAsync()
    {
        // Serialised against itself and against Stop. Without this, switching off while a start
        // was still in flight lost the stop: the session came up afterwards and kept running with
        // the switch showing off - a machine-wide ETW session nobody could see or close.
        await _frameCounterGate.WaitAsync().ConfigureAwait(true);
        try
        {
            FrameCounterStarting = true;
            await Task.Run(_frameRate.Start).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Start reports its own refusals through IsRunning; nothing here is fatal to the page.
        }
        finally
        {
            FrameCounterStarting = false;
            _frameCounterGate.Release();
        }

        // The switch may have been turned off while the session was coming up. Honour the state
        // the user left it in, not the one it had when the start began.
        if (!FrameCounterEnabled)
        {
            _frameRate.Stop();
            return;
        }

        if (!_frameRate.IsRunning)
        {
            FrameRateHint = _localization.Format("Monitor_Fps_Unavailable", _frameRate.UnavailableReason);
        }

        RefreshVisibility();
    }

    partial void OnKeepFrameCounterInBackgroundChanged(bool value) => SaveSettings();

    protected override async Task OnEnterAsync()
    {
        if (!IsInitialized)
        {
            HardwareInfo hardware = await _systemInfo.GetHardwareInfoAsync(PageToken).ConfigureAwait(true);
            CpuName = hardware.CpuName;
            GpuName = hardware.GpuName;

            LoadSettings();
        }

        // Restores the switch rather than acting on a page visit. The trace session used to be
        // started here the first time anyone opened the page and then left running for the life of
        // the app; now it runs when it has been asked to and not otherwise.
        if (FrameCounterEnabled && !_frameRate.IsRunning)
        {
            await StartFrameCounterAsync().ConfigureAwait(true);
        }

        _timer.Start();

        // Gated like the tick is. Sampling unconditionally here meant every page visit paid for a
        // WMI query and a driver call even with every temperature switched off.
        if (_selection.NeedsSensors)
        {
            await SampleSensorsAsync().ConfigureAwait(true);
        }

        Sample();
        RefreshVisibility();
    }

    protected override Task OnLeaveAsync()
    {
        _timer.Stop();

        // Leaving the page stops the counter unless it was asked to keep going. Someone who wants
        // to alt-tab into a game and read the numbers afterwards needs it left on; someone who
        // just looked at the page does not need a trace session running for the rest of the day.
        if (!KeepFrameCounterInBackground && _frameRate.IsRunning)
        {
            _frameRate.Stop();
        }

        return Task.CompletedTask;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // Skipped outright when nothing on screen needs it. A WMI query and a call into the
        // vendor's driver library are not free, and taking them for a tile that is switched off
        // is exactly the kind of background work this app exists to remove.
        if (_tickCount++ % SensorTickInterval == 0 && _selection.NeedsSensors)
        {
            _ = SampleSensorsAsync();
        }

        Sample();
    }

    private void Sample()
    {
        SystemSnapshot snapshot = _systemInfo.GetSnapshot();

        CpuUsage = Math.Round(snapshot.CpuUsagePercent, 1);
        RamUsedMb = snapshot.RamUsedMb;
        RamTotalMb = snapshot.RamTotalMb;
        RamUsagePercent = Math.Round(snapshot.RamUsagePercent, 1);
        ProcessCount = snapshot.ProcessCount;
        StandbyMb = snapshot.StandbyMemoryMb;
        DriveFreeMb = snapshot.SystemDriveFreeMb;
        DriveTotalMb = snapshot.SystemDriveTotalMb;
        Uptime = snapshot.Uptime;

        Append(CpuHistory, snapshot.CpuUsagePercent);
        Append(RamHistory, snapshot.RamUsagePercent);

        // Appended here rather than from the sensor sample, which runs at half this rate. Three
        // graphs side by side have to share one time base or the shapes cannot be compared, and
        // the GPU one would silently be showing twice the history of the two next to it.
        if (GpuUsage is { } load)
        {
            Append(GpuHistory, load);
        }

        SampleFrameRate();

        // The preview shows live numbers, so it moves with them.
        OnPropertyChanged(nameof(Preview));
    }

    private void SampleFrameRate()
    {
        FrameRateReading? reading = _frameRate.Read();

        if (reading is null)
        {
            Fps = null;
            GameName = string.Empty;
            FrameRateHint = _frameRate.IsRunning
                ? _localization["Monitor_Fps_Idle"]
                : _localization.Format("Monitor_Fps_Unavailable", _frameRate.UnavailableReason);

            // The history is not cleared. Someone who just closed a game still wants to see the
            // shape of the last minute they played.
            return;
        }

        Fps = reading.RoundedFps;
        OnePercentLowFps = reading.RoundedOnePercentLowFps;
        FrameTimeMs = Math.Round(reading.FrameTimeMs, 1);
        GameName = reading.ProcessName;
        FrameRateHint = string.Empty;

        Append(FpsHistory, reading.Fps);
        OnPropertyChanged(nameof(FpsScale));
    }

    private async Task SampleSensorsAsync()
    {
        try
        {
            SensorReadings readings = await _sensors.ReadAsync(PageToken).ConfigureAwait(true);

            CpuTemperature = readings.Cpu?.Rounded;
            GpuTemperature = readings.Gpu?.Rounded;
            GpuUsage = readings.GpuUsagePercent;
            GpuFan = readings.GpuFanPercent;

        }
        catch (Exception)
        {
            // A sensor that will not answer is a missing tile, never a broken page.
        }
        finally
        {
            _sensorsChecked = true;
            OnPropertyChanged(nameof(HasNoTemperature));
        }
    }

    private static void Append(ObservableCollection<double> series, double value)
    {
        series.Add(value);

        while (series.Count > HistoryLength)
        {
            series.RemoveAt(0);
        }
    }
}
