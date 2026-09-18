namespace SysTuneX.Core.Models;

/// <summary>What a line in the preview is, which decides how it is drawn.</summary>
public enum PreviewLineKind
{
    /// <summary>The counts at the top.</summary>
    Summary,

    /// <summary>How many of the profile's tweaks are already in place.</summary>
    AlreadyApplied,

    /// <summary>A tweak's name and risk.</summary>
    TweakHeading,

    /// <summary>A service's name and risk.</summary>
    ServiceHeading,

    /// <summary>The profile would switch the power scheme.</summary>
    PowerSchemeHeading,

    /// <summary>This tweak is code rather than a registry write, so there are no values to list.</summary>
    HandlerDriven,

    /// <summary>
    /// One registry value: where it is, what it holds, what it would hold. A null second
    /// argument means the machine does not have the value at all, which is not the same as
    /// holding an empty one.
    /// </summary>
    ValueLine,

    /// <summary>One service: its key, its start mode now, and the one the profile wants.</summary>
    ServiceLine,
}

/// <summary>
/// One line of the preview, as facts rather than as text. The words are the application's job -
/// they are translated - and the arguments arrive in the order the format string takes them.
/// </summary>
public sealed record PreviewLine(PreviewLineKind Kind, IReadOnlyList<string?> Arguments)
{
    public PreviewLine(PreviewLineKind kind, params string?[] arguments)
        : this(kind, (IReadOnlyList<string?>)arguments)
    {
    }

    /// <summary>Set on the headings, which show what applying that entry risks.</summary>
    public RiskLevel? Risk { get; init; }
}

/// <summary>
/// Turns a profile preview into the list of lines the confirmation dialog shows.
///
/// This used to be ninety lines of WPF layout built by hand inside the service that shows toasts,
/// which meant the decisions buried in it - which entries are listed, which are left out, what a
/// tweak with no registry values says instead - could only be checked by applying a profile and
/// reading the dialog. They are decisions about content, not about layout, so they live here and
/// the markup draws what comes out.
/// </summary>
public static class ProfilePreviewReport
{
    public static IReadOnlyList<PreviewLine> Build(ProfilePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var lines = new List<PreviewLine>
        {
            new(
                PreviewLineKind.Summary,
                preview.PendingCount.ToString(System.Globalization.CultureInfo.CurrentCulture),
                preview.PendingServiceCount.ToString(System.Globalization.CultureInfo.CurrentCulture)),
        };

        // Only what would actually change. Listing a dozen tweaks that are already in place is
        // noise that hides the two that are not.
        int alreadyApplied = preview.Tweaks.Count - preview.PendingCount;

        if (alreadyApplied > 0)
        {
            lines.Add(new PreviewLine(
                PreviewLineKind.AlreadyApplied,
                alreadyApplied.ToString(System.Globalization.CultureInfo.CurrentCulture)));
        }

        foreach (TweakPreview tweak in preview.Tweaks.Where(t => !t.AlreadyApplied))
        {
            lines.Add(new PreviewLine(PreviewLineKind.TweakHeading, tweak.Name) { Risk = tweak.Risk });

            if (tweak.IsHandlerDriven)
            {
                // Core parking and the hypervisor flag are not registry writes; saying so beats
                // an empty gap under the heading.
                lines.Add(new PreviewLine(PreviewLineKind.HandlerDriven));
                continue;
            }

            foreach (ValueChangePreview value in tweak.Values)
            {
                lines.Add(new PreviewLine(
                    PreviewLineKind.ValueLine,
                    $"{value.KeyPath}\\{value.ValueName}",
                    value.Current,
                    value.Optimized));
            }
        }

        foreach (ServicePreview service in preview.Services.Where(s => s.WouldChange))
        {
            lines.Add(new PreviewLine(PreviewLineKind.ServiceHeading, service.DisplayName) { Risk = service.Risk });
            lines.Add(new PreviewLine(
                PreviewLineKind.ServiceLine,
                service.ServiceName,
                service.CurrentStartMode.ToString(),
                service.TargetStartMode.ToString()));
        }

        if (preview.ChangesPowerScheme)
        {
            lines.Add(new PreviewLine(PreviewLineKind.PowerSchemeHeading));
        }

        return lines;
    }
}
