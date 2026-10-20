namespace DeepClean.Core.Configuration;

/// <summary>
/// Provides read/write access to the persisted <see cref="AppSettings"/> instance.
/// </summary>
public interface ISettingsStore
{
    /// <summary>The settings currently in use. Never null.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after <see cref="Current"/> was replaced or mutated and saved.</summary>
    event EventHandler<AppSettings>? Changed;

    /// <summary>
    /// Loads settings from disk. Falls back to defaults when the file is missing or unreadable.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Mutates and persists the settings in one step, then raises <see cref="Changed"/>.</summary>
    Task UpdateAsync(Action<AppSettings> mutate, CancellationToken cancellationToken = default);

    /// <summary>Persists the <see cref="Current"/> instance.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
