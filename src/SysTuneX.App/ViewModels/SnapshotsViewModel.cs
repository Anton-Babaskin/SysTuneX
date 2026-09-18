using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// Before and after: record the machine as it is now, and say what moved between two recordings.
///
/// It shares the history page with the change log, and shared a class with it until the two grew
/// apart. They answer different questions - "what did SysTuneX change" against "what does this
/// machine look like" - and the second one works just as well for changes SysTuneX had nothing to
/// do with, which is exactly when it earns its keep.
/// </summary>
public sealed partial class SnapshotsViewModel : ObservableObject
{
    private readonly ISnapshotService _snapshots;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;
    private readonly IBusyScope _busy;

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

    public SnapshotsViewModel(
        ISnapshotService snapshots,
        IUserInteraction interaction,
        ILocalizationService localization,
        IBusyScope busy)
    {
        _snapshots = snapshots;
        _interaction = interaction;
        _localization = localization;
        _busy = busy;
    }

    public ObservableCollection<SystemStateSnapshot> Captured { get; } = [];

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

    /// <summary>
    /// Records the machine as it is now. Runs off the UI thread: reading every tweak's status
    /// means real registry work and, for a few of them, running powercfg.
    /// </summary>
    [RelayCommand]
    private async Task CaptureSnapshotAsync()
    {
        await _busy.RunAsync(
            _localization["Snapshot_Capturing"],
            async token =>
            {
                string label = string.IsNullOrWhiteSpace(SnapshotLabel)
                    ? DateTime.Now.ToString("g")
                    : SnapshotLabel;

                await Task.Run(() => _snapshots.CaptureAsync(label, token), token).ConfigureAwait(true);

                SnapshotLabel = string.Empty;
                await LoadAsync().ConfigureAwait(true);
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
        await LoadAsync().ConfigureAwait(true);
    }

    public async Task LoadAsync()
    {
        await _snapshots.LoadAsync().ConfigureAwait(true);

        Captured.Clear();
        foreach (SystemStateSnapshot snapshot in _snapshots.Snapshots)
        {
            Captured.Add(snapshot);
        }

        // A snapshot that was compared and then deleted must not leave a stale table behind.
        if (Comparison is { } current && !(Contains(current.Before) && Contains(current.After)))
        {
            Comparison = null;
            Differences.Clear();
        }

        bool Contains(SystemStateSnapshot snapshot) =>
            Captured.Any(s => string.Equals(s.Id, snapshot.Id, StringComparison.Ordinal));
    }
}
