using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SysTuneX.App.Helpers;

/// <summary>
/// Attached property that fixes mouse wheel scrolling when inner controls
/// (ToggleSwitch, Card) consume the MouseWheel event before it reaches the ScrollViewer.
/// Uses PreviewMouseWheel (tunneling) to intercept and handle scroll before children.
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

    private static void OnFixMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer sv)
        {
            if ((bool)e.NewValue)
                sv.PreviewMouseWheel += OnPreviewMouseWheel;
            else
                sv.PreviewMouseWheel -= OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }
}
