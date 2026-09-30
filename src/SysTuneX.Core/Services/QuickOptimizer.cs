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
    public async Task<IReadOnlyList<TweakDefinition>> GetPendingTweaksAsync(CancellationToken cancellationToken = default)
    {
        List<TweakDefinition> safe = [.. _tweaks.GetSupportedTweaks().Where(tweak => tweak.Risk == RiskLevel.Safe)];

        IReadOnlyDictionary<string, TweakStatus> statuses = await _tweaks
            .GetStatusesAsync(safe, cancellationToken)
            .ConfigureAwait(false);

        return [.. safe.Where(tweak => statuses[tweak.Id] != TweakStatus.Applied)];
    }

    public async Task<QuickOptimizeResult> RunAsync(
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Awaited before the batch rather than passed in as an argument expression. The argument
        // used to be a synchronous status read, which ran on the dashboard's thread before this
        // method had awaited anything - a PowerShell launch, with the window frozen around it.
        IReadOnlyList<TweakDefinition> pending = await GetPendingTweaksAsync(cancellationToken).ConfigureAwait(false);

        BatchResult tweaks = await _tweaks
            .ApplyManyAsync(pending, progress, cancellationToken)
            .ConfigureAwait(false);

        OperationResult power = await _power
            .ActivateHighPerformanceAsync(cancellationToken)
            .ConfigureAwait(false);

        MemoryTrimResult memory = await _memory.TrimMemoryAsync(cancellationToken).ConfigureAwait(false);

        return new QuickOptimizeResult(tweaks, power.Success, memory);
    }
}
