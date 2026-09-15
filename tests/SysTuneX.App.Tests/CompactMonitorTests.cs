using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using SysTuneX.App.Services;
using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using Xunit;

namespace SysTuneX.App.Tests;

/// <summary>
/// The compact readout is the one window in the app that is not a page, so the page smoke tests do
/// not reach it. Everything they exist to catch - BAML that will not parse, a resource that only
/// resolves at first paint, a binding into a template - applies here just the same.
/// </summary>
[Collection(WpfApplicationCollection.Name)]
public sealed class CompactMonitorTests(WpfApplicationFixture host)
{
    /// <summary>
    /// Constructed through the container and laid out for real. Construction alone parses the
    /// BAML; only a measure and arrange pass instantiates the item template, which is where the
    /// tiles and their brushes actually resolve.
    /// </summary>
    [Fact]
    public void The_window_constructs_and_lays_out()
    {
        if (!host.IsSupported)
        {
            return;
        }

        host.OnUiThread(() =>
        {
            var window = App.Services.GetRequiredService<CompactMonitorWindow>();

            window.Measure(new Size(480, 200));
            window.Arrange(new Rect(0, 0, 480, 200));
            window.UpdateLayout();

            window.ViewModel.Dispose();
        });
    }

    /// <summary>
    /// It floats above other windows and stays out of the taskbar. Both are what make it usable
    /// next to a game rather than another window to alt-tab past, and both are one XAML attribute
    /// away from being lost in an edit.
    /// </summary>
    [Fact]
    public void The_window_stays_on_top_and_out_of_the_taskbar()
    {
        if (!host.IsSupported)
        {
            return;
        }

        host.OnUiThread(() =>
        {
            var window = App.Services.GetRequiredService<CompactMonitorWindow>();

            Assert.True(window.Topmost);
            Assert.False(window.ShowInTaskbar);
            Assert.Equal(WindowStyle.None, window.WindowStyle);

            window.ViewModel.Dispose();
        });
    }

    /// <summary>
    /// Sampling replaces the tiles in place rather than clearing and refilling them, so that a
    /// window sitting over a game does not make WPF rebuild every visual twice a second. The bug
    /// that shape of code invites is the readout growing a duplicate set of tiles on every tick.
    /// </summary>
    [Fact]
    public void Sampling_repeatedly_does_not_pile_up_tiles()
    {
        if (!host.IsSupported)
        {
            return;
        }

        host.OnUiThread(() =>
        {
            var viewModel = App.Services.GetRequiredService<CompactMonitorViewModel>();

            viewModel.Start();
            int first = viewModel.Tiles.Count;

            viewModel.Start();
            viewModel.Start();

            Assert.Equal(first, viewModel.Tiles.Count);

            viewModel.Dispose();
        });
    }

    /// <summary>
    /// Whatever the machine turns out to supply, no tile is drawn with nothing in it. An empty
    /// value next to a unit is the plausible-looking wrong reading this project refuses everywhere.
    /// </summary>
    [Fact]
    public void No_tile_is_drawn_without_a_value()
    {
        if (!host.IsSupported)
        {
            return;
        }

        host.OnUiThread(() =>
        {
            var viewModel = App.Services.GetRequiredService<CompactMonitorViewModel>();
            viewModel.Start();

            Assert.All(viewModel.Tiles, tile =>
            {
                Assert.False(string.IsNullOrWhiteSpace(tile.Value));
                Assert.False(string.IsNullOrWhiteSpace(tile.Label));
            });

            viewModel.Dispose();
        });
    }

    /// <summary>
    /// A closed readout must leave nothing running. The whole reason the window and its view model
    /// are transient rather than singletons is that a timer and a sensor sample outliving the
    /// window would be exactly the background work this app removes from other people's machines.
    /// </summary>
    [Fact]
    public void Each_open_gets_its_own_view_model()
    {
        if (!host.IsSupported)
        {
            return;
        }

        host.OnUiThread(() =>
        {
            var first = App.Services.GetRequiredService<CompactMonitorViewModel>();
            var second = App.Services.GetRequiredService<CompactMonitorViewModel>();

            Assert.NotSame(first, second);

            first.Dispose();
            second.Dispose();
        });
    }

    /// <summary>
    /// Nothing is open before anything opens it. Trivial, and it is the assertion that would catch
    /// a service that reported the state of a window it had already let go of.
    /// </summary>
    [Fact]
    public void The_service_reports_nothing_open_until_it_is()
    {
        if (!host.IsSupported)
        {
            return;
        }

        host.OnUiThread(() =>
        {
            var service = App.Services.GetRequiredService<ICompactMonitorService>();

            // The tests never show it, so this is the state it should be in throughout.
            Assert.False(service.IsOpen);
        });
    }
}
