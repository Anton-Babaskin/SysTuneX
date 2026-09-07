using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using SysTuneX.App.ViewModels;
using Xunit;

namespace SysTuneX.App.Tests;

/// <summary>
/// The game mode card is rebuilt by an event that never arrives on the UI thread.
///
/// <c>GameModeService</c> raises <c>Changed</c> after <c>await _gate.WaitAsync().ConfigureAwait(false)</c>,
/// so the continuation is on a thread-pool thread even when the user pressed the switch by hand;
/// the schedule and the game watcher tick on their own timers to begin with. That was harmless
/// while the handler only assigned scalar properties - the binding engine marshals those. It
/// stopped being harmless when the card gained a bound collection.
///
/// The failure was nastier than a crash. The throw lands in the service's own catch, so game mode
/// really came on - services stopped, power scheme switched - while the user was shown an error
/// message saying it had not.
/// </summary>
[Collection(WpfApplicationCollection.Name)]
public sealed class GameModeCardThreadingTests(WpfApplicationFixture host)
{
    [Fact]
    public void The_card_is_rebuilt_safely_when_the_news_arrives_off_the_ui_thread()
    {
        if (!host.IsSupported)
        {
            return;
        }

        DashboardViewModel? viewModel = null;

        host.OnUiThread(() =>
        {
            viewModel = App.Services.GetRequiredService<DashboardViewModel>();

            // A collection view is what gives the collection its thread affinity, and a binding is
            // what creates one. Without this the collection would accept changes from any thread
            // and the test would pass whether the fix were present or not.
            _ = CollectionViewSource.GetDefaultView(viewModel.GameModeChanges);
        });

        Exception? failure = Record.Exception(
            () => Task.Run(() => viewModel!.RefreshGameMode()).GetAwaiter().GetResult());

        Assert.True(
            failure is null,
            $"Rebuilding the card off the UI thread threw {failure?.GetType().Name}: {failure?.Message}");
    }

    /// <summary>
    /// Called on the UI thread it must still do the work, rather than posting it somewhere and
    /// leaving the card stale - a "fix" that posted unconditionally would pass the test above.
    /// </summary>
    [Fact]
    public void Called_on_the_ui_thread_it_updates_in_place()
    {
        if (!host.IsSupported)
        {
            return;
        }

        host.OnUiThread(() =>
        {
            var viewModel = App.Services.GetRequiredService<DashboardViewModel>();

            viewModel.RefreshGameMode();

            // Game mode is off in a test run, so the card shows its explanation rather than a list.
            Assert.Empty(viewModel.GameModeChanges);
            Assert.False(viewModel.IsGameModeOn);
            Assert.NotEqual(string.Empty, viewModel.GameModeDetail);
        });
    }
}
