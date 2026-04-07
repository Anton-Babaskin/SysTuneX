using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SysTuneX.App;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    private readonly INavigationService _navigationService;

    public MainWindow(MainViewModel viewModel, INavigationService navigationService, IPageService pageService)
    {
        ViewModel = viewModel;
        _navigationService = navigationService;
        DataContext = this;
        InitializeComponent();

        RootNavigation.SetPageService(pageService);
        _navigationService.SetNavigationControl(RootNavigation);

        Loaded += (_, _) =>
        {
            _navigationService.Navigate(typeof(DashboardPage));
        };
    }
}
