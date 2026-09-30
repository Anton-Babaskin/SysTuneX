namespace SysTuneX.App.ViewModels;

/// <summary>
/// Runs something slow with the page's spinner up.
///
/// A page view model owns the spinner, but the panels it is made of are the ones doing the slow
/// work. This is the whole of what such a panel needs from its page - not the page itself, which
/// would tie every panel to the thirteen dependencies of whichever page currently hosts it.
/// </summary>
public interface IBusyScope
{
    /// <summary>
    /// Runs a read with <paramref name="message"/> shown, handing it the page's token, and clears
    /// the spinner afterwards whether it finished, threw or was cancelled.
    /// </summary>
    Task RunBusyAsync(string message, Func<CancellationToken, Task> operation);

    /// <summary>
    /// Runs a change to the machine with <paramref name="message"/> shown. It is handed no token:
    /// leaving the page must not stop a change halfway. One change runs at a time, app-wide.
    /// </summary>
    Task RunChangeAsync(string message, Func<Task> change);

    /// <summary>Cut when the user leaves the page, so a scan started here stops with it.</summary>
    CancellationToken Token { get; }
}
