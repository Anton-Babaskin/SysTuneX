using System.Windows.Controls;
using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views;

public partial class NetworkPage : Page
{
    public NetworkViewModel ViewModel { get; }

    public NetworkPage(NetworkViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Initialize();
    }
}
