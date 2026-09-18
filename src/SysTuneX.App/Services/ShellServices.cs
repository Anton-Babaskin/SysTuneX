using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SysTuneX.App.Services;

/// <summary>
/// Getting onto the user interface thread.
///
/// A seam rather than a convenience. Several view models reached for
/// <c>Application.Current.Dispatcher</c> directly, which is a static that exists only inside a
/// running WPF application - so the one piece of logic that most needed a test, "does this hop to
/// the UI thread before touching a bound collection", could only be checked by running the app.
/// That is not hypothetical: a bound collection mutated off the UI thread threw, the exception was
/// swallowed by a service's catch, and game mode came on for real while the screen showed an error
/// saying it had not.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>True when the caller is already on the interface thread.</summary>
    bool IsOnUiThread { get; }

    /// <summary>Queues work on the interface thread and returns immediately.</summary>
    void Post(Action action);

    /// <summary>Runs work on the interface thread, waiting if the caller is on another one.</summary>
    void Invoke(Action action);
}

/// <summary>Ending the session and bringing the window back.</summary>
public interface IAppLifetime
{
    /// <summary>Closes SysTuneX.</summary>
    void Shutdown();

    /// <summary>Shows and activates the main window, for the tray icon to call.</summary>
    void RestoreMainWindow();
}

/// <summary>
/// The window's theme and backdrop.
///
/// Here rather than in the settings view model because applying a backdrop is WPF-UI's business
/// and gets it wrong in an interesting way: changing the property makes it rebuild the window
/// chrome, and replacing one that already carries an inheritance context throws from inside WPF.
/// That put an unusable window in front of anyone who picked a backdrop, on every launch
/// afterwards. The refusal is handled in one place now instead of at each call site.
/// </summary>
public interface IWindowAppearance
{
    /// <summary>
    /// Applies a theme. <see cref="ApplicationTheme.Unknown"/> means follow Windows, which also
    /// starts the watcher; anything else stops it first, or the next time Windows changes its own
    /// theme the watcher would apply that over the top and the manual choice would stop holding.
    /// </summary>
    void ApplyTheme(ApplicationTheme theme);

    /// <summary>
    /// Applies a window backdrop, surviving WPF-UI refusing it.
    /// </summary>
    /// <returns>
    /// False when the change could not be made live. The caller says so; swallowing it silently
    /// leaves the user looking at a backdrop they did not pick with nothing to explain why.
    /// </returns>
    bool ApplyBackdrop(WindowBackdropType backdrop);
}

/// <inheritdoc cref="IUiDispatcher"/>
public sealed class UiDispatcher : IUiDispatcher
{
    private static Dispatcher? Current => Application.Current?.Dispatcher;

    public bool IsOnUiThread => Current?.CheckAccess() ?? false;

    public void Post(Action action)
    {
        Dispatcher? dispatcher = Current;

        // No application means a test host or a shutdown in progress. Running inline is the only
        // thing left to do and is what the caller would have got from a direct dispatcher call.
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    public void Invoke(Action action)
    {
        Dispatcher? dispatcher = Current;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}

/// <inheritdoc cref="IAppLifetime"/>
public sealed class AppLifetime : IAppLifetime
{
    private readonly IUiDispatcher _dispatcher;

    public AppLifetime(IUiDispatcher dispatcher) => _dispatcher = dispatcher;

    public void Shutdown() => _dispatcher.Invoke(() => Application.Current?.Shutdown());

    public void RestoreMainWindow() => _dispatcher.Invoke(() =>
    {
        if (Application.Current?.MainWindow is not { } window)
        {
            return;
        }

        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    });
}

/// <inheritdoc cref="IWindowAppearance"/>
public sealed class WindowAppearance : IWindowAppearance
{
    private readonly ILogger<WindowAppearance> _logger;

    public WindowAppearance(ILogger<WindowAppearance> logger) => _logger = logger;

    private static Window? MainWindow => Application.Current?.MainWindow;

    public void ApplyTheme(ApplicationTheme theme)
    {
        Window? window = MainWindow;

        if (theme == ApplicationTheme.Unknown)
        {
            ApplicationThemeManager.ApplySystemTheme();

            if (window is not null)
            {
                SystemThemeWatcher.Watch(window);
            }

            return;
        }

        // Stop following Windows before applying the choice. The watcher used to be left running,
        // so picking Dark by hand worked until the next time Windows changed its own theme - at
        // which point the watcher applied that over the top and the manual setting silently
        // stopped holding.
        if (window is not null)
        {
            SystemThemeWatcher.UnWatch(window);
        }

        ApplicationThemeManager.Apply(theme);
    }

    public bool ApplyBackdrop(WindowBackdropType backdrop)
    {
        if (MainWindow is not FluentWindow window || window.WindowBackdropType == backdrop)
        {
            return true;
        }

        try
        {
            window.WindowBackdropType = backdrop;
            return true;
        }
        catch (Exception ex)
        {
            // WPF-UI rebuilds the window chrome on this change, and replacing a WindowChrome that
            // already has an inheritance context throws from inside WPF. The preference is saved
            // either way; only the live change is lost, and it applies on the next launch.
            _logger.LogWarning(ex, "The {Backdrop} backdrop could not be applied live", backdrop);
            return false;
        }
    }
}
