using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;
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

/// <summary>
/// The switch that puts the machine into a gaming state, and the list of what it is holding.
///
/// Everything it does is undoable without a reboot, which is what separates it from applying a
/// profile - and the list is how that promise is kept in view rather than in a toast.
/// </summary>
public sealed partial class GameModeCardViewModel : ObservableObject
{
    private readonly IGameModeService _gameMode;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcher _dispatcher;
    private readonly IBusyScope _busy;
    private readonly Func<Task> _afterChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caption))]
    private bool _isOn;

    [ObservableProperty]
    private string _detail = string.Empty;

    /// <summary>How long the session has been on, refreshed on the tick like any other reading.</summary>
    [ObservableProperty]
    private string _elapsed = string.Empty;

    /// <param name="afterChange">
    /// Re-reads the counts the dashboard shows. Game mode stops services and swaps the power
    /// scheme, so the tuning score next to this card is stale the moment the switch moves.
    /// </param>
    public GameModeCardViewModel(
        IGameModeService gameMode,
        IUserInteraction interaction,
        ILocalizationService localization,
        IUiDispatcher dispatcher,
        IBusyScope busy,
        Func<Task> afterChange)
    {
        _gameMode = gameMode;
        _interaction = interaction;
        _localization = localization;
        _dispatcher = dispatcher;
        _busy = busy;
        _afterChange = afterChange;

        _gameMode.Changed += (_, _) => Refresh();
    }

    /// <summary>
    /// What game mode is holding right now, one line per change.
    ///
    /// The switch used to say what it had done in a toast that disappeared after a few seconds,
    /// and nothing afterwards. It stops services, replaces the power scheme and frees gigabytes -
    /// all of it invisible a minute later, which is a poor way to earn trust from something asking
    /// for administrator rights. The list stays on screen for as long as the session does.
    /// </summary>
    public ObservableCollection<GameModeChangeRow> Changes { get; } = [];

    public string Caption => _localization[IsOn ? "GameMode_On" : "GameMode_Off"];

    public async Task LoadAsync()
    {
        await _gameMode.LoadAsync().ConfigureAwait(true);
        Update();
    }

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
    public void Refresh() => _dispatcher.Post(Update);

    /// <summary>
    /// One switch that puts the machine into a gaming state and back. Everything it does is
    /// undoable without a reboot, which is what separates it from applying a profile.
    /// </summary>
    [RelayCommand]
    private async Task ToggleAsync()
    {
        bool turningOn = !_gameMode.IsActive;

        await _busy.RunAsync(
            _localization[turningOn ? "GameMode_Starting" : "GameMode_Stopping"],
            async token =>
            {
                var progress = new Progress<string>(step => BusyStep(step));

                GameModeResult result = turningOn
                    ? await _gameMode.EnableAsync(progress, cancellationToken: token).ConfigureAwait(true)
                    : await _gameMode.DisableAsync(progress, token).ConfigureAwait(true);

                Update();

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

                await _afterChange().ConfigureAwait(true);
            }).ConfigureAwait(true);
    }

    private void BusyStep(string step) => BusyMessageChanged?.Invoke(this, _localization[step switch
    {
        "power" => "GameMode_Step_Power",
        "services" => "GameMode_Step_Services",
        _ => "GameMode_Step_Memory",
    }]);

    /// <summary>Raised with the caption for the step game mode has reached, for the page's spinner.</summary>
    public event EventHandler<string>? BusyMessageChanged;

    private void Update()
    {
        IsOn = _gameMode.IsActive;
        Changes.Clear();

        if (_gameMode.Session is not { } session)
        {
            Detail = _localization["GameMode_Hint"];
            Elapsed = string.Empty;
            return;
        }

        foreach (GameModeEffect effect in GameModeEffects.Describe(session, ServiceCatalog.All))
        {
            Changes.Add(Describe(effect));
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
            Detail = session.TriggeredBy.Length > 0
                ? _localization.Format("GameMode_TriggeredBy", session.TriggeredBy)
                : _localization["GameMode_TriggeredBy_Game"];
        }
        else
        {
            Detail = _localization[session.TriggerKind == GameModeTriggerKind.Schedule
                ? "GameMode_TriggeredBy_Schedule"
                : "GameMode_TriggeredBy_User"];
        }

        UpdateElapsed();
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

    /// <summary>Called on the dashboard's tick, so the elapsed time counts up while the page is open.</summary>
    public void UpdateElapsed()
    {
        if (_gameMode.Session is not { } session)
        {
            Elapsed = string.Empty;
            return;
        }

        // Clamped at zero: the clock, or a session file carried over a time zone change, can
        // otherwise put the start in the future and produce "on for -3 min".
        TimeSpan elapsed = DateTimeOffset.Now - session.StartedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        Elapsed = elapsed.TotalHours >= 1
            ? _localization.Format("GameMode_Elapsed_Hours", (int)elapsed.TotalHours, elapsed.Minutes)
            : _localization.Format("GameMode_Elapsed_Minutes", (int)elapsed.TotalMinutes);
    }
}
