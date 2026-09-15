using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// A window that opens off the edge of the desktop is unrecoverable: no title bar to alt-drag, no
/// taskbar button to right-click. These are the cases that put it there.
/// </summary>
public sealed class WindowPlacementTests
{
    private static readonly ScreenRect Single = new(0, 0, 1920, 1080);

    /// <summary>Two monitors side by side; the right-hand one is the second.</summary>
    private static readonly ScreenRect Dual = new(0, 0, 3840, 1080);

    [Fact]
    public void A_position_still_on_screen_is_kept()
    {
        (double left, double top) = WindowPlacement.Resolve((400, 300), 300, 120, Single);

        Assert.Equal(400, left);
        Assert.Equal(300, top);
    }

    /// <summary>The whole point: the second monitor went away and the window has to come back.</summary>
    [Fact]
    public void A_position_on_a_monitor_that_is_gone_falls_back_to_a_corner()
    {
        (double left, double top) = WindowPlacement.Resolve((2600, 400), 300, 120, Single);

        Assert.True(
            WindowPlacement.IsReachable(left, top, 300, 120, Single),
            $"({left}, {top}) is not reachable on a single 1920x1080 screen.");
    }

    [Fact]
    public void The_same_position_is_kept_while_that_monitor_is_still_there()
    {
        (double left, double top) = WindowPlacement.Resolve((2600, 400), 300, 120, Dual);

        Assert.Equal(2600, left);
        Assert.Equal(400, top);
    }

    /// <summary>
    /// No remembered position is not the same as a remembered (0, 0). A fresh install gets the
    /// corner; somebody who deliberately dragged it to the top left keeps it.
    /// </summary>
    [Fact]
    public void No_remembered_position_opens_in_a_corner_not_at_the_origin()
    {
        (double left, double top) = WindowPlacement.Resolve(null, 300, 120, Single);

        Assert.NotEqual(0, left);
        Assert.True(left + 300 <= Single.Right, "The default corner hangs off the right edge.");
        Assert.True(top >= 0, "The default corner is above the top edge.");
    }

    [Fact]
    public void A_deliberate_top_left_is_kept()
    {
        Assert.Equal((0, 0), WindowPlacement.Resolve((0, 0), 300, 120, Single));
    }

    [Theory]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.NaN)]
    [InlineData(double.PositiveInfinity, 100)]
    public void A_settings_file_with_nonsense_in_it_still_opens_somewhere_visible(double left, double top)
    {
        (double resolvedLeft, double resolvedTop) = WindowPlacement.Resolve((left, top), 300, 120, Single);

        Assert.True(WindowPlacement.IsReachable(resolvedLeft, resolvedTop, 300, 120, Single));
    }

    /// <summary>
    /// Hanging off the bottom is recoverable - the grab handle is still on screen. Hanging off the
    /// top is not, which is why the two are not treated the same.
    /// </summary>
    [Fact]
    public void Dragged_below_the_bottom_edge_is_allowed_but_above_the_top_is_not()
    {
        Assert.True(WindowPlacement.IsReachable(400, 1040, 300, 120, Single));
        Assert.False(WindowPlacement.IsReachable(400, -80, 300, 120, Single));
    }

    [Fact]
    public void A_sliver_on_screen_does_not_count_as_reachable()
    {
        // Four pixels of a 300-wide window poking onto the desktop.
        Assert.False(WindowPlacement.IsReachable(-296, 400, 300, 120, Single));
    }

    /// <summary>
    /// A screen smaller than the minimum still has to place the window, or the rule meant to keep
    /// it reachable would decide nothing is.
    /// </summary>
    [Fact]
    public void A_window_larger_than_the_screen_is_still_placed()
    {
        var tiny = new ScreenRect(0, 0, 40, 30);

        (double left, double top) = WindowPlacement.Resolve(null, 300, 120, tiny);

        Assert.True(WindowPlacement.IsReachable(left, top, 300, 120, tiny));
    }
}
