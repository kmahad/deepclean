using System.Windows;
using DeepClean.Core.Configuration;
using DeepClean.Core.Monitoring;
using Microsoft.Extensions.Logging;

namespace DeepClean.App.Services;

/// <summary>
/// Runs the hardware sampling loop on a background thread and publishes results on
/// the UI thread. Sampling pauses naturally while the UI is busy, which keeps the
/// application from queueing work it cannot display.
/// </summary>
public sealed class MonitoringService : IDisposable, IAsyncDisposable
{
    private readonly ISystemMonitor _monitor;
    private readonly ISettingsStore _settings;
    private readonly ILogger<MonitoringService> _logger;

    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public MonitoringService(ISystemMonitor monitor, ISettingsStore settings, ILogger<MonitoringService> logger)
    {
        _monitor = monitor;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Raised on the UI thread after every sampling pass.</summary>
    public event EventHandler<SystemSample>? Sampled;

    public bool IsRunning => _loop is not null;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cancellation.Token));
        _logger.LogInformation("Monitoring started (interval {Interval} ms)", _settings.Current.MonitoringIntervalMs);
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _logger.LogInformation("Monitoring stopped");
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        Stop();

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "The monitoring loop ended with an error");
            }

            _loop = null;
        }

        _cancellation?.Dispose();
        _cancellation = null;
        _monitor.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var sample = _monitor.Sample();
                Publish(sample);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A sampling pass failed");
            }

            try
            {
                await Task.Delay(_settings.Current.MonitoringIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Publish(SystemSample sample)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Sampled?.Invoke(this, sample);
            return;
        }

        try
        {
            dispatcher.Invoke(() => Sampled?.Invoke(this, sample));
        }
        catch (Exception ex) when (ex is TaskCanceledException or InvalidOperationException)
        {
            // The application is shutting down; dropping the sample is expected.
        }
    }
}
