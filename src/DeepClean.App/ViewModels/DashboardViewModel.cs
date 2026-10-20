using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DeepClean.App.Services;
using DeepClean.Core.Configuration;
using DeepClean.Core.Monitoring;
using DeepClean.Core.Utilities;

namespace DeepClean.App.ViewModels;

/// <summary>
/// Presents one process in the dashboard list. Values are pre-formatted so the view
/// does not need converters.
/// </summary>
public sealed class ProcessRow
{
    public ProcessRow(int processId, string name, double cpuPercent, long workingSetBytes)
    {
        ProcessId = processId;
        Name = name;
        CpuPercent = cpuPercent;
        WorkingSetBytes = workingSetBytes;
        CpuText = cpuPercent <= 0 ? "—" : cpuPercent.ToString("0.0", CultureInfo.CurrentCulture);
        MemoryText = SizeFormatter.FormatBytes(workingSetBytes);
    }

    public int ProcessId { get; }

    public string Name { get; }

    public double CpuPercent { get; }

    public long WorkingSetBytes { get; }

    public string CpuText { get; }

    public string MemoryText { get; }
}

/// <summary>
/// Live system overview. Subscribes to <see cref="MonitoringService"/> and keeps the
/// last minute of usage for the dashboard sparklines.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private const int HistoryCapacity = 60;
    private const int TopProcessCount = 12;

    private readonly MonitoringService _monitoring;
    private readonly ISystemMonitor _monitor;
    private readonly ISettingsStore _settings;

    private readonly CircularBuffer<double> _cpuSamples = new(HistoryCapacity);
    private readonly CircularBuffer<double> _gpuSamples = new(HistoryCapacity);
    private readonly CircularBuffer<double> _memorySamples = new(HistoryCapacity);

    private bool _disposed;

    public DashboardViewModel(MonitoringService monitoring, ISystemMonitor monitor, ISettingsStore settings)
    {
        _monitoring = monitoring;
        _monitor = monitor;
        _settings = settings;

        SensorStatus = monitor.SensorStatus;
        SensorsAvailable = monitor.SensorHardwareAvailable;
        ShowProcessListPanel = settings.Current.ShowProcessList;

        _monitoring.Sampled += OnSampled;
    }

    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    private string _cpuValueText = "—";

    [ObservableProperty]
    private string _cpuTemperatureText = "—";

    [ObservableProperty]
    private double _gpuUsage;

    [ObservableProperty]
    private string _gpuValueText = "—";

    [ObservableProperty]
    private string _gpuTemperatureText = "—";

    [ObservableProperty]
    private string _gpuMemoryText = "—";

    [ObservableProperty]
    private bool _gpuAvailable;

    [ObservableProperty]
    private double _memoryUsage;

    [ObservableProperty]
    private string _memoryValueText = "—";

    [ObservableProperty]
    private string _memoryDetailText = "—";

    [ObservableProperty]
    private int _processCount;

    [ObservableProperty]
    private string _updatedText = "Waiting for the first sample…";

    [ObservableProperty]
    private string _sensorStatus = string.Empty;

    [ObservableProperty]
    private bool _sensorsAvailable;

    [ObservableProperty]
    private bool _showProcessListPanel = true;

    [ObservableProperty]
    private IReadOnlyList<ProcessRow> _processes = Array.Empty<ProcessRow>();

    [ObservableProperty]
    private IReadOnlyList<double> _cpuHistory = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<double> _gpuHistory = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<double> _memoryHistory = Array.Empty<double>();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitoring.Sampled -= OnSampled;
    }

    private void OnSampled(object? sender, SystemSample sample)
    {
        CpuUsage = sample.CpuLoadPercent;
        CpuValueText = FormatPercent(sample.CpuLoadPercent);
        CpuTemperatureText = FormatTemperature(sample.CpuTemperatureCelsius);

        GpuAvailable = sample.GpuLoadPercent is not null || sample.GpuMemoryUsedBytes > 0;
        GpuUsage = sample.GpuLoadPercent ?? 0;
        GpuValueText = sample.GpuLoadPercent is null ? "—" : FormatPercent(sample.GpuLoadPercent.Value);
        GpuTemperatureText = FormatTemperature(sample.GpuTemperatureCelsius);
        GpuMemoryText = sample.GpuMemoryUsedBytes == 0
            ? "—"
            : SizeFormatter.FormatBytes(sample.GpuMemoryUsedBytes);

        MemoryUsage = sample.MemoryUsedPercent;
        MemoryValueText = FormatPercent(sample.MemoryUsedPercent);
        MemoryDetailText = string.Format(
            CultureInfo.CurrentCulture,
            "{0} of {1} in use",
            SizeFormatter.FormatBytes(sample.MemoryUsedBytes),
            SizeFormatter.FormatBytes(sample.MemoryTotalBytes));

        ProcessCount = sample.Processes.Count;
        UpdatedText = "Updated " + sample.Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

        SensorsAvailable = sample.SensorHardwareAvailable;
        SensorStatus = _monitor.SensorStatus;
        ShowProcessListPanel = _settings.Current.ShowProcessList;

        _cpuSamples.Add(sample.CpuLoadPercent);
        _gpuSamples.Add(sample.GpuLoadPercent ?? 0);
        _memorySamples.Add(sample.MemoryUsedPercent);

        CpuHistory = _cpuSamples.ToArray();
        GpuHistory = _gpuSamples.ToArray();
        MemoryHistory = _memorySamples.ToArray();

        Processes = BuildProcessRows(sample.Processes);
    }

    /// <summary>
    /// Only the busiest processes get a row object so the per second allocation stays small.
    /// </summary>
    private static IReadOnlyList<ProcessRow> BuildProcessRows(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return Array.Empty<ProcessRow>();
        }

        var candidates = new List<ProcessSnapshot>(snapshots);
        candidates.Sort(static (left, right) =>
        {
            var byCpu = right.CpuPercent.CompareTo(left.CpuPercent);
            return byCpu != 0 ? byCpu : right.WorkingSetBytes.CompareTo(left.WorkingSetBytes);
        });

        var count = Math.Min(candidates.Count, TopProcessCount);
        var rows = new ProcessRow[count];

        for (var i = 0; i < count; i++)
        {
            var snapshot = candidates[i];
            rows[i] = new ProcessRow(snapshot.ProcessId, snapshot.Name, snapshot.CpuPercent, snapshot.WorkingSetBytes);
        }

        return rows;
    }

    private static string FormatPercent(double value) =>
        value.ToString("0.0", CultureInfo.CurrentCulture) + " %";

    private static string FormatTemperature(double? celsius) =>
        celsius is null ? "—" : celsius.Value.ToString("0", CultureInfo.CurrentCulture) + " °C";
}
