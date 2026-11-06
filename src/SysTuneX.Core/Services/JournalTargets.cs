using System.Buffers;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.Core.Services;

/// <summary>
/// What a recorded change is allowed to point at before SysTuneX acts on it as administrator.
///
/// The change journal and the game mode session are files, and both are replayed with the full
/// token SysTuneX always runs with: the registry value, scheduled task or service they name gets
/// written, enabled or started, whatever it is. The data folder is locked to administrators, but a
/// replay that believes the file is one mistake in that lock - or one other program running as
/// administrator - away from doing whatever the file says. So each target is checked against what
/// SysTuneX itself could have recorded, and anything else is left alone.
///
/// Checked where the entry is replayed rather than where it is written: the writer is SysTuneX, so
/// an entry that fails here was not written by it.
/// </summary>
internal static class JournalTargets
{
    /// <summary>
    /// Every value the tweak catalogue writes, by key path - and every value it used to write. A
    /// machine that applied an older version of a tweak still has that value in its journal, and it
    /// is exactly as much SysTuneX's own record as one this version would make.
    /// </summary>
    private static readonly Lazy<ILookup<string, string>> CatalogueValues = new(
        () => TweakCatalog.All
            .SelectMany(tweak => tweak.Changes.Concat(tweak.FormerChanges))
            .ToLookup(change => change.KeyPath, change => change.ValueName, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Characters Windows refuses in a task name. Tasks are kept as files under System32\Tasks, so
    /// this is the file name list - spelled out rather than asked of the runtime, which only knows
    /// the rules of the system it happens to be running on.
    /// </summary>
    private static readonly char[] InvalidTaskNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>
    /// A value the tweak catalogue writes, or one the Nagle handler writes under a network
    /// adapter's own key. Those two are the only registry writes that are journalled.
    /// </summary>
    public static bool IsKnownRegistryValue(string? keyPath, string? valueName)
    {
        if (keyPath is null || valueName is null)
        {
            return false;
        }

        return CatalogueValues.Value[keyPath].Contains(valueName, StringComparer.OrdinalIgnoreCase)
               || IsAdapterValue(keyPath, valueName);
    }

    /// <summary>
    /// A path Task Scheduler could hold: rooted, with every folder and the name non-empty and free
    /// of the characters it refuses. Not a list of tasks - this restorer is for tasks no tweak owns.
    /// </summary>
    public static bool IsScheduledTaskPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '\\')
        {
            return false;
        }

        return path[1..].Split('\\').All(IsTaskPathSegment);
    }

    /// <summary>
    /// A service in the catalogue, which is also everything game mode can stop, or a per-user
    /// instance of one: Windows registers those as "CDPUserSvc_4f2a1", with a hex suffix that
    /// differs per sign-in, and that full name is what gets recorded.
    /// </summary>
    public static bool IsKnownService(string? serviceName)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            return false;
        }

        if (ServiceCatalog.Find(serviceName) is not null)
        {
            return true;
        }

        int separator = serviceName.LastIndexOf('_');

        return separator > 0
               && IsHex(serviceName.AsSpan(separator + 1))
               && ServiceCatalog.Find(serviceName[..separator]) is { IsPerUserService: true };
    }

    private static bool IsAdapterValue(string keyPath, string valueName)
    {
        string prefix = NagleTweakHandler.InterfacesKey + "\\";

        return keyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && Guid.TryParseExact(keyPath.AsSpan(prefix.Length), "B", out _)
               && NagleTweakHandler.ValueNames.Contains(valueName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsTaskPathSegment(string segment) =>
        segment.Length > 0
        && segment is not ("." or "..")
        && segment.IndexOfAny(InvalidTaskNameCharacters) < 0
        && !segment.Any(char.IsControl);

    private static bool IsHex(ReadOnlySpan<char> text) =>
        text.Length is > 0 and <= 16 && !text.ContainsAnyExcept(HexDigits);
}
