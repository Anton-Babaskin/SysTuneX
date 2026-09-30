using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Where the tweak engine does its work, and in what order.
///
/// Status used to be a synchronous method that blocked on a handler's console tool. Its signature
/// said "cheap", so it was used as if it were: Quick Optimize called it on the UI thread for every
/// safe tweak, and one safe tweak's status is a PowerShell launch. And every page read its tweaks
/// one after another, so it took as long as all of its console tools put together.
///
/// The handlers here are synchronous inside their "async" methods, because a real one is: the
/// Nagle handler's read is an adapter enumeration and a registry walk from end to end.
/// </summary>
public sealed class TweakEngineThreadingTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Reading_a_status_does_not_run_the_handler_on_the_callers_thread()
    {
        var handler = new BlockingHandler("slow");
        TweakEngine engine = Engine(handler);

        bool returned = await ReturnsWhileBlockedAsync(() => engine.GetStatusAsync(HandlerTweak("slow")), handler);

        Assert.True(returned, "GetStatusAsync ran the handler on the thread that called it");
    }

    [Fact]
    public async Task Applying_does_not_run_on_the_callers_thread()
    {
        var handler = new BlockingHandler("slow");
        TweakEngine engine = Engine(handler);

        bool returned = await ReturnsWhileBlockedAsync(() => engine.ApplyAsync(HandlerTweak("slow")), handler);

        Assert.True(returned, "ApplyAsync ran on the thread that called it");
    }

    [Fact]
    public async Task Reverting_does_not_run_on_the_callers_thread()
    {
        var handler = new BlockingHandler("slow");
        TweakEngine engine = Engine(handler);

        bool returned = await ReturnsWhileBlockedAsync(() => engine.RevertAsync(HandlerTweak("slow")), handler);

        Assert.True(returned, "RevertAsync ran on the thread that called it");
    }

    /// <summary>
    /// Each handler waits until the other has started. Read one after another, the first waits for
    /// a second that never starts, gives up, and reports unknown.
    /// </summary>
    [Fact]
    public async Task A_pages_statuses_are_read_together_rather_than_one_after_another()
    {
        using var arrived = new CountdownEvent(2);
        TweakEngine engine = Engine(new RendezvousHandler("first", arrived), new RendezvousHandler("second", arrived));

        IReadOnlyDictionary<string, TweakStatus> statuses = await engine.GetStatusesAsync(
            [HandlerTweak("first"), HandlerTweak("second")]);

        Assert.Equal(TweakStatus.Applied, statuses["first"]);
        Assert.Equal(TweakStatus.Applied, statuses["second"]);
    }

    [Fact]
    public async Task Statuses_come_back_keyed_by_tweak_and_a_repeated_tweak_is_read_once()
    {
        var handler = new FakeTweakHandler("h") { Status = TweakStatus.Applied };
        TweakEngine engine = Engine(handler);

        IReadOnlyDictionary<string, TweakStatus> statuses = await engine.GetStatusesAsync(
            [HandlerTweak("h"), HandlerTweak("h"), Tweak("plain")]);

        Assert.Equal(2, statuses.Count);
        Assert.Equal(TweakStatus.Applied, statuses["h"]);
        Assert.Equal(TweakStatus.NotApplied, statuses["plain"]);
    }

    /// <summary>
    /// The case a user met, end to end on the real engine: press Quick Optimize while a safe
    /// tweak's status needs a slow console tool. The dashboard used to stop responding before the
    /// busy overlay meant to cover the wait could even be drawn.
    /// </summary>
    [Fact]
    public async Task Quick_optimize_returns_to_the_dashboard_while_a_safe_tweaks_status_is_still_being_read()
    {
        var handler = new BlockingHandler("telemetry_tasks");
        TweakEngine engine = Engine(handler);
        var optimizer = new QuickOptimizer(engine, new FakePowerService(), new FakeProcessService());

        bool returned = await ReturnsWhileBlockedAsync(() => optimizer.RunAsync(), handler);

        Assert.True(returned, "Quick Optimize read a tweak's status on the thread that pressed the button");
    }

    /// <summary>
    /// A page navigated away from stops waiting at once, even on a handler stuck in synchronous
    /// work that no token can reach. The read finishes on its own and lands in the cache.
    /// </summary>
    [Fact]
    public async Task A_caller_that_leaves_stops_waiting_for_a_status_the_handler_is_still_reading()
    {
        var handler = new BlockingHandler("slow");
        TweakEngine engine = Engine(handler);
        using var leaving = new CancellationTokenSource();

        Task<TweakStatus> read = engine.GetStatusAsync(HandlerTweak("slow"), leaving.Token);
        Assert.True(handler.Entered.Wait(Patience));

        await leaving.CancelAsync();

        try
        {
            // Still blocked here: the cancellation has to reach the caller without the handler's help.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        }
        finally
        {
            handler.Release();
        }
    }

    /// <summary>
    /// Calls <paramref name="call"/> from a thread of its own and reports whether it came back
    /// while <paramref name="handler"/> was still blocked. Bounded, so a regression fails rather
    /// than hangs; the handler is released either way so nothing is left stuck.
    /// </summary>
    private static async Task<bool> ReturnsWhileBlockedAsync(Func<Task> call, BlockingHandler handler)
    {
        Task? operation = null;

        // A statement body, deliberately: as an expression the lambda would return the operation,
        // and Task.Run would wait for that instead of for the call.
        Task caller = Task.Run(() => { operation = call(); });
        Task first = await Task.WhenAny(caller, Task.Delay(Patience));

        handler.Release();
        await caller;
        await operation!;

        return first == caller;
    }

    private static TweakEngine Engine(params ISpecialTweakHandler[] handlers) =>
        new(
            NullLogger<TweakEngine>.Instance,
            new TracingRegistryService(),
            new FakeBackupService(),
            new FakeEnvironment(),
            handlers,
            []);

    private static TweakDefinition Tweak(string id) => new()
    {
        Id = id,
        Category = TweakCategory.Privacy,
        GroupKey = "Group_Test",
        Name = id,
        Description = id,
        Risk = RiskLevel.Safe,
        Changes = [new RegistryChange(@"HKLM\Test", id, 1, null, Microsoft.Win32.RegistryValueKind.DWord)],
    };

    private static TweakDefinition HandlerTweak(string key) => new()
    {
        Id = key,
        Category = TweakCategory.Privacy,
        GroupKey = "Group_Test",
        Name = key,
        Description = key,
        Risk = RiskLevel.Safe,
        HandlerKey = key,
    };

    /// <summary>
    /// Blocks inside every method until released, then hands back a task that is already finished -
    /// synchronous work behind an asynchronous signature, which is what a slow console tool behind a
    /// careless handler looks like to its caller.
    /// </summary>
    private sealed class BlockingHandler(string key) : ISpecialTweakHandler
    {
        private readonly ManualResetEventSlim _release = new(false);

        public ManualResetEventSlim Entered { get; } = new(false);

        public string Key { get; } = key;

        public void Release() => _release.Set();

        public Task<TweakStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            Block();
            return Task.FromResult(TweakStatus.NotApplied);
        }

        public Task<OperationResult> ApplyAsync(CancellationToken cancellationToken = default)
        {
            Block();
            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default)
        {
            Block();
            return Task.FromResult(OperationResult.Ok());
        }

        private void Block()
        {
            Entered.Set();
            _release.Wait(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>Reports applied only if every other rendezvous handler has started by the time it asks.</summary>
    private sealed class RendezvousHandler(string key, CountdownEvent arrived) : ISpecialTweakHandler
    {
        public string Key { get; } = key;

        public async Task<TweakStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            arrived.Signal();

            bool together = await Task.Run(() => arrived.Wait(TimeSpan.FromSeconds(3)), cancellationToken);

            return together ? TweakStatus.Applied : TweakStatus.Unknown;
        }

        public Task<OperationResult> ApplyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok());
    }
}
