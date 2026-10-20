using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepClean.App.Services;
using DeepClean.App.Views.Pages;
using DeepClean.Core.Monitoring;
using DeepClean.Platform.Security;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace DeepClean.App.ViewModels;

/// <summary>
/// Backs the shell window: window title, tray menu commands and the tray tooltip.
/// Window specific behaviour (hiding, closing) stays in the window itself.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly MonitoringService _monitoring;
    private readonly ILogger<MainWindowViewModel> _logger;

    private bool _disposed;

    public MainWindowViewModel(
        INavigationService navigationService,
        MonitoringService monitoring,
        IElevationService elevation,
        ILogger<MainWindowViewModel> logger)
    {
        _navigationService = navigationService;
        _monitoring = monitoring;
        _logger = logger;

        IsElevated = elevation.IsElevated;
        VersionText = "Version " + (typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

        _monitoring.Sampled += OnSampled;
    }

    /// <summary>Raised when the user asks for the window from the tray.</summary>
    public event EventHandler? ShowWindowRequested;

    /// <summary>Raised when the user picks Exit in the tray menu.</summary>
    public event EventHandler? ExitRequested;

    public string ApplicationName => "DeepClean";

    public string VersionText { get; }

    public bool IsElevated { get; }

    public string ElevationText => IsElevated ? "Administrator" : "Standard user";

    [ObservableProperty]
    private string _trayToolTip = "DeepClean";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitoring.Sampled -= OnSampled;
    }

    [RelayCommand]
    private void ShowWindow() => ShowWindowRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenDashboard() => NavigateTo(typeof(DashboardPage));

    [RelayCommand]
    private void OpenCleanup() => NavigateTo(typeof(CleanupPage));

    [RelayCommand]
    private void OpenDuplicates() => NavigateTo(typeof(DuplicatesPage));

    [RelayCommand]
    private void OpenSettings() => NavigateTo(typeof(SettingsPage));

    [RelayCommand]
    private void OpenLogs() => NavigateTo(typeof(LogsPage));

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void NavigateTo(Type pageType)
    {
        ShowWindow();

        try
        {
            _navigationService.Navigate(pageType);
        }
        catch (Exception ex)
        {
            // Navigation can fail if the shell is not ready yet; the user can retry.
            _logger.LogWarning(ex, "Navigating to {Page} failed", pageType.Name);
        }
    }

    private void OnSampled(object? sender, SystemSample sample)
    {
        TrayToolTip = string.Format(
            CultureInfo.CurrentCulture,
            "DeepClean — CPU {0:0}% · RAM {1:0}%",
            sample.CpuLoadPercent,
            sample.MemoryUsedPercent);
    }
}
