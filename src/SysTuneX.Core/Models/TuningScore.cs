namespace SysTuneX.Core.Models;

/// <summary>
/// How tuned this machine is, out of 100.
///
/// Lives here rather than in the dashboard's view model because it is a claim about the machine,
/// not a detail of how the number is drawn - and because the arithmetic then has somewhere a test
/// can reach it. The defect that prompted the move was invisible for exactly that reason: the
/// service term counted any service that merely happened not to be running, so most of a stock
/// machine's on-demand services counted as tuned and an untouched install scored close to full
/// marks on that quarter of the total.
/// </summary>
public static class TuningScore
{
    /// <summary>
    /// Tweaks are the bulk of what SysTuneX does; services matter less than the marketing usually
    /// claims; the power scheme is one binary choice. The three weights add up to 100.
    /// </summary>
    public const double TweakWeight = 55;
    public const double ServiceWeight = 25;
    public const double PowerWeight = 20;

    /// <param name="appliedTweaks">Tweaks whose values are all in place.</param>
    /// <param name="totalTweaks">Tweaks that apply to this Windows build at all.</param>
    /// <param name="tunedServices">
    /// Services whose start type already matches what SysTuneX would set. Not services that are
    /// merely stopped: an on-demand service idling is the state Windows ships it in.
    /// </param>
    /// <param name="totalServices">Managed services present on this machine.</param>
    /// <param name="highPerformancePlan">Whether a high-performance power scheme is active.</param>
    public static double Calculate(
        int appliedTweaks,
        int totalTweaks,
        int tunedServices,
        int totalServices,
        bool highPerformancePlan)
    {
        double tweaks = Share(appliedTweaks, totalTweaks) * TweakWeight;
        double services = Share(tunedServices, totalServices) * ServiceWeight;
        double power = highPerformancePlan ? PowerWeight : 0;

        return Math.Round(tweaks + services + power);
    }

    /// <summary>
    /// A category with nothing in it scores nothing rather than everything. Zero of zero is not
    /// "fully tuned", and dividing by it would be worse than either answer.
    /// </summary>
    private static double Share(int done, int total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return Math.Clamp((double)done / total, 0, 1);
    }
}
