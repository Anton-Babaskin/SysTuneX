using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The preview is the project's central claim held at profile scale: nothing is applied without
/// the user having been shown the exact value it writes. A preview that quietly misreports what
/// the machine has now would be worse than no preview at all.
/// </summary>
public sealed class ProfilePreviewTests
{
    [Fact]
    public async Task Each_registry_change_is_listed_with_what_the_machine_has_now()
    {
        TweakDefinition tweak = Tweak("gpu_scheduling", RiskLevel.Moderate,
            Change(@"HKLM\System\GraphicsDrivers", "HwSchMode", optimized: 2, windowsDefault: 1));

        ProfilePreview preview = await Preview(
            engine: new FakeTweakEngine().Add(tweak, TweakStatus.NotApplied),
            registry: new FakeRegistryService().Set(@"HKLM\System\GraphicsDrivers", "HwSchMode", 1),
            tweakIds: ["gpu_scheduling"]);

        ValueChangePreview value = Assert.Single(Assert.Single(preview.Tweaks).Values);

        Assert.Equal(@"HKLM\System\GraphicsDrivers", value.KeyPath);
        Assert.Equal("HwSchMode", value.ValueName);
        Assert.Equal("1", value.Current);
        Assert.Equal("2", value.Optimized);
        Assert.Equal("1", value.WindowsDefault);
        Assert.True(value.WouldChange);
    }

    /// <summary>
    /// "Value does not exist" and "value is zero" are different things, and the difference is
    /// the whole reason revert can delete rather than invent. The preview has to keep it.
    /// </summary>
    [Fact]
    public async Task A_value_the_machine_does_not_have_reads_as_absent_not_as_zero()
    {
        TweakDefinition tweak = Tweak("telemetry", RiskLevel.Safe,
            Change(@"HKLM\Software\Policies", "AllowTelemetry", optimized: 0, windowsDefault: null));

        ProfilePreview preview = await Preview(
            engine: new FakeTweakEngine().Add(tweak, TweakStatus.NotApplied),
            registry: new FakeRegistryService(),
            tweakIds: ["telemetry"]);

        ValueChangePreview value = Assert.Single(Assert.Single(preview.Tweaks).Values);

        Assert.Null(value.Current);
        Assert.Null(value.WindowsDefault);
        Assert.True(value.WouldChange);
    }

    [Fact]
    public async Task A_tweak_already_in_place_is_marked_and_not_counted_as_pending()
    {
        TweakDefinition applied = Tweak("a", RiskLevel.Safe, Change(@"HKLM\A", "V", 1, 0));
        TweakDefinition pending = Tweak("b", RiskLevel.Safe, Change(@"HKLM\B", "V", 1, 0));

        ProfilePreview preview = await Preview(
            engine: new FakeTweakEngine()
                .Add(applied, TweakStatus.Applied)
                .Add(pending, TweakStatus.NotApplied),
            registry: new FakeRegistryService().Set(@"HKLM\A", "V", 1),
            tweakIds: ["a", "b"]);

        Assert.Equal(2, preview.Tweaks.Count);
        Assert.True(preview.Tweaks.Single(t => t.TweakId == "a").AlreadyApplied);
        Assert.Equal(1, preview.PendingCount);
        Assert.False(preview.IsNoOp);
    }

    [Fact]
    public async Task A_profile_with_nothing_left_to_do_says_so()
    {
        TweakDefinition tweak = Tweak("a", RiskLevel.Safe, Change(@"HKLM\A", "V", 1, 0));

        ProfilePreview preview = await Preview(
            engine: new FakeTweakEngine().Add(tweak, TweakStatus.Applied),
            registry: new FakeRegistryService().Set(@"HKLM\A", "V", 1),
            tweakIds: ["a"]);

        Assert.True(preview.IsNoOp);
        Assert.Equal(0, preview.PendingCount);
    }

    /// <summary>
    /// Core parking and the hypervisor flag are not registry writes. An empty value list under a
    /// heading looks like a bug; the preview marks them so the interface can say what they are.
    /// </summary>
    [Fact]
    public async Task A_tweak_handled_by_code_is_marked_rather_than_shown_empty()
    {
        TweakDefinition tweak = Tweak("core_parking", RiskLevel.Moderate) with { HandlerKey = "core_parking" };

        ProfilePreview preview = await Preview(
            engine: new FakeTweakEngine().Add(tweak, TweakStatus.NotApplied),
            registry: new FakeRegistryService(),
            tweakIds: ["core_parking"]);

        Assert.True(Assert.Single(preview.Tweaks).IsHandlerDriven);
    }

    [Fact]
    public async Task A_restart_is_only_announced_for_changes_that_are_still_pending()
    {
        TweakDefinition needsRestart = Tweak("vbs", RiskLevel.Advanced, Change(@"HKLM\V", "V", 0, 1))
            with { RequiresRestart = true };

        ProfilePreview stillPending = await Preview(
            engine: new FakeTweakEngine().Add(needsRestart, TweakStatus.NotApplied),
            registry: new FakeRegistryService(),
            tweakIds: ["vbs"]);

        Assert.True(stillPending.RequiresRestart);
        Assert.True(stillPending.HasAdvanced);

        // Already applied: no restart to announce, and no advanced warning to raise.
        ProfilePreview alreadyDone = await Preview(
            engine: new FakeTweakEngine().Add(needsRestart, TweakStatus.Applied),
            registry: new FakeRegistryService(),
            tweakIds: ["vbs"]);

        Assert.False(alreadyDone.RequiresRestart);
        Assert.False(alreadyDone.HasAdvanced);
    }

    [Fact]
    public async Task A_service_that_is_not_installed_is_left_out()
    {
        // DiagTrack exists in the catalog; the fake reports it as not installed.
        ProfilePreview preview = await Preview(
            engine: new FakeTweakEngine(),
            registry: new FakeRegistryService(),
            tweakIds: [],
            serviceNames: ["DiagTrack"],
            services: new FakeServiceManager());

        Assert.Empty(preview.Services);
    }

    [Fact]
    public async Task A_service_already_at_the_target_start_mode_is_listed_but_not_pending()
    {
        FakeServiceManager services = new FakeServiceManager().Add("DiagTrack", RiskLevel.Safe, running: true);

        ProfilePreview preview = await Preview(
            engine: new FakeTweakEngine(),
            registry: new FakeRegistryService(),
            tweakIds: [],
            serviceNames: ["DiagTrack"],
            services: services);

        ServicePreview service = Assert.Single(preview.Services);

        Assert.Equal(ServiceStartMode.Automatic, service.CurrentStartMode);
        Assert.True(service.WouldChange);
        Assert.Equal(1, preview.PendingServiceCount);
    }

    /// <summary>
    /// The profile's own ceiling, not just the caller's opt-in. A profile that does not go above
    /// Moderate must not surface an advanced change even when advanced changes are allowed -
    /// this is what stops "Quick optimise" from ever offering to disable VBS.
    /// </summary>
    [Fact]
    public async Task A_profile_that_stops_at_moderate_never_previews_an_advanced_tweak()
    {
        TweakDefinition advanced = Tweak("vbs", RiskLevel.Advanced, Change(@"HKLM\V", "V", 0, 1));
        TweakDefinition moderate = Tweak("safe", RiskLevel.Moderate, Change(@"HKLM\S", "V", 1, 0));

        ProfilePreview preview = await Preview(
            engine: new FakeTweakEngine()
                .Add(advanced, TweakStatus.NotApplied)
                .Add(moderate, TweakStatus.NotApplied),
            registry: new FakeRegistryService(),
            tweakIds: ["vbs", "safe"],
            maxRisk: RiskLevel.Moderate);

        Assert.Equal("safe", Assert.Single(preview.Tweaks).TweakId);
        Assert.False(preview.HasAdvanced);
    }

    [Fact]
    public async Task Declining_advanced_changes_drops_them_from_the_preview()
    {
        TweakDefinition advanced = Tweak("vbs", RiskLevel.Advanced, Change(@"HKLM\V", "V", 0, 1));

        ProfilePreview preview = await Preview(
            engine: new FakeTweakEngine().Add(advanced, TweakStatus.NotApplied),
            registry: new FakeRegistryService(),
            tweakIds: ["vbs"],
            includeAdvanced: false);

        Assert.Empty(preview.Tweaks);
        Assert.True(preview.IsNoOp);
    }

    private static RegistryChange Change(string keyPath, string valueName, object optimized, object? windowsDefault) =>
        new(keyPath, valueName, optimized, windowsDefault, RegistryValueKind.DWord);

    private static TweakDefinition Tweak(string id, RiskLevel risk, params RegistryChange[] changes) => new()
    {
        Id = id,
        Category = TweakCategory.Gaming,
        GroupKey = "Group_Test",
        Name = id,
        Description = id,
        Risk = risk,
        Changes = changes,
    };

    private static Task<ProfilePreview> Preview(
        FakeTweakEngine engine,
        FakeRegistryService registry,
        IReadOnlyList<string> tweakIds,
        IReadOnlyList<string>? serviceNames = null,
        FakeServiceManager? services = null,
        RiskLevel maxRisk = RiskLevel.Advanced,
        bool includeAdvanced = true)
    {
        // PreviewAsync reads the catalog, the registry and the service manager, and nothing
        // else - the remaining dependencies exist for apply and restore.
        var profiles = new ProfileService(
            NullLogger<ProfileService>.Instance,
            engine,
            services ?? new FakeServiceManager(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new FakeEnvironment(),
            registry);

        var profile = new GameProfile
        {
            Id = "test",
            Name = "Test",
            Description = "Test",
            TweakIds = tweakIds,
            ServiceNames = serviceNames ?? [],
            MaxRisk = maxRisk,
        };

        return profiles.PreviewAsync(profile, includeAdvanced);
    }
}
