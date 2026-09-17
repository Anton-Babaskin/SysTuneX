namespace SysTuneX.Core.Models;

/// <summary>What Windows says about a scheduled task.</summary>
public enum ScheduledTaskState
{
    /// <summary>Windows would not answer, or the task is not on this build.</summary>
    Unknown,

    /// <summary>Enabled and waiting for its trigger.</summary>
    Ready,

    /// <summary>Enabled and running right now.</summary>
    Running,

    /// <summary>Switched off.</summary>
    Disabled,
}

/// <summary>A task and the state it was found in.</summary>
public sealed record ScheduledTaskInfo(string Path, ScheduledTaskState State);

/// <summary>
/// Reads what the scheduled-task query printed.
///
/// <c>schtasks.exe</c> is the obvious tool and the wrong one: it prints the state as a translated
/// word, so "Ready" is "Готово" on a Russian install and the parse silently fails - or worse,
/// matches nothing and reports every task as already disabled, which would make the tweak claim
/// to have done work it never did. The query goes through PowerShell's <c>Get-ScheduledTask</c>
/// instead, whose State is a .NET enum and therefore prints the same words everywhere.
///
/// Each line is <c>path|state</c>. Splitting on a character a task path cannot contain keeps this
/// a three-line parse instead of a second format to get wrong.
/// </summary>
public static class ScheduledTaskQuery
{
    /// <summary>Separator between the path and the state.</summary>
    public const char Separator = '|';

    public static IReadOnlyList<ScheduledTaskInfo> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var tasks = new List<ScheduledTaskInfo>();

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // From the right: a task name may contain almost anything, and splitting on the first
            // separator would cut a name in half and lose the state.
            int split = line.LastIndexOf(Separator);
            if (split <= 0 || split == line.Length - 1)
            {
                continue;
            }

            tasks.Add(new ScheduledTaskInfo(
                Normalize(line[..split]),
                ParseState(line[(split + 1)..].Trim())));
        }

        return tasks;
    }

    /// <summary>
    /// One leading backslash, none trailing, no doubles.
    ///
    /// Get-ScheduledTask reports a task's folder and name separately and the folder ends in a
    /// backslash, so joining them gives a path with a double separator in it. Comparing that
    /// against the catalogue's spelling is how a task that is right there looks absent.
    /// </summary>
    public static string Normalize(string path)
    {
        string trimmed = path.Trim().Replace('/', '\\');

        while (trimmed.Contains("\\\\", StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        trimmed = trimmed.TrimEnd('\\');

        return trimmed.StartsWith('\\') ? trimmed : '\\' + trimmed;
    }

    private static ScheduledTaskState ParseState(string state) =>
        Enum.TryParse(state, ignoreCase: true, out ScheduledTaskState parsed) ? parsed : ScheduledTaskState.Unknown;
}
