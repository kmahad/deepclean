using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace DeepClean.Core.Logging;

/// <summary>
/// Creates the application wide <see cref="ILoggerFactory"/>. Log files are written to
/// <see cref="AppPaths.LogDirectory"/> and rolled once per day.
/// </summary>
public static class LogBootstrapper
{
    public static ILoggerFactory CreateLoggerFactory(string logDirectory, string minimumLevel, int retainedFileCount)
    {
        var level = ParseLevel(minimumLevel);

        AppPaths.EnsureDirectory(logDirectory);

        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .WriteTo.File(
                Path.Combine(logDirectory, "deepclean-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Clamp(retainedFileCount, 1, 30),
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        return new SerilogLoggerFactory(logger, dispose: true);
    }

    public static LogEventLevel ParseLevel(string? value) =>
        Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level) ? level : LogEventLevel.Information;
}
