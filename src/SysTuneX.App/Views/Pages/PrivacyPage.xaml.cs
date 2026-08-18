using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views.Pages;

public partial class PrivacyPage : SysTuneXPage
{
    public PrivacyPage(PrivacyViewModel viewModel)
    {
        ViewModel = viewModel;
        Bind(viewModel);
        InitializeComponent();
    }

    public PrivacyViewModel ViewModel { get; }
}
