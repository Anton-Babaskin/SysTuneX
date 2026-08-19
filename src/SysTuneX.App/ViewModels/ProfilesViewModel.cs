using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

public sealed partial class ProfilesViewModel : PageViewModel
{
    private readonly IProfileService _profiles;
    private readonly IRestorePointService _restorePoints;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;
    private readonly CatalogText _text;
    private readonly IAppSettingsService _settings;

    [ObservableProperty]
    private bool _includeAdvanced;

    [ObservableProperty]
    private bool _createRestorePoint = true;

    [ObservableProperty]
    private bool _restorePointAvailable = true;

    [ObservableProperty]
    private bool _restartRequired;

    public ProfilesViewModel(
        IProfileService profiles,
        IRestorePointService restorePoints,
        IUserInteraction interaction,
        ILocalizationService localization,
        CatalogText text,
        IAppSettingsService settings)
    {
        _profiles = profiles;
        _restorePoints = restorePoints;
        _interaction = interaction;
        _localization = localization;
        _text = text;
        _settings = settings;

        CreateRestorePoint = settings.Current.CreateRestorePointBeforeProfiles;
        localization.LanguageChanged += (_, _) => RefreshText();
    }

    public ObservableCollection<ProfileCardViewModel> Profiles { get; } = [];

    protected override async Task OnEnterAsync()
    {
        if (Profiles.Count == 0)
        {
            foreach (GameProfile profile in _profiles.GetProfiles())
            {
                Profiles.Add(new ProfileCardViewModel(profile, _text, _profiles));
            }
        }

        RestorePointAvailable = await _restorePoints.IsAvailableAsync(PageToken).ConfigureAwait(true);
        await RefreshCompletionAsync().ConfigureAwait(true);
    }

    partial void OnCreateRestorePointChanged(bool value)
    {
        _settings.Current.CreateRestorePointBeforeProfiles = value;
        _ = _settings.SaveAsync();
    }

    [RelayCommand]
    private async Task ApplyAsync(ProfileCardViewModel? card)
    {
        if (card is null || IsBusy)
        {
            return;
        }

        bool advanced = IncludeAdvanced && card.HasAdvanced;

        if (advanced && _settings.Current.ConfirmAdvancedChanges)
        {
            bool confirmed = await _interaction
                .ConfirmAdvancedAsync(
                    card.Name,
                    card.Description,
                    _localization["Warning_Vbs"],
                    PageToken)
                .ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }
        }

        await RunBusyAsync(
            card.Name,
            async token =>
            {
                var progress = new Progress<BatchProgress>(p =>
                {
                    BusyMessage = string.IsNullOrEmpty(p.CurrentItem) ? card.Name : p.CurrentItem;
                    Progress = p.Total == 0 ? -1 : p.Completed * 100.0 / p.Total;
                });

                var options = new ProfileApplyOptions(
                    IncludeAdvanced: advanced,
                    CreateRestorePoint: CreateRestorePoint && RestorePointAvailable);

                ProfileApplyResult result = await _profiles
                    .ApplyAsync(card.Profile, options, progress, token)
                    .ConfigureAwait(true);

                if (result.RequiresRestart)
                {
                    RestartRequired = true;
                }

                await RefreshCompletionAsync().ConfigureAwait(true);

                _interaction.ShowSuccess(
                    _localization.Format("Msg_ProfileApplied", card.Name) + " · " +
                    _localization.Format("Msg_BatchDone", result.Tweaks.Succeeded, result.Tweaks.Failed, result.Tweaks.Skipped));

                if (options.CreateRestorePoint && !result.RestorePointCreated && result.RestorePointMessage is not null)
                {
                    _interaction.ShowWarning(result.RestorePointMessage);
                }
            }).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshCompletionAsync();

    private async Task RefreshCompletionAsync()
    {
        foreach (ProfileCardViewModel card in Profiles)
        {
            try
            {
                card.Completion = await _profiles.GetCompletionAsync(card.Profile, PageToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void RefreshText()
    {
        foreach (ProfileCardViewModel card in Profiles)
        {
            card.RefreshText();
        }
    }
}

public sealed partial class ProfileCardViewModel : ObservableObject
{
    private readonly CatalogText _text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompletionPercent))]
    private double _completion;

    public ProfileCardViewModel(GameProfile profile, CatalogText text, IProfileService profiles)
    {
        Profile = profile;
        _text = text;

        TweakCount = profiles.ResolveTweaks(profile, includeAdvanced: true).Count;
        ServiceCount = profile.ServiceNames.Count;
        HasAdvanced = profile.MaxRisk == RiskLevel.Advanced;
    }

    public GameProfile Profile { get; }

    public string Id => Profile.Id;

    public string Name => _text.Name(Profile);

    public string Description => _text.Description(Profile);

    public string Icon => Profile.Icon;

    public string AccentColor => Profile.AccentColor;

    public IReadOnlyList<string> ExampleGames => Profile.ExampleGames;

    public bool HasExampleGames => Profile.ExampleGames.Count > 0;

    public int TweakCount { get; }

    public int ServiceCount { get; }

    public bool HasAdvanced { get; }

    public double CompletionPercent => Math.Round(Completion * 100);

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }
}
