using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepClean.Core;
using DeepClean.Core.Configuration;
using DeepClean.Platform.Security;
using DeepClean.Platform.Startup;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace DeepClean.App.ViewModels;

/// <summary>A named choice shown in a drop down.</summary>
public sealed record Option<T>(string Name, T Value);

/// <summary>
/// Settings backed by <see cref="ISettingsStore"/>. Every change is persisted
/// immediately, so there is no separate save step to forget.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IStartupRegistration _startup;
    private readonly IElevationService _elevation;
    private readonly ISnackbarService _snackbar;
    private readonly ILogger<SettingsViewModel> _logger;

    /// <summary>Suppresses the change handlers while the controls are being populated.</summary>
    private bool _isLoading;

    public SettingsViewModel(
        ISettingsStore settings,
        IStartupRegistration startup,
        IElevationService elevation,
        ISnackbarService snackbar,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _startup = startup;
        _elevation = elevation;
        _snackbar = snackbar;
        _logger = logger;

        ThemeOptions =
        [
            new Option<AppThemeMode>("Match Windows", AppThemeMode.System),
            new Option<AppThemeMode>("Dark", AppThemeMode.Dark),
            new Option<AppThemeMode>("Light", AppThemeMode.Light),
        ];

        IntervalOptions =
        [
            new Option<int>("Every 0.5 seconds (fastest)", 500),
            new Option<int>("Every second (balanced)", 1000),
            new Option<int>("Every 2 seconds (lighter)", 2000),
            new Option<int>("Every 5 seconds (lightest)", 5000),
        ];

        StartupBehaviorOptions =
        [
            new Option<StartupWindowBehavior>("Show the window", StartupWindowBehavior.ShowWindow),
            new Option<StartupWindowBehavior>("Start in the notification area", StartupWindowBehavior.StartMinimizedToTray),
        ];

        LogLevelOptions =
        [
            new Option<string>("Verbose", "Verbose"),
            new Option<string>("Debug", "Debug"),
            new Option<string>("Information", "Information"),
            new Option<string>("Warning", "Warning"),
            new Option<string>("Error", "Error"),
            new Option<string>("Fatal", "Fatal"),
        ];

        RetainedLogFilesOptions =
        [
            new Option<int>("3 days", 3),
            new Option<int>("7 days", 7),
            new Option<int>("14 days", 14),
            new Option<int>("30 days", 30),
        ];

        IsElevated = _elevation.IsElevated;
        ApplyFrom(_settings.Current);
    }

    public IReadOnlyList<Option<AppThemeMode>> ThemeOptions { get; }

    public IReadOnlyList<Option<int>> IntervalOptions { get; }

    public IReadOnlyList<Option<StartupWindowBehavior>> StartupBehaviorOptions { get; }

    public IReadOnlyList<Option<string>> LogLevelOptions { get; }

    public IReadOnlyList<Option<int>> RetainedLogFilesOptions { get; }

    public bool IsElevated { get; }

    public bool ShowElevationNotice => !IsElevated;

    public string DataFolderPath => AppPaths.RootDirectory;

    public string LogFolderPath => AppPaths.LogDirectory;

    [ObservableProperty]
    private Option<AppThemeMode>? _theme;

    [ObservableProperty]
    private Option<int>? _interval;

    [ObservableProperty]
    private Option<StartupWindowBehavior>? _startupBehavior;

    [ObservableProperty]
    private Option<string>? _logLevel;

    [ObservableProperty]
    private Option<int>? _retainedLogFiles;

    [ObservableProperty]
    private bool _enableGpuMonitoring;

    [ObservableProperty]
    private bool _showProcessList;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _enableNotifications;

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = AppSettings.CreateDefault();
        ApplyFrom(defaults);

        if (!_startup.TrySetEnabled(defaults.StartWithWindows, out var error))
        {
            _logger.LogWarning("The auto start entry could not be reset: {Error}", error);
        }

        Persist(settings => settings.CopyFrom(defaults));
        Notify("Settings", "Every setting was restored to its default value.", ControlAppearance.Success);
    }

    [RelayCommand]
    private void RestartAsAdministrator()
    {
        if (_elevation.IsElevated)
        {
            return;
        }

        if (_elevation.TryRestartElevated(out var error))
        {
            _logger.LogInformation("Restarting elevated at the request of the user");
            Application.Current?.Shutdown();
            return;
        }

        _logger.LogInformation("The elevated restart was not started: {Error}", error);
        Notify("Administrator rights", error ?? "The request was cancelled.", ControlAppearance.Caution);
    }

    [RelayCommand]
    private void OpenLogFolder() => OpenFolder(AppPaths.LogDirectory);

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(AppPaths.RootDirectory);

    partial void OnThemeChanged(Option<AppThemeMode>? value)
    {
        if (_isLoading || value is null)
        {
            return;
        }

        Persist(settings => settings.Theme = value.Value);
    }

    partial void OnIntervalChanged(Option<int>? value)
    {
        if (_isLoading || value is null)
        {
            return;
        }

        Persist(settings => settings.MonitoringIntervalMs = value.Value);
    }

    partial void OnStartupBehaviorChanged(Option<StartupWindowBehavior>? value)
    {
        if (_isLoading || value is null)
        {
            return;
        }

        Persist(settings => settings.StartupBehavior = value.Value);
    }

    partial void OnLogLevelChanged(Option<string>? value)
    {
        if (_isLoading || value is null)
        {
            return;
        }

        Persist(settings => settings.LogLevel = value.Value);
        Notify("Logging", "The new log level takes effect the next time DeepClean starts.", ControlAppearance.Info);
    }

    partial void OnRetainedLogFilesChanged(Option<int>? value)
    {
        if (_isLoading || value is null)
        {
            return;
        }

        Persist(settings => settings.RetainedLogFiles = value.Value);
        Notify("Logging", "The new retention period takes effect the next time DeepClean starts.", ControlAppearance.Info);
    }

    partial void OnEnableGpuMonitoringChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        Persist(settings => settings.EnableGpuMonitoring = value);
    }

    partial void OnShowProcessListChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        Persist(settings => settings.ShowProcessList = value);
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        Persist(settings => settings.CloseToTray = value);
    }

    partial void OnEnableNotificationsChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        Persist(settings => settings.EnableNotifications = value);
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        if (!_startup.TrySetEnabled(value, out var error))
        {
            _logger.LogWarning("The auto start entry could not be changed: {Error}", error);

            // Put the switch back where it was so the UI keeps matching reality.
            _isLoading = true;
            StartWithWindows = _startup.IsEnabled();
            _isLoading = false;

            Notify("Auto start", error ?? "The auto start entry could not be changed.", ControlAppearance.Danger);
            return;
        }

        Persist(settings => settings.StartWithWindows = value);
        Notify(
            "Auto start",
            value ? "DeepClean will start with Windows." : "DeepClean will no longer start with Windows.",
            ControlAppearance.Success);
    }

    private void ApplyFrom(AppSettings settings)
    {
        _isLoading = true;

        try
        {
            Theme = Find(ThemeOptions, settings.Theme);
            Interval = Find(IntervalOptions, settings.MonitoringIntervalMs) ?? IntervalOptions[1];
            StartupBehavior = Find(StartupBehaviorOptions, settings.StartupBehavior);
            LogLevel = Find(LogLevelOptions, settings.LogLevel) ?? LogLevelOptions[2];
            RetainedLogFiles = Find(RetainedLogFilesOptions, settings.RetainedLogFiles) ?? RetainedLogFilesOptions[1];

            EnableGpuMonitoring = settings.EnableGpuMonitoring;
            ShowProcessList = settings.ShowProcessList;
            StartWithWindows = settings.StartWithWindows;
            CloseToTray = settings.CloseToTray;
            EnableNotifications = settings.EnableNotifications;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static Option<T>? Find<T>(IReadOnlyList<Option<T>> options, T value)
    {
        foreach (var option in options)
        {
            if (EqualityComparer<T>.Default.Equals(option.Value, value))
            {
                return option;
            }
        }

        return null;
    }

    private void Persist(Action<AppSettings> mutate) => _ = PersistAsync(mutate);

    private async Task PersistAsync(Action<AppSettings> mutate)
    {
        try
        {
            await _settings.UpdateAsync(mutate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving the settings failed");
            Notify("Settings", "The change could not be saved: " + ex.Message, ControlAppearance.Danger);
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            AppPaths.EnsureDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Opening {Path} failed", path);
            Notify("Folder", "The folder could not be opened: " + ex.Message, ControlAppearance.Danger);
        }
    }

    private void Notify(string title, string message, ControlAppearance appearance)
    {
        if (!_settings.Current.EnableNotifications)
        {
            return;
        }

        try
        {
            _snackbar.Show(title, message, appearance, null!, TimeSpan.FromSeconds(4));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Showing a notification failed");
        }
    }
}
