using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// Switching game mode on by itself when a watched game starts, and the list of what to watch for.
/// </summary>
public sealed partial class GameWatchSettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _host;
    private readonly IWatchedGameList _watcher;
    private readonly GameModeAutomation _automation;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoGameModeSummary))]
    private bool _autoGameMode;

    [ObservableProperty]
    private string _newGameName = string.Empty;

    public GameWatchSettingsViewModel(
        ISettingsHost host,
        IWatchedGameList watcher,
        GameModeAutomation automation,
        IUserInteraction interaction,
        ILocalizationService localization)
    {
        _host = host;
        _watcher = watcher;
        _automation = automation;
        _interaction = interaction;
        _localization = localization;
    }

    public ObservableCollection<WatchedGame> WatchedGames { get; } = [];

    public string AutoGameModeSummary => AutoGameMode
        ? _localization.Format("Settings_Summary_GamesWatched", WatchedGames.Count(g => g.Enabled))
        : _localization["Settings_Summary_Off"];

    public void Load()
    {
        AutoGameMode = _host.Settings.AutoGameMode;
        RefreshWatchedGames();
    }

    partial void OnAutoGameModeChanged(bool value)
    {
        // Applied straight away rather than on next launch, so the effect of the switch is
        // something the user can see before they close the page.
        _automation.IsEnabled = value;

        if (_host.IsLoading)
        {
            return;
        }

        _host.Settings.AutoGameMode = value;
        _host.Save();
    }

    [RelayCommand]
    private async Task AddGameAsync()
    {
        OperationResult result = await _watcher
            .AddAsync(NewGameName, NewGameName)
            .ConfigureAwait(true);

        if (!result.Success)
        {
            _interaction.ShowError(result.Describe(_localization));
            return;
        }

        if (!result.Changed)
        {
            _interaction.ShowInfo(result.Describe(_localization));
            return;
        }

        NewGameName = string.Empty;
        RefreshWatchedGames();
    }

    [RelayCommand]
    private async Task RemoveGameAsync(WatchedGame? game)
    {
        if (game is null)
        {
            return;
        }

        await _watcher.RemoveAsync(game.ProcessName).ConfigureAwait(true);
        RefreshWatchedGames();
    }

    [RelayCommand]
    private async Task ToggleGameAsync(WatchedGame? game)
    {
        if (game is null)
        {
            return;
        }

        await _watcher.SetEnabledAsync(game.ProcessName, !game.Enabled).ConfigureAwait(true);
        RefreshWatchedGames();
    }

    private void RefreshWatchedGames()
    {
        WatchedGames.Clear();
        foreach (WatchedGame game in _watcher.Games.OrderBy(g => g.DisplayName, StringComparer.CurrentCulture))
        {
            WatchedGames.Add(game);
        }

        OnPropertyChanged(nameof(AutoGameModeSummary));
    }

    /// <summary>Re-reads the words after a language change; the values themselves do not move.</summary>
    public void RefreshText() => OnPropertyChanged(nameof(AutoGameModeSummary));
}
