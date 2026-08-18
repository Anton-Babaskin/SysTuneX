using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace SysTuneX.Core.Models;

/// <summary>
/// One recorded "before" state. SysTuneX writes one of these <em>before</em> every change,
/// so a revert restores what the machine actually had rather than a value we guessed.
/// </summary>
public sealed record BackupEntry
{
    public required string Id { get; init; }

    public required BackupKind Kind { get; init; }

    /// <summary>Tweak / profile / service operation that produced this entry, for grouping in the history view.</summary>
    public string? OwnerId { get; init; }

    /// <summary>Registry: full key path. Service: service name. Dns: adapter id. Power: scheme. Hosts: file path.</summary>
    public required string Target { get; init; }

    /// <summary>Registry value name, empty for the other kinds.</summary>
    public string ValueName { get; init; } = string.Empty;

    /// <summary>
    /// The original value, serialised as a string. <see langword="null"/> means the value did not
    /// exist before, and reverting therefore deletes it rather than writing something.
    /// </summary>
    public string? OriginalValue { get; init; }

    /// <summary>Registry kind of <see cref="OriginalValue"/>, so REG_SZ does not come back as REG_DWORD.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegistryValueKind OriginalValueKind { get; init; } = RegistryValueKind.Unknown;

    /// <summary>Service start type before the change.</summary>
    public ServiceStartMode OriginalStartMode { get; init; } = ServiceStartMode.Unknown;

    /// <summary>Service was running before the change.</summary>
    public bool OriginalWasRunning { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>Set once the entry has been rolled back, so history shows what is still in effect.</summary>
    public DateTimeOffset? RevertedAt { get; init; }

    [JsonIgnore]
    public bool IsActive => RevertedAt is null;

    /// <summary>Stable identity of what this entry points at, used to find the newest entry for a target.</summary>
    [JsonIgnore]
    public string TargetKey => $"{Kind}|{Target}|{ValueName}".ToUpperInvariant();
}
