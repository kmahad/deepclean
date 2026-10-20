using System.Windows.Controls;
using DeepClean.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace DeepClean.App.Views.Pages;

/// <summary>
/// Shows what DeepClean can safely clean. The scan is read only, and everything
/// the page offers to remove is re-checked before it happens.
/// </summary>
public partial class CleanupPage : UserControl, INavigableView<CleanupViewModel>, INavigationAware
{
    public CleanupPage(CleanupViewModel viewModel)
    {
        ViewModel = viewModel;

        InitializeComponent();

        DataContext = viewModel;
    }

    public CleanupViewModel ViewModel { get; }

    /// <summary>The Recycle Bin changes outside the app, so its numbers are refreshed on every visit.</summary>
    public Task OnNavigatedToAsync()
    {
        ViewModel.RefreshRecycleBin();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}
