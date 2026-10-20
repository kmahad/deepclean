using System.Windows.Controls;
using DeepClean.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace DeepClean.App.Views.Pages;

public partial class PerformancePage : UserControl, INavigableView<PerformanceViewModel>
{
    public PerformancePage(PerformanceViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    public PerformanceViewModel ViewModel { get; }
}
