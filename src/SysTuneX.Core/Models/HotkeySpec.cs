namespace SysTuneX.Core.Models;

/// <summary>
/// Why a global hotkey is not listening.
///
/// A code rather than a sentence, so the interface can say it in the user's language. The one
/// that matters is <see cref="AlreadyTaken"/> - it is by far the most common, and it is the only
/// one the user can do anything about.
/// </summary>
public enum HotkeyFailure
{
    /// <summary>It is registered and listening.</summary>
    None,

    /// <summary>Asked for before the window existed. A programming error, not a user-facing one.</summary>
    WindowNotReady,

    /// <summary>The text parsed, but Windows has no key by that name.</summary>
    UnknownKey,

    /// <summary>Another program got there first.</summary>
    AlreadyTaken,

    /// <summary>Windows said no for some other reason; the error number is the only detail there is.</summary>
    Refused,
}

/// <summary>Modifier keys a global hotkey can require, as a set.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,

    /// <summary>The Windows key. Windows reserves a great many of these; see <see cref="HotkeySpec"/>.</summary>
    Windows = 8,
}

/// <summary>
/// A global hotkey, as text the user can read and as parts the registration needs.
///
/// Parsing lives here rather than beside the Windows call because it is the half that can be
/// wrong in interesting ways - "Ctrl + Shift + M", "ctrl+shift+m" and "Shift+Ctrl+M" all mean the
/// same thing and all have to survive a round trip through a settings file - and the half that a
/// test can reach. The registration itself is three lines of P/Invoke that either work or return
/// false.
/// </summary>
public sealed record HotkeySpec
{
    private static readonly (string Name, HotkeyModifiers Flag)[] ModifierNames =
    [
        ("ctrl", HotkeyModifiers.Control),
        ("control", HotkeyModifiers.Control),
        ("alt", HotkeyModifiers.Alt),
        ("shift", HotkeyModifiers.Shift),
        ("win", HotkeyModifiers.Windows),
        ("windows", HotkeyModifiers.Windows),
    ];

    public required HotkeyModifiers Modifiers { get; init; }

    /// <summary>The non-modifier key, upper-cased: "M", "F8", "OEM3".</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Ctrl+Shift+M. Deliberately not a single key and not Win-based: a bare key would fire while
    /// the user is typing anywhere on the machine, and Windows already owns most Win combinations.
    /// </summary>
    public static HotkeySpec Default { get; } =
        new() { Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift, Key = "M" };

    /// <summary>
    /// Reads "Ctrl+Shift+M" in any order, spacing or case. Returns false rather than throwing,
    /// because the input is a settings file somebody may have edited by hand.
    /// </summary>
    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = Default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        string? key = null;

        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            HotkeyModifiers? flag = ModifierNames
                .Where(m => string.Equals(m.Name, raw, StringComparison.OrdinalIgnoreCase))
                .Select(m => (HotkeyModifiers?)m.Flag)
                .FirstOrDefault();

            if (flag is { } found)
            {
                modifiers |= found;
                continue;
            }

            // Two non-modifier keys is not a combination Windows can register, and silently
            // keeping the last one would hand the user a hotkey they did not ask for.
            if (key is not null)
            {
                return false;
            }

            key = raw.ToUpperInvariant();
        }

        if (key is null || modifiers == HotkeyModifiers.None)
        {
            // A modifier-free hotkey fires while the user is typing in any other application.
            return false;
        }

        spec = new HotkeySpec { Modifiers = modifiers, Key = key };
        return true;
    }

    /// <summary>
    /// Always in the order people write them, whatever order they were given in, so the settings
    /// file and the button on screen agree.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>(4);

        if (Modifiers.HasFlag(HotkeyModifiers.Control)) { parts.Add("Ctrl"); }
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) { parts.Add("Alt"); }
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) { parts.Add("Shift"); }
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) { parts.Add("Win"); }

        parts.Add(Key);
        return string.Join("+", parts);
    }
}
