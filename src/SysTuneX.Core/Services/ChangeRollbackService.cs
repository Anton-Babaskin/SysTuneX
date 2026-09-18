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
    private readonly IBackupService _backup;
    private readonly ILogger<ChangeRollbackService> _logger;

    public ChangeRollbackService(
        IEnumerable<IChangeRestorer> restorers,
        IBackupService backup,
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

            outcomes[restorer.Id] = await restorer
                .RestoreAsync(mine, progress, cancellationToken)
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
}
