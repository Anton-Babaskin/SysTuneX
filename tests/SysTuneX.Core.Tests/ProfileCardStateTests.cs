using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// What a profile card is allowed to claim.
///
/// The reported bug: after applying "Battle royale" the page gave no way to tell which profile was
/// on. Every card showed a completion percentage in the high eighties, because profiles share most
/// of their tweaks, so the number that was meant to answer the question could not — it measures
/// the machine, and the question is about a decision.
/// </summary>
public sealed class ProfileCardStateTests
{
    private static AppliedProfile Applied(string id) =>
        new() { ProfileId = id, AppliedAt = DateTimeOffset.Now };

    [Fact]
    public void With_nothing_applied_no_card_claims_anything()
    {
        Assert.Equal(
            ProfileCardState.Inactive,
            ProfileCardStates.For(applied: null, "battle-royale", completion: 0.93));
    }

    /// <summary>
    /// The heart of the bug. A card that was not chosen stays inactive however high its share
    /// climbs — and it climbs high precisely because the profiles overlap.
    /// </summary>
    [Fact]
    public void A_profile_nobody_applied_is_not_active_however_complete_it_looks()
    {
        Assert.Equal(
            ProfileCardState.Inactive,
            ProfileCardStates.For(Applied("battle-royale"), "competitive-fps", completion: 0.99));
    }

    [Fact]
    public void The_applied_profile_is_active_while_it_holds()
    {
        Assert.Equal(
            ProfileCardState.Active,
            ProfileCardStates.For(Applied("battle-royale"), "battle-royale", completion: 0.97));
    }

    [Fact]
    public void The_boundary_belongs_to_the_state_it_names()
    {
        Assert.Equal(
            ProfileCardState.Active,
            ProfileCardStates.For(Applied("battle-royale"), "battle-royale", ProfileCardStates.HeldThreshold));
    }

    /// <summary>
    /// Applied, then partly undone by hand. Claiming it is simply "on" would be the comfortable
    /// half of the truth, and claiming it is off would throw away what the user actually did.
    /// </summary>
    [Fact]
    public void A_profile_that_has_been_partly_reverted_says_so()
    {
        Assert.Equal(
            ProfileCardState.PartiallyHeld,
            ProfileCardStates.For(Applied("battle-royale"), "battle-royale", completion: 0.55));
    }

    [Fact]
    public void Matching_the_id_is_not_case_sensitive()
    {
        Assert.Equal(
            ProfileCardState.Active,
            ProfileCardStates.For(Applied("Battle-Royale"), "battle-royale", completion: 1.0));
    }
}
