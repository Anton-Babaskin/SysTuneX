namespace SysTuneX.Core.Models;

/// <summary>How a temperature reads at a glance, before anyone works out what the number means.</summary>
public enum TemperatureBand
{
    /// <summary>Nothing to look at.</summary>
    Normal,

    /// <summary>Working hard. Expected under load, worth noticing at idle.</summary>
    Warm,

    /// <summary>At or past the point where the part starts slowing itself down to survive.</summary>
    Hot,
}

/// <summary>
/// The two numbers that turn a temperature into a colour.
///
/// Deliberately per part rather than one threshold for the whole machine: a laptop CPU at 85 °C
/// is having an ordinary afternoon, and a GPU at 85 °C is already throttling. One shared limit
/// would have to be wrong for one of them, and a monitor that cries wolf gets ignored - which
/// costs more than showing no colour at all.
///
/// The bands are read against the point each part starts thermal throttling rather than against
/// its absolute maximum, because throttling is the moment the number begins costing frames, and
/// frames are what this page exists to explain.
/// </summary>
public readonly record struct TemperatureScale
{
    private TemperatureScale(int warm, int hot)
    {
        Warm = warm;
        Hot = hot;
    }

    /// <summary>At or above this, the reading is warm.</summary>
    public int Warm { get; }

    /// <summary>At or above this, the reading is hot.</summary>
    public int Hot { get; }

    /// <summary>
    /// Modern laptop and desktop parts sit in the eighties under load by design and throttle in
    /// the high nineties, so eighty is "working" rather than "wrong".
    /// </summary>
    public static TemperatureScale Cpu { get; } = new(80, 95);

    /// <summary>
    /// Graphics parts run cooler and throttle earlier: the high eighties is where clocks start
    /// coming down on most desktop and laptop cards.
    /// </summary>
    public static TemperatureScale Gpu { get; } = new(75, 88);

    /// <summary>
    /// Which band a reading falls in. A missing reading is <see cref="TemperatureBand.Normal"/>:
    /// the tile hides itself when there is nothing to show, and colouring an absent number would
    /// be inventing a fact about a machine that never answered.
    /// </summary>
    public TemperatureBand Band(int? celsius) => celsius switch
    {
        null => TemperatureBand.Normal,
        >= 0 and var c when c >= Hot => TemperatureBand.Hot,
        >= 0 and var c when c >= Warm => TemperatureBand.Warm,
        _ => TemperatureBand.Normal,
    };
}
