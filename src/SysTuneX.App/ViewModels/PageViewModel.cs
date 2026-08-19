using CommunityToolkit.Mvvm.ComponentModel;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// Shared plumbing for the page view models: a busy flag with a caption, first-load tracking
/// and a cancellation token that is cut when the user leaves the page.
/// </summary>
public abstract partial class PageViewModel : ObservableObject
{
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

    /// <summary>Cancelled when the page is navigated away from, so long scans stop with it.</summary>
    protected CancellationToken PageToken => (_pageScope ??= new CancellationTokenSource()).Token;

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

    /// <summary>Runs an operation with the busy flag set, and always clears it again.</summary>
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
            // Leaving the page mid-operation is not an error.
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            Progress = -1;
        }
    }
}
