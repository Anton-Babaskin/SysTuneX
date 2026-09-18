using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// What the confirmation dialog says a profile would do.
///
/// This is the last screen between the user and a dozen registry writes, and the project's rule is
/// that it names every one of them. While it was built control by control inside the toast service
/// there was no way to check that except by applying a profile and reading the dialog - so the
/// omissions that matter most were the ones nothing could see.
/// </summary>
public sealed class ProfilePreviewReportTests
{
    [Fact]
    public void A_pending_tweak_is_listed_with_its_key_its_current_value_and_the_new_one()
    {
        IReadOnlyList<PreviewLine> lines = ProfilePreviewReport.Build(Preview(
            Tweak("game-dvr", "Game DVR", RiskLevel.Safe, applied: false,
                Value(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", current: "1", optimized: "0"))));

        PreviewLine heading = Assert.Single(lines, l => l.Kind == PreviewLineKind.TweakHeading);
        Assert.Equal("Game DVR", Assert.Single(heading.Arguments));
        Assert.Equal(RiskLevel.Safe, heading.Risk);

        PreviewLine value = Assert.Single(lines, l => l.Kind == PreviewLineKind.ValueLine);
        Assert.Equal([@"HKCU\System\GameConfigStore\GameDVR_Enabled", "1", "0"], value.Arguments);
    }

    /// <summary>
    /// Listing a dozen tweaks that are already in place is noise that hides the two that are not.
    /// The count is still shown, because "nine of these are already done" is worth knowing.
    /// </summary>
    [Fact]
    public void A_tweak_that_is_already_applied_is_counted_but_not_listed()
    {
        IReadOnlyList<PreviewLine> lines = ProfilePreviewReport.Build(Preview(
            Tweak("done", "Already done", RiskLevel.Safe, applied: true, Value("k", "v", "0", "0")),
            Tweak("todo", "Still to do", RiskLevel.Safe, applied: false, Value("k", "v", "1", "0"))));

        PreviewLine heading = Assert.Single(lines, l => l.Kind == PreviewLineKind.TweakHeading);
        Assert.Equal("Still to do", Assert.Single(heading.Arguments));

        PreviewLine already = Assert.Single(lines, l => l.Kind == PreviewLineKind.AlreadyApplied);
        Assert.Equal("1", Assert.Single(already.Arguments));
    }

    [Fact]
    public void Nothing_says_already_applied_when_nothing_is()
    {
        IReadOnlyList<PreviewLine> lines = ProfilePreviewReport.Build(Preview(
            Tweak("todo", "Still to do", RiskLevel.Safe, applied: false, Value("k", "v", "1", "0"))));

        Assert.DoesNotContain(lines, l => l.Kind == PreviewLineKind.AlreadyApplied);
    }

    /// <summary>
    /// A value the machine does not have is not a value that is empty. The dialog says "not set"
    /// for the first; carrying an empty string here would make the two indistinguishable by the
    /// time anything could tell them apart.
    /// </summary>
    [Fact]
    public void A_value_the_machine_does_not_have_arrives_as_null_not_as_empty()
    {
        IReadOnlyList<PreviewLine> lines = ProfilePreviewReport.Build(Preview(
            Tweak("new", "Writes a new value", RiskLevel.Safe, applied: false,
                Value("k", "v", current: null, optimized: "1"))));

        Assert.Null(Assert.Single(lines, l => l.Kind == PreviewLineKind.ValueLine).Arguments[1]);
    }

    [Fact]
    public void An_empty_value_stays_empty_and_is_not_confused_with_an_absent_one()
    {
        IReadOnlyList<PreviewLine> lines = ProfilePreviewReport.Build(Preview(
            Tweak("blank", "Blanks a value", RiskLevel.Safe, applied: false,
                Value("k", "v", current: string.Empty, optimized: "1"))));

        Assert.Equal(string.Empty, Assert.Single(lines, l => l.Kind == PreviewLineKind.ValueLine).Arguments[1]);
    }

    /// <summary>
    /// Core parking and the hypervisor boot flag are code, not registry writes. An empty gap under
    /// the heading would read as "this does nothing", which is the opposite of the truth.
    /// </summary>
    [Fact]
    public void A_tweak_with_no_registry_values_says_so_instead_of_showing_a_gap()
    {
        IReadOnlyList<PreviewLine> lines = ProfilePreviewReport.Build(Preview(
            Tweak("core-parking", "Core parking", RiskLevel.Moderate, applied: false)));

        Assert.Contains(lines, l => l.Kind == PreviewLineKind.HandlerDriven);
        Assert.DoesNotContain(lines, l => l.Kind == PreviewLineKind.ValueLine);
    }

    [Fact]
    public void A_service_is_listed_with_the_mode_it_has_and_the_mode_it_would_get()
    {
        var preview = new ProfilePreview
        {
            ProfileId = "test",
            Services =
            [
                new ServicePreview("SysMain", "SysMain", RiskLevel.Moderate,
                    ServiceStartMode.Automatic, ServiceStartMode.Disabled, WouldChange: true),
                new ServicePreview("DiagTrack", "Telemetry", RiskLevel.Safe,
                    ServiceStartMode.Disabled, ServiceStartMode.Disabled, WouldChange: false),
            ],
        };

        IReadOnlyList<PreviewLine> lines = ProfilePreviewReport.Build(preview);

        PreviewLine heading = Assert.Single(lines, l => l.Kind == PreviewLineKind.ServiceHeading);
        Assert.Equal("SysMain", Assert.Single(heading.Arguments));

        PreviewLine detail = Assert.Single(lines, l => l.Kind == PreviewLineKind.ServiceLine);
        Assert.Equal(["SysMain", "Automatic", "Disabled"], detail.Arguments);
    }

    [Fact]
    public void The_power_scheme_is_mentioned_only_when_the_profile_changes_it()
    {
        Assert.Contains(
            ProfilePreviewReport.Build(new ProfilePreview { ProfileId = "p", ChangesPowerScheme = true }),
            l => l.Kind == PreviewLineKind.PowerSchemeHeading);

        Assert.DoesNotContain(
            ProfilePreviewReport.Build(new ProfilePreview { ProfileId = "p", ChangesPowerScheme = false }),
            l => l.Kind == PreviewLineKind.PowerSchemeHeading);
    }

    /// <summary>The counts at the top are the first thing shown, and they count only what is pending.</summary>
    [Fact]
    public void The_summary_comes_first_and_counts_only_what_would_change()
    {
        var preview = new ProfilePreview
        {
            ProfileId = "test",
            Tweaks =
            [
                Tweak("a", "A", RiskLevel.Safe, applied: false, Value("k", "v", "1", "0")),
                Tweak("b", "B", RiskLevel.Safe, applied: true, Value("k", "v", "0", "0")),
            ],
            Services =
            [
                new ServicePreview("S", "S", RiskLevel.Safe,
                    ServiceStartMode.Automatic, ServiceStartMode.Disabled, WouldChange: true),
            ],
        };

        IReadOnlyList<PreviewLine> lines = ProfilePreviewReport.Build(preview);

        Assert.Equal(PreviewLineKind.Summary, lines[0].Kind);
        Assert.Equal(["1", "1"], lines[0].Arguments);
    }

    /// <summary>
    /// A profile with nothing left to do still gets a report - the dialog says "nothing to do"
    /// rather than opening empty.
    /// </summary>
    [Fact]
    public void A_profile_with_nothing_pending_still_reports_its_counts()
    {
        IReadOnlyList<PreviewLine> lines = ProfilePreviewReport.Build(new ProfilePreview { ProfileId = "p" });

        Assert.Equal(PreviewLineKind.Summary, Assert.Single(lines).Kind);
    }

    [Fact]
    public void A_heading_carries_the_risk_of_what_it_names()
    {
        IReadOnlyList<PreviewLine> lines = ProfilePreviewReport.Build(Preview(
            Tweak("risky", "Risky", RiskLevel.Advanced, applied: false, Value("k", "v", "0", "1"))));

        Assert.Equal(RiskLevel.Advanced, Assert.Single(lines, l => l.Kind == PreviewLineKind.TweakHeading).Risk);
    }

    private static ProfilePreview Preview(params TweakPreview[] tweaks) =>
        new() { ProfileId = "test", Tweaks = tweaks };

    private static TweakPreview Tweak(
        string id,
        string name,
        RiskLevel risk,
        bool applied,
        params ValueChangePreview[] values) =>
        new(id, name, risk, applied, RequiresRestart: false, RequiresSignOut: false, values);

    private static ValueChangePreview Value(string key, string name, string? current, string optimized) =>
        new(key, name, current, optimized, WindowsDefault: null);
}
