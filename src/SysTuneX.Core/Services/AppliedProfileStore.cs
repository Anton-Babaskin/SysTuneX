using System.Text.Json;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <summary>
/// Remembers which profile is applied, across restarts.
///
/// On disk rather than in memory because the question outlives the window: the machine keeps the
/// tweaks after the app closes, so the app has to keep the record too, or it reopens knowing less
/// than the machine does.
///
/// Its own type because it is a second thing <c>ProfileService</c> was doing - applying profiles is
/// the first - and because a store is trivially testable while a service with eleven dependencies
/// is not. Its tests used to pass <c>null!</c> for six of those eleven just to reach this.
/// </summary>
public interface IAppliedProfileStore
{
    /// <summary>The profile currently applied, or null when none is.</summary>
    AppliedProfile? Current { get; }

    /// <summary>Records a profile as applied. Null clears the record.</summary>
    void Write(AppliedProfile? applied);
}

/// <inheritdoc cref="IAppliedProfileStore"/>
public sealed class AppliedProfileStore : IAppliedProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<AppliedProfileStore> _logger;
    private readonly string _file;

    public AppliedProfileStore(ILogger<AppliedProfileStore> logger, IEnvironmentService environment)
    {
        _logger = logger;
        _file = Path.Combine(environment.DataDirectory, "profile.json");

        Current = Read();
    }

    public AppliedProfile? Current { get; private set; }

    public void Write(AppliedProfile? applied)
    {
        Current = applied;

        try
        {
            if (applied is null)
            {
                if (File.Exists(_file))
                {
                    File.Delete(_file);
                }

                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(applied, JsonOptions));
        }
        catch (Exception ex)
        {
            // The record is still correct in memory for this session; only the next launch loses it.
            _logger.LogWarning(ex, "The applied profile record could not be saved to {Path}", _file);
        }
    }

    /// <summary>
    /// Read once at construction: every caller wants it immediately, and a missing or unreadable
    /// file means "nothing applied", which is the honest answer when we genuinely do not know.
    /// </summary>
    private AppliedProfile? Read()
    {
        try
        {
            return File.Exists(_file)
                ? JsonSerializer.Deserialize<AppliedProfile>(File.ReadAllText(_file))
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The applied profile record could not be read: {Path}", _file);
            return null;
        }
    }
}
