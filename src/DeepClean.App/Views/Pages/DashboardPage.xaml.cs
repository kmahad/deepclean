using System.Windows.Controls;
using DeepClean.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace DeepClean.App.Views.Pages;

/// <summary>Live processor, graphics and memory usage for the whole machine.</summary>
public partial class DashboardPage : UserControl, INavigableView<DashboardViewModel>
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;

        InitializeComponent();

        DataContext = viewModel;
    }

    public DashboardViewModel ViewModel { get; }
}
