using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using Wpf.Ui.Controls;

namespace SysTuneX.App;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        RootNavigation.SetServiceProvider(serviceProvider);

        Loaded += (_, _) =>
        {
            RootNavigation.Navigate(typeof(DashboardPage));
        };
    }
}


