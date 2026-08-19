using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views.Pages;

public partial class SettingsPage : SysTuneXPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        Bind(viewModel);
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }
}
