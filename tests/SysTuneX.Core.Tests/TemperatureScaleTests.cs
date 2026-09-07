using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The bands decide what colour a temperature is drawn in, which makes them a claim about the
/// machine rather than a decoration. A threshold that fires too early trains the reader to ignore
/// the colour, and one that fires too late is worse than none at all.
/// </summary>
public sealed class TemperatureScaleTests
{
    [Theory]
    [InlineData(35, TemperatureBand.Normal)]
    [InlineData(79, TemperatureBand.Normal)]
    [InlineData(80, TemperatureBand.Warm)]      // the boundary belongs to the band it names
    [InlineData(94, TemperatureBand.Warm)]
    [InlineData(95, TemperatureBand.Hot)]
    [InlineData(103, TemperatureBand.Hot)]
    public void A_processor_is_read_against_its_own_throttling_point(int celsius, TemperatureBand expected)
    {
        Assert.Equal(expected, TemperatureScale.Cpu.Band(celsius));
    }

    [Theory]
    [InlineData(60, TemperatureBand.Normal)]
    [InlineData(74, TemperatureBand.Normal)]
    [InlineData(75, TemperatureBand.Warm)]
    [InlineData(87, TemperatureBand.Warm)]
    [InlineData(88, TemperatureBand.Hot)]
    public void A_graphics_card_throttles_earlier_and_is_read_earlier(int celsius, TemperatureBand expected)
    {
        Assert.Equal(expected, TemperatureScale.Gpu.Band(celsius));
    }

    /// <summary>
    /// The whole point of having two scales. 85 °C is an ordinary afternoon for a laptop CPU and
    /// a card that is already losing clocks - one shared threshold would have to lie about one
    /// of them.
    /// </summary>
    [Fact]
    public void The_same_reading_means_different_things_on_different_parts()
    {
        Assert.Equal(TemperatureBand.Warm, TemperatureScale.Cpu.Band(85));
        Assert.Equal(TemperatureBand.Warm, TemperatureScale.Gpu.Band(85));

        Assert.Equal(TemperatureBand.Warm, TemperatureScale.Cpu.Band(90));
        Assert.Equal(TemperatureBand.Hot, TemperatureScale.Gpu.Band(90));
    }

    /// <summary>
    /// A sensor that did not answer must not be coloured. The tile hides itself in that case, and
    /// painting an absent number would be inventing a fact about a machine that never replied -
    /// the one thing this project refuses everywhere else.
    /// </summary>
    [Fact]
    public void A_missing_reading_is_not_coloured()
    {
        Assert.Equal(TemperatureBand.Normal, TemperatureScale.Cpu.Band(null));
        Assert.Equal(TemperatureBand.Normal, TemperatureScale.Gpu.Band(null));
    }

    /// <summary>A negative reading is a broken sensor, not a cold one; it gets no colour either.</summary>
    [Fact]
    public void A_nonsense_reading_is_not_coloured()
    {
        Assert.Equal(TemperatureBand.Normal, TemperatureScale.Cpu.Band(-40));
    }

    [Fact]
    public void Warm_comes_before_hot_on_every_scale()
    {
        Assert.True(TemperatureScale.Cpu.Warm < TemperatureScale.Cpu.Hot);
        Assert.True(TemperatureScale.Gpu.Warm < TemperatureScale.Gpu.Hot);
    }
}
