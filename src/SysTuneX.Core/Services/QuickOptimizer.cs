using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IQuickOptimizer"/>
public sealed class QuickOptimizer : IQuickOptimizer
{
    private readonly ITweakEngine _tweaks;
    private readonly IPowerSchemeService _power;
    private readonly IMemoryTrimmer _memory;

    public QuickOptimizer(ITweakEngine tweaks, IPowerSchemeService power, IMemoryTrimmer memory)
    {
        _tweaks = tweaks;
        _power = power;
        _memory = memory;
    }

    /// <summary>
    /// Safe tweaks only, and only the ones that are not already in place.
    ///
    /// Anything moderate or advanced stays behind an explicit choice on its own page, so a single
    /// click can never disable virtualisation-based security or break printing. That rule is the
    /// entire reason this button is allowed to exist without a confirmation dialog.
    /// </summary>
    public IReadOnlyList<TweakDefinition> GetPendingTweaks() =>
    [
        .. _tweaks.GetSupportedTweaks()
            .Where(tweak => tweak.Risk == RiskLevel.Safe)
            .Where(tweak => _tweaks.GetStatus(tweak) != TweakStatus.Applied),
    ];

    public async Task<QuickOptimizeResult> RunAsync(
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        BatchResult tweaks = await _tweaks
            .ApplyManyAsync(GetPendingTweaks(), progress, cancellationToken)
            .ConfigureAwait(false);

        OperationResult power = await _power
            .ActivateHighPerformanceAsync(cancellationToken)
            .ConfigureAwait(false);

        MemoryTrimResult memory = await _memory.TrimMemoryAsync(cancellationToken).ConfigureAwait(false);

        return new QuickOptimizeResult(tweaks, power.Success, memory);
    }
}
