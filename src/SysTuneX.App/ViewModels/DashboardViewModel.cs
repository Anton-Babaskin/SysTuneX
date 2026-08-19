using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.App.ViewModels;

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
    private readonly DispatcherTimer _timer;

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
        ILocalizationService localization)
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

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;

        localization.LanguageChanged += (_, _) => OnPropertyChanged(nameof(ScoreCaption));
    }

    public ObservableCollection<double> CpuHistory { get; } = [];

    public ObservableCollection<double> RamHistory { get; } = [];

    public bool IsElevated => _environment.IsElevated;

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
        }

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
                            services.Count(s => s.State.IsDisabled || !s.State.IsRunning),
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

            // 55 / 25 / 20 weighting: tweaks are the bulk of what SysTuneX does, services matter
            // less than the marketing usually claims, and the power scheme is one binary choice.
            double tweakScore = total > 0 ? (double)applied / total * 55 : 0;
            double serviceScore = totalServices > 0 ? (double)disabledServices / totalServices * 25 : 0;
            double powerScore = highPerformance ? 20 : 0;

            Score = Math.Round(tweakScore + serviceScore + powerScore);
        }
        catch (OperationCanceledException)
        {
            // Navigated away mid-refresh.
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
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
