using SysTuneX.App.Localization;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// The words for <see cref="ProfilePreviewReport"/>'s lines: which resource string each kind of
/// line uses, and what goes into it.
///
/// It is only this translation step. Which lines appear at all is decided in Core, where it can be
/// tested without a window.
/// </summary>
public sealed class ProfilePreviewViewModel
{
    public ProfilePreviewViewModel(ProfilePreview preview, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(localization);

        List<PreviewLineViewModel> lines =
        [
            .. ProfilePreviewReport.Build(preview).Select(line => new PreviewLineViewModel(line, localization)),
        ];

        // The two counts at the top sit above the scrolling list, not inside it.
        Captions = [.. lines.Where(l => l.IsCaption)];
        Entries = [.. lines.Where(l => !l.IsCaption)];

        NeedsRestartNotice = preview.RequiresRestart || preview.RequiresSignOut;
        RestartNotice = localization[preview.RequiresRestart ? "Preview_NeedsRestart" : "Preview_NeedsSignOut"];
        HasAdvanced = preview.HasAdvanced;
        AdvancedTitle = localization["Risk_Advanced"];
        AdvancedNotice = localization["Preview_HasAdvanced"];
    }

    public IReadOnlyList<PreviewLineViewModel> Captions { get; }

    public IReadOnlyList<PreviewLineViewModel> Entries { get; }

    public bool NeedsRestartNotice { get; }

    public string RestartNotice { get; }

    public bool HasAdvanced { get; }

    public string AdvancedTitle { get; }

    public string AdvancedNotice { get; }
}

public sealed class PreviewLineViewModel
{
    public PreviewLineViewModel(PreviewLine line, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(localization);

        Kind = line.Kind;
        Text = Describe(line, localization);
    }

    public PreviewLineKind Kind { get; }

    public string Text { get; }

    public bool IsCaption => Kind is PreviewLineKind.Summary or PreviewLineKind.AlreadyApplied;

    public bool IsHeading => Kind is PreviewLineKind.TweakHeading
        or PreviewLineKind.ServiceHeading
        or PreviewLineKind.PowerSchemeHeading;

    public bool IsDetail => !IsCaption && !IsHeading;

    private static string Describe(PreviewLine line, ILocalizationService localization)
    {
        string At(int index) => index < line.Arguments.Count ? line.Arguments[index] ?? string.Empty : string.Empty;

        return line.Kind switch
        {
            PreviewLineKind.Summary => localization.Format("Preview_Summary", At(0), At(1)),
            PreviewLineKind.AlreadyApplied => localization.Format("Preview_AlreadyApplied", At(0)),
            PreviewLineKind.HandlerDriven => localization["Preview_HandlerDriven"],
            PreviewLineKind.PowerSchemeHeading => localization["Preview_PowerScheme"],

            // A value the machine does not have at all reads as "not set" rather than as blank,
            // which would be indistinguishable from a value that is genuinely empty.
            PreviewLineKind.ValueLine => localization.Format(
                "Preview_ValueLine",
                At(0),
                line.Arguments.Count > 1 && line.Arguments[1] is { } current
                    ? current
                    : localization["Preview_NotSet"],
                At(2)),

            PreviewLineKind.ServiceLine => localization.Format("Preview_ServiceLine", At(0), At(1), At(2)),

            // The two headings that name something, with the risk of applying it after the name.
            _ => line.Risk is { } risk ? $"{At(0)}  ·  {localization[$"Risk_{risk}"]}" : At(0),
        };
    }
}
