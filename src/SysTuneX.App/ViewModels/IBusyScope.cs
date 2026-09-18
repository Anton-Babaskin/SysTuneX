namespace SysTuneX.App.ViewModels;

/// <summary>
/// Runs something slow with the page's spinner up and its cancellation token in hand.
///
/// A page view model owns the spinner, but the panels it is made of are the ones doing the slow
/// work. This is the whole of what such a panel needs from its page - not the page itself, which
/// would tie every panel to the thirteen dependencies of whichever page currently hosts it.
/// </summary>
public interface IBusyScope
{
    /// <summary>
    /// Runs <paramref name="operation"/> with <paramref name="message"/> shown, and clears the
    /// spinner afterwards whether it finished, threw or was cancelled.
    /// </summary>
    Task RunAsync(string message, Func<CancellationToken, Task> operation);

    /// <summary>Cut when the user leaves the page, so a scan started here stops with it.</summary>
    CancellationToken Token { get; }
}
