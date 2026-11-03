using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// The change log: every value SysTuneX recorded before it wrote over it.
///
/// This page is the visible half of the rollback promise in the project's safety rules - if a
/// change is not here, SysTuneX did not make it and will not claim to be able to undo it.
/// </summary>
public sealed partial class HistoryViewModel : PageViewModel
{
    private readonly IChangeJournalReader _backup;
    private readonly IProfileService _profiles;
    private readonly IEnvironmentService _environment;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;
    private readonly CatalogText _text;
    private readonly IShellLauncher _shell;

    [ObservableProperty]
    private bool _showReverted;

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// The row shown for each entry, kept between reloads. An entry is immutable - reverting one
    /// replaces it - so a row whose entry is still the same instance is still right, and searching
    /// can move only the rows whose match changed. The list is not virtualised, and a journal holds
    /// a row per registry value SysTuneX ever wrote: rebuilding all of them on every keystroke is
    /// what made typing into this search box stall once the journal had some history in it.
    /// </summary>
    private Dictionary<BackupEntry, BackupEntryViewModel> _rows = new(ReferenceEqualityComparer.Instance);

    public HistoryViewModel(
        IChangeJournalReader backup,
        IProfileService profiles,
        IEnvironmentService environment,
        IUserInteraction interaction,
        ILocalizationService localization,
        CatalogText text,
        ISnapshotService snapshots,
        IShellLauncher shell)
    {
        _backup = backup;
        _profiles = profiles;
        _environment = environment;
        _interaction = interaction;
        _localization = localization;
        _text = text;
        _shell = shell;

        Snapshots = new SnapshotsViewModel(snapshots, interaction, localization, this);

        // Rows carry text in the language they were built in, so a new language needs new rows.
        localization.LanguageChanged += (_, _) =>
        {
            _rows.Clear();
            Entries.Clear();
            Reload();
        };
    }

    public ObservableCollection<BackupEntryViewModel> Entries { get; } = [];

    /// <summary>The other half of this page, which shares only the spinner with this one.</summary>
    public SnapshotsViewModel Snapshots { get; }

    public bool IsEmpty => Entries.Count == 0;

    protected override Task OnEnterAsync()
    {
        _ = Snapshots.LoadAsync();
        Reload();
        return Task.CompletedTask;
    }

    partial void OnShowRevertedChanged(bool value) => Reload();

    partial void OnSearchTextChanged(string value) => Reload();

    [RelayCommand]
    private void Refresh() => Reload();

    [RelayCommand]
    private async Task RevertAllAsync()
    {
        if (ActiveCount == 0)
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

        await RunChangeAsync(
            _localization["Common_Working"],
            async () =>
            {
                var progress = new Progress<BatchProgress>(p =>
                {
                    BusyMessage = p.CurrentItem;
                    Progress = p.Total == 0 ? -1 : p.Completed * 100.0 / p.Total;
                });

                ProfileApplyResult result = await _profiles.RestoreEverythingAsync(progress).ConfigureAwait(true);
                Reload();

                _interaction.ShowSuccess(
                    _localization.Format("Msg_RestoreDone", result.Tweaks.Succeeded + result.ServicesChanged));

                if (result.Errors.Count > 0)
                {
                    _interaction.ShowWarning(result.Errors[0]);
                }
            }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"SysTuneX-changes-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        OperationResult result = await _backup.ExportAsync(path).ConfigureAwait(true);

        if (result.Success)
        {
            _interaction.ShowSuccess(_localization.Format("Msg_Exported", path));
        }
        else
        {
            _interaction.ShowError(result.Describe(_localization));
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            OperationResult opened = _shell.OpenFolder(_environment.DataDirectory);
            if (!opened.Success)
            {
                _interaction.ShowError(opened.Describe(_localization));
            }
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
    }

    private void Reload()
    {
        IReadOnlyList<BackupEntry> entries = ShowReverted ? _backup.GetAll() : _backup.GetActive();

        // Rebuilt from the current entries so a row for an entry that has gone does not linger.
        var rows = new Dictionary<BackupEntry, BackupEntryViewModel>(entries.Count, ReferenceEqualityComparer.Instance);

        foreach (BackupEntry entry in entries)
        {
            rows[entry] = _rows.TryGetValue(entry, out BackupEntryViewModel? existing)
                ? existing
                : new BackupEntryViewModel(entry, _text, _localization);
        }

        _rows = rows;

        FilteredView.ShowOnly(Entries, [.. entries.Select(entry => rows[entry]).Where(row => row.Matches(SearchText))]);

        ActiveCount = _backup.GetActive().Count;
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed class BackupEntryViewModel
{
    private readonly ILocalizationService _localization;

    public BackupEntryViewModel(BackupEntry entry, CatalogText text, ILocalizationService localization)
    {
        Entry = entry;
        _localization = localization;
        KindText = text.BackupKind(entry.Kind);
    }

    public BackupEntry Entry { get; }

    public string KindText { get; }

    public string Target => string.IsNullOrEmpty(Entry.ValueName)
        ? Entry.Target
        : $"{Entry.Target}\\{Entry.ValueName}";

    public string OriginalValue => Entry.Kind switch
    {
        BackupKind.ServiceConfiguration =>
            $"{Entry.OriginalStartMode}, {(Entry.OriginalWasRunning ? _localization["Common_Running"] : _localization["Common_Stopped"])}",
        _ => Entry.OriginalValue ?? _localization["History_ValueAbsent"],
    };

    public string Owner => Entry.OwnerId ?? string.Empty;

    public string Timestamp => Entry.CreatedAt.LocalDateTime.ToString("g", _localization.CurrentCulture);

    public bool IsActive => Entry.IsActive;

    public string StateText => Entry.IsActive ? _localization["History_Active"] : _localization["History_Reverted"];

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        Target.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Owner.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        KindText.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
