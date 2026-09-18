using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>Which power scheme Windows is on, and switching to another one.</summary>
public sealed partial class PowerSchemeSettingsViewModel : ObservableObject
{
    private readonly IPowerSchemeService _power;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PowerSummary))]
    private PowerScheme? _selectedPowerScheme;

    public PowerSchemeSettingsViewModel(
        IPowerSchemeService power,
        IUserInteraction interaction,
        ILocalizationService localization)
    {
        _power = power;
        _interaction = interaction;
        _localization = localization;
    }

    public ObservableCollection<PowerScheme> PowerSchemes { get; } = [];

    public string PowerSummary => SelectedPowerScheme?.Name ?? _localization["Settings_Summary_Unknown"];

    /// <summary>
    /// Reads the schemes registered on this machine rather than assuming the three well-known
    /// GUIDs: OEMs ship their own, and Ultimate Performance only exists once something has
    /// duplicated it.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            IReadOnlyList<PowerScheme> schemes = await _power.GetSchemesAsync().ConfigureAwait(true);

            PowerSchemes.Clear();
            foreach (PowerScheme scheme in schemes)
            {
                PowerSchemes.Add(scheme);
            }

            SelectedPowerScheme = schemes.FirstOrDefault(s => s.IsActive);
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ApplyPowerSchemeAsync()
    {
        if (SelectedPowerScheme is not { } scheme || scheme.IsActive)
        {
            return;
        }

        OperationResult result = await _power.SetActiveSchemeAsync(scheme.Guid).ConfigureAwait(true);

        if (!result.Success)
        {
            _interaction.ShowError(result.Describe(_localization));
            return;
        }

        _interaction.ShowSuccess(string.Format(_localization["Settings_PowerPlan_Done"], scheme.Name));

        // Re-read rather than assume: the scheme that ends up active is the one powercfg says is.
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Re-reads the words after a language change; the values themselves do not move.</summary>
    public void RefreshText() => OnPropertyChanged(nameof(PowerSummary));
}
