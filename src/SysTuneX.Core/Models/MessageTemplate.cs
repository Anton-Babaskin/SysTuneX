using System.Globalization;

namespace SysTuneX.Core.Models;

/// <summary>
/// A message the system layer can produce, identified by a stable code.
///
/// Core must not depend on the UI, but its messages are the ones a user actually reads — so it
/// carries a code the UI can translate and an English format string it falls back to. The English
/// text is also what goes in the log, which is deliberate: a log is worth more in one language
/// the developer can read than in whichever language the machine happened to be set to.
/// </summary>
/// <param name="Code">Stable identifier. The UI looks up <c>Core_{Code}</c>.</param>
/// <param name="Format">English text, with <c>{0}</c>-style placeholders for the arguments.</param>
public sealed record MessageTemplate(string Code, string Format)
{
    /// <summary>Number of distinct placeholders, so a test can catch a translation with the wrong arity.</summary>
    public int PlaceholderCount
    {
        get
        {
            int highest = -1;
            for (int i = 0; i < 10; i++)
            {
                if (Format.Contains($"{{{i}}}", StringComparison.Ordinal))
                {
                    highest = i;
                }
            }

            return highest + 1;
        }
    }

    public string Render(params object?[] args) =>
        args.Length == 0 ? Format : string.Format(CultureInfo.InvariantCulture, Format, args);
}
