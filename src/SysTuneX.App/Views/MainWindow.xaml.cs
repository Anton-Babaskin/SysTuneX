using System.ComponentModel;
using System.Windows;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.App.ViewModels;
using SysTuneX.App.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SysTuneX.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly IAppSettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly ITrayIconService _tray;

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationViewPageProvider pageProvider,
        INavigationService navigationService,
        ISnackbarService snackbarService,
        IContentDialogService dialogService,
        IAppSettingsService settings,
        ILocalizationService localization,
        ITrayIconService tray)
    {
        ViewModel = viewModel;
        _settings = settings;
        _localization = localization;
        _tray = tray;

        DataContext = viewModel;
        InitializeComponent();

        // Without a page provider, NavigationView tries to construct pages itself and every
        // page with an injected view model throws. This one line is what makes navigation work.
        RootNavigation.SetPageProviderService(pageProvider);
        navigationService.SetNavigationControl(RootNavigation);

        snackbarService.SetSnackbarPresenter(SnackbarHost);
        dialogService.SetDialogHost(DialogHost);

        ApplyWindowState();

        Loaded += OnLoaded;
        Closing += OnClosing;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public MainWindowViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Mica needs the window handle, so the backdrop is applied once the window exists.
        WindowBackdropType = _settings.Current.Backdrop;

        if (_settings.Current.Theme == ApplicationTheme.Unknown)
        {
            SystemThemeWatcher.Watch(this);
        }

        RootNavigation.Navigate(typeof(DashboardPage));
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _settings.Current.WindowMaximized = WindowState == WindowState.Maximized;

        if (WindowState == WindowState.Normal)
        {
            _settings.Current.WindowWidth = Width;
            _settings.Current.WindowHeight = Height;
        }

        // Hiding rather than exiting is only honest while there is a tray icon to get back
        // from. Without one the window would vanish with no way to reopen it.
        if (_settings.Current.MinimizeToTray && _tray.IsVisible)
        {
            e.Cancel = true;
            Hide();
        }
    }

    /// <summary>
    /// Navigation item captions come from a markup extension bound to the localization
    /// indexer, but the selected item's header is cached, so the pane is re-rendered by
    /// re-navigating to the current page.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (RootNavigation.SelectedItem?.TargetPageType is { } current)
        {
            Dispatcher.BeginInvoke(() => RootNavigation.Navigate(current));
        }
    }

    private void ApplyWindowState()
    {
        AppSettings settings = _settings.Current;

        if (settings.WindowWidth >= MinWidth)
        {
            Width = settings.WindowWidth;
        }

        if (settings.WindowHeight >= MinHeight)
        {
            Height = settings.WindowHeight;
        }

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }
}
