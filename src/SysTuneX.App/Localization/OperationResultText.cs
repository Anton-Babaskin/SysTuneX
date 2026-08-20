using System.Globalization;
using SysTuneX.Core.Models;

namespace SysTuneX.App.Localization;

/// <summary>
/// Turns a Core result into text in the user's language.
///
/// Core stays UI-agnostic and reports in English, which is what the log wants. The UI looks the
/// message up by code and falls back to that English text when a translation is missing — so an
/// untranslated message reads a little out of place rather than showing a raw key, and adding a
/// language never risks blanking out an error.
/// </summary>
public static class OperationResultText
{
    /// <summary>Localized detail for a result, or the caller's generic error text when it carries none.</summary>
    public static string Describe(this OperationResult result, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        return Localize(result, localization) ?? localization["Msg_Error"];
    }

    /// <summary>Localized detail, or null when the result did not explain itself.</summary>
    public static string? Detail(this OperationResult result, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        return Localize(result, localization);
    }

    private static string? Localize(OperationResult result, ILocalizationService localization)
    {
        if (result.Code is not { Length: > 0 } code)
        {
            return string.IsNullOrEmpty(result.Message) ? null : result.Message;
        }

        // The English rendering is the fallback, so a key that is not translated yet still shows
        // a sentence rather than "Core_Registry_WriteFailed".
        string format = localization.Get($"Core_{code}", result.Message ?? code);

        if (result.Args.Count == 0)
        {
            return format;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, [.. result.Args]);
        }
        catch (FormatException)
        {
            // A translation with the wrong placeholders must not throw in front of the user.
            // CoreMessageCoverageTests exists to make sure this never ships, but the guard stays.
            return result.Message ?? format;
        }
    }
}
