using System.Windows.Controls;
using DeepClean.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace DeepClean.App.Views.Pages;

/// <summary>Preferences that are persisted as soon as they change.</summary>
public partial class SettingsPage : UserControl, INavigableView<SettingsViewModel>
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;

        InitializeComponent();

        DataContext = viewModel;
    }

    public SettingsViewModel ViewModel { get; }
}
