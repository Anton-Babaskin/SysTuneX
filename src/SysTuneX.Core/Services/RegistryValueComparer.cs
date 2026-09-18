using Microsoft.Win32;
using System.Globalization;

namespace SysTuneX.Core.Services;

/// <summary>
/// Compares registry values across the types the catalog uses. The old code did
/// <c>a.ToString() == b.ToString()</c>, which reported the string "2000" and the number 2000
/// as different values and threw on byte arrays.
/// </summary>
public static class RegistryValueComparer
{
    /// <summary>
    /// Separator used to flatten a REG_MULTI_SZ into one journal line. A newline is safe because
    /// a registry string cannot contain one, whereas a space can appear inside a value.
    /// </summary>
    public const char MultiStringSeparator = '\n';

    public static bool AreEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left is byte[] leftBytes && right is byte[] rightBytes)
        {
            return leftBytes.AsSpan().SequenceEqual(rightBytes);
        }

        if (left is string[] leftStrings && right is string[] rightStrings)
        {
            return leftStrings.SequenceEqual(rightStrings, StringComparer.OrdinalIgnoreCase);
        }

        // DWORD/QWORD values arrive as int, uint, long or a numeric string depending on the source.
        if (TryToInt64(left, out long leftNumber) && TryToInt64(right, out long rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(Stringify(left), Stringify(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Round-trippable text form, used when writing an entry to the backup journal.</summary>
    public static string Stringify(object value) => value switch
    {
        byte[] bytes => Convert.ToHexString(bytes),
        string[] strings => string.Join(MultiStringSeparator, strings),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static bool TryToInt64(object value, out long result)
    {
        switch (value)
        {
            case int i: result = i; return true;

            // A REG_DWORD of 0xFFFFFFFF is read back as int -1 but written from the catalog as
            // an unsigned literal. Both name the same 32 bits, so they must compare equal.
            case uint ui: result = ui > int.MaxValue ? unchecked((int)ui) : ui; return true;
            case long l: result = l; return true;
            case ulong ul when ul <= long.MaxValue: result = (long)ul; return true;
            case short s: result = s; return true;
            case byte b: result = b; return true;
            case string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    /// <summary>
    /// Turns a recorded value back into something the registry will accept.
    ///
    /// The journal stores every value as text, because one file has to hold DWORDs, strings and
    /// byte arrays alike. Putting one back means knowing which it was - and this lived inside
    /// TweakEngine as a private method, which meant the restorer that puts back a registry value
    /// nothing else owns had no way to reach it and would have grown a second copy.
    /// </summary>
    /// <param name="recordedKind">What the journal says it was.</param>
    /// <param name="fallbackKind">
    /// What to assume when the journal does not say - an entry written before the kind was
    /// recorded. The catalogue knows what the value should be, so it is the better guess than text.
    /// </param>
    public static object? Materialize(
        string text,
        RegistryValueKind recordedKind,
        RegistryValueKind fallbackKind = RegistryValueKind.String)
    {
        RegistryValueKind kind = recordedKind == RegistryValueKind.Unknown ? fallbackKind : recordedKind;

        return kind switch
        {
            RegistryValueKind.DWord => int.TryParse(text, out int dword) ? dword : null,
            RegistryValueKind.QWord => long.TryParse(text, out long qword) ? qword : null,
            RegistryValueKind.Binary => TryParseHex(text),
            RegistryValueKind.MultiString => text.Split(MultiStringSeparator, StringSplitOptions.RemoveEmptyEntries),
            _ => text,
        };
    }

    private static byte[]? TryParseHex(string text)
    {
        try
        {
            return Convert.FromHexString(text);
        }
        catch (FormatException)
        {
            // A journal entry that is not hex is not a byte array; saying so beats writing rubbish.
            return null;
        }
    }
}
