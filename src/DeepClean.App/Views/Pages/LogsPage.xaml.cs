using System.Windows.Controls;
using DeepClean.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace DeepClean.App.Views.Pages;

/// <summary>Shows where DeepClean keeps its data and what it recently recorded.</summary>
public partial class LogsPage : UserControl, INavigableView<LogsViewModel>, INavigationAware
{
    public LogsPage(LogsViewModel viewModel)
    {
        ViewModel = viewModel;

        InitializeComponent();

        DataContext = viewModel;
    }

    public LogsViewModel ViewModel { get; }

    /// <summary>The newest lines only exist on disk, so the list is rebuilt on every visit.</summary>
    public Task OnNavigatedToAsync()
    {
        ViewModel.RefreshCommand.Execute(null);
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}
