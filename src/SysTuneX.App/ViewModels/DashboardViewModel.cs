using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using Wpf.Ui;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// The front page: what this machine is, what it is doing, how tuned it is, and the four actions
/// that are safe enough to offer without opening a page first.
///
/// The cards it is made of are their own view models. This class owns the tick that drives them,
/// the counts behind the score, and the actions - and nothing else. It used to own all of it:
/// fourteen dependencies and eight jobs, where the hardware read once, the counters every second
/// and the game mode card from whichever thread the news arrived on, all in the same file.
/// </summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly IServiceManager _services;
    private readonly ITweakEngine _tweaks;
    private readonly IPowerSchemeService _power;
    private readonly IQuickOptimizer _quickOptimize;
    private readonly IMemoryTrimmer _memory;
    private readonly IProfileService _profiles;
    private readonly IChangeJournalReader _backup;
    private readonly IEnvironmentService _environment;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;
    private readonly INavigationService _navigation;
    private readonly DispatcherTimer _timer;

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
    private string _powerPlan = string.Empty;

    public DashboardViewModel(
        ISystemInfoService systemInfo,
        ITweakEngine tweaks,
        IServiceManager services,
        IQuickOptimizer quickOptimize,
        IMemoryTrimmer memory,
        IPowerSchemeService power,
        IProfileService profiles,
        IChangeJournalReader backup,
        IUiDispatcher dispatcher,
        IEnvironmentService environment,
        IUserInteraction interaction,
        ILocalizationService localization,
        ISensorService sensors,
        IGameModeService gameMode,
        INavigationService navigation)
    {
        _tweaks = tweaks;
        _services = services;
        _quickOptimize = quickOptimize;
        _memory = memory;
        _power = power;
        _profiles = profiles;
        _backup = backup;
        _environment = environment;
        _interaction = interaction;
        _localization = localization;
        _navigation = navigation;

        Hardware = new HardwareCardViewModel(systemInfo);
        Counters = new LiveCountersViewModel(systemInfo, sensors);
        GameMode = new GameModeCardViewModel(
            gameMode, interaction, localization, dispatcher, this, RefreshCountersAsync);

        GameMode.BusyMessageChanged += (_, message) => BusyMessage = message;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;

        localization.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ScoreCaption));

            // The change list is built from resources once, so it would otherwise stay in the
            // language it was built in until game mode was switched off and on again.
            GameMode.Refresh();
        };
    }

    public HardwareCardViewModel Hardware { get; }

    public LiveCountersViewModel Counters { get; }

    public GameModeCardViewModel GameMode { get; }

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
            await Hardware.LoadAsync(PageToken).ConfigureAwait(true);
            await GameMode.LoadAsync().ConfigureAwait(true);
        }

        await Counters.SampleSensorsAsync().ConfigureAwait(true);
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

                QuickOptimizeResult result = await _quickOptimize.RunAsync(progress, token).ConfigureAwait(true);

                await RefreshCountersAsync().ConfigureAwait(true);

                string summary = _localization.Format(
                    "Msg_BatchDone",
                    result.Tweaks.Succeeded,
                    result.Tweaks.Failed,
                    result.Tweaks.Skipped);

                string freed = Converters.BytesToSizeConverter.Format(result.Memory.FreedBytes);

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
                MemoryTrimResult result = await _memory.TrimMemoryAsync(token).ConfigureAwait(true);
                string freed = Converters.BytesToSizeConverter.Format(result.FreedBytes);

                _interaction.ShowSuccess(
                    result.FreedBytes > 0
                        ? _localization.Format("Msg_MemoryFreed", freed)
                        : _localization.Format("Msg_MemoryTrimmed", result.TrimmedProcesses));
            }).ConfigureAwait(true);
    }

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

                        // Asked of the service rather than of the scheme: a high-performance scheme
                        // SysTuneX duplicated itself carries a fresh GUID and the source scheme's
                        // name, which is not the English string on a Russian or Ukrainian Windows.
                        bool isHighPerformance = await _power
                            .IsHighPerformanceActiveAsync(PageToken)
                            .ConfigureAwait(false);

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

    private void OnTick(object? sender, EventArgs e)
    {
        Counters.Tick();

        if (GameMode.IsOn)
        {
            GameMode.UpdateElapsed();
        }
    }
}
