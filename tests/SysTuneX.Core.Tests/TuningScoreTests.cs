using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The number on the front page. It is the first thing anyone sees and the thing they judge the
/// tool by, so a score that flatters an untouched machine is not a cosmetic problem - it is the
/// app telling someone their machine is tuned when nothing has been done to it.
/// </summary>
public sealed class TuningScoreTests
{
    [Fact]
    public void An_untouched_machine_scores_nothing()
    {
        Assert.Equal(0, TuningScore.Calculate(0, 100, 0, 40, highPerformancePlan: false));
    }

    [Fact]
    public void A_fully_tuned_machine_scores_one_hundred()
    {
        Assert.Equal(100, TuningScore.Calculate(100, 100, 40, 40, highPerformancePlan: true));
    }

    [Fact]
    public void The_three_parts_carry_their_stated_weights()
    {
        Assert.Equal(55, TuningScore.Calculate(100, 100, 0, 40, false));
        Assert.Equal(25, TuningScore.Calculate(0, 100, 40, 40, false));
        Assert.Equal(20, TuningScore.Calculate(0, 100, 0, 40, true));
    }

    [Fact]
    public void The_weights_add_up_to_one_hundred() =>
        Assert.Equal(100, TuningScore.TweakWeight + TuningScore.ServiceWeight + TuningScore.PowerWeight);

    [Fact]
    public void Half_the_tweaks_is_half_the_tweak_weight()
    {
        Assert.Equal(28, TuningScore.Calculate(50, 100, 0, 40, false));
    }

    [Fact]
    public void A_category_with_nothing_in_it_scores_nothing_rather_than_everything()
    {
        // No tweaks apply to this build and no managed services are installed. Zero of zero is
        // not "fully tuned", and it must not divide by zero either.
        Assert.Equal(0, TuningScore.Calculate(0, 0, 0, 0, highPerformancePlan: false));
        Assert.Equal(20, TuningScore.Calculate(0, 0, 0, 0, highPerformancePlan: true));
    }

    [Fact]
    public void A_count_larger_than_the_total_cannot_push_it_over()
    {
        // Defensive: the two numbers come from separate queries and could disagree after a
        // service is uninstalled between them. A score above 100 would look like a bug because
        // it would be one.
        Assert.Equal(100, TuningScore.Calculate(120, 100, 50, 40, highPerformancePlan: true));
    }
}
