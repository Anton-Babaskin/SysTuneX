using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// Shared behaviour for the four tweak pages: grouping, search, risk filtering, and the
/// apply/revert commands. Only the category and the page copy differ between them.
/// </summary>
public abstract partial class TweakPageViewModel : PageViewModel
{
    private readonly ITweakEngine _tweaks;
    private readonly IEnvironmentService _environment;
    private readonly IAppSettingsService _settings;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private RiskLevel? _riskFilter;

    [ObservableProperty]
    private bool? _appliedFilter;

    [ObservableProperty]
    private int _appliedCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _visibleCount;

    [ObservableProperty]
    private bool _restartRequired;

    [ObservableProperty]
    private bool _explorerRestartSuggested;

    protected TweakPageViewModel(
        ITweakEngine tweaks,
        IEnvironmentService environment,
        IUserInteraction interaction,
        ILocalizationService localization,
        CatalogText text,
        IAppSettingsService settings)
    {
        _tweaks = tweaks;
        _environment = environment;
        _settings = settings;
        Interaction = interaction;
        Localization = localization;
        Text = text;

        Localization.LanguageChanged += (_, _) => RefreshText();
    }

    public ObservableCollection<TweakGroupViewModel> Groups { get; } = [];

    protected IUserInteraction Interaction { get; }

    protected ILocalizationService Localization { get; }

    protected CatalogText Text { get; }

    protected abstract TweakCategory Category { get; }

    public string CountSummary => Localization.Format("Tweaks_AppliedCount", AppliedCount, TotalCount);

    protected override async Task OnEnterAsync()
    {
        if (!IsInitialized)
        {
            await ReloadAsync().ConfigureAwait(true);
        }
        else
        {
            // Coming back to a cached page: refresh status without rebuilding the list, so
            // changes made on another page (or outside the app) show up.
            await RefreshStatusAsync().ConfigureAwait(true);
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnRiskFilterChanged(RiskLevel? value) => ApplyFilters();

    partial void OnAppliedFilterChanged(bool? value) => ApplyFilters();

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();

    [RelayCommand]
    private void SetRiskFilter(string? risk) =>
        RiskFilter = Enum.TryParse(risk, out RiskLevel parsed) && RiskFilter != parsed ? parsed : null;

    [RelayCommand]
    private void SetAppliedFilter(string? state) => AppliedFilter = state switch
    {
        "applied" when AppliedFilter != true => true,
        "not-applied" when AppliedFilter != false => false,
        _ => null,
    };

    [RelayCommand]
    private async Task ToggleAsync(TweakItemViewModel? item)
    {
        if (item is null || item.IsBusy)
        {
            return;
        }

        bool applying = !item.IsApplied;

        if (applying && item.Definition.RequiresConfirmation && _settings.Current.ConfirmAdvancedChanges)
        {
            bool confirmed = await Interaction
                .ConfirmAdvancedAsync(item.Name, item.Description, Text.Warning(item.Definition), PageToken)
                .ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }
        }

        item.IsBusy = true;
        item.LastError = null;

        try
        {
            OperationResult result = applying
                ? await _tweaks.ApplyAsync(item.Definition, PageToken).ConfigureAwait(true)
                : await _tweaks.RevertAsync(item.Definition, PageToken).ConfigureAwait(true);

            item.Status = await Task.Run(() => _tweaks.GetStatus(item.Definition), PageToken).ConfigureAwait(true);

            if (result.Success)
            {
                Interaction.ShowSuccess(Localization.Format(applying ? "Msg_Applied" : "Msg_Reverted", item.Name));

                if (applying && item.RequiresRestart)
                {
                    RestartRequired = true;
                }

                if (applying && item.RequiresExplorerRestart)
                {
                    ExplorerRestartSuggested = true;
                }
            }
            else
            {
                item.LastError = result.Detail(Localization);
                Interaction.ShowError(result.Describe(Localization), item.Name);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            item.LastError = ex.Message;
            Interaction.ShowError(ex.Message, item.Name);
        }
        finally
        {
            item.IsBusy = false;
            UpdateCounts();
        }
    }

    [RelayCommand]
    private Task ApplySafeAsync() => ApplyManyAsync(t => t.Definition.Risk == RiskLevel.Safe, apply: true);

    [RelayCommand]
    private async Task ApplyAllAsync()
    {
        List<TweakItemViewModel> advanced = AllItems()
            .Where(i => i.Definition.Risk == RiskLevel.Advanced && !i.IsApplied)
            .ToList();

        if (advanced.Count > 0 && _settings.Current.ConfirmAdvancedChanges)
        {
            string names = string.Join(", ", advanced.Select(i => i.Name));
            bool confirmed = await Interaction
                .ConfirmAdvancedAsync(Localization["Dialog_Advanced_Title"], names, string.Empty, PageToken)
                .ConfigureAwait(true);

            if (!confirmed)
            {
                // Still apply everything below the advanced line rather than doing nothing.
                await ApplyManyAsync(t => t.Definition.Risk != RiskLevel.Advanced, apply: true).ConfigureAwait(true);
                return;
            }
        }

        await ApplyManyAsync(_ => true, apply: true).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task RevertAllAsync() => ApplyManyAsync(_ => true, apply: false);

    [RelayCommand]
    private async Task RestartExplorerAsync()
    {
        await RunBusyAsync(
            Localization["Common_Working"],
            async token =>
            {
                OperationResult result = await _environment.RestartExplorerAsync(token).ConfigureAwait(true);

                if (result.Success)
                {
                    ExplorerRestartSuggested = false;
                    Interaction.ShowSuccess(Localization["Msg_ExplorerRestarted"]);
                }
                else
                {
                    Interaction.ShowError(result.Describe(Localization));
                }
            }).ConfigureAwait(true);
    }

    protected async Task ReloadAsync()
    {
        await RunBusyAsync(
            Localization["Common_Loading"],
            async token =>
            {
                IReadOnlyList<TweakDefinition> definitions = _tweaks.GetSupportedTweaks(Category);

                // Reading status hits the registry once per value, so it happens off the UI thread.
                Dictionary<string, TweakStatus> statuses = await Task.Run(
                        () => definitions.ToDictionary(d => d.Id, d => _tweaks.GetStatus(d)),
                        token)
                    .ConfigureAwait(true);

                Groups.Clear();

                foreach (IGrouping<string, TweakDefinition> group in definitions.GroupBy(d => d.GroupKey))
                {
                    var groupViewModel = new TweakGroupViewModel(group.Key, Text);

                    foreach (TweakDefinition definition in group)
                    {
                        groupViewModel.Items.Add(
                            new TweakItemViewModel(definition, statuses[definition.Id], Text));
                    }

                    Groups.Add(groupViewModel);
                }

                ApplyFilters();
                UpdateCounts();
            }).ConfigureAwait(true);
    }

    protected async Task RefreshStatusAsync()
    {
        List<TweakItemViewModel> items = AllItems().ToList();
        if (items.Count == 0)
        {
            return;
        }

        Dictionary<string, TweakStatus> statuses = await Task.Run(
                () => items.ToDictionary(i => i.Id, i => _tweaks.GetStatus(i.Definition)),
                PageToken)
            .ConfigureAwait(true);

        foreach (TweakItemViewModel item in items)
        {
            item.Status = statuses[item.Id];
        }

        ApplyFilters();
        UpdateCounts();
    }

    private async Task ApplyManyAsync(Func<TweakItemViewModel, bool> predicate, bool apply)
    {
        List<TweakItemViewModel> targets = AllItems()
            .Where(predicate)
            .Where(i => i.Status != TweakStatus.Unsupported)
            .Where(i => apply ? !i.IsApplied : i.IsApplied || i.Status == TweakStatus.Partial)
            .ToList();

        if (targets.Count == 0)
        {
            Interaction.ShowInfo(Localization["Msg_NoChanges"]);
            return;
        }

        await RunBusyAsync(
            Localization["Common_Working"],
            async token =>
            {
                var progress = new Progress<BatchProgress>(p =>
                {
                    BusyMessage = p.CurrentItem;
                    Progress = p.Total == 0 ? -1 : p.Completed * 100.0 / p.Total;
                });

                IReadOnlyList<TweakDefinition> definitions = targets.Select(t => t.Definition).ToList();

                BatchResult result = apply
                    ? await _tweaks.ApplyManyAsync(definitions, progress, token).ConfigureAwait(true)
                    : await _tweaks.RevertManyAsync(definitions, progress, token).ConfigureAwait(true);

                await RefreshStatusAsync().ConfigureAwait(true);

                if (result.RequiresRestart)
                {
                    RestartRequired = true;
                }

                if (apply && targets.Any(t => t.RequiresExplorerRestart))
                {
                    ExplorerRestartSuggested = true;
                }

                string summary = Localization.Format("Msg_BatchDone", result.Succeeded, result.Failed, result.Skipped);

                if (result.Failed > 0)
                {
                    Interaction.ShowWarning(summary);
                }
                else
                {
                    Interaction.ShowSuccess(summary);
                }
            }).ConfigureAwait(true);
    }

    protected IEnumerable<TweakItemViewModel> AllItems() => Groups.SelectMany(g => g.Items);

    protected void ApplyFilters()
    {
        int visible = 0;

        foreach (TweakGroupViewModel group in Groups)
        {
            group.VisibleItems.Clear();

            foreach (TweakItemViewModel item in group.Items)
            {
                if (!item.Matches(SearchText))
                {
                    continue;
                }

                if (RiskFilter is { } risk && item.Risk != risk)
                {
                    continue;
                }

                if (AppliedFilter is { } applied && item.IsApplied != applied)
                {
                    continue;
                }

                group.VisibleItems.Add(item);
                visible++;
            }

            group.IsVisible = group.VisibleItems.Count > 0;
        }

        VisibleCount = visible;
    }

    protected void UpdateCounts()
    {
        List<TweakItemViewModel> items = AllItems().ToList();
        TotalCount = items.Count;
        AppliedCount = items.Count(i => i.IsApplied);
        OnPropertyChanged(nameof(CountSummary));
    }

    private void RefreshText()
    {
        foreach (TweakGroupViewModel group in Groups)
        {
            group.RefreshText();
        }

        OnPropertyChanged(nameof(CountSummary));
        ApplyFilters();
    }
}
