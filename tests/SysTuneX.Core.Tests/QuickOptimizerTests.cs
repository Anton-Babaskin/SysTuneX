using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The one button that changes the machine without asking a second time.
///
/// Its rule - safe tweaks only, never moderate, never advanced - is what lets it skip the
/// confirmation every other bulk action shows. While that rule was a <c>.Where</c> in the middle of
/// a WPF command handler nothing could assert it, and one careless edit there turns a click labelled
/// "optimise" into one that disables virtualisation-based security.
/// </summary>
public sealed class QuickOptimizerTests
{
    [Fact]
    public void Only_safe_tweaks_are_offered()
    {
        var engine = new FakeTweakEngine(
            Tweak("safe", RiskLevel.Safe),
            Tweak("moderate", RiskLevel.Moderate),
            Tweak("advanced", RiskLevel.Advanced));

        QuickOptimizer optimizer = Build(engine);

        Assert.Equal("safe", Assert.Single(optimizer.GetPendingTweaks()).Id);
    }

    [Fact]
    public void A_safe_tweak_that_is_already_applied_is_left_alone()
    {
        var engine = new FakeTweakEngine(Tweak("done", RiskLevel.Safe), Tweak("todo", RiskLevel.Safe));
        engine.Statuses["done"] = TweakStatus.Applied;

        Assert.Equal("todo", Assert.Single(Build(engine).GetPendingTweaks()).Id);
    }

    /// <summary>
    /// The three steps all happen, in order, and the result carries all three outcomes - the
    /// dashboard reports what was applied and how much memory came back.
    /// </summary>
    [Fact]
    public async Task Running_it_applies_the_tweaks_switches_the_scheme_and_trims_memory()
    {
        var engine = new FakeTweakEngine(Tweak("a", RiskLevel.Safe), Tweak("b", RiskLevel.Safe));
        var power = new RecordingPowerSchemeService();
        var memory = new RecordingMemoryTrimmer();

        QuickOptimizeResult result = await new QuickOptimizer(engine, power, memory).RunAsync();

        Assert.Equal(["a", "b"], engine.Applied);
        Assert.True(power.Activated);
        Assert.True(memory.Trimmed);

        Assert.Equal(2, result.Tweaks.Succeeded);
        Assert.True(result.PowerSchemeChanged);
        Assert.Equal(512, result.Memory.FreedBytes);
    }

    /// <summary>
    /// A machine with neither high-performance scheme still gets its tweaks and its memory back.
    /// Reporting the scheme as changed when powercfg refused would be the worse failure.
    /// </summary>
    [Fact]
    public async Task A_power_scheme_that_cannot_be_activated_does_not_stop_the_rest()
    {
        var engine = new FakeTweakEngine(Tweak("a", RiskLevel.Safe));
        var power = new RecordingPowerSchemeService { Succeeds = false };
        var memory = new RecordingMemoryTrimmer();

        QuickOptimizeResult result = await new QuickOptimizer(engine, power, memory).RunAsync();

        Assert.False(result.PowerSchemeChanged);
        Assert.Equal(1, result.Tweaks.Succeeded);
        Assert.True(memory.Trimmed);
    }

    [Fact]
    public async Task Nothing_left_to_apply_still_switches_the_scheme_and_trims()
    {
        var engine = new FakeTweakEngine(Tweak("done", RiskLevel.Safe));
        engine.Statuses["done"] = TweakStatus.Applied;

        var power = new RecordingPowerSchemeService();
        var memory = new RecordingMemoryTrimmer();

        QuickOptimizeResult result = await new QuickOptimizer(engine, power, memory).RunAsync();

        Assert.Empty(engine.Applied);
        Assert.True(power.Activated);
        Assert.True(memory.Trimmed);
        Assert.Equal(0, result.Tweaks.Succeeded);
    }

    private static QuickOptimizer Build(FakeTweakEngine engine) =>
        new(engine, new RecordingPowerSchemeService(), new RecordingMemoryTrimmer());

    private static TweakDefinition Tweak(string id, RiskLevel risk) => new()
    {
        Id = id,
        Name = id,
        Description = id,
        GroupKey = "Group_Test",
        Category = TweakCategory.Gaming,
        Risk = risk,
    };

    private sealed class FakeTweakEngine : ITweakEngine
    {
        private readonly List<TweakDefinition> _tweaks;

        public FakeTweakEngine(params TweakDefinition[] tweaks) => _tweaks = [.. tweaks];

        public Dictionary<string, TweakStatus> Statuses { get; } = new(StringComparer.Ordinal);

        public List<string> Applied { get; } = [];

        public IReadOnlyList<TweakDefinition> GetSupportedTweaks(TweakCategory? category = null) =>
            category is null ? _tweaks : [.. _tweaks.Where(t => t.Category == category)];

        public TweakDefinition? Find(string tweakId) =>
            _tweaks.FirstOrDefault(t => string.Equals(t.Id, tweakId, StringComparison.Ordinal));

        public TweakStatus GetStatus(TweakDefinition tweak) =>
            Statuses.TryGetValue(tweak.Id, out TweakStatus status) ? status : TweakStatus.NotApplied;

        public Task<OperationResult> ApplyAsync(TweakDefinition tweak, CancellationToken cancellationToken = default)
        {
            Applied.Add(tweak.Id);
            return Task.FromResult(OperationResult.Ok());
        }

        public Task<BatchResult> ApplyManyAsync(
            IEnumerable<TweakDefinition> tweaks,
            IProgress<BatchProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            int count = 0;

            foreach (TweakDefinition tweak in tweaks)
            {
                Applied.Add(tweak.Id);
                count++;
            }

            return Task.FromResult(new BatchResult(count, 0, 0, []));
        }

        public Task<OperationResult> RevertAsync(TweakDefinition tweak, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<BatchResult> RevertManyAsync(
            IEnumerable<TweakDefinition> tweaks,
            IProgress<BatchProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BatchResult(0, 0, 0, []));
    }

    private sealed class RecordingPowerSchemeService : IPowerSchemeService
    {
        public bool Activated { get; private set; }

        public bool Succeeds { get; init; } = true;

        public Task<OperationResult> ActivateHighPerformanceAsync(CancellationToken cancellationToken = default)
        {
            Activated = true;
            return Task.FromResult(Succeeds
                ? OperationResult.Ok()
                : OperationResult.Fail(CoreMessages.PowerNoHighPerformanceScheme));
        }

        public Task<IReadOnlyList<PowerScheme>> GetSchemesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PowerScheme>>([]);

        public Task<PowerScheme?> GetActiveSchemeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<PowerScheme?>(null);

        public Task<OperationResult> RestorePreviousSchemeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<bool> IsHighPerformanceActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Activated && Succeeds);

        public Task<OperationResult> SetActiveSchemeAsync(Guid schemeGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok());
    }

    private sealed class RecordingMemoryTrimmer : IMemoryTrimmer
    {
        public bool Trimmed { get; private set; }

        public Task<MemoryTrimResult> TrimMemoryAsync(CancellationToken cancellationToken = default)
        {
            Trimmed = true;
            return Task.FromResult(new MemoryTrimResult(3, StandbyPurged: true, FreedBytes: 512));
        }
    }
}
