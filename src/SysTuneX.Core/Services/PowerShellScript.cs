using System.Text;

namespace SysTuneX.Core.Services;

/// <summary>
/// Hands values to a PowerShell script as arguments, never as script text.
///
/// Quoting a value into a script means knowing every character PowerShell reads as a quote, and
/// the list is longer than it looks: besides the ASCII apostrophe, U+2018 to U+201B all open and
/// close a single-quoted string. The escaping this replaced doubled only the ASCII one, so a task
/// path with a typographic apostrophe in it ended the string early, and whatever followed ran as a
/// PowerShell statement - with the administrator token SysTuneX always has.
///
/// So the values stay out of the text. The script runs as a script block and each value is passed
/// to it as an argument, written as base64 that PowerShell decodes itself: an alphabet with no
/// quote, no space and nothing else the parser could take for syntax. The script reads them from
/// <c>$args</c> or a <c>param()</c> block, like any other arguments.
///
/// One difference from running the script bare: an error the block writes without stopping does
/// not make PowerShell exit non-zero. A script whose caller decides from the exit code has to make
/// its errors terminating, with <c>-ErrorAction Stop</c> or <c>$ErrorActionPreference</c>.
/// </summary>
internal static class PowerShellScript
{
    public static string WithArguments(string script, params IReadOnlyList<string> arguments)
    {
        var command = new StringBuilder("& {\n").Append(script).Append("\n}");

        foreach (string argument in arguments)
        {
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(argument));
            command.Append(" ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('")
                .Append(encoded)
                .Append("')))");
        }

        return command.ToString();
    }
}
