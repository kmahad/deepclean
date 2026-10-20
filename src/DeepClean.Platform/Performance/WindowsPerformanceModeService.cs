using System.Diagnostics;
using System.Text.RegularExpressions;
using DeepClean.Core.Performance;

namespace DeepClean.Platform.Performance;

/// <summary>Windows power-plan switching and graceful closing of approved desktop apps.</summary>
public sealed class WindowsPerformanceModeService : IPerformanceModeService, IDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private static readonly Regex ActiveSchemePattern = new(
        @"Power Scheme GUID:\s*\{?(?<guid>[0-9a-fA-F-]{36})\}?(?:\s*\((?<name>[^)]+)\))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string PowerSaverPlan = "a1841308-3541-4fab-bc81-f71556f20b4a";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _originalPlanId;
    private PerformanceMode _activeMode = PerformanceMode.Normal;
    private bool _disposed;

    public Task<IReadOnlyList<BackgroundApplication>> GetBackgroundApplicationsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var apps = new List<(string Name, bool IsBackground)>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id <= 4 || process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    var fileName = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(fileName) || fileName.StartsWith(windowsPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    apps.Add((process.ProcessName, string.IsNullOrWhiteSpace(process.MainWindowTitle)));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Processes can exit or deny access while the list is being collected.
                }
            }
        }

        IReadOnlyList<BackgroundApplication> applications = apps
            .GroupBy(a => a.Name)
            .Select(group => new BackgroundApplication(
                group.Key,
                group.Key,
                group.Count(),
                group.First().IsBackground))
            .OrderBy(app => app.IsBackground)
            .ThenBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return Task.FromResult(applications);
    }

    public async Task<PerformanceModeResult> ApplyAsync(
        PerformanceMode mode,
        IReadOnlyCollection<string> approvedApps,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approvedApps);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (mode == _activeMode)
            {
                var closedCount = mode == PerformanceMode.Gaming ? CloseApprovedApplications(approvedApps) : 0;
                var suffix = closedCount == 0
                    ? string.Empty
                    : $" Sent a close request to {closedCount} approved app{(closedCount == 1 ? "" : "s")}.";
                return new PerformanceModeResult(true, $"{mode} mode is already active.{suffix}", _activeMode);
            }

            var currentPlanId = await GetActivePlanIdAsync(cancellationToken).ConfigureAwait(false);
            var restorePlanId = _originalPlanId ?? currentPlanId;
            var targetPlanId = mode switch
            {
                PerformanceMode.Normal => _originalPlanId ?? currentPlanId,
                PerformanceMode.Gaming => HighPerformancePlan,
                PerformanceMode.PowerSaving => PowerSaverPlan,
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };

            if (!string.Equals(currentPlanId, targetPlanId, StringComparison.OrdinalIgnoreCase))
            {
                var setResult = await RunPowerCfgAsync(["/setactive", targetPlanId], cancellationToken).ConfigureAwait(false);
                if (setResult.ExitCode != 0)
                {
                    var message = mode == PerformanceMode.Normal
                        ? "Windows could not restore the power plan that was active before optimization. " + setResult.Error
                        : $"Windows could not switch to the {mode} power plan. The plan may not be available on this device. " + setResult.Error;
                    return new PerformanceModeResult(false, message.Trim(), _activeMode);
                }
            }

            if (mode == PerformanceMode.Normal)
            {
                _originalPlanId = null;
            }
            else if (_originalPlanId is null)
            {
                _originalPlanId = restorePlanId;
            }

            _activeMode = mode;

            if (mode == PerformanceMode.Gaming)
            {
                var closedCount = CloseApprovedApplications(approvedApps);
                var suffix = closedCount == 0
                    ? " No approved open apps needed closing."
                    : $" Sent a close request to {closedCount} approved app{(closedCount == 1 ? "" : "s")}.";
                return new PerformanceModeResult(true, "Gaming power plan enabled." + suffix, _activeMode);
            }

            var planName = mode == PerformanceMode.Normal ? "original" : "power-saving";
            return new PerformanceModeResult(true, $"Restored the {planName} power plan.", _activeMode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PerformanceModeResult(false, "The performance mode could not be applied: " + ex.Message, _activeMode);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private static int CloseApprovedApplications(IReadOnlyCollection<string> approvedApps)
    {
        if (approvedApps.Count == 0)
        {
            return 0;
        }

        var approved = new HashSet<string>(approvedApps, StringComparer.OrdinalIgnoreCase);
        var closeRequests = 0;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id != Environment.ProcessId
                        && approved.Contains(process.ProcessName)
                        && !string.IsNullOrWhiteSpace(process.MainWindowTitle)
                        && process.CloseMainWindow())
                    {
                        closeRequests++;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Closing is best-effort; never force-terminate a process.
                }
            }
        }

        return closeRequests;
    }

    private static async Task<string> GetActivePlanIdAsync(CancellationToken cancellationToken)
    {
        var result = await RunPowerCfgAsync(["/getactivescheme"], cancellationToken).ConfigureAwait(false);
        var match = ActiveSchemePattern.Match(result.Output);
        if (result.ExitCode != 0 || !match.Success)
        {
            throw new InvalidOperationException("Windows did not return the active power plan.");
        }

        return match.Groups["guid"].Value;
    }

    private static async Task<CommandResult> RunPowerCfgAsync(string[] arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Windows power configuration could not be started.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        return new CommandResult(process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}
