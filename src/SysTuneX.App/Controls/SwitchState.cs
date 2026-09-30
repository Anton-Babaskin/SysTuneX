using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;

namespace SysTuneX.App.Controls;

/// <summary>
/// Keeps a switch showing what is true rather than what was clicked.
///
/// Every switch in this application that changes the machine is bound one-way to the state it
/// shows and runs a command when clicked. A toggle button flips itself on the click, before the
/// command has run. When the command then changes nothing - the confirmation was declined, the
/// write was refused, the service would not stop - the property the switch is bound to does not
/// change either, so nothing ever tells the switch to go back. It showed a tweak as on that was
/// off, and a service as running that was still disabled, until the page was rebuilt.
///
/// With this set, the switch reads its binding again once the command has finished, whatever the
/// command did. Fixing it here rather than in each command is the point: five commands would have
/// to remember, and the sixth would not.
/// </summary>
public static class SwitchState
{
    public static readonly DependencyProperty FollowsSourceProperty = DependencyProperty.RegisterAttached(
        "FollowsSource",
        typeof(bool),
        typeof(SwitchState),
        new PropertyMetadata(false, OnFollowsSourceChanged));

    public static bool GetFollowsSource(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(FollowsSourceProperty);
    }

    public static void SetFollowsSource(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FollowsSourceProperty, value);
    }

    private static void OnFollowsSourceChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ToggleButton toggle)
        {
            return;
        }

        toggle.Click -= OnClick;

        if ((bool)e.NewValue)
        {
            toggle.Click += OnClick;
        }
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleButton)sender;

        // The switch has flipped and the command has not run yet: a button runs its command straight
        // after raising Click. Queued behind that, so the command's task exists by the time this looks.
        toggle.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _ = ResyncAsync(toggle));
    }

    /// <summary>Waits for the switch's command to finish, then shows the state it is bound to.</summary>
    internal static async Task ResyncAsync(ToggleButton toggle)
    {
        ArgumentNullException.ThrowIfNull(toggle);

        if (toggle.Command is IAsyncRelayCommand { ExecutionTask: { } running })
        {
            try
            {
                await running.ConfigureAwait(true);
            }
            catch
            {
                // The command reports its own failures. This only puts the switch right.
            }
        }

        BindingOperations.GetBindingExpression(toggle, ToggleButton.IsCheckedProperty)?.UpdateTarget();
    }
}
