namespace DeepClean.Core.Performance;

/// <summary>Power and background-application behavior requested by the user.</summary>
public enum PerformanceMode
{
    Normal,
    Gaming,
    PowerSaving,
}

/// <summary>A desktop application or background process that can be approved for a graceful close.</summary>
public sealed record BackgroundApplication(string ProcessName, string DisplayName, int ProcessCount, bool IsBackground);

public sealed record PerformanceModeResult(bool Succeeded, string Message, PerformanceMode ActiveMode);

/// <summary>Applies reversible power-plan changes and user-approved graceful app closes.</summary>
public interface IPerformanceModeService
{
    Task<IReadOnlyList<BackgroundApplication>> GetBackgroundApplicationsAsync(CancellationToken cancellationToken = default);

    Task<PerformanceModeResult> ApplyAsync(
        PerformanceMode mode,
        IReadOnlyCollection<string> approvedApps,
        CancellationToken cancellationToken = default);
}
