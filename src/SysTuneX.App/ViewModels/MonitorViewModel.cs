using System.Collections.ObjectModel;
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

    // ── What the monitor measures ────────────────────────────────────────────
    //
    // Each of these is a real cost rather than a preference about clutter, so each is a switch:
    // the frame counter holds an ETW session open, and the temperatures cost a WMI query or a call
    // into the vendor's driver library on every sample.

    [ObservableProperty]
    private bool _frameCounterEnabled;

    [ObservableProperty]
    private bool _keepFrameCounterInBackground = true;

    [ObservableProperty]
    private bool _showCpuLoad = true;

    [ObservableProperty]
    private bool _showCpuTemperature = true;

    [ObservableProperty]
    private bool _showGpuLoad = true;

    [ObservableProperty]
    private bool _showGpuTemperature = true;

    [ObservableProperty]
    private bool _showGpuFan;

    [ObservableProperty]
    private bool _showMemory = true;

    [ObservableProperty]
    private bool _showProcessCount;

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

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

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

    /// <summary>
    /// False only once a sample has actually come back empty. Before that the tiles say nothing
    /// rather than announcing an absence that has not been established yet.
    /// </summary>
    public bool HasNoTemperature =>
        _sensorsChecked &&
        (ShowCpuTemperature || ShowGpuTemperature) &&
        CpuTemperature is null &&
        GpuTemperature is null;

    /// <summary>The GPU card is worth drawing only if at least one of its readings is wanted.</summary>
    public bool ShowGpuCard => ShowGpuLoad || ShowGpuTemperature || ShowGpuFan;

    /// <summary>Nothing ticked at all, which is a state worth naming rather than showing a blank page.</summary>
    public bool ShowNothing => !FrameCounterEnabled && !ShowCpuLoad && !ShowGpuCard && !ShowMemory;

    private bool _loading;

    private void LoadSettings()
    {
        MonitorSettings monitor = _settings.Current.Monitor;

        _loading = true;
        try
        {
            FrameCounterEnabled = monitor.FrameCounter;
            KeepFrameCounterInBackground = monitor.KeepFrameCounterInBackground;
            ShowCpuLoad = monitor.ShowCpuLoad;
            ShowCpuTemperature = monitor.ShowCpuTemperature;
            ShowGpuLoad = monitor.ShowGpuLoad;
            ShowGpuTemperature = monitor.ShowGpuTemperature;
            ShowGpuFan = monitor.ShowGpuFan;
            ShowMemory = monitor.ShowMemory;
            ShowProcessCount = monitor.ShowProcessCount;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Writes the switches back. Called from every one of them, so it stays cheap and idempotent.</summary>
    private void SaveSettings()
    {
        if (_loading)
        {
            return;
        }

        MonitorSettings monitor = _settings.Current.Monitor;

        monitor.FrameCounter = FrameCounterEnabled;
        monitor.KeepFrameCounterInBackground = KeepFrameCounterInBackground;
        monitor.ShowCpuLoad = ShowCpuLoad;
        monitor.ShowCpuTemperature = ShowCpuTemperature;
        monitor.ShowGpuLoad = ShowGpuLoad;
        monitor.ShowGpuTemperature = ShowGpuTemperature;
        monitor.ShowGpuFan = ShowGpuFan;
        monitor.ShowMemory = ShowMemory;
        monitor.ShowProcessCount = ShowProcessCount;

        _ = _settings.SaveAsync();
    }

    /// <summary>
    /// The switch that owns the trace session. Creating one takes long enough to be felt, so it
    /// happens off the interface thread with the switch showing that it is working.
    /// </summary>
    async partial void OnFrameCounterEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNothing));
        SaveSettings();

        if (_loading)
        {
            return;
        }

        if (value)
        {
            await StartFrameCounterAsync().ConfigureAwait(true);
            return;
        }

        _frameRate.Stop();
        Fps = null;
        GameName = string.Empty;
        FpsHistory.Clear();
        FrameRateHint = _localization["Monitor_Fps_Off"];
    }

    private async Task StartFrameCounterAsync()
    {
        FrameCounterStarting = true;
        try
        {
            await Task.Run(_frameRate.Start, PageToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Navigated away while the session was being created.
        }
        finally
        {
            FrameCounterStarting = false;
        }

        // A refusal is reported on the switch itself rather than only in the empty tile, because
        // the person who just flipped it is owed an answer about why nothing happened.
        if (!_frameRate.IsRunning)
        {
            FrameRateHint = _localization.Format("Monitor_Fps_Unavailable", _frameRate.UnavailableReason);
        }
    }

    partial void OnKeepFrameCounterInBackgroundChanged(bool value) => SaveSettings();

    partial void OnShowCpuLoadChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNothing));
        SaveSettings();
    }

    partial void OnShowCpuTemperatureChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoTemperature));
        SaveSettings();
    }

    partial void OnShowGpuLoadChanged(bool value) => OnGpuSwitchChanged();

    partial void OnShowGpuTemperatureChanged(bool value) => OnGpuSwitchChanged();

    partial void OnShowGpuFanChanged(bool value) => OnGpuSwitchChanged();

    private void OnGpuSwitchChanged()
    {
        OnPropertyChanged(nameof(ShowGpuCard));
        OnPropertyChanged(nameof(HasNoTemperature));
        OnPropertyChanged(nameof(ShowNothing));
        SaveSettings();
    }

    partial void OnShowMemoryChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNothing));
        SaveSettings();
    }

    partial void OnShowProcessCountChanged(bool value) => SaveSettings();

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

        await SampleSensorsAsync().ConfigureAwait(true);
        Sample();
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
        if (_tickCount++ % SensorTickInterval == 0 && _settings.Current.Monitor.NeedsSensors)
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
