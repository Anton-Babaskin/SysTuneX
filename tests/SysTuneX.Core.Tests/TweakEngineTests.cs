using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The single place every tweak is written through, which makes it the single place the
/// project's promises are kept or lost: record before write, restore what was recorded rather
/// than a guessed default, and never write to a build the tweak does not apply to.
/// </summary>
public sealed class TweakEngineTests
{
    /// <summary>
    /// The line the whole rollback story depends on. Writing first and recording afterwards
    /// would look identical in every other test and would lose the original value on any crash
    /// or access-denied between the two.
    /// </summary>
    [Fact]
    public async Task The_previous_value_is_recorded_before_the_new_one_is_written()
    {
        var trace = new CallTrace();
        var registry = new TracingRegistryService(trace).Set(@"HKLM\Test", "Value", 1);
        var backup = new FakeBackupService(trace);

        await Engine(registry, backup).ApplyAsync(Tweak("t", Change(@"HKLM\Test", "Value", 2, 1)));

        Assert.Equal(
            [@"record HKLM\Test\Value=1", @"write HKLM\Test\Value=2"],
            trace.Calls);
    }

    [Fact]
    public async Task A_value_that_did_not_exist_is_recorded_as_absent_not_as_zero()
    {
        var trace = new CallTrace();
        var registry = new TracingRegistryService(trace);
        var backup = new FakeBackupService(trace);

        await Engine(registry, backup).ApplyAsync(Tweak("t", Change(@"HKLM\Test", "Value", 2, 1)));

        BackupEntry entry = Assert.Single(backup.GetAll());

        // Null, not "0". This is what lets revert delete the value rather than invent one.
        Assert.Null(entry.OriginalValue);
    }

    [Fact]
    public async Task Reverting_restores_the_recorded_value_rather_than_the_windows_default()
    {
        var registry = new TracingRegistryService().Set(@"HKLM\Test", "Value", 2);

        // The machine had 5 before SysTuneX ran, which is not what Windows ships.
        var backup = new FakeBackupService().Record(@"HKLM\Test", "Value", "5");

        await Engine(registry, backup).RevertAsync(Tweak("t", Change(@"HKLM\Test", "Value", 2, windowsDefault: 1)));

        Assert.Equal(5, registry.GetValue(@"HKLM\Test", "Value"));
    }

    /// <summary>
    /// Recorded as absent means the machine shipped without it, so putting it back means
    /// removing it. Writing the Windows default here is how a "restore" leaves a machine with a
    /// value it never had.
    /// </summary>
    [Fact]
    public async Task Reverting_deletes_a_value_that_was_recorded_as_absent()
    {
        var registry = new TracingRegistryService().Set(@"HKLM\Test", "Value", 2);
        var backup = new FakeBackupService().Record(@"HKLM\Test", "Value", originalValue: null);

        await Engine(registry, backup).RevertAsync(Tweak("t", Change(@"HKLM\Test", "Value", 2, windowsDefault: 1)));

        Assert.Null(registry.GetValue(@"HKLM\Test", "Value"));
        Assert.Contains(@"HKLM\Test\Value", registry.Deleted);
    }

    [Fact]
    public async Task With_nothing_recorded_the_documented_windows_default_is_written()
    {
        var registry = new TracingRegistryService().Set(@"HKLM\Test", "Value", 2);

        await Engine(registry, new FakeBackupService())
            .RevertAsync(Tweak("t", Change(@"HKLM\Test", "Value", 2, windowsDefault: 1)));

        Assert.Equal(1, registry.GetValue(@"HKLM\Test", "Value"));
    }

    [Fact]
    public async Task With_nothing_recorded_and_no_windows_default_the_value_is_removed()
    {
        var registry = new TracingRegistryService().Set(@"HKLM\Test", "Value", 2);

        await Engine(registry, new FakeBackupService())
            .RevertAsync(Tweak("t", Change(@"HKLM\Test", "Value", 2, windowsDefault: null)));

        Assert.Null(registry.GetValue(@"HKLM\Test", "Value"));
    }

    [Fact]
    public async Task A_restored_entry_is_marked_reverted_so_history_shows_what_is_still_in_effect()
    {
        var backup = new FakeBackupService().Record(@"HKLM\Test", "Value", "5");

        await Engine(new TracingRegistryService(), backup)
            .RevertAsync(Tweak("t", Change(@"HKLM\Test", "Value", 2, 1)));

        Assert.Single(backup.RevertedIds);
        Assert.Empty(backup.GetActive());
    }

    [Fact]
    public async Task A_revert_that_could_not_write_keeps_the_recorded_original()
    {
        // The value this machine really had was 5. If the write is refused - not elevated, key
        // protected - the journal entry has to survive, or the next attempt has nothing left to
        // restore and falls back to the documented Windows default: a rollback that quietly turns
        // into a guess about a machine it has stopped knowing anything about.
        var backup = new FakeBackupService().Record(@"HKLM\Locked", "Value", "5");
        var registry = new TracingRegistryService();
        registry.ReadOnlyPaths.Add(@"HKLM\Locked");

        OperationResult result = await Engine(registry, backup)
            .RevertAsync(Tweak("t", Change(@"HKLM\Locked", "Value", 2, 1)));

        Assert.False(result.Success);
        Assert.Empty(backup.RevertedIds);
        Assert.Single(backup.GetActive());
    }

    [Fact]
    public async Task A_partly_failed_revert_retires_only_the_entries_it_put_back()
    {
        // One key writable, one not. Retiring both would lose the original of the one that is
        // still changed, and the interface would stop showing it as outstanding.
        var backup = new FakeBackupService()
            .Record(@"HKLM\Open", "Value", "5")
            .Record(@"HKLM\Locked", "Value", "7");

        var registry = new TracingRegistryService();
        registry.ReadOnlyPaths.Add(@"HKLM\Locked");

        await Engine(registry, backup).RevertAsync(Tweak(
            "t",
            Change(@"HKLM\Open", "Value", 2, 1),
            Change(@"HKLM\Locked", "Value", 2, 1)));

        Assert.Single(backup.RevertedIds);
        Assert.Single(backup.GetActive());
        Assert.Equal(@"HKLM\Locked", backup.GetActive()[0].Target);
    }

    [Fact]
    public async Task A_tweak_the_running_build_does_not_support_writes_nothing()
    {
        var registry = new TracingRegistryService();

        TweakDefinition win11Only = Tweak("recall", Change(@"HKLM\Test", "Value", 1, 0)) with { MinBuild = 26100 };

        OperationResult result = await Engine(registry, new FakeBackupService(), build: 19045)
            .ApplyAsync(win11Only);

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Equal("Tweak_BuildGated", result.Code);
        Assert.Null(registry.GetValue(@"HKLM\Test", "Value"));
    }

    [Fact]
    public void A_tweak_outside_its_build_range_reports_as_unsupported_rather_than_not_applied()
    {
        TweakDefinition win11Only = Tweak("recall", Change(@"HKLM\Test", "Value", 1, 0)) with { MinBuild = 26100 };

        TweakStatus status = Engine(new TracingRegistryService(), new FakeBackupService(), build: 19045)
            .GetStatus(win11Only);

        Assert.Equal(TweakStatus.Unsupported, status);
    }

    /// <summary>
    /// A tweak owning several values can end up half-written when one key is protected. Rounding
    /// that down to "not applied" would tell the user to apply it again, which changes nothing.
    /// </summary>
    [Fact]
    public void A_half_written_tweak_reports_as_partial()
    {
        var registry = new TracingRegistryService()
            .Set(@"HKLM\A", "V", 1)
            .Set(@"HKLM\B", "V", 0);

        TweakDefinition tweak = Tweak("t",
            Change(@"HKLM\A", "V", 1, 0),
            Change(@"HKLM\B", "V", 1, 0));

        Assert.Equal(TweakStatus.Partial, Engine(registry, new FakeBackupService()).GetStatus(tweak));
    }

    [Fact]
    public void A_fully_written_tweak_reports_as_applied()
    {
        var registry = new TracingRegistryService().Set(@"HKLM\A", "V", 1).Set(@"HKLM\B", "V", 1);

        TweakDefinition tweak = Tweak("t",
            Change(@"HKLM\A", "V", 1, 0),
            Change(@"HKLM\B", "V", 1, 0));

        Assert.Equal(TweakStatus.Applied, Engine(registry, new FakeBackupService()).GetStatus(tweak));
    }

    [Fact]
    public void An_untouched_tweak_reports_as_not_applied() =>
        Assert.Equal(
            TweakStatus.NotApplied,
            Engine(new TracingRegistryService(), new FakeBackupService())
                .GetStatus(Tweak("t", Change(@"HKLM\A", "V", 1, 0))));

    [Fact]
    public async Task A_tweak_whose_writes_all_fail_reports_failure()
    {
        var registry = new TracingRegistryService();
        registry.ReadOnlyPaths.Add(@"HKLM\Locked");

        OperationResult result = await Engine(registry, new FakeBackupService())
            .ApplyAsync(Tweak("t", Change(@"HKLM\Locked", "V", 1, 0)));

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }

    /// <summary>
    /// One protected key out of three should not throw away the two that were written, but it
    /// must not be reported as a clean success either.
    /// </summary>
    [Fact]
    public async Task A_partly_written_tweak_succeeds_but_says_what_failed()
    {
        var registry = new TracingRegistryService();
        registry.ReadOnlyPaths.Add(@"HKLM\Locked");

        OperationResult result = await Engine(registry, new FakeBackupService()).ApplyAsync(
            Tweak("t",
                Change(@"HKLM\Open", "V", 1, 0),
                Change(@"HKLM\Locked", "V", 1, 0)));

        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Message));
        Assert.Equal(1, registry.GetValue(@"HKLM\Open", "V"));
    }

    [Fact]
    public async Task Applying_a_tweak_already_in_place_reports_no_change()
    {
        var registry = new TracingRegistryService().Set(@"HKLM\Test", "Value", 2);

        OperationResult result = await Engine(registry, new FakeBackupService())
            .ApplyAsync(Tweak("t", Change(@"HKLM\Test", "Value", 2, 1)));

        Assert.True(result.Success);
        Assert.False(result.Changed);
    }

    [Fact]
    public async Task A_handler_driven_tweak_goes_to_its_handler_and_not_to_the_registry()
    {
        var registry = new TracingRegistryService();
        var handler = new FakeTweakHandler("core_parking");

        TweakDefinition tweak = Tweak("core_parking") with { HandlerKey = "core_parking" };
        TweakEngine engine = Engine(registry, new FakeBackupService(), handlers: [handler]);

        await engine.ApplyAsync(tweak);
        await engine.RevertAsync(tweak);

        Assert.Equal(1, handler.Applied);
        Assert.Equal(1, handler.Reverted);
        Assert.Empty(registry.Deleted);
    }

    [Fact]
    public async Task A_tweak_naming_a_handler_that_is_not_registered_fails_by_name()
    {
        TweakDefinition tweak = Tweak("orphan") with { HandlerKey = "nobody_registered_this" };

        OperationResult result = await Engine(new TracingRegistryService(), new FakeBackupService())
            .ApplyAsync(tweak);

        Assert.False(result.Success);
        Assert.Equal("Tweak_NoHandler", result.Code);
    }

    [Fact]
    public void A_handler_reports_the_status_of_a_handler_driven_tweak()
    {
        var handler = new FakeTweakHandler("core_parking") { Status = TweakStatus.Applied };

        TweakDefinition tweak = Tweak("core_parking") with { HandlerKey = "core_parking" };

        Assert.Equal(
            TweakStatus.Applied,
            Engine(new TracingRegistryService(), new FakeBackupService(), handlers: [handler]).GetStatus(tweak));
    }

    /// <summary>
    /// Applying has to drop the cached status, or the page that just applied a tweak would keep
    /// showing the value from before the change for the next ten seconds.
    /// </summary>
    [Fact]
    public async Task Applying_a_handler_tweak_invalidates_its_cached_status()
    {
        var handler = new FakeTweakHandler("core_parking") { Status = TweakStatus.NotApplied };
        TweakDefinition tweak = Tweak("core_parking") with { HandlerKey = "core_parking" };

        TweakEngine engine = Engine(new TracingRegistryService(), new FakeBackupService(), handlers: [handler]);

        Assert.Equal(TweakStatus.NotApplied, engine.GetStatus(tweak));

        handler.Status = TweakStatus.Applied;
        await engine.ApplyAsync(tweak);

        Assert.Equal(TweakStatus.Applied, engine.GetStatus(tweak));
    }

    [Fact]
    public async Task Each_change_is_recorded_against_the_tweak_that_made_it()
    {
        var backup = new FakeBackupService();

        await Engine(new TracingRegistryService(), backup)
            .ApplyAsync(Tweak("gpu_scheduling", Change(@"HKLM\Test", "Value", 2, 1)));

        // History groups by owner, so a rollback can undo one tweak rather than everything.
        Assert.Equal("tweak:gpu_scheduling", Assert.Single(backup.GetAll()).OwnerId);
    }

    private static TweakEngine Engine(
        IRegistryService registry,
        IBackupService backup,
        int build = 26100,
        IEnumerable<ISpecialTweakHandler>? handlers = null) =>
        new(
            NullLogger<TweakEngine>.Instance,
            registry,
            backup,
            new FakeEnvironment
            {
                Windows = new WindowsVersionInfo { Major = 10, Minor = 0, Build = build, ProductName = "Windows" },
            },
            handlers ?? []);

    private static RegistryChange Change(string keyPath, string valueName, object optimized, object? windowsDefault) =>
        new(keyPath, valueName, optimized, windowsDefault, RegistryValueKind.DWord);

    private static TweakDefinition Tweak(string id, params RegistryChange[] changes) => new()
    {
        Id = id,
        Category = TweakCategory.Gaming,
        GroupKey = "Group_Test",
        Name = id,
        Description = id,
        Risk = RiskLevel.Safe,
        Changes = changes,
    };
}
