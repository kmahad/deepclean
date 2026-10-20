using System.ComponentModel;
using System.Windows;
using DeepClean.App.ViewModels;
using DeepClean.App.Views.Pages;
using DeepClean.Core.Configuration;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace DeepClean.App.Views;

/// <summary>
/// The application shell: title bar, navigation, notifications and the tray icon.
/// Closing the window hides it to the notification area unless the user disabled
/// that behaviour or asked to exit.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly INavigationService _navigationService;
    private readonly INavigationViewPageProvider _pageProvider;
    private readonly IServiceProvider _services;
    private readonly ISettingsStore _settings;
    private readonly ILogger<MainWindow> _logger;

    private bool _allowClose;

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationService navigationService,
        INavigationViewPageProvider pageProvider,
        IServiceProvider services,
        ISettingsStore settings,
        ISnackbarService snackbarService,
        ILogger<MainWindow> logger)
    {
        _viewModel = viewModel;
        _navigationService = navigationService;
        _pageProvider = pageProvider;
        _services = services;
        _settings = settings;
        _logger = logger;

        InitializeComponent();

        DataContext = viewModel;

        RootNavigation.SetPageProviderService(_pageProvider);
        RootNavigation.SetServiceProvider(_services);
        _navigationService.SetNavigationControl(RootNavigation);
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);

        // The tray menu lives in its own popup, so it is pointed at the view model explicitly.
        if (TrayIcon.ContextMenu is { } trayMenu)
        {
            trayMenu.DataContext = viewModel;
        }

        _viewModel.ShowWindowRequested += OnShowWindowRequested;
        _viewModel.ExitRequested += OnExitRequested;

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>Hides the window and asks Windows to trim its working set.</summary>
    public void HideToTray()
    {
        try
        {
            WindowExtensions.Hide(this, enableEfficiencyMode: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hiding the window to the notification area failed");
            Hide();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        try
        {
            // The navigation control needs an applied template before it can show a page.
            _navigationService.Navigate(typeof(DashboardPage));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Showing the dashboard failed");
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_settings.Current.CloseToTray)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.ShowWindowRequested -= OnShowWindowRequested;
        _viewModel.ExitRequested -= OnExitRequested;

        try
        {
            TrayIcon.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disposing the tray icon failed");
        }
    }

    private void OnShowWindowRequested(object? sender, EventArgs e) => RestoreFromTray();

    private void OnExitRequested(object? sender, EventArgs e)
    {
        _allowClose = true;
        Application.Current?.Shutdown();
    }

    private void RestoreFromTray()
    {
        try
        {
            WindowExtensions.Show(this, disableEfficiencyMode: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restoring the window from the notification area failed");
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        Focus();
    }
}
