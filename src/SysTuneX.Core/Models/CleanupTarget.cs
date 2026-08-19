namespace SysTuneX.Core.Models;

/// <summary>Where a cleanup target lives, so the UI can group and explain it.</summary>
public enum CleanupCategory
{
    Temporary,
    WindowsUpdate,
    ShaderCache,
    Logs,
    Thumbnails,
}

/// <summary>
/// One cleanable location. Nothing is deleted until the user has seen the resolved paths
/// and the measured size — the README calls for cleanup to say exactly what it will remove.
/// </summary>
public sealed record CleanupTarget
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required CleanupCategory Category { get; init; }
    public required RiskLevel Risk { get; init; }

    /// <summary>Directories to sweep. Environment variables are expanded at scan time.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>Glob applied to file names inside <see cref="Paths"/>.</summary>
    public string SearchPattern { get; init; } = "*";

    /// <summary>Skip files newer than this, so a game currently compiling shaders is not disturbed.</summary>
    public TimeSpan MinimumAge { get; init; } = TimeSpan.Zero;

    /// <summary>Remove directories left empty after the sweep.</summary>
    public bool RemoveEmptyDirectories { get; init; } = true;

    /// <summary>Selected by default in the UI.</summary>
    public bool EnabledByDefault { get; init; } = true;
}

/// <summary>Measured size of a cleanup target.</summary>
public sealed record CleanupScanResult
{
    public required string TargetId { get; init; }
    public long SizeBytes { get; init; }
    public int FileCount { get; init; }

    /// <summary>Paths that exist on this machine, after environment expansion.</summary>
    public IReadOnlyList<string> ResolvedPaths { get; init; } = [];
}

/// <summary>What a cleanup run actually managed to delete.</summary>
public sealed record CleanupRunResult
{
    public long FreedBytes { get; init; }
    public int DeletedFiles { get; init; }

    /// <summary>Files that were in use and left alone. Not an error — just reported.</summary>
    public int SkippedFiles { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>A Store app SysTuneX offers to remove.</summary>
public sealed record AppPackage
{
    public required string PackageFamilyName { get; init; }
    public required string DisplayName { get; init; }
    public string Publisher { get; init; } = string.Empty;

    /// <summary>Removing this one may break part of the shell — surfaced as a warning in the UI.</summary>
    public bool IsSystemRelevant { get; init; }
}
