namespace DeepClean.Core.Monitoring;

/// <summary>A single process as reported by the system monitor.</summary>
public sealed record ProcessSnapshot(int ProcessId, string Name, double CpuPercent, long WorkingSetBytes);

/// <summary>Everything the monitor observed during one sampling pass.</summary>
public sealed record SystemSample(
    DateTime Timestamp,
    double CpuLoadPercent,
    double? CpuTemperatureCelsius,
    double? GpuLoadPercent,
    double? GpuTemperatureCelsius,
    ulong GpuMemoryUsedBytes,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    bool SensorHardwareAvailable,
    IReadOnlyList<ProcessSnapshot> Processes)
{
    public double MemoryUsedPercent => MemoryTotalBytes == 0
        ? 0
        : (double)MemoryUsedBytes / MemoryTotalBytes * 100.0;
}

/// <summary>
/// Samples CPU, GPU, memory and process usage. Implementations must never throw:
/// sensor access is best effort and unavailable values are reported as <c>null</c>.
/// </summary>
public interface ISystemMonitor : IDisposable
{
    /// <summary>
    /// True when temperature / GPU sensors are readable. The current process usually
    /// needs administrator rights for the full sensor set.
    /// </summary>
    bool SensorHardwareAvailable { get; }

    /// <summary>Human readable explanation shown in the UI when sensors are unavailable.</summary>
    string SensorStatus { get; }

    /// <summary>
    /// Performs one sampling pass. The first call after start-up cannot compute
    /// per-process CPU percentages yet and reports them as zero.
    /// </summary>
    SystemSample Sample();
}
