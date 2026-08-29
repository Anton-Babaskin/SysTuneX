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
    private readonly IGlobalSearch _search;
    private readonly INavigationService _navigation;

    /// <summary>Results behind the suggestions currently on screen, keyed by their display text.</summary>
    private readonly Dictionary<string, SearchHit> _hits = new(StringComparer.Ordinal);

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationViewPageProvider pageProvider,
        INavigationService navigationService,
        ISnackbarService snackbarService,
        IContentDialogService dialogService,
        IAppSettingsService settings,
        ILocalizationService localization,
        ITrayIconService tray,
        IGlobalSearch search)
    {
        ViewModel = viewModel;
        _settings = settings;
        _localization = localization;
        _tray = tray;
        _search = search;
        _navigation = navigationService;

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


    private void OnSearchTextChanged(
        Wpf.Ui.Controls.AutoSuggestBox sender,
        Wpf.Ui.Controls.AutoSuggestBoxTextChangedEventArgs args)
    {
        // Only when the user typed. Reacting to programmatic changes would re-open the list
        // every time a chosen suggestion is written back into the box.
        if (args.Reason != Wpf.Ui.Controls.AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _hits.Clear();

        var suggestions = new List<string>();
        foreach (SearchHit hit in _search.Search(sender.Text))
        {
            // The category is part of the text so two settings with similar names stay
            // distinguishable, and because "which page is this on" is the question being asked.
            string label = $"{hit.Title}  ·  {hit.Category}";
            _hits[label] = hit;
            suggestions.Add(label);
        }

        sender.ItemsSource = suggestions;
    }

    private void OnSearchSuggestionChosen(
        Wpf.Ui.Controls.AutoSuggestBox sender,
        Wpf.Ui.Controls.AutoSuggestBoxSuggestionChosenEventArgs args) =>
        GoTo(args.SelectedItem as string);

    private void OnSearchSubmitted(
        Wpf.Ui.Controls.AutoSuggestBox sender,
        Wpf.Ui.Controls.AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // Enter with nothing selected takes the best match, which is what a search box that
        // ranks its results should do.
        GoTo(_hits.Keys.FirstOrDefault());
    }

    private void GoTo(string? label)
    {
        if (label is null || !_hits.TryGetValue(label, out SearchHit? hit))
        {
            return;
        }

        _navigation.Navigate(hit.PageType);

        // Put the item's own name in the destination page's filter, so it is the one row on
        // screen rather than one of thirty.
        if (!string.IsNullOrEmpty(hit.Filter) &&
            App.Services.GetService(hit.PageType) is FrameworkElement { DataContext: IFilterablePage page })
        {
            page.SearchText = hit.Filter;
        }

        SearchBox.Text = string.Empty;
        SearchBox.ItemsSource = null;
        _hits.Clear();
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
