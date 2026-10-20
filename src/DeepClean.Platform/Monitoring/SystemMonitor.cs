using System.Diagnostics;
using System.Runtime.InteropServices;
using DeepClean.Core.Configuration;
using DeepClean.Core.Monitoring;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace DeepClean.Platform.Monitoring;

/// <summary>
/// Samples the machine without requiring administrator rights.
/// <list type="bullet">
///   <item>CPU load and per process usage are derived from process time deltas.</item>
///   <item>Physical memory comes from <c>GlobalMemoryStatusEx</c>.</item>
///   <item>Temperatures and GPU data come from LibreHardwareMonitor when the
///         hardware allows it; otherwise those values stay <c>null</c>.</item>
/// </list>
/// </summary>
public sealed class SystemMonitor : ISystemMonitor
{
    private const string CpuPackageSensor = "CPU Package";
    private const string GpuCoreSensor = "GPU Core";

    private readonly ILogger<SystemMonitor> _logger;
    private readonly ISettingsStore _settings;
    private readonly object _gate = new();

    private Computer? _computer;
    private bool _gpuEnabled;
    private bool _configurationAttempted;

    private Dictionary<int, TimeSpan> _previousCpuTimes = new();
    private DateTime _previousSampleUtc;
    private bool _disposed;

    public SystemMonitor(ILogger<SystemMonitor> logger, ISettingsStore settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public bool SensorHardwareAvailable { get; private set; }

    public string SensorStatus { get; private set; } = "Hardware sensors have not been initialised yet.";

    public SystemSample Sample()
    {
        lock (_gate)
        {
            var nowUtc = DateTime.UtcNow;
            var now = DateTime.Now;

            EnsureConfiguration();

            var memory = ReadMemory();

            double? cpuTemperature = null;
            double? gpuLoad = null;
            double? gpuTemperature = null;
            ulong gpuMemoryUsed = 0;

            // The very first pass only establishes the baseline for process time deltas.
            var cpuLoad = SampleProcesses(nowUtc, out var processes);

            if (_computer is not null)
            {
                ReadSensors(out cpuTemperature, out gpuLoad, out gpuTemperature, out gpuMemoryUsed);
            }

            UpdateSensorStatus(cpuTemperature, gpuLoad);

            return new SystemSample(
                Timestamp: now,
                CpuLoadPercent: cpuLoad,
                CpuTemperatureCelsius: cpuTemperature,
                GpuLoadPercent: gpuLoad,
                GpuTemperatureCelsius: gpuTemperature,
                GpuMemoryUsedBytes: gpuMemoryUsed,
                MemoryUsedBytes: memory.Used,
                MemoryTotalBytes: memory.Total,
                SensorHardwareAvailable: SensorHardwareAvailable,
                Processes: processes);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _computer?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Closing the hardware monitor failed");
            }

            _computer = null;
        }
    }

    private void EnsureConfiguration()
    {
        var wantGpu = _settings.Current.EnableGpuMonitoring;

        if (_configurationAttempted && _gpuEnabled == wantGpu)
        {
            return;
        }

        _configurationAttempted = true;
        _gpuEnabled = wantGpu;

        try
        {
            _computer?.Close();

            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsMemoryEnabled = true,
                IsGpuEnabled = wantGpu,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsStorageEnabled = false,
                IsNetworkEnabled = false,
            };

            _computer.Open();
            SensorHardwareAvailable = true;
            _logger.LogInformation("Hardware sensors opened (GPU sensors: {GpuEnabled})", wantGpu);
        }
        catch (Exception ex)
        {
            _computer = null;
            SensorHardwareAvailable = false;
            SensorStatus = "Hardware sensors could not be opened. Temperature and GPU readings are hidden.";
            _logger.LogWarning(ex, "LibreHardwareMonitor could not be initialised");
        }
    }

    private void UpdateSensorStatus(double? cpuTemperature, double? gpuLoad)
    {
        if (!SensorHardwareAvailable)
        {
            return;
        }

        if (!_gpuEnabled)
        {
            SensorStatus = cpuTemperature is null
                ? "Running without elevated rights: CPU temperature is unavailable."
                : "Sensors active. GPU monitoring is disabled in settings.";
            return;
        }

        if (cpuTemperature is null && gpuLoad is null)
        {
            SensorStatus = "Restart DeepClean as administrator to read CPU and GPU sensors.";
            return;
        }

        SensorStatus = "Sensors active.";
    }

    private void ReadSensors(
        out double? cpuTemperature,
        out double? gpuLoad,
        out double? gpuTemperature,
        out ulong gpuMemoryUsed)
    {
        double? cpuPackage = null;
        double? cpuMax = null;
        double? gpuCoreSelection = null;
        double? gpuMax = null;
        double? gpuCoreTemperature = null;
        double? gpuMaxTemperature = null;
        double? gpuMemoryMegabytes = null;
        double? gpuMemoryGigabytes = null;

        try
        {
            foreach (var hardware in _computer!.Hardware)
            {
                Collect(hardware);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Reading hardware sensors failed");
        }

        cpuTemperature = cpuPackage ?? cpuMax;
        gpuLoad = gpuCoreSelection ?? gpuMax;
        gpuTemperature = gpuCoreTemperature ?? gpuMaxTemperature;
        gpuMemoryUsed = gpuMemoryMegabytes is not null
            ? (ulong)(gpuMemoryMegabytes.Value * 1024 * 1024)
            : gpuMemoryGigabytes is not null
                ? (ulong)(gpuMemoryGigabytes.Value * 1024 * 1024 * 1024)
                : 0;

        return;

        void Collect(IHardware hardware)
        {
            try
            {
                hardware.Update();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Updating sensor group {Hardware} failed", hardware.Name);
            }

            var isCpu = hardware.HardwareType == HardwareType.Cpu;
            var isGpu = hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.Value is not { } value)
                {
                    continue;
                }

                if (isCpu && sensor.SensorType == SensorType.Temperature)
                {
                    if (sensor.Name.Contains(CpuPackageSensor, StringComparison.OrdinalIgnoreCase))
                    {
                        cpuPackage = Max(cpuPackage, value);
                    }
                    else if (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                    {
                        cpuMax = Max(cpuMax, value);
                    }
                }
                else if (isGpu && sensor.SensorType == SensorType.Load)
                {
                    if (sensor.Name.Equals(GpuCoreSensor, StringComparison.OrdinalIgnoreCase))
                    {
                        gpuCoreSelection = Max(gpuCoreSelection, value);
                    }
                    else if (!sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                    {
                        gpuMax = Max(gpuMax, value);
                    }
                }
                else if (isGpu && sensor.SensorType == SensorType.Temperature)
                {
                    if (sensor.Name.Equals(GpuCoreSensor, StringComparison.OrdinalIgnoreCase))
                    {
                        gpuCoreTemperature = Max(gpuCoreTemperature, value);
                    }
                    else
                    {
                        gpuMaxTemperature = Max(gpuMaxTemperature, value);
                    }
                }
                else if (isGpu && sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                {
                    if (sensor.SensorType == SensorType.SmallData)
                    {
                        gpuMemoryMegabytes = Max(gpuMemoryMegabytes, value);
                    }
                    else if (sensor.SensorType == SensorType.Data)
                    {
                        gpuMemoryGigabytes = Max(gpuMemoryGigabytes, value);
                    }
                }
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                Collect(subHardware);
            }
        }

        static double? Max(double? current, double candidate) =>
            current is null ? candidate : Math.Max(current.Value, candidate);
    }

    private (ulong Total, ulong Used) ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };

        if (!GlobalMemoryStatusEx(ref status))
        {
            return (0, 0);
        }

        return (status.TotalPhysical, status.TotalPhysical - status.AvailablePhysical);
    }

    /// <summary>
    /// Samples every process and returns the machine wide CPU load together with the
    /// per process usage. Returns 0 on the first pass because deltas need two samples.
    /// </summary>
    private double SampleProcesses(DateTime nowUtc, out IReadOnlyList<ProcessSnapshot> processes)
    {
        var elapsedSeconds = (nowUtc - _previousSampleUtc).TotalSeconds;
        var processorCount = Environment.ProcessorCount;
        var hasBaseline = _previousSampleUtc != default && elapsedSeconds > 0.05;

        var results = new List<ProcessSnapshot>(256);
        var nextCpuTimes = new Dictionary<int, TimeSpan>(_previousCpuTimes.Count);
        var totalSeconds = 0.0;

        Process[] allProcesses;

        try
        {
            allProcesses = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Enumerating processes failed");
            processes = results;
            return 0;
        }

        foreach (var process in allProcesses)
        {
            try
            {
                var id = process.Id;

                // Idle (0) accumulates all unused processor time and would skew the total.
                if (id <= 0)
                {
                    continue;
                }

                var cpuTime = process.TotalProcessorTime;
                nextCpuTimes[id] = cpuTime;

                double cpuPercent = 0;

                if (hasBaseline && _previousCpuTimes.TryGetValue(id, out var previous))
                {
                    var delta = (cpuTime - previous).TotalSeconds;

                    if (delta > 0)
                    {
                        cpuPercent = delta / (elapsedSeconds * processorCount) * 100.0;
                        totalSeconds += delta;
                    }
                }

                long workingSet;

                try
                {
                    workingSet = process.WorkingSet64;
                }
                catch (Exception)
                {
                    workingSet = 0;
                }

                results.Add(new ProcessSnapshot(id, process.ProcessName, Math.Round(cpuPercent, 1), workingSet));
            }
            catch (Exception)
            {
                // Protected or already exited processes cannot be inspected; skip them.
            }
            finally
            {
                process.Dispose();
            }
        }

        _previousCpuTimes = nextCpuTimes;
        _previousSampleUtc = nowUtc;

        processes = results;

        if (!hasBaseline)
        {
            return 0;
        }

        var totalCpu = totalSeconds / (elapsedSeconds * processorCount) * 100.0;
        return Math.Clamp(Math.Round(totalCpu, 1), 0, 100);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
