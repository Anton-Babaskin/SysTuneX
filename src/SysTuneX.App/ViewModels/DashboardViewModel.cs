using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// One line of "here is what game mode is doing to your machine right now".
///
/// The icon is chosen here rather than in a template selector because the choice is a property of
/// the kind of change, and three kinds do not justify three templates.
/// </summary>
/// <param name="Icon">Glyph for the kind of change.</param>
/// <param name="Text">What is changed, with the name Windows uses for it.</param>
/// <param name="Restores">What happens to it when game mode is switched off.</param>
public sealed record GameModeChangeRow(SymbolRegular Icon, string Text, string Restores);

public sealed partial class DashboardViewModel : PageViewModel
{
    private const int HistoryLength = 60;

    private readonly ISystemInfoService _systemInfo;
    private readonly ITweakEngine _tweaks;
    private readonly IServiceManager _services;
    private readonly IProcessService _processes;
    private readonly IPowerService _power;
    private readonly IProfileService _profiles;
    private readonly IBackupService _backup;
    private readonly IEnvironmentService _environment;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;
    private readonly ISensorService _sensors;
    private readonly IGameModeService _gameMode;
    private readonly INavigationService _navigation;
    private readonly DispatcherTimer _timer;

    /// <summary>Sensors are read every few ticks; WMI and NVML are far heavier than a counter read.</summary>
    private int _tickCount;

    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    private long _ramUsedMb;

    [ObservableProperty]
    private long _ramTotalMb;

    [ObservableProperty]
    private double _ramUsagePercent;

    [ObservableProperty]
    private long _standbyMb;

    [ObservableProperty]
    private int _processCount;

    [ObservableProperty]
    private long _driveFreeMb;

    [ObservableProperty]
    private long _driveTotalMb;

    [ObservableProperty]
    private TimeSpan _uptime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreCaption))]
    private double _score;

    [ObservableProperty]
    private int _appliedTweaks;

    [ObservableProperty]
    private int _totalTweaks;

    [ObservableProperty]
    private int _disabledServices;

    [ObservableProperty]
    private int _totalServices;

    [ObservableProperty]
    private string _cpuName = string.Empty;

    [ObservableProperty]
    private string _gpuName = string.Empty;

    [ObservableProperty]
    private string _gpuDetail = string.Empty;

    [ObservableProperty]
    private string _memorySummary = string.Empty;

    [ObservableProperty]
    private string _windowsSummary = string.Empty;

    [ObservableProperty]
    private string _motherboard = string.Empty;

    [ObservableProperty]
    private string _driveModel = string.Empty;

    [ObservableProperty]
    private string _powerPlan = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCpuTemperature))]
    private int? _cpuTemperature;

    [ObservableProperty]
    private string _cpuTemperatureSource = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGpuTemperature))]
    private int? _gpuTemperature;

    [ObservableProperty]
    private string _gpuTemperatureSource = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGpuUsage))]
    private int? _gpuUsage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnySensor))]
    private bool _sensorsChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameModeCaption))]
    private bool _isGameModeOn;

    [ObservableProperty]
    private string _gameModeDetail = string.Empty;

    /// <summary>How long the session has been on, refreshed on the tick like any other reading.</summary>
    [ObservableProperty]
    private string _gameModeElapsed = string.Empty;

    public DashboardViewModel(
        ISystemInfoService systemInfo,
        ITweakEngine tweaks,
        IServiceManager services,
        IProcessService processes,
        IPowerService power,
        IProfileService profiles,
        IBackupService backup,
        IEnvironmentService environment,
        IUserInteraction interaction,
        ILocalizationService localization,
        ISensorService sensors,
        IGameModeService gameMode,
        INavigationService navigation)
    {
        _systemInfo = systemInfo;
        _tweaks = tweaks;
        _services = services;
        _processes = processes;
        _power = power;
        _profiles = profiles;
        _backup = backup;
        _environment = environment;
        _interaction = interaction;
        _localization = localization;
        _sensors = sensors;
        _gameMode = gameMode;
        _navigation = navigation;

        _gameMode.Changed += OnGameModeChanged;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;

        localization.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ScoreCaption));

            // The change list is built from resources once, so it would otherwise stay in the
            // language it was built in until game mode was switched off and on again.
            RefreshGameMode();
        };
    }

    public ObservableCollection<double> CpuHistory { get; } = [];

    public ObservableCollection<double> RamHistory { get; } = [];

    /// <summary>
    /// What game mode is holding right now, one line per change.
    ///
    /// The switch used to say what it had done in a toast that disappeared after a few seconds,
    /// and nothing afterwards. It stops services, replaces the power scheme and frees gigabytes -
    /// all of it invisible a minute later, which is a poor way to earn trust from something asking
    /// for administrator rights. The list stays on screen for as long as the session does.
    /// </summary>
    public ObservableCollection<GameModeChangeRow> GameModeChanges { get; } = [];

    public bool IsElevated => _environment.IsElevated;

    public bool HasCpuTemperature => CpuTemperature is not null;

    public bool HasGpuTemperature => GpuTemperature is not null;

    public bool HasGpuUsage => GpuUsage is not null;

    /// <summary>
    /// False once a sample has come back with nothing. The card then explains why rather than
    /// showing an empty box - or worse, a zero that reads like a real temperature.
    /// </summary>
    public bool HasAnySensor => !SensorsChecked || HasCpuTemperature || HasGpuTemperature;

    public string GameModeCaption =>
        _localization[IsGameModeOn ? "GameMode_On" : "GameMode_Off"];

    public string ScoreCaption => Score switch
    {
        < 25 => _localization["Dashboard_Score_Stock"],
        < 70 => _localization["Dashboard_Score_Partial"],
        _ => _localization["Dashboard_Score_Tuned"],
    };

    protected override async Task OnEnterAsync()
    {
        _timer.Start();

        if (!IsInitialized)
        {
            await LoadHardwareAsync().ConfigureAwait(true);
            await _gameMode.LoadAsync().ConfigureAwait(true);
            UpdateGameMode();
        }

        await SampleSensorsAsync().ConfigureAwait(true);
        await RefreshCountersAsync().ConfigureAwait(true);
    }

    protected override Task OnLeaveAsync()
    {
        // The old build left this timer running for every page instance it ever created.
        _timer.Stop();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshCountersAsync();

    /// <summary>The temperatures here are a glance; the Monitor page is where they mean something.</summary>
    [RelayCommand]
    private void OpenMonitor() => _navigation.Navigate(typeof(Views.Pages.MonitorPage));

    [RelayCommand]
    private async Task QuickOptimizeAsync()
    {
        await RunBusyAsync(
            _localization["Common_Working"],
            async token =>
            {
                var progress = new Progress<BatchProgress>(p =>
                {
                    BusyMessage = p.CurrentItem;
                    Progress = p.Total == 0 ? -1 : p.Completed * 100.0 / p.Total;
                });

                // Safe tweaks only. Anything moderate or advanced stays behind an explicit choice
                // on its own page, so a single click can never disable VBS or break printing.
                List<TweakDefinition> safe = _tweaks.GetSupportedTweaks()
                    .Where(t => t.Risk == RiskLevel.Safe)
                    .Where(t => _tweaks.GetStatus(t) != TweakStatus.Applied)
                    .ToList();

                BatchResult result = await _tweaks.ApplyManyAsync(safe, progress, token).ConfigureAwait(true);

                BusyMessage = _localization["Dashboard_TrimMemory"];
                await _power.ActivateHighPerformanceAsync(token).ConfigureAwait(true);
                MemoryTrimResult trim = await _processes.TrimMemoryAsync(token).ConfigureAwait(true);

                await RefreshCountersAsync().ConfigureAwait(true);

                string summary = _localization.Format("Msg_BatchDone", result.Succeeded, result.Failed, result.Skipped);
                string freed = Converters.BytesToSizeConverter.Format(trim.FreedBytes);

                _interaction.ShowSuccess($"{summary} · {_localization.Format("Msg_MemoryFreed", freed)}");
            }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestoreAllAsync()
    {
        if (_backup.GetActive().Count == 0)
        {
            _interaction.ShowInfo(_localization["History_Empty"]);
            return;
        }

        bool confirmed = await _interaction
            .ConfirmAsync(
                _localization["Dialog_RestoreAll_Title"],
                _localization["Dialog_RestoreAll_Message"],
                _localization["Common_RevertAll"],
                PageToken)
            .ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(
            _localization["Common_Working"],
            async token =>
            {
                var progress = new Progress<BatchProgress>(p =>
                {
                    BusyMessage = p.CurrentItem;
                    Progress = p.Total == 0 ? -1 : p.Completed * 100.0 / p.Total;
                });

                ProfileApplyResult result = await _profiles.RestoreEverythingAsync(progress, token).ConfigureAwait(true);
                await RefreshCountersAsync().ConfigureAwait(true);

                int reverted = result.Tweaks.Succeeded + result.ServicesChanged;
                _interaction.ShowSuccess(_localization.Format("Msg_RestoreDone", reverted));
            }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task TrimMemoryAsync()
    {
        await RunBusyAsync(
            _localization["Dashboard_TrimMemory"],
            async token =>
            {
                MemoryTrimResult result = await _processes.TrimMemoryAsync(token).ConfigureAwait(true);
                string freed = Converters.BytesToSizeConverter.Format(result.FreedBytes);

                _interaction.ShowSuccess(
                    result.FreedBytes > 0
                        ? _localization.Format("Msg_MemoryFreed", freed)
                        : _localization.Format("Msg_MemoryTrimmed", result.TrimmedProcesses));
            }).ConfigureAwait(true);
    }

    private async Task LoadHardwareAsync()
    {
        HardwareInfo hardware = await _systemInfo.GetHardwareInfoAsync(PageToken).ConfigureAwait(true);

        CpuName = hardware.CpuName;
        GpuName = hardware.GpuName;
        GpuDetail = hardware.GpuVramMb > 0
            ? $"{hardware.GpuVramMb / 1024.0:0.#} GB · {hardware.GpuDriverVersion}"
            : hardware.GpuDriverVersion;
        MemorySummary = hardware.RamSummary;
        WindowsSummary = hardware.Windows.ToString();
        Motherboard = hardware.MotherboardName;
        DriveModel = hardware.SystemDriveModel;

        CpuCores = hardware.CpuCores;
        CpuThreads = hardware.CpuThreads;
        OnPropertyChanged(nameof(CpuTopology));
    }

    public int CpuCores { get; private set; }

    public int CpuThreads { get; private set; }

    public string CpuTopology => CpuCores > 0 ? $"{CpuCores}C / {CpuThreads}T" : string.Empty;

    /// <summary>
    /// Reads the counters that need a WMI or process-control round trip. Deliberately separate
    /// from the one-second tick, which only touches cheap native calls.
    /// </summary>
    private async Task RefreshCountersAsync()
    {
        try
        {
            (int applied, int total, int disabledServices, int totalServices, string powerPlan, bool highPerformance) =
                await Task.Run(
                    async () =>
                    {
                        IReadOnlyList<TweakDefinition> tweaks = _tweaks.GetSupportedTweaks();
                        int appliedCount = tweaks.Count(t => _tweaks.GetStatus(t) == TweakStatus.Applied);

                        IReadOnlyList<(ServiceDefinition Definition, ServiceSnapshot State)> services =
                            await _services.GetManagedServicesAsync(PageToken).ConfigureAwait(false);

                        PowerScheme? scheme = await _power.GetActiveSchemeAsync(PageToken).ConfigureAwait(false);

                        bool isHighPerformance = scheme is not null &&
                            (scheme.Guid == PowerScheme.UltimatePerformance ||
                             scheme.Guid == PowerScheme.HighPerformance ||
                             scheme.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase) ||
                             scheme.Name.Contains("High performance", StringComparison.OrdinalIgnoreCase));

                        return (
                            appliedCount,
                            tweaks.Count,
                            // A service counts as tuned when its start type already matches what
                            // SysTuneX would set it to - the same test the profile preview uses.
                            // Counting anything merely not running instead meant every on-demand
                            // service idling on a stock machine scored as tuned, which handed an
                            // untouched install almost the whole service weighting.
                            services.Count(s => s.State.StartMode == s.Definition.DisabledStartMode),
                            services.Count,
                            scheme?.Name ?? string.Empty,
                            isHighPerformance);
                    },
                    PageToken).ConfigureAwait(true);

            AppliedTweaks = applied;
            TotalTweaks = total;
            DisabledServices = disabledServices;
            TotalServices = totalServices;
            PowerPlan = powerPlan;

            Score = TuningScore.Calculate(applied, total, disabledServices, totalServices, highPerformance);
        }
        catch (OperationCanceledException)
        {
            // Navigated away mid-refresh.
        }
    }

    /// <summary>
    /// One switch that puts the machine into a gaming state and back. Everything it does is
    /// undoable without a reboot, which is what separates it from applying a profile.
    /// </summary>
    [RelayCommand]
    private async Task ToggleGameModeAsync()
    {
        bool turningOn = !_gameMode.IsActive;

        await RunBusyAsync(
            _localization[turningOn ? "GameMode_Starting" : "GameMode_Stopping"],
            async token =>
            {
                var progress = new Progress<string>(step => BusyMessage = _localization[step switch
                {
                    "power" => "GameMode_Step_Power",
                    "services" => "GameMode_Step_Services",
                    _ => "GameMode_Step_Memory",
                }]);

                GameModeResult result = turningOn
                    ? await _gameMode.EnableAsync(progress, cancellationToken: token).ConfigureAwait(true)
                    : await _gameMode.DisableAsync(progress, token).ConfigureAwait(true);

                UpdateGameMode();

                if (!result.Result.Success)
                {
                    _interaction.ShowError(result.Result.Describe(_localization));
                    return;
                }

                _interaction.ShowSuccess(string.Format(
                    _localization[turningOn ? "GameMode_Enabled" : "GameMode_Disabled"],
                    result.ServicesAffected,
                    result.FreedMemoryMb));

                // Anything that refused is named rather than swallowed - a service that would
                // not stop is exactly the sort of thing worth knowing before blaming the game.
                if (result.Notes.Count > 0)
                {
                    _interaction.ShowWarning(string.Join(Environment.NewLine, result.Notes.Take(4)));
                }

                await RefreshCountersAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
    }

    private void OnGameModeChanged(object? sender, EventArgs e) => RefreshGameMode();

    /// <summary>
    /// Rebuilds the card, from whichever thread the news arrived on.
    ///
    /// The event always arrives on a thread-pool thread. <c>GameModeService</c> raises it after
    /// <c>await _gate.WaitAsync().ConfigureAwait(false)</c>, so even pressing the switch by hand
    /// resumes off the UI thread - and the schedule and the game watcher tick on their own timers
    /// to begin with.
    ///
    /// That was harmless while this only assigned scalar properties, which the binding engine
    /// marshals by itself. It stopped being harmless when the card gained a bound collection:
    /// changing one off the UI thread throws NotSupportedException out of the collection view.
    /// Worse, the throw lands in the service's own catch, so game mode would come on for real
    /// while the user was shown an error saying it had not. The tray icon already guards the same
    /// event the same way.
    /// </summary>
    internal void RefreshGameMode()
    {
        Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            UpdateGameMode();
            return;
        }

        // Posted rather than blocking: this is a redraw, nothing waits on it, and Invoke from a
        // thread the UI may be waiting on is a deadlock waiting to be discovered.
        dispatcher.BeginInvoke(UpdateGameMode);
    }

    private void UpdateGameMode()
    {
        IsGameModeOn = _gameMode.IsActive;
        GameModeChanges.Clear();

        if (_gameMode.Session is not { } session)
        {
            GameModeDetail = _localization["GameMode_Hint"];
            GameModeElapsed = string.Empty;
            return;
        }

        foreach (GameModeEffect effect in GameModeEffects.Describe(session, ServiceCatalog.All))
        {
            GameModeChanges.Add(Describe(effect));
        }

        // Worth naming: a switch that moved on its own is confusing unless it says what moved it.
        //
        // AutoStarted is consulted as well as the kind because a session file written before
        // TriggerKind existed carries the default, User - and reading back a game's session as
        // "switched on by hand" would be worse than the toast this replaces.
        bool byGame = session.TriggerKind == GameModeTriggerKind.Game ||
                      (session.AutoStarted && session.TriggerKind == GameModeTriggerKind.User);

        if (byGame)
        {
            GameModeDetail = session.TriggeredBy.Length > 0
                ? _localization.Format("GameMode_TriggeredBy", session.TriggeredBy)
                : _localization["GameMode_TriggeredBy_Game"];
        }
        else
        {
            GameModeDetail = _localization[session.TriggerKind == GameModeTriggerKind.Schedule
                ? "GameMode_TriggeredBy_Schedule"
                : "GameMode_TriggeredBy_User"];
        }

        UpdateGameModeElapsed();
    }

    /// <summary>
    /// Puts a resource string around the names Core recorded.
    ///
    /// The names themselves stay as Windows wrote them - translating "Connected User Experiences
    /// and Telemetry" into Russian would leave the user searching services.msc for a service that
    /// is not called that.
    /// </summary>
    private GameModeChangeRow Describe(GameModeEffect effect) => effect.Kind switch
    {
        GameModeEffectKind.PowerScheme => new GameModeChangeRow(
            SymbolRegular.BatteryCharge20,
            effect.Subject.Length > 0
                ? _localization.Format("GameMode_Effect_Power", effect.Subject)
                : _localization["GameMode_Effect_Power_Unnamed"],
            effect.Previous.Length > 0
                ? _localization.Format("GameMode_Effect_Power_Restores", effect.Previous)
                : string.Empty),

        GameModeEffectKind.Memory => new GameModeChangeRow(
            SymbolRegular.Ram20,
            _localization.Format("GameMode_Effect_Memory", effect.AmountMb),
            _localization["GameMode_Effect_Memory_Note"]),

        _ => new GameModeChangeRow(
            SymbolRegular.Server20,
            effect.Subject,
            _localization["GameMode_Effect_Service_Restores"]),
    };

    private void UpdateGameModeElapsed()
    {
        if (_gameMode.Session is not { } session)
        {
            GameModeElapsed = string.Empty;
            return;
        }

        // Clamped at zero: the clock, or a session file carried over a time zone change, can
        // otherwise put the start in the future and produce "on for -3 min".
        TimeSpan elapsed = DateTimeOffset.Now - session.StartedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        GameModeElapsed = elapsed.TotalHours >= 1
            ? _localization.Format("GameMode_Elapsed_Hours", (int)elapsed.TotalHours, elapsed.Minutes)
            : _localization.Format("GameMode_Elapsed_Minutes", (int)elapsed.TotalMinutes);
    }

    private async Task SampleSensorsAsync()
    {
        try
        {
            SensorReadings readings = await _sensors.ReadAsync().ConfigureAwait(true);

            CpuTemperature = readings.Cpu?.Rounded;
            CpuTemperatureSource = readings.Cpu?.Source ?? string.Empty;
            GpuTemperature = readings.Gpu?.Rounded;
            GpuTemperatureSource = readings.Gpu?.Source ?? string.Empty;
            GpuUsage = readings.GpuUsagePercent;
            SensorsChecked = true;
        }
        catch (Exception)
        {
            // A sensor that will not answer is a missing tile, never a broken dashboard.
            SensorsChecked = true;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // WMI and NVML cost orders of magnitude more than a counter read, and a temperature
        // does not move meaningfully inside a second, so they get their own slower cadence.
        if (_tickCount++ % 5 == 0)
        {
            _ = SampleSensorsAsync();
        }

        if (IsGameModeOn)
        {
            UpdateGameModeElapsed();
        }

        SystemSnapshot snapshot = _systemInfo.GetSnapshot();

        CpuUsage = Math.Round(snapshot.CpuUsagePercent, 1);
        RamUsedMb = snapshot.RamUsedMb;
        RamTotalMb = snapshot.RamTotalMb;
        RamUsagePercent = Math.Round(snapshot.RamUsagePercent, 1);
        StandbyMb = snapshot.StandbyMemoryMb;
        ProcessCount = snapshot.ProcessCount;
        DriveFreeMb = snapshot.SystemDriveFreeMb;
        DriveTotalMb = snapshot.SystemDriveTotalMb;
        Uptime = snapshot.Uptime;

        Append(CpuHistory, snapshot.CpuUsagePercent);
        Append(RamHistory, snapshot.RamUsagePercent);
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
