namespace SysTuneX.Core.Models;

/// <summary>
/// The profile the user last applied, and when.
///
/// This exists because the profiles page could not answer the one question anybody asks of it:
/// which profile is on. It only showed, per card, what share of that profile's tweaks currently
/// held — and profiles deliberately overlap, so applying any one of them left every card reading
/// somewhere in the high eighties. Six near-identical percentages answer nothing.
///
/// A percentage cannot be made to answer it either, because it is a measurement of the machine
/// rather than a memory of a decision. The decision has to be recorded when it is made.
/// </summary>
public sealed record AppliedProfile
{
    /// <summary>Catalogue id of the profile, e.g. "battle-royale".</summary>
    public required string ProfileId { get; init; }

    public DateTimeOffset AppliedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// Whether the risky half went in too. The same profile applied with and without its advanced
    /// tweaks leaves the machine in genuinely different states, so the card should not describe
    /// them with the same sentence.
    /// </summary>
    public bool IncludedAdvanced { get; init; }
}

/// <summary>
/// How a profile card should read.
///
/// Kept apart from <see cref="AppliedProfile"/> because the two answer different questions and the
/// interface needs both: the record says what was chosen, the share says how much of it the
/// machine still holds. They disagree after somebody reverts a tweak by hand, and that
/// disagreement is worth showing rather than hiding behind whichever number is prettier.
/// </summary>
public enum ProfileCardState
{
    /// <summary>Not the applied profile.</summary>
    Inactive,

    /// <summary>Applied, and everything it asked for is still in place.</summary>
    Active,

    /// <summary>Applied, but part of it has since been reverted or overwritten.</summary>
    PartiallyHeld,
}

public static class ProfileCardStates
{
    /// <summary>
    /// Below this share of a profile's tweaks still being applied, the card stops claiming the
    /// profile is simply "on" and says it is partly held instead.
    ///
    /// Not a round number for its own sake: a profile whose tweaks are nearly all still in place
    /// is, for practical purposes, the profile the machine is running. One that has lost a
    /// meaningful slice of itself is a different machine, and saying otherwise would be the
    /// comfortable half of the truth.
    /// </summary>
    public const double HeldThreshold = 0.9;

    /// <summary>
    /// What a card should say, given what was recorded and what the machine currently reports.
    /// </summary>
    /// <param name="applied">The recorded decision, or null when no profile has been applied.</param>
    /// <param name="profileId">The card being drawn.</param>
    /// <param name="completion">Share of that profile's tweaks currently in place, 0 to 1.</param>
    public static ProfileCardState For(AppliedProfile? applied, string profileId, double completion)
    {
        if (applied is null || !string.Equals(applied.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            return ProfileCardState.Inactive;
        }

        return completion >= HeldThreshold ? ProfileCardState.Active : ProfileCardState.PartiallyHeld;
    }
}
