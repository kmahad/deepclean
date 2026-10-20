using System.Windows.Controls;
using DeepClean.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace DeepClean.App.Views.Pages;

/// <summary>
/// Shows files that hold the same contents. The scan only reads, nothing is picked for the user,
/// and every copy is re-checked before it is removed.
/// </summary>
public partial class DuplicatesPage : UserControl, INavigableView<DuplicatesViewModel>, INavigationAware
{
    public DuplicatesPage(DuplicatesViewModel viewModel)
    {
        ViewModel = viewModel;

        InitializeComponent();

        DataContext = viewModel;
    }

    public DuplicatesViewModel ViewModel { get; }

    public Task OnNavigatedToAsync() => Task.CompletedTask;

    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}
