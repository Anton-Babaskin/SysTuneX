using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SysTuneX.App.Services;
using SysTuneX.App.ViewModels;
using SysTuneX.Core.Models;

namespace SysTuneX.App.Views;

/// <summary>
/// The small readout that stays above other windows.
///
/// Not an overlay. Nothing is injected into the game, nothing hooks its swap chain and no driver
/// is loaded - it is an ordinary top-level window with its chrome removed. The cost of that choice
/// is real and worth stating plainly: a game running in exclusive full screen owns the display and
/// this window will not appear over it. Borderless windowed mode, which is what most people play
/// in now, works. That is the trade this project makes everywhere - nothing that an anti-cheat
/// could reasonably mistake for a cheat.
/// </summary>
public partial class CompactMonitorWindow : Window
{
    private readonly IAppSettingsService _settings;

    public CompactMonitorWindow(CompactMonitorViewModel viewModel, IAppSettingsService settings)
    {
        ViewModel = viewModel;
        _settings = settings;

        DataContext = viewModel;
        InitializeComponent();

        Opacity = ClampOpacity(settings.Current.CompactMonitor.Opacity);

        Loaded += OnLoaded;
        Closing += OnClosing;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    public CompactMonitorViewModel ViewModel { get; }

    /// <summary>
    /// Where the window should open, given what was remembered and what screens exist now.
    ///
    /// Read from the live desktop rather than a single monitor: a position on a second screen is
    /// perfectly good while that screen is plugged in, and the arithmetic that decides is in
    /// <see cref="WindowPlacement"/> where a test can reach it.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CompactMonitorSettings compact = _settings.Current.CompactMonitor;

        var desktop = new ScreenRect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        // ActualWidth is only real once the window has been laid out, which is why this runs from
        // Loaded rather than the constructor - SizeToContent means there is no width before then.
        (double left, double top) = WindowPlacement.Resolve(
            (compact.Left, compact.Top),
            ActualWidth,
            ActualHeight,
            desktop);

        Left = left;
        Top = top;

        ViewModel.Start();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        ViewModel.Stop();

        CompactMonitorSettings compact = _settings.Current.CompactMonitor;
        compact.Left = Left;
        compact.Top = Top;

        _ = _settings.SaveAsync();
    }

    /// <summary>
    /// Drag from anywhere. With no title bar there is nowhere else to grab, and a window the user
    /// cannot move is a window in the way of whatever they are doing.
    /// </summary>
    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove throws when the button is already up - a click that was too quick, or one
            // the close button handled first. Nothing to do and nothing worth reporting.
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Kept solid enough to find. A settings file saying zero would leave an invisible window with
    /// no taskbar button and no way to close it.
    /// </summary>
    private static double ClampOpacity(double value) =>
        double.IsNaN(value) ? 0.9 : Math.Clamp(value, 0.35, 1.0);
}
