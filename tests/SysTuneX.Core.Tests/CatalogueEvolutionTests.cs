using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using SysTuneX.Core.Tweaks;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The catalogue changes between versions, and Windows changes under it. What was applied on a
/// machine has to stay undoable through both.
///
/// Reverting works through a tweak's definition, so three ordinary changes each used to strand
/// values on users' machines while Restore All reported success: taking a value out of a tweak,
/// gating a tweak to the builds it still works on, and dropping a tweak that never did anything.
/// Each test here applies with one shape of the world and reverts with the next.
/// </summary>
public sealed class CatalogueEvolutionTests
{
    private const string Key = @"HKCU\Software\Test";
    private const int Windows11_23H2 = 22631;
    private const int Windows11_26H2 = 26300;

    [Fact]
    public async Task A_value_taken_out_of_a_tweak_is_still_put_back_when_the_tweak_is_reverted()
    {
        var registry = new TracingRegistryService().Set(Key, "Kept", 1).Set(Key, "Dropped", 1);
        var backup = new FakeBackupService();

        TweakDefinition before = Tweak("t", Change("Kept"), Change("Dropped"));
        await Engine(registry, backup).ApplyAsync(before);

        // The next version stops writing "Dropped" - Windows began guarding it, say.
        TweakDefinition after = before with { Changes = [Change("Kept")], FormerChanges = [Change("Dropped")] };
        await Engine(registry, backup).RevertAsync(after);

        Assert.Equal(1, registry.GetValue(Key, "Kept"));
        Assert.Equal(1, registry.GetValue(Key, "Dropped"));
        Assert.Empty(backup.GetActive());
    }

    /// <summary>
    /// A former value has no Windows default to fall back on - this version never writes it - so
    /// without a journal entry saying SysTuneX set it here, it is someone else's and stays as it is.
    /// </summary>
    [Fact]
    public async Task A_former_value_the_journal_never_recorded_is_left_alone()
    {
        var registry = new TracingRegistryService().Set(Key, "Kept", 0).Set(Key, "UsersOwn", 7);
        var backup = new FakeBackupService().Record(Key, "Kept", "1");

        TweakDefinition tweak = Tweak("t", Change("Kept")) with { FormerChanges = [Change("UsersOwn")] };
        await Engine(registry, backup).RevertAsync(tweak);

        Assert.Equal(7, registry.GetValue(Key, "UsersOwn"));
        Assert.DoesNotContain(registry.Deleted, deleted => deleted.EndsWith("UsersOwn", StringComparison.Ordinal));
    }

    /// <summary>
    /// The upgrade case. A tweak gated to the builds it still works on was applied on 23H2; the
    /// machine is now on 26H2. Restore All used to count it as skipped and leave its values set.
    /// </summary>
    [Fact]
    public async Task Restore_all_undoes_a_tweak_the_machine_has_since_upgraded_past()
    {
        var registry = new TracingRegistryService().Set(Key, "Legacy", 0);
        var backup = new FakeBackupService();

        TweakDefinition legacy = Tweak("legacy", Change("Legacy")) with { MaxBuild = Windows11_23H2 };
        await Engine(registry, backup, Windows11_23H2).ApplyAsync(legacy);
        Assert.Equal(1, registry.GetValue(Key, "Legacy"));

        BatchResult result = await Engine(registry, backup, Windows11_26H2).RevertManyAsync([legacy]);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, registry.GetValue(Key, "Legacy"));
        Assert.Empty(backup.GetActive());
    }

    /// <summary>Applying is still gated: a tweak outside its builds is skipped, not written.</summary>
    [Fact]
    public async Task Applying_is_still_gated_on_the_build()
    {
        var registry = new TracingRegistryService();

        TweakDefinition legacy = Tweak("legacy", Change("Legacy")) with { MaxBuild = Windows11_23H2 };
        BatchResult result = await Engine(registry, new FakeBackupService(), Windows11_26H2).ApplyManyAsync([legacy]);

        Assert.Equal(1, result.Skipped);
        Assert.Null(registry.GetValue(Key, "Legacy"));
    }

    [Fact]
    public async Task A_retired_tweak_cannot_be_applied()
    {
        var registry = new TracingRegistryService();
        TweakDefinition retired = Tweak("placebo", Change("Value")) with { Retired = true };

        OperationResult single = await Engine(registry, new FakeBackupService()).ApplyAsync(retired);
        BatchResult batch = await Engine(registry, new FakeBackupService()).ApplyManyAsync([retired]);

        Assert.Equal("Tweak_Retired", single.Code);
        Assert.Equal(1, batch.Skipped);
        Assert.Null(registry.GetValue(Key, "Value"));
    }

    /// <summary>The whole point of keeping a retired tweak in the catalogue.</summary>
    [Fact]
    public async Task A_retired_tweak_that_was_applied_can_still_be_reverted()
    {
        var registry = new TracingRegistryService().Set(Key, "Value", 0);
        var backup = new FakeBackupService();

        TweakDefinition offered = Tweak("placebo", Change("Value"));
        await Engine(registry, backup).ApplyAsync(offered);

        BatchResult result = await Engine(registry, backup).RevertManyAsync([offered with { Retired = true }]);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, registry.GetValue(Key, "Value"));
        Assert.Empty(backup.GetActive());
    }

    /// <summary>
    /// Journal replay refuses any registry value the catalogue does not account for - the file is
    /// replayed as administrator. A value a tweak used to write is still SysTuneX's own record.
    /// </summary>
    [Fact]
    public void Journal_replay_recognises_every_value_the_catalogue_used_to_write()
    {
        List<RegistryChange> former = [.. TweakCatalog.All.SelectMany(tweak => tweak.FormerChanges)];

        // Not vacuous: the catalogue does carry former values.
        Assert.NotEmpty(former);
        Assert.All(former, change => Assert.True(
            JournalTargets.IsKnownRegistryValue(change.KeyPath, change.ValueName),
            $@"{change.KeyPath}\{change.ValueName} is a former value but journal replay would refuse it"));
    }

    [Fact]
    public void Retired_tweaks_are_not_listed_but_can_still_be_found()
    {
        var engine = Engine(new TracingRegistryService(), new FakeBackupService(), Windows11_26H2);
        TweakDefinition[] retiredTweaks = [.. TweakCatalog.All.Where(tweak => tweak.Retired)];

        Assert.NotEmpty(retiredTweaks);

        foreach (TweakDefinition retired in retiredTweaks)
        {
            Assert.DoesNotContain(engine.GetSupportedTweaks(), tweak => tweak.Id == retired.Id);
            Assert.Same(retired, engine.Find(retired.Id));
        }
    }

    private static TweakEngine Engine(TracingRegistryService registry, FakeBackupService backup, int build = 26100) =>
        new(
            NullLogger<TweakEngine>.Instance,
            registry,
            backup,
            new FakeEnvironment
            {
                Windows = new WindowsVersionInfo { Major = 10, Minor = 0, Build = build, ProductName = "Windows" },
            },
            [],
            []);

    private static RegistryChange Change(string valueName) =>
        new(Key, valueName, 1, 0, RegistryValueKind.DWord);

    private static TweakDefinition Tweak(string id, params RegistryChange[] changes) => new()
    {
        Id = id,
        Category = TweakCategory.Windows11,
        GroupKey = "Group_Test",
        Name = id,
        Description = id,
        Risk = RiskLevel.Safe,
        Changes = changes,
    };
}
