using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views.Pages;

public partial class Windows11Page : SysTuneXPage
{
    public Windows11Page(Windows11ViewModel viewModel)
    {
        ViewModel = viewModel;
        Bind(viewModel);
        InitializeComponent();
    }

    public Windows11ViewModel ViewModel { get; }
}
