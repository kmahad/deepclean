using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepClean.Core;
using DeepClean.Core.Configuration;
using DeepClean.Core.Monitoring;
using DeepClean.Core.Utilities;
using DeepClean.Platform.Security;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace DeepClean.App.ViewModels;

/// <summary>One log file on disk.</summary>
public sealed class LogFileRow
{
    public LogFileRow(string path, long sizeBytes, DateTime lastWriteTime)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        SizeBytes = sizeBytes;
        LastWriteTime = lastWriteTime;
    }

    public string Path { get; }

    public string Name { get; }

    public long SizeBytes { get; }

    public DateTime LastWriteTime { get; }

    public string Detail => string.Format(
        CultureInfo.CurrentCulture,
        "{0}  ·  {1}  ·  {2:yyyy-MM-dd HH:mm}",
        Name,
        SizeFormatter.FormatBytes(SizeBytes),
        LastWriteTime);
}

/// <summary>
/// Shows the diagnostics the user needs when something goes wrong: where the logs
/// live, what was recently written and how the application is running.
/// </summary>
public sealed partial class LogsViewModel : ObservableObject
{
    private const int PreviewLineCount = 300;
    private const int PreviewByteLimit = 256 * 1024;
    private const string LogSearchPattern = "deepclean-*.log";

    private readonly ISystemMonitor _monitor;
    private readonly ISettingsStore _settings;
    private readonly IElevationService _elevation;
    private readonly ISnackbarService _snackbar;
    private readonly ILogger<LogsViewModel> _logger;

    public LogsViewModel(
        ISystemMonitor monitor,
        ISettingsStore settings,
        IElevationService elevation,
        ISnackbarService snackbar,
        ILogger<LogsViewModel> logger)
    {
        _monitor = monitor;
        _settings = settings;
        _elevation = elevation;
        _snackbar = snackbar;
        _logger = logger;

        Refresh();
    }

    public string LogFolderPath => AppPaths.LogDirectory;

    public string DataFolderPath => AppPaths.RootDirectory;

    [ObservableProperty]
    private IReadOnlyList<LogFileRow> _logFiles = Array.Empty<LogFileRow>();

    [ObservableProperty]
    private LogFileRow? _selectedLogFile;

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private string _previewStatusText = "No log file selected.";

    [ObservableProperty]
    private string _diagnosticsText = string.Empty;

    [RelayCommand]
    private void Refresh()
    {
        LoadLogFiles();
        DiagnosticsText = BuildDiagnostics();
    }

    [RelayCommand]
    private void OpenLogFolder() => OpenFolder(AppPaths.LogDirectory);

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(AppPaths.RootDirectory);

    partial void OnSelectedLogFileChanged(LogFileRow? value) => LoadPreview(value);

    private void LoadLogFiles()
    {
        var selectedPath = SelectedLogFile?.Path;
        var rows = new List<LogFileRow>();

        try
        {
            var directory = new DirectoryInfo(AppPaths.LogDirectory);

            if (directory.Exists)
            {
                foreach (var file in directory.EnumerateFiles(LogSearchPattern))
                {
                    try
                    {
                        rows.Add(new LogFileRow(file.FullName, file.Length, file.LastWriteTime));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Reading the metadata of {File} failed", file.FullName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Listing the log directory failed");
        }

        rows.Sort(static (left, right) => right.LastWriteTime.CompareTo(left.LastWriteTime));
        LogFiles = rows;

        // Keep the previous selection when it still exists, otherwise show the newest file.
        SelectedLogFile = rows.Find(row => row.Path == selectedPath) ?? rows.FirstOrDefault();
    }

    private void LoadPreview(LogFileRow? row)
    {
        if (row is null)
        {
            PreviewText = string.Empty;
            PreviewStatusText = "No log file selected.";
            return;
        }

        try
        {
            PreviewText = ReadTail(row.Path, PreviewLineCount, PreviewByteLimit);
            PreviewStatusText = string.Format(
                CultureInfo.CurrentCulture,
                "Showing the most recent lines of {0}.",
                row.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading {File} failed", row.Path);
            PreviewText = string.Empty;
            PreviewStatusText = "The log file could not be read: " + ex.Message;
        }
    }

    /// <summary>
    /// Reads only the tail of the file so a large log does not have to be loaded whole.
    /// </summary>
    private static string ReadTail(string path, int maxLines, int maxBytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var start = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();

        if (start > 0)
        {
            // Drop the first line, which is almost certainly cut in half.
            var firstBreak = text.IndexOf('\n');

            if (firstBreak >= 0)
            {
                text = text[(firstBreak + 1)..];
            }
        }

        var lines = text.Split('\n');

        if (lines.Length > maxLines)
        {
            text = string.Join('\n', lines[^maxLines..]);
        }

        return text.TrimEnd('\r', '\n');
    }

    private string BuildDiagnostics()
    {
        var version = typeof(LogsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        var builder = new StringBuilder();
        builder.AppendLine("Application");
        builder.AppendLine($"  Version            : {version}");
        builder.AppendLine($"  Running as         : {(_elevation.IsElevated ? "Administrator" : "Standard user")}");
        builder.AppendLine($"  Framework          : .NET {Environment.Version}");
        builder.AppendLine($"  Operating system   : {RuntimeInformation.OSDescription}");
        builder.AppendLine($"  Processor count    : {Environment.ProcessorCount}");
        builder.AppendLine();
        builder.AppendLine("Monitoring");
        builder.AppendLine($"  Sample interval    : {_settings.Current.MonitoringIntervalMs} ms");
        builder.AppendLine($"  GPU sensors        : {(_settings.Current.EnableGpuMonitoring ? "enabled" : "disabled")}");
        builder.AppendLine($"  Hardware sensors   : {(_monitor.SensorHardwareAvailable ? "available" : "unavailable")}");
        builder.AppendLine($"  Sensor status      : {_monitor.SensorStatus}");
        builder.AppendLine();
        builder.AppendLine("Storage");
        builder.AppendLine($"  Data folder        : {AppPaths.RootDirectory}");
        builder.AppendLine($"  Settings file      : {AppPaths.SettingsFilePath}");
        builder.AppendLine($"  Log folder         : {AppPaths.LogDirectory}");
        builder.AppendLine($"  Log files kept     : {_settings.Current.RetainedLogFiles}");

        return builder.ToString();
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

            if (_settings.Current.EnableNotifications)
            {
                _snackbar.Show("Folder", "The folder could not be opened: " + ex.Message, ControlAppearance.Danger, null!, TimeSpan.FromSeconds(4));
            }
        }
    }
}
