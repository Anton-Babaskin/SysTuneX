using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SysTuneX.App;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel, IPageService pageService)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        RootNavigation.SetPageService(pageService);

        Loaded += (_, _) =>
        {
            RootNavigation.Navigate(typeof(DashboardPage));
        };
    }
}

