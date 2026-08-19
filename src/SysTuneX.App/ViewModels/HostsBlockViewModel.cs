using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>The hosts-file block card on the privacy page.</summary>
public sealed partial class HostsBlockViewModel : ObservableObject
{
    private readonly IPrivacyService _privacy;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private bool _isBlocked;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isListExpanded;

    public HostsBlockViewModel(IPrivacyService privacy, IUserInteraction interaction, ILocalizationService localization)
    {
        _privacy = privacy;
        _interaction = interaction;
        _localization = localization;

        Hosts = privacy.GetTelemetryHosts();
    }

    public IReadOnlyList<string> Hosts { get; }

    public string ShowListCaption => _localization.Format("Privacy_Hosts_Show", Hosts.Count);

    public void Refresh() => IsBlocked = _privacy.AreTelemetryHostsBlocked();

    [RelayCommand]
    private void ToggleList() => IsListExpanded = !IsListExpanded;

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            OperationResult result = IsBlocked
                ? await _privacy.UnblockTelemetryHostsAsync().ConfigureAwait(true)
                : await _privacy.BlockTelemetryHostsAsync().ConfigureAwait(true);

            Refresh();

            if (result.Success)
            {
                _interaction.ShowSuccess(
                    _localization.Format(IsBlocked ? "Msg_Applied" : "Msg_Reverted", _localization["Privacy_Hosts_Title"]));
            }
            else
            {
                _interaction.ShowError(result.Message ?? _localization["Msg_Error"], _localization["Privacy_Hosts_Title"]);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
