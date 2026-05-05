using System.Windows.Controls;
using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views;

public partial class StartupPage : Page
{
    public StartupViewModel ViewModel { get; }

    public StartupPage(StartupViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Initialize();
    }
}
