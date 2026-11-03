using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Applies, reverts and inspects tweaks. This is the only place that writes tweak state,
/// so backup-before-write and Windows-build gating cannot be forgotten at a call site.
///
/// Nothing here runs on the caller's thread. Every caller that matters is a page on the UI thread,
/// and behind any of these methods there can be powercfg, bcdedit, a PowerShell session or a
/// settings broadcast that waits on every window on the desktop.
/// </summary>
public interface ITweakEngine
{
    /// <summary>Catalog entries that apply to the Windows build the app is running on.</summary>
    IReadOnlyList<TweakDefinition> GetSupportedTweaks(TweakCategory? category = null);

    TweakDefinition? Find(string tweakId);

    /// <summary>
    /// Whether the tweak is in place. For most tweaks that is a registry read; for one implemented
    /// by a handler it is a console tool, which is why this is not a plain property-like call.
    /// </summary>
    Task<TweakStatus> GetStatusAsync(TweakDefinition tweak, CancellationToken cancellationToken = default);

    Task<OperationResult> ApplyAsync(TweakDefinition tweak, CancellationToken cancellationToken = default);

    /// <summary>Restores the value recorded before the tweak was applied, falling back to the documented Windows default.</summary>
    Task<OperationResult> RevertAsync(TweakDefinition tweak, CancellationToken cancellationToken = default);

    Task<BatchResult> ApplyManyAsync(
        IEnumerable<TweakDefinition> tweaks,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BatchResult> RevertManyAsync(
        IEnumerable<TweakDefinition> tweaks,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public static class TweakEngineExtensions
{
    /// <summary>
    /// The status of every tweak given, keyed by id, read all at once.
    ///
    /// All at once is the point. A page's tweaks were read one after another, so a page holding two
    /// powercfg tweaks and a PowerShell one took as long as all three put together; read together it
    /// takes as long as the slowest. The dashboard reads the whole catalogue, and felt it most.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, TweakStatus>> GetStatusesAsync(
        this ITweakEngine engine,
        IEnumerable<TweakDefinition> tweaks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);

        List<TweakDefinition> list = [.. tweaks.DistinctBy(t => t.Id, StringComparer.Ordinal)];

        TweakStatus[] statuses = await Task
            .WhenAll(list.Select(tweak => engine.GetStatusAsync(tweak, cancellationToken)))
            .ConfigureAwait(false);

        var byId = new Dictionary<string, TweakStatus>(list.Count, StringComparer.Ordinal);
        for (int i = 0; i < list.Count; i++)
        {
            byId[list[i].Id] = statuses[i];
        }

        return byId;
    }
}

/// <summary>Handles a tweak that is not a plain registry write (boot configuration, power settings).</summary>
public interface ISpecialTweakHandler
{
    /// <summary>Matches <see cref="TweakDefinition.HandlerKey"/>.</summary>
    string Key { get; }

    Task<TweakStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> ApplyAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default);
}

public sealed record BatchProgress(string CurrentItem, int Completed, int Total);

public sealed record BatchResult(int Succeeded, int Failed, int Skipped, IReadOnlyList<string> Errors)
{
    public bool RequiresRestart { get; init; }

    public static readonly BatchResult Empty = new(0, 0, 0, []);
}
