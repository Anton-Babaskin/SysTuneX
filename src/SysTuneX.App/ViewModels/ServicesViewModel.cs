using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

public sealed partial class ServicesViewModel : PageViewModel
{
    private readonly IServiceManager _services;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;
    private readonly CatalogText _text;
    private readonly IAppSettingsService _settings;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private RiskLevel? _riskFilter;

    [ObservableProperty]
    private int _disabledCount;

    [ObservableProperty]
    private int _totalCount;

    public ServicesViewModel(
        IServiceManager services,
        IUserInteraction interaction,
        ILocalizationService localization,
        CatalogText text,
        IAppSettingsService settings)
    {
        _services = services;
        _interaction = interaction;
        _localization = localization;
        _text = text;
        _settings = settings;

        localization.LanguageChanged += (_, _) => RefreshText();
    }

    public ObservableCollection<ServiceItemViewModel> Items { get; } = [];

    public ObservableCollection<ServiceItemViewModel> VisibleItems { get; } = [];

    public string CountSummary => _localization.Format("Services_DisabledCount", DisabledCount, TotalCount);

    protected override Task OnEnterAsync() => ReloadAsync();

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnRiskFilterChanged(RiskLevel? value) => ApplyFilters();

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();

    [RelayCommand]
    private void SetRiskFilter(string? risk) =>
        RiskFilter = Enum.TryParse(risk, out RiskLevel parsed) && RiskFilter != parsed ? parsed : null;

    [RelayCommand]
    private async Task ToggleAsync(ServiceItemViewModel? item)
    {
        if (item is null || item.IsBusy)
        {
            return;
        }

        bool disabling = !item.IsDisabled;

        if (disabling && item.Risk == RiskLevel.Advanced && _settings.Current.ConfirmAdvancedChanges)
        {
            bool confirmed = await _interaction
                .ConfirmAdvancedAsync(item.Name, item.Description, string.Empty, PageToken)
                .ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }
        }

        item.IsBusy = true;

        try
        {
            OperationResult result = disabling
                ? await _services.DisableAsync(item.Definition, PageToken).ConfigureAwait(true)
                : await _services.RestoreAsync(item.Definition.ServiceName, PageToken).ConfigureAwait(true);

            item.Update(_services.GetState(item.Definition.ServiceName));

            if (result.Success)
            {
                Interaction(result, item, disabling);
            }
            else
            {
                _interaction.ShowError(result.Describe(_localization), item.Name);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message, item.Name);
        }
        finally
        {
            item.IsBusy = false;
            UpdateCounts();
        }
    }

    [RelayCommand]
    private async Task DisableSafeAsync()
    {
        List<ServiceItemViewModel> targets = Items
            .Where(i => i.Risk == RiskLevel.Safe && !i.IsDisabled)
            .ToList();

        if (targets.Count == 0)
        {
            _interaction.ShowInfo(_localization["Msg_NoChanges"]);
            return;
        }

        await RunBusyAsync(
            _localization["Common_Working"],
            async token =>
            {
                int changed = 0;
                int failed = 0;

                for (int i = 0; i < targets.Count; i++)
                {
                    ServiceItemViewModel item = targets[i];
                    BusyMessage = item.Name;
                    Progress = i * 100.0 / targets.Count;

                    OperationResult result = await _services.DisableAsync(item.Definition, token).ConfigureAwait(true);
                    item.Update(_services.GetState(item.Definition.ServiceName));

                    if (result.Success)
                    {
                        changed++;
                    }
                    else
                    {
                        failed++;
                    }
                }

                UpdateCounts();
                _interaction.ShowSuccess(_localization.Format("Msg_BatchDone", changed, failed, 0));
            }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestoreAllAsync()
    {
        List<ServiceItemViewModel> targets = Items.Where(i => i.IsDisabled).ToList();

        if (targets.Count == 0)
        {
            _interaction.ShowInfo(_localization["Msg_NoChanges"]);
            return;
        }

        await RunBusyAsync(
            _localization["Common_Working"],
            async token =>
            {
                int changed = 0;
                int failed = 0;

                for (int i = 0; i < targets.Count; i++)
                {
                    ServiceItemViewModel item = targets[i];
                    BusyMessage = item.Name;
                    Progress = i * 100.0 / targets.Count;

                    OperationResult result = await _services
                        .RestoreAsync(item.Definition.ServiceName, token)
                        .ConfigureAwait(true);

                    item.Update(_services.GetState(item.Definition.ServiceName));

                    if (result.Success)
                    {
                        changed++;
                    }
                    else
                    {
                        failed++;
                    }
                }

                UpdateCounts();
                _interaction.ShowSuccess(_localization.Format("Msg_RestoreDone", changed));
            }).ConfigureAwait(true);
    }

    private void Interaction(OperationResult result, ServiceItemViewModel item, bool disabling)
    {
        // A service that refused to stop but did get its start type changed reports a message
        // alongside success, and the user should see it rather than a plain "done".
        if (result.Detail(_localization) is { Length: > 0 } detail)
        {
            _interaction.ShowWarning(detail, item.Name);
            return;
        }

        _interaction.ShowSuccess(_localization.Format(disabling ? "Msg_Applied" : "Msg_Reverted", item.Name));
    }

    private async Task ReloadAsync()
    {
        await RunBusyAsync(
            _localization["Common_Loading"],
            async token =>
            {
                IReadOnlyList<(ServiceDefinition Definition, ServiceSnapshot State)> services =
                    await _services.GetManagedServicesAsync(token).ConfigureAwait(true);

                Items.Clear();

                foreach ((ServiceDefinition definition, ServiceSnapshot state) in services)
                {
                    Items.Add(new ServiceItemViewModel(definition, state, _text));
                }

                ApplyFilters();
                UpdateCounts();
            }).ConfigureAwait(true);
    }

    private void ApplyFilters()
    {
        VisibleItems.Clear();

        foreach (ServiceItemViewModel item in Items)
        {
            if (!item.Matches(SearchText))
            {
                continue;
            }

            if (RiskFilter is { } risk && item.Risk != risk)
            {
                continue;
            }

            VisibleItems.Add(item);
        }
    }

    private void UpdateCounts()
    {
        TotalCount = Items.Count;
        DisabledCount = Items.Count(i => i.IsDisabled);
        OnPropertyChanged(nameof(CountSummary));
    }

    private void RefreshText()
    {
        foreach (ServiceItemViewModel item in Items)
        {
            item.RefreshText();
        }

        OnPropertyChanged(nameof(CountSummary));
        ApplyFilters();
    }
}

public sealed partial class ServiceItemViewModel : ObservableObject
{
    private readonly CatalogText _text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisabled))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(StartModeText))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private ServiceSnapshot _state;

    [ObservableProperty]
    private bool _isBusy;

    public ServiceItemViewModel(ServiceDefinition definition, ServiceSnapshot state, CatalogText text)
    {
        Definition = definition;
        _state = state;
        _text = text;
    }

    public ServiceDefinition Definition { get; }

    public string ServiceName => Definition.ServiceName;

    public string Name => _text.Name(Definition);

    public string Description => _text.Description(Definition);

    public RiskLevel Risk => Definition.Risk;

    public string RiskText => _text.Risk(Definition.Risk);

    public bool IsDisabled => State.StartMode == ServiceStartMode.Disabled ||
                              (State.StartMode == Definition.DisabledStartMode && !State.IsRunning);

    public bool IsRunning => State.IsRunning;

    public string StartModeText => _text.StartMode(State.StartMode);

    public string StateText => _text.RunningState(State.IsRunning);

    public void Update(ServiceSnapshot snapshot) => State = snapshot;

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(RiskText));
        OnPropertyChanged(nameof(StartModeText));
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        ServiceName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
