using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <param name="Changed">How many things were actually put back.</param>
/// <param name="Failed">How many refused.</param>
/// <param name="Errors">What to tell the user about the refusals.</param>
public sealed record RestoreOutcome(int Changed, int Failed, IReadOnlyList<string> Errors)
{
    public static RestoreOutcome Nothing { get; } = new(0, 0, []);
}

/// <summary>
/// Puts back one kind of recorded change.
///
/// "Restore All" used to be a chain of <c>if (active.Any(e =&gt; e.Kind == X))</c> blocks inside
/// <c>ProfileService</c>, and the same shape was repeated in the diagnostics report and the history
/// page. A new kind of change that nobody remembered to add to that chain would be recorded
/// faithfully, shown in the journal, and then silently skipped by the button whose entire promise
/// is that it puts everything back - the worst possible way for this feature to fail, because
/// nothing anywhere would say so.
///
/// Restorers are resolved as a set and each one claims the entries it is responsible for. What
/// makes the risk go away is not the extensibility but the last step: whatever no restorer claimed
/// is reported as an error rather than dropped, and a test asserts that every <see cref="BackupKind"/>
/// is claimed by somebody.
/// </summary>
public interface IChangeRestorer
{
    /// <summary>
    /// A stable name, so a caller that wants this restorer's numbers specifically can find them
    /// without knowing the order the set came back in.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The order restorers run in, lowest first. It matters: a registry entry owned by a tweak has
    /// to go through the tweak's own revert - which may be a handler doing more than one write -
    /// rather than being put back value by value.
    /// </summary>
    int Order { get; }

    /// <summary>Whether this restorer is responsible for <paramref name="entry"/>.</summary>
    bool Handles(BackupEntry entry);

    /// <summary>
    /// Restores <paramref name="entries"/>, which are exactly the ones <see cref="Handles"/>
    /// claimed. Never given an empty list.
    /// </summary>
    Task<RestoreOutcome> RestoreAsync(
        IReadOnlyList<BackupEntry> entries,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs every restorer over the journal and reports what nobody claimed.</summary>
public interface IChangeRollbackService
{
    /// <summary>
    /// Puts back every change SysTuneX recorded and has not already reverted.
    ///
    /// Deliberately not "reset everything to defaults": blanket-reverting the whole catalogue, as
    /// an older build did, overwrites settings the user chose themselves.
    /// </summary>
    Task<RollbackReport> RestoreEverythingAsync(
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <param name="ByRestorer">Each restorer's outcome, keyed by its id.</param>
/// <param name="Unclaimed">
/// Journal entries no restorer took responsibility for. Always empty in a correct build; the
/// rollback reports them rather than pretending the machine was fully restored.
/// </param>
public sealed record RollbackReport(
    IReadOnlyDictionary<string, RestoreOutcome> ByRestorer,
    IReadOnlyList<BackupEntry> Unclaimed)
{
    public RestoreOutcome For(string restorerId) =>
        ByRestorer.TryGetValue(restorerId, out RestoreOutcome? outcome) ? outcome : RestoreOutcome.Nothing;

    public IReadOnlyList<string> AllErrors => [.. ByRestorer.Values.SelectMany(o => o.Errors)];
}
