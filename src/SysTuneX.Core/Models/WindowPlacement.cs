namespace SysTuneX.Core.Models;

/// <summary>A rectangle in screen coordinates. Doubles because that is what WPF deals in.</summary>
public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

/// <summary>
/// Where a remembered window should actually open.
///
/// The failure this exists to stop: the compact readout is dragged onto a second monitor, the
/// monitor is unplugged, and the saved position now names a place no screen covers. The window
/// opens where it was told, which is nowhere the user can see, with no title bar to alt-drag back
/// and no taskbar button to right-click. It is simply gone.
///
/// Plain arithmetic, so it can be tested without a screen attached - which is the only way this
/// could ever have been tested, and why the arithmetic is here rather than in the code-behind.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// How much of the window's width has to fall on screen. A window a single pixel onto the
    /// desktop is technically visible and practically lost, so a corner is not enough.
    /// </summary>
    private const double MinimumVisible = 48;

    /// <summary>
    /// How much has to hang below the top edge to be grabbable. Smaller than the width rule
    /// because they are different questions: the width is about seeing the window at all, this is
    /// about having a strip of it under the pointer to drag.
    /// </summary>
    private const double MinimumGrab = 24;

    /// <summary>
    /// The position to open at: the remembered one when it is still reachable, otherwise a corner
    /// of <paramref name="screen"/>.
    /// </summary>
    /// <param name="saved">
    /// The remembered position. NaN in either coordinate means there is none, which is what a
    /// fresh install has and is a different thing from a remembered (0, 0).
    /// </param>
    public static (double Left, double Top) Resolve(
        (double Left, double Top)? saved,
        double width,
        double height,
        ScreenRect screen)
    {
        if (saved is not { } position ||
            double.IsNaN(position.Left) || double.IsNaN(position.Top) ||
            double.IsInfinity(position.Left) || double.IsInfinity(position.Top) ||
            !IsReachable(position.Left, position.Top, width, height, screen))
        {
            return DefaultCorner(width, height, screen);
        }

        return position;
    }

    /// <summary>
    /// Whether enough of the window falls inside the screen for the user to grab it. Measured
    /// against the whole desktop, so a position on a second monitor is fine while that monitor is
    /// still there.
    /// </summary>
    public static bool IsReachable(double left, double top, double width, double height, ScreenRect screen)
    {
        if (width <= 0 || height <= 0 || screen.Width <= 0 || screen.Height <= 0)
        {
            return false;
        }

        double visibleWidth = Math.Min(left + width, screen.Right) - Math.Max(left, screen.Left);

        // Capped by the screen as well as the window: on a display narrower than the rule, the
        // rule would otherwise decide that no position at all is reachable.
        if (visibleWidth < Math.Min(MinimumVisible, Math.Min(width, screen.Width)))
        {
            return false;
        }

        // Vertically it is the top edge that matters, because the grab handle is the strip along
        // the top. A window hanging off the bottom of the screen can still be dragged back; one
        // whose top edge is above the screen cannot be grabbed at all.
        return top >= screen.Top &&
               top <= screen.Bottom - Math.Min(MinimumGrab, Math.Min(height, screen.Height));
    }

    /// <summary>
    /// Top right, inset. Chosen because that is where a readout goes least in the way: the middle
    /// of the screen is the game, and the bottom is where most games put their own interface.
    ///
    /// A window that does not fit goes flush into the corner instead. Insetting it would push the
    /// part the user needs off the edge, which is the failure this whole file is about.
    /// </summary>
    private static (double Left, double Top) DefaultCorner(double width, double height, ScreenRect screen)
    {
        const double Margin = 24;

        double left = screen.Width >= width + (Margin * 2) ? screen.Right - width - Margin : screen.Left;
        double top = screen.Height >= height + (Margin * 2) ? screen.Top + Margin : screen.Top;

        return (left, top);
    }
}
