using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// The one button on the dashboard that changes the machine without asking a second time.
///
/// It is a contract about what that button is allowed to do, which is why it is here and not in a
/// view model. The rule it keeps - safe tweaks only, never moderate, never advanced - was a
/// <c>.Where</c> in the middle of a WPF command handler, three layers away from anything that
/// could assert it. One careless edit there and a single click disables virtualisation-based
/// security on a machine whose owner clicked "optimise".
/// </summary>
public interface IQuickOptimizer
{
    /// <summary>
    /// Applies every safe tweak that is not already in place, switches to a high-performance
    /// scheme and trims memory. Reports progress per tweak.
    /// </summary>
    Task<QuickOptimizeResult> RunAsync(
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>What <see cref="RunAsync"/> would apply, without applying it.</summary>
    IReadOnlyList<TweakDefinition> GetPendingTweaks();
}

/// <param name="Tweaks">How the batch of tweaks went.</param>
/// <param name="PowerSchemeChanged">A high-performance scheme was activated.</param>
/// <param name="Memory">What trimming freed.</param>
public sealed record QuickOptimizeResult(
    BatchResult Tweaks,
    bool PowerSchemeChanged,
    MemoryTrimResult Memory);
