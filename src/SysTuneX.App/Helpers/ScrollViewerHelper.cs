using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SysTuneX.App.Helpers;

/// <summary>
/// Attached property that fixes mouse wheel scrolling on pages where WPF-UI's
/// NavigationView internal ScrollViewer marks PreviewMouseWheel as handled before
/// it reaches the page's own ScrollViewer. Uses AddHandler(handledEventsToo:true)
/// so the handler fires even when an ancestor already consumed the event.
/// </summary>
public static class ScrollViewerHelper
{
    public static readonly DependencyProperty FixMouseWheelProperty =
        DependencyProperty.RegisterAttached(
            "FixMouseWheel",
            typeof(bool),
            typeof(ScrollViewerHelper),
            new PropertyMetadata(false, OnFixMouseWheelChanged));

    public static bool GetFixMouseWheel(ScrollViewer sv) =>
        (bool)sv.GetValue(FixMouseWheelProperty);

    public static void SetFixMouseWheel(ScrollViewer sv, bool value) =>
        sv.SetValue(FixMouseWheelProperty, value);

    private static readonly MouseWheelEventHandler _handler = OnPreviewMouseWheel;

    private static void OnFixMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer sv)
        {
            if ((bool)e.NewValue)
                sv.AddHandler(UIElement.PreviewMouseWheelEvent, _handler, handledEventsToo: true);
            else
                sv.RemoveHandler(UIElement.PreviewMouseWheelEvent, _handler);
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }
}
