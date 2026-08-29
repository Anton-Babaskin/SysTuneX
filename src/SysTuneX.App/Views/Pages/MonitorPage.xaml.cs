using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views.Pages;

public partial class MonitorPage : SysTuneXPage
{
    public MonitorPage(MonitorViewModel viewModel)
    {
        ViewModel = viewModel;
        Bind(viewModel);
        InitializeComponent();
    }

    public MonitorViewModel ViewModel { get; }
}
