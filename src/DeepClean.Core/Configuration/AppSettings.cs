namespace DeepClean.Core.Configuration;

/// <summary>Theme preference of the application window.</summary>
public enum AppThemeMode
{
    System,
    Dark,
    Light,
}

/// <summary>What the application should do right after it starts.</summary>
public enum StartupWindowBehavior
{
    ShowWindow,
    StartMinimizedToTray,
}

/// <summary>
/// User configurable application state. Persisted as JSON in <see cref="AppPaths.SettingsFilePath"/>.
/// </summary>
public sealed class AppSettings
{
    public AppThemeMode Theme { get; set; } = AppThemeMode.Dark;

    /// <summary>How often hardware sensors and processes are sampled, in milliseconds.</summary>
    public int MonitoringIntervalMs { get; set; } = 1000;

    /// <summary>When false only CPU, memory and processes are sampled (lower overhead).</summary>
    public bool EnableGpuMonitoring { get; set; } = true;

    /// <summary>Shows the top processes panel on the dashboard.</summary>
    public bool ShowProcessList { get; set; } = true;

    /// <summary>Registers the application in the current user's Run key.</summary>
    public bool StartWithWindows { get; set; }

    public StartupWindowBehavior StartupBehavior { get; set; } = StartupWindowBehavior.ShowWindow;

    /// <summary>Closing the window keeps the tray icon alive instead of exiting.</summary>
    public bool CloseToTray { get; set; } = true;

    public bool EnableNotifications { get; set; } = true;

    /// <summary>Serilog minimum level name. Takes effect after the next application start.</summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>Number of daily log files kept on disk.</summary>
    public int RetainedLogFiles { get; set; } = 7;

    /// <summary>
    /// Folders, and everything below them, that cleaning must never touch. Also used as the
    /// starting list in the cleanup page.
    /// </summary>
    public List<string> ExcludedPaths { get; set; } = [];

    /// <summary>
    /// Off by default. When on, cleaning deletes without going through the Recycle Bin, which
    /// means nothing can be recovered afterwards.
    /// </summary>
    public bool DeletePermanently { get; set; }

    /// <summary>Extra age floor applied to every cleaning rule, in days.</summary>
    public int MinimumJunkAgeDays { get; set; }

    public static AppSettings CreateDefault() => new();

    /// <summary>Clamps values into supported ranges so a hand edited file cannot break the app.</summary>
    public void Normalize()
    {
        MonitoringIntervalMs = Math.Clamp(MonitoringIntervalMs, 250, 10_000);
        RetainedLogFiles = Math.Clamp(RetainedLogFiles, 1, 30);
        MinimumJunkAgeDays = Math.Clamp(MinimumJunkAgeDays, 0, 365);

        ExcludedPaths ??= [];
        ExcludedPaths = [.. ExcludedPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase)];

        if (!Enum.IsDefined(Theme))
        {
            Theme = AppThemeMode.Dark;
        }

        if (!Enum.IsDefined(StartupBehavior))
        {
            StartupBehavior = StartupWindowBehavior.ShowWindow;
        }

        if (string.IsNullOrWhiteSpace(LogLevel))
        {
            LogLevel = "Information";
        }
    }

    public AppSettings Clone() => new()
    {
        Theme = Theme,
        MonitoringIntervalMs = MonitoringIntervalMs,
        EnableGpuMonitoring = EnableGpuMonitoring,
        ShowProcessList = ShowProcessList,
        StartWithWindows = StartWithWindows,
        StartupBehavior = StartupBehavior,
        CloseToTray = CloseToTray,
        EnableNotifications = EnableNotifications,
        LogLevel = LogLevel,
        RetainedLogFiles = RetainedLogFiles,
        ExcludedPaths = [.. ExcludedPaths],
        DeletePermanently = DeletePermanently,
        MinimumJunkAgeDays = MinimumJunkAgeDays,
    };

    public void CopyFrom(AppSettings other)
    {
        Theme = other.Theme;
        MonitoringIntervalMs = other.MonitoringIntervalMs;
        EnableGpuMonitoring = other.EnableGpuMonitoring;
        ShowProcessList = other.ShowProcessList;
        StartWithWindows = other.StartWithWindows;
        StartupBehavior = other.StartupBehavior;
        CloseToTray = other.CloseToTray;
        EnableNotifications = other.EnableNotifications;
        LogLevel = other.LogLevel;
        RetainedLogFiles = other.RetainedLogFiles;
        ExcludedPaths = [.. other.ExcludedPaths];
        DeletePermanently = other.DeletePermanently;
        MinimumJunkAgeDays = other.MinimumJunkAgeDays;
    }
}
