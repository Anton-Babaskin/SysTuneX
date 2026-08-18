using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views.Pages;

public partial class HistoryPage : SysTuneXPage
{
    public HistoryPage(HistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        Bind(viewModel);
        InitializeComponent();
    }

    public HistoryViewModel ViewModel { get; }
}
