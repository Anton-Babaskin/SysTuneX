using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views.Pages;

public partial class CleanupPage : SysTuneXPage
{
    public CleanupPage(CleanupViewModel viewModel)
    {
        ViewModel = viewModel;
        Bind(viewModel);
        InitializeComponent();
    }

    public CleanupViewModel ViewModel { get; }
}
