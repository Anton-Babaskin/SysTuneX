using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IChangeRollbackService"/>
[SupportedOSPlatform("windows")]
public sealed class ChangeRollbackService : IChangeRollbackService
{
    private readonly IReadOnlyList<IChangeRestorer> _restorers;
    private readonly IChangeJournalReader _backup;
    private readonly ILogger<ChangeRollbackService> _logger;

    public ChangeRollbackService(
        IEnumerable<IChangeRestorer> restorers,
        IChangeJournalReader backup,
        ILogger<ChangeRollbackService> logger)
    {
        _restorers = [.. restorers.OrderBy(r => r.Order)];
        _backup = backup;
        _logger = logger;
    }

    public async Task<RollbackReport> RestoreEverythingAsync(
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Only what SysTuneX actually recorded. Blanket-reverting the whole catalogue, as an older
        // build did, overwrites settings the user chose themselves.
        var remaining = new List<BackupEntry>(_backup.GetActive());
        var outcomes = new Dictionary<string, RestoreOutcome>(StringComparer.Ordinal);

        foreach (IChangeRestorer restorer in _restorers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<BackupEntry> mine = [.. remaining.Where(restorer.Handles)];
            if (mine.Count == 0)
            {
                continue;
            }

            // Claimed before running, so an entry cannot be restored twice by two restorers that
            // both think it is theirs - the tweak restorer and the registry one overlap by design.
            remaining.RemoveAll(restorer.Handles);

            outcomes[restorer.Id] = await RestoreOneAsync(restorer, mine, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        if (remaining.Count > 0)
        {
            // The failure this whole arrangement exists to make visible. A kind of change that
            // nobody claimed used to be skipped in silence by the button whose entire promise is
            // that it puts everything back.
            _logger.LogError(
                "{Count} recorded changes had no restorer: {Kinds}",
                remaining.Count,
                string.Join(", ", remaining.Select(e => e.Kind).Distinct()));
        }

        return new RollbackReport(outcomes, remaining);
    }

    /// <summary>
    /// Runs one restorer and turns an unexpected throw into that restorer's own failure.
    ///
    /// Restorers run in order, so without this one of them throwing takes every later one with it -
    /// and the report built to make a silently skipped change visible is never returned at all. The
    /// user would be told the rollback failed, with nothing saying which half of it went through.
    ///
    /// No restorer is known to throw today: each one's underlying service answers with an
    /// <see cref="OperationResult"/>, including the hosts file, which Defender can refuse. But they
    /// reach bcdedit, netsh, the registry, the task scheduler and PowerShell, and "everything down
    /// there reports rather than throws" is not a property this class can rely on. So a throw
    /// becomes what a refusal already is: named, counted, and not the end of the rollback.
    ///
    /// Cancellation is not a failure and is left to propagate - the caller cut the token.
    /// </summary>
    private async Task<RestoreOutcome> RestoreOneAsync(
        IChangeRestorer restorer,
        IReadOnlyList<BackupEntry> mine,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            return await restorer.RestoreAsync(mine, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The {Restorer} restorer threw; {Count} changes were not put back", restorer.Id, mine.Count);

            return new RestoreOutcome(
                0,
                mine.Count,
                [CoreMessages.RollbackRestorerFailed.Render(mine.Count, restorer.Id, ex.Message)]);
        }
    }
}
