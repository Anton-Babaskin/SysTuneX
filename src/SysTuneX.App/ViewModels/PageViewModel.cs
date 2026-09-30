using CommunityToolkit.Mvvm.ComponentModel;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// Shared plumbing for the page view models: a busy flag with a caption, first-load tracking
/// and a cancellation token that is cut when the user leaves the page.
///
/// Two kinds of work run here, and they must not be confused. A read - a scan, a status refresh -
/// belongs to the page and stops when the user leaves it. A change to the machine does not belong
/// to the page at all: leaving must not stop it halfway.
/// </summary>
public abstract partial class PageViewModel : ObservableObject, IBusyScope
{
    /// <summary>
    /// One change to the machine at a time, across every page.
    ///
    /// Process-wide because what it guards is: the machine. A change outlives the page that started
    /// it, so without this a Restore All pressed on the dashboard could run alongside an Apply All
    /// still working through the gaming page - each correct on its own, and together leaving a
    /// machine that matches neither what the user asked for nor what the journal says. The second
    /// waits for the first instead, under its own spinner.
    /// </summary>
    private static readonly SemaphoreSlim ChangeGate = new(1, 1);

    private CancellationTokenSource? _pageScope;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    /// <summary>0-100 while a batch runs, or -1 for an indeterminate operation.</summary>
    [ObservableProperty]
    private double _progress = -1;

    [ObservableProperty]
    private bool _isInitialized;

    public bool IsIdle => !IsBusy;

    /// <summary>
    /// Cancelled when the page is navigated away from, so long scans stop with it. For reads only:
    /// nothing that changes the machine may be handed this.
    /// </summary>
    protected CancellationToken PageToken => (_pageScope ??= new CancellationTokenSource()).Token;

    CancellationToken IBusyScope.Token => PageToken;

    public async Task EnterAsync()
    {
        _pageScope?.Dispose();
        _pageScope = new CancellationTokenSource();

        try
        {
            await OnEnterAsync().ConfigureAwait(true);
            IsInitialized = true;
        }
        catch (OperationCanceledException)
        {
            // The user navigated away while the page was still loading.
        }
    }

    public async Task LeaveAsync()
    {
        try
        {
            if (_pageScope is not null)
            {
                await _pageScope.CancelAsync().ConfigureAwait(true);
            }

            await OnLeaveAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected: cancelling is the point.
        }
    }

    protected virtual Task OnEnterAsync() => Task.CompletedTask;

    protected virtual Task OnLeaveAsync() => Task.CompletedTask;

    Task IBusyScope.RunBusyAsync(string message, Func<CancellationToken, Task> operation) =>
        RunBusyAsync(message, operation);

    Task IBusyScope.RunChangeAsync(string message, Func<Task> change) =>
        RunChangeAsync(message, change);

    /// <summary>
    /// Runs a read with the busy flag set, and always clears it again. The read is handed the page's
    /// token and stops when the user leaves.
    /// </summary>
    protected async Task RunBusyAsync(string message, Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyMessage = message;
        Progress = -1;

        try
        {
            await operation(PageToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Leaving the page mid-read is not an error.
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            Progress = -1;
        }
    }

    /// <summary>
    /// Runs a change to the machine under the page's spinner - applying, reverting, restoring,
    /// cleaning - and lets it finish wherever the user goes meanwhile.
    ///
    /// It is handed no token, deliberately. Every one of these used to receive the page's token, and
    /// the page cancels that on the way out: press Apply All, click Dashboard to watch the score, and
    /// the batch stopped after whichever tweak it had reached. Nothing said so - a cancellation is
    /// not an error, so it was swallowed. Restore All stopped the same way, and so did turning game
    /// mode on, which is the one that could leave services stopped with nothing recording that game
    /// mode had started. The view models are singletons, so coming back to the page shows the
    /// spinner still turning and the result arriving when it is done.
    /// </summary>
    protected async Task RunChangeAsync(string message, Func<Task> change)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyMessage = message;
        Progress = -1;

        try
        {
            await RunItemChangeAsync(change).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            Progress = -1;
        }
    }

    /// <summary>
    /// The same, for a change to one row that shows its own spinner rather than the page's: one
    /// switch, one DNS setting, one app. Waits its turn behind any other change first.
    /// </summary>
    protected static async Task RunItemChangeAsync(Func<Task> change)
    {
        await ChangeGate.WaitAsync().ConfigureAwait(true);

        try
        {
            await change().ConfigureAwait(true);
        }
        finally
        {
            ChangeGate.Release();
        }
    }
}
