using Xunit;
using System.Windows;
using System.Windows.Threading;
using SysTuneX.App;

namespace SysTuneX.App.Tests;

/// <summary>
/// Hosts one real <see cref="Application"/> on a dedicated STA thread with a running dispatcher.
///
/// WPF allows a single Application per AppDomain, so every test in the collection shares this
/// one and marshals its work onto the same thread. Construction failures are captured rather
/// than thrown, so a broken App.xaml produces a readable assertion instead of a fixture crash
/// that reports nothing.
/// </summary>
public sealed class WpfApplicationFixture : IDisposable
{
    private readonly Thread? _thread;
    private readonly Dispatcher? _dispatcher;

    public WpfApplicationFixture()
    {
        IsSupported = OperatingSystem.IsWindows();
        if (!IsSupported)
        {
            return;
        }

        // Deliberately not disposed: if the wait below times out, the host thread may still
        // be alive and about to signal it, and disposing underneath it would take the test
        // run down with an ObjectDisposedException on a thread nobody is watching.
        var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;

        _thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;

            try
            {
                // A pack URI that omits the assembly resolves against Application.ResourceAssembly,
                // which WPF fixes to the entry assembly - SysTuneX.exe when shipped, testhost.exe
                // here - and refuses to change afterwards. The app's URIs name their assembly, so
                // this is only a best-effort nudge for anything that does not.
                TryPointResourceAssemblyAtTheApp();

                var application = new App();

                // This is what the generated Main calls, and it is where App.xaml and every
                // merged dictionary are parsed. A bad resource dies right here.
                application.InitializeComponent();
            }
            catch (Exception exception)
            {
                StartupFailure = exception;
            }
            finally
            {
                ready.Set();
            }

            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "SysTuneX WPF test host",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(60)))
        {
            StartupFailure = new TimeoutException("The WPF application did not finish starting within 60 seconds.");
        }

        _dispatcher = dispatcher;
    }

    /// <summary>False off Windows, where the tests skip instead of failing.</summary>
    public bool IsSupported { get; }

    /// <summary>Whatever killed <c>InitializeComponent</c>, or null when the app came up.</summary>
    public Exception? StartupFailure { get; private set; }

    /// <summary>Runs <paramref name="action"/> on the UI thread and rethrows what it throws.</summary>
    public void OnUiThread(Action action)
    {
        // Without this, every later test reports a confusing NullReferenceException from
        // Application.Current instead of the startup failure that actually caused it.
        if (StartupFailure is not null)
        {
            throw new WpfHostException(StartupFailure);
        }

        if (_dispatcher is null)
        {
            throw new InvalidOperationException("The WPF test host is not running.");
        }

        Exception? failure = null;
        _dispatcher.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        if (failure is not null)
        {
            throw new WpfHostException(failure);
        }
    }

    public void Dispose() => _dispatcher?.InvokeShutdown();

    private static void TryPointResourceAssemblyAtTheApp()
    {
        try
        {
            if (!ReferenceEquals(Application.ResourceAssembly, typeof(App).Assembly))
            {
                Application.ResourceAssembly = typeof(App).Assembly;
            }
        }
        catch (InvalidOperationException)
        {
            // Already pinned to the runner by the time we got here. Harmless: every pack URI in
            // the app names its assembly, so none of them go looking through this property.
        }
    }
}

[CollectionDefinition(WpfApplicationCollection.Name)]
public sealed class WpfApplicationCollection : ICollectionFixture<WpfApplicationFixture>
{
    public const string Name = "WPF application";
}
