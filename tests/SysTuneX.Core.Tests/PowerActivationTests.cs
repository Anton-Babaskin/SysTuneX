using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Services;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// What counts as "the scheme is active".
///
/// This exists because of a real report: on a machine where <c>powercfg /setactive</c> took longer
/// than the ten seconds it was given, SysTuneX killed it and told the user it could not activate
/// the scheme - while the scheme was in fact active. Saying a change did not happen when it did is
/// the same failure as saying it did when it did not, and it is worse here, because the user then
/// goes looking for a problem that is not there.
/// </summary>
public sealed class PowerActivationTests
{
    private static readonly Guid Target = PowerScheme.UltimatePerformance;
    private static readonly Guid Other = PowerScheme.Balanced;

    [Fact]
    public void A_command_that_succeeded_is_believed_without_a_second_look()
    {
        Assert.True(PowerService.Activated(commandSucceeded: true, activeAfterwards: null, Target));
    }

    /// <summary>The bug, in one line: killed on the clock, but the machine had already switched.</summary>
    [Fact]
    public void A_timed_out_command_still_counts_when_the_machine_reports_the_scheme()
    {
        Assert.True(PowerService.Activated(commandSucceeded: false, activeAfterwards: Target, Target));
    }

    [Fact]
    public void A_failed_command_that_left_another_scheme_active_is_a_failure()
    {
        Assert.False(PowerService.Activated(commandSucceeded: false, activeAfterwards: Other, Target));
    }

    /// <summary>
    /// The read-back is evidence only when it answers. A machine that would not say which scheme
    /// is active proves nothing, and guessing in the app's favour there would be exactly the
    /// comfortable lie this whole check exists to prevent.
    /// </summary>
    [Fact]
    public void A_scheme_that_cannot_be_read_back_is_not_evidence()
    {
        Assert.False(PowerService.Activated(commandSucceeded: false, activeAfterwards: null, Target));
    }
}
