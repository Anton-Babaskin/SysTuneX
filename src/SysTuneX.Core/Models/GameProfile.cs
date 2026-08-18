namespace SysTuneX.Core.Models;

/// <summary>A named bundle of tweaks and services applied together.</summary>
public sealed record GameProfile
{
    public required string Id { get; init; }

    /// <summary>Neutral name; the UI prefers the resource <c>Profile_{Id}_Name</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Neutral description; the UI prefers the resource <c>Profile_{Id}_Desc</c>.</summary>
    public required string Description { get; init; }

    /// <summary>Example titles this profile was tuned against.</summary>
    public IReadOnlyList<string> ExampleGames { get; init; } = [];

    /// <summary>Fluent System Icons glyph name resolved by the UI (e.g. <c>Target24</c>).</summary>
    public string Icon { get; init; } = "Games24";

    /// <summary>Accent used for the profile card, as a hex ARGB/RGB string.</summary>
    public string AccentColor { get; init; } = "#7C5CFF";

    public IReadOnlyList<string> TweakIds { get; init; } = [];
    public IReadOnlyList<string> ServiceNames { get; init; } = [];

    /// <summary>Switch the machine to the high performance / ultimate performance scheme.</summary>
    public bool ActivateHighPerformancePower { get; init; } = true;

    /// <summary>Trim working sets and purge the standby list right after applying.</summary>
    public bool TrimMemory { get; init; }

    /// <summary>The highest risk level this profile is allowed to touch.</summary>
    public RiskLevel MaxRisk { get; init; } = RiskLevel.Moderate;
}
