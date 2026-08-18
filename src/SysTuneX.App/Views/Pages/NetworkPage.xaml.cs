using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views.Pages;

public partial class NetworkPage : SysTuneXPage
{
    public NetworkPage(NetworkViewModel viewModel)
    {
        ViewModel = viewModel;
        Bind(viewModel);
        InitializeComponent();
    }

    public NetworkViewModel ViewModel { get; }
}
