using System.Windows;
using DeepClean.App.Services;
using DeepClean.App.ViewModels;
using DeepClean.App.Views;
using DeepClean.App.Views.Pages;
using DeepClean.Core;
using DeepClean.Core.Cleaning;
using DeepClean.Core.Configuration;
using DeepClean.Core.Duplicates;
using DeepClean.Core.Logging;
using DeepClean.Core.Monitoring;
using DeepClean.Platform.Monitoring;
using DeepClean.Platform.Security;
using DeepClean.Platform.Startup;
using DeepClean.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Abstractions;

namespace DeepClean.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\DeepClean.SingleInstance";
    private const string StartInTrayArgument = "--tray";

    private Mutex? _instanceMutex;
    private ServiceProvider? _services;
    private ILoggerFactory? _loggerFactory;
    private ILogger<App>? _logger;
    private bool _isShuttingDown;
    private bool _isReportingException;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show(
                "DeepClean is already running. Look for its icon in the notification area.",
                "DeepClean",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        RegisterGlobalExceptionHandlers();

        try
        {
            StartApplication(e.Args);
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "DeepClean could not start");

            MessageBox.Show(
                $"DeepClean could not start.\n\n{ex.Message}",
                "DeepClean",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isShuttingDown = true;

        try
        {
            if (_services is not null)
            {
                var monitoring = _services.GetService<MonitoringService>();

                if (monitoring is not null)
                {
                    monitoring.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }

                var settings = _services.GetService<ISettingsStore>();

                if (settings is not null)
                {
                    settings.SaveAsync().GetAwaiter().GetResult();
                }
            }

            _logger?.LogInformation("DeepClean stopped");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Shutdown failed");
        }
        finally
        {
            if (_services is not null)
            {
                _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            _loggerFactory?.Dispose();
            _instanceMutex?.Dispose();
            _services = null;
            _loggerFactory = null;
        }

        base.OnExit(e);
    }

    private void StartApplication(string[] args)
    {
        // Log level and retention come from the settings file, so the bootstrap logger
        // starts with defaults and those two settings apply from the next launch.
        _loggerFactory = LogBootstrapper.CreateLoggerFactory(
            AppPaths.LogDirectory,
            "Information",
            AppSettings.CreateDefault().RetainedLogFiles);

        var services = new ServiceCollection();
        services.AddSingleton(_loggerFactory);
        services.AddLogging();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        _logger = _services.GetRequiredService<ILogger<App>>();

        var settingsStore = _services.GetRequiredService<ISettingsStore>();
        settingsStore.LoadAsync().GetAwaiter().GetResult();
        settingsStore.Changed += OnSettingsChanged;

        var elevation = _services.GetRequiredService<IElevationService>();

        _logger.LogInformation(
            "DeepClean {Version} starting (administrator: {IsElevated})",
            typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            elevation.IsElevated);

        ThemeApplier.Apply(settingsStore.Current.Theme);

        _services.GetRequiredService<MonitoringService>().Start();

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;

        var startInTray = args.Contains(StartInTrayArgument, StringComparer.OrdinalIgnoreCase)
            || settingsStore.Current.StartupBehavior == StartupWindowBehavior.StartMinimizedToTray;

        // Show() initialises the tray icon; hiding within the same dispatcher operation
        // means the window is never painted, so no flash is visible on start-up.
        window.Show();

        if (startInTray)
        {
            window.HideToTray();
        }
        else
        {
            window.Activate();
        }

        if (settingsStore.Current.StartWithWindows != _services.GetRequiredService<IStartupRegistration>().IsEnabled())
        {
            _logger.LogDebug("Auto start registration differs from the saved setting; syncing");
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ISystemMonitor, SystemMonitor>();
        services.AddSingleton<IElevationService, ElevationService>();
        services.AddSingleton<IStartupRegistration, RegistryStartupRegistration>();
        services.AddSingleton<MonitoringService>();

        services.AddSingleton<IFileRemover, RecycleBinFileRemover>();
        services.AddSingleton<ISystemCleaner, FileSystemCleaner>();
        services.AddSingleton<IRecycleBinService, RecycleBinService>();
        services.AddSingleton<IDuplicateFinder, FileDuplicateFinder>();
        services.AddSingleton<IDuplicateCleaner, DuplicateCleaner>();

        services.AddSingleton<INavigationViewPageProvider, PageProvider>();
        services.AddSingleton<INavigationService>(provider =>
            new NavigationService(provider.GetRequiredService<INavigationViewPageProvider>()));
        services.AddSingleton<ISnackbarService, SnackbarService>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<CleanupViewModel>();
        services.AddSingleton<DuplicatesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<LogsViewModel>();

        services.AddSingleton<DashboardPage>();
        services.AddSingleton<CleanupPage>();
        services.AddSingleton<DuplicatesPage>();
        services.AddSingleton<LogsPage>();
        services.AddSingleton<SettingsPage>();

        services.AddSingleton<MainWindow>();
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (_isShuttingDown)
        {
            return;
        }

        Dispatcher.Invoke(() => ThemeApplier.Apply(settings.Theme));
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            // A modal dialog runs its own message loop, so the very dispatch that failed can be
            // tried again while the dialog is up. Reporting that from here as well stacks dialogs
            // on top of each other until the stack runs out, which buries the first failure.
            // Only the first failure is reported; the rest are logged and dropped.
            if (_isReportingException)
            {
                _logger?.LogError(args.Exception, "Another unhandled UI exception occurred while one was being reported");
                args.Handled = true;
                return;
            }

            _isReportingException = true;

            try
            {
                _logger?.LogError(args.Exception, "Unhandled UI exception");

                MessageBox.Show(
                    $"Something went wrong:\n\n{args.Exception.Message}",
                    "DeepClean",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _isReportingException = false;
            }

            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger?.LogCritical(args.ExceptionObject as Exception, "Unhandled application exception");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.LogError(args.Exception, "Unobserved background task exception");
            args.SetObserved();
        };
    }
}
