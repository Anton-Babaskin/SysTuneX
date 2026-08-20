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
    private readonly IBackupService _backup;
    private readonly IProfileService _profiles;
    private readonly IEnvironmentService _environment;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;
    private readonly CatalogText _text;
    private readonly ISnapshotService _snapshots;

    [ObservableProperty]
    private bool _showReverted;

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private SystemStateSnapshot? _firstSnapshot;

    [ObservableProperty]
    private SystemStateSnapshot? _secondSnapshot;

    [ObservableProperty]
    private string _snapshotLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComparison))]
    [NotifyPropertyChangedFor(nameof(ComparisonSummary))]
    private SnapshotComparison? _comparison;

    public HistoryViewModel(
        IBackupService backup,
        IProfileService profiles,
        IEnvironmentService environment,
        IUserInteraction interaction,
        ILocalizationService localization,
        CatalogText text,
        ISnapshotService snapshots)
    {
        _backup = backup;
        _profiles = profiles;
        _environment = environment;
        _interaction = interaction;
        _localization = localization;
        _text = text;
        _snapshots = snapshots;

        localization.LanguageChanged += (_, _) => Reload();
    }

    public ObservableCollection<BackupEntryViewModel> Entries { get; } = [];

    public ObservableCollection<SystemStateSnapshot> Snapshots { get; } = [];

    public ObservableCollection<SnapshotChange> Differences { get; } = [];

    public bool HasComparison => Comparison is not null;

    /// <summary>
    /// Says plainly when two snapshots are identical. "Nothing changed" is a real answer and a
    /// useful one - it means the thing you applied did not take.
    /// </summary>
    public string ComparisonSummary => Comparison switch
    {
        null => string.Empty,
        { HasChanges: false } => _localization["Snapshot_NoDifference"],
        var c => string.Format(_localization["Snapshot_Differences"], c.Changes.Count),
    };

    public bool IsEmpty => Entries.Count == 0;

    protected override Task OnEnterAsync()
    {
        _ = LoadSnapshotsAsync();
        Reload();
        return Task.CompletedTask;
    }

    partial void OnShowRevertedChanged(bool value) => Reload();

    partial void OnSearchTextChanged(string value) => Reload();

    /// <summary>
    /// Records the machine as it is now. Runs off the UI thread: reading every tweak's status
    /// means real registry work and, for a few of them, running powercfg.
    /// </summary>
    [RelayCommand]
    private async Task CaptureSnapshotAsync()
    {
        await RunBusyAsync(
            _localization["Snapshot_Capturing"],
            async token =>
            {
                string label = string.IsNullOrWhiteSpace(SnapshotLabel)
                    ? DateTime.Now.ToString("g")
                    : SnapshotLabel;

                await Task.Run(() => _snapshots.CaptureAsync(label, token), token).ConfigureAwait(true);

                SnapshotLabel = string.Empty;
                await LoadSnapshotsAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CompareSnapshots()
    {
        if (FirstSnapshot is null || SecondSnapshot is null || ReferenceEquals(FirstSnapshot, SecondSnapshot))
        {
            _interaction.ShowInfo(_localization["Snapshot_PickTwo"]);
            return;
        }

        Comparison = _snapshots.Compare(FirstSnapshot, SecondSnapshot);

        Differences.Clear();
        foreach (SnapshotChange change in Comparison.Changes)
        {
            Differences.Add(change);
        }
    }

    [RelayCommand]
    private async Task DeleteSnapshotAsync(SystemStateSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        await _snapshots.DeleteAsync(snapshot.Id).ConfigureAwait(true);
        await LoadSnapshotsAsync().ConfigureAwait(true);
    }

    private async Task LoadSnapshotsAsync()
    {
        await _snapshots.LoadAsync().ConfigureAwait(true);

        Snapshots.Clear();
        foreach (SystemStateSnapshot snapshot in _snapshots.Snapshots)
        {
            Snapshots.Add(snapshot);
        }

        // A snapshot that was compared and then deleted must not leave a stale table behind.
        if (Comparison is { } current && !(Contains(current.Before) && Contains(current.After)))
        {
            Comparison = null;
            Differences.Clear();
        }

        bool Contains(SystemStateSnapshot snapshot) =>
            Snapshots.Any(s => string.Equals(s.Id, snapshot.Id, StringComparison.Ordinal));
    }

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

        OperationResult result = await _backup.ExportAsync(path, PageToken).ConfigureAwait(true);

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
            Directory.CreateDirectory(_environment.DataDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _environment.DataDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
    }

    private void Reload()
    {
        IReadOnlyList<BackupEntry> entries = ShowReverted ? _backup.GetAll() : _backup.GetActive();

        Entries.Clear();

        foreach (BackupEntry entry in entries)
        {
            var item = new BackupEntryViewModel(entry, _text, _localization);

            if (!item.Matches(SearchText))
            {
                continue;
            }

            Entries.Add(item);
        }

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
