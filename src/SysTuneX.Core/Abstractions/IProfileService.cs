using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>Applies and rolls back the preset bundles shown on the profiles page.</summary>
public interface IProfileService
{
    IReadOnlyList<GameProfile> GetProfiles();

    /// <summary>Tweaks the profile refers to that exist and apply to this Windows build.</summary>
    IReadOnlyList<TweakDefinition> ResolveTweaks(GameProfile profile, bool includeAdvanced);

    /// <summary>How far through the profile the machine already is, as a fraction between 0 and 1.</summary>
    Task<double> GetCompletionAsync(GameProfile profile, CancellationToken cancellationToken = default);

    Task<ProfileApplyResult> ApplyAsync(
        GameProfile profile,
        ProfileApplyOptions options,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Rolls every recorded change back, whichever profile or page made it.</summary>
    Task<ProfileApplyResult> RestoreEverythingAsync(
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <param name="IncludeAdvanced">Apply the profile's advanced-risk entries too. Requires an explicit confirmation in the UI.</param>
/// <param name="CreateRestorePoint">Ask Windows for a restore point before the first change.</param>
public sealed record ProfileApplyOptions(bool IncludeAdvanced = false, bool CreateRestorePoint = true);

public sealed record ProfileApplyResult
{
    public required BatchResult Tweaks { get; init; }
    public int ServicesChanged { get; init; }
    public int ServicesFailed { get; init; }
    public bool PowerSchemeChanged { get; init; }
    public long MemoryFreedBytes { get; init; }
    public bool RestorePointCreated { get; init; }
    public string? RestorePointMessage { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool RequiresRestart => Tweaks.RequiresRestart;
}
