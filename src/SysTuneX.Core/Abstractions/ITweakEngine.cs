using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Applies, reverts and inspects tweaks. This is the only place that writes tweak state,
/// so backup-before-write and Windows-build gating cannot be forgotten at a call site.
/// </summary>
public interface ITweakEngine
{
    /// <summary>Catalog entries that apply to the Windows build the app is running on.</summary>
    IReadOnlyList<TweakDefinition> GetSupportedTweaks(TweakCategory? category = null);

    TweakDefinition? Find(string tweakId);

    TweakStatus GetStatus(TweakDefinition tweak);

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
