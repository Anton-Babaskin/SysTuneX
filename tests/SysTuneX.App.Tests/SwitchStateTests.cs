using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Controls;
using Xunit;

namespace SysTuneX.App.Tests;

/// <summary>
/// A switch that runs a command flips on the click, before the command has run. When the command
/// then changes nothing - a declined confirmation, a refused write - the property the switch is
/// bound to never changes, and nothing ever tells the switch to go back.
///
/// These click a real toggle button, the way a mouse does: it flips, raises Click, and runs its
/// command, in that order.
/// </summary>
[Collection(WpfApplicationCollection.Name)]
public sealed class SwitchStateTests(WpfApplicationFixture host)
{
    /// <summary>
    /// The control. Without the behaviour the switch keeps showing the click - which proves the
    /// setup here reproduces the fault, so the test after it is not passing by accident.
    /// </summary>
    [Fact]
    public void Without_it_a_switch_whose_command_changed_nothing_keeps_showing_the_click()
    {
        if (!host.IsSupported)
        {
            return;
        }

        ClickableSwitch? toggle = null;

        host.OnUiThread(() =>
        {
            toggle = Switch(new State(), new AsyncRelayCommand(() => Task.CompletedTask), follows: false);
            toggle.PerformClick();
        });

        host.OnUiThread(() => Assert.True(toggle!.IsChecked), DispatcherPriority.ContextIdle);
    }

    [Fact]
    public void A_switch_whose_command_changed_nothing_goes_back()
    {
        if (!host.IsSupported)
        {
            return;
        }

        ClickableSwitch? toggle = null;

        host.OnUiThread(() =>
        {
            toggle = Switch(new State(), new AsyncRelayCommand(() => Task.CompletedTask), follows: true);
            toggle.PerformClick();

            // What the user sees at the moment of the click.
            Assert.True(toggle.IsChecked);
        });

        host.OnUiThread(() => Assert.False(toggle!.IsChecked), DispatcherPriority.ContextIdle);
    }

    [Fact]
    public void A_switch_whose_command_did_the_change_stays_where_it_was_put()
    {
        if (!host.IsSupported)
        {
            return;
        }

        var state = new State();
        ClickableSwitch? toggle = null;

        host.OnUiThread(() =>
        {
            toggle = Switch(state, new AsyncRelayCommand(() =>
            {
                state.IsOn = true;
                return Task.CompletedTask;
            }), follows: true);

            toggle.PerformClick();
        });

        host.OnUiThread(() => Assert.True(toggle!.IsChecked), DispatcherPriority.ContextIdle);
    }

    /// <summary>
    /// A slow command - a PowerShell launch, a service that takes its time to stop - is waited for.
    /// Putting the switch back while the change is still under way would show off for something
    /// that is about to be on.
    /// </summary>
    [Fact]
    public void A_slow_command_is_waited_for_before_the_switch_is_put_right()
    {
        if (!host.IsSupported)
        {
            return;
        }

        var finish = new TaskCompletionSource();
        ClickableSwitch? toggle = null;

        host.OnUiThread(() =>
        {
            toggle = Switch(new State(), new AsyncRelayCommand(() => finish.Task), follows: true);
            toggle.PerformClick();
        });

        host.OnUiThread(() => Assert.True(toggle!.IsChecked), DispatcherPriority.ContextIdle);

        host.OnUiThread(() => finish.SetResult());

        host.OnUiThread(() => Assert.False(toggle!.IsChecked), DispatcherPriority.ContextIdle);
    }

    /// <summary>A refused change often surfaces as an exception; the switch still has to go back.</summary>
    [Fact]
    public void A_command_that_throws_still_puts_the_switch_right()
    {
        if (!host.IsSupported)
        {
            return;
        }

        ClickableSwitch? toggle = null;

        host.OnUiThread(() =>
        {
            // Flowed to the task rather than rethrown on the dispatcher, which is not what this is about.
            var command = new AsyncRelayCommand(
                () => Task.FromException(new InvalidOperationException("refused")),
                AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);

            toggle = Switch(new State(), command, follows: true);
            toggle.PerformClick();
        });

        host.OnUiThread(() => Assert.False(toggle!.IsChecked), DispatcherPriority.ContextIdle);
    }

    private static ClickableSwitch Switch(State state, IAsyncRelayCommand command, bool follows)
    {
        var toggle = new ClickableSwitch { DataContext = state, Command = command };
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(State.IsOn)) { Mode = BindingMode.OneWay });
        SwitchState.SetFollowsSource(toggle, follows);
        return toggle;
    }

    /// <summary>A toggle button that can be clicked from code the way the mouse clicks it.</summary>
    private sealed class ClickableSwitch : ToggleButton
    {
        public void PerformClick() => OnClick();
    }

    private sealed class State : INotifyPropertyChanged
    {
        private bool _isOn;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value)
                {
                    return;
                }

                _isOn = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
