using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace DeepClean.Core.Configuration;

/// <summary>
/// JSON backed settings store. Reads and writes are atomic: the new file is written
/// to a temporary location and then moved over the previous file, so a crash while
/// saving cannot leave a truncated settings file behind.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsStore(ILogger<JsonSettingsStore> logger, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? AppPaths.SettingsFilePath;
    }

    public AppSettings Current { get; private set; } = AppSettings.CreateDefault();

    public event EventHandler<AppSettings>? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No settings file found at {Path}; using defaults", _filePath);
                Current = AppSettings.CreateDefault();
                return;
            }

            await using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            var loaded = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (loaded is null)
            {
                throw new JsonException("Settings file contained no data.");
            }

            loaded.Normalize();
            Current = loaded;
            _logger.LogInformation("Settings loaded from {Path}", _filePath);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not read settings from {Path}; falling back to defaults", _filePath);
            Current = AppSettings.CreateDefault();
            TryPreserveCorruptFile();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task UpdateAsync(Action<AppSettings> mutate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        mutate(Current);
        Current.Normalize();
        return SaveAsync(cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppPaths.EnsureDirectory(Path.GetDirectoryName(_filePath)!);

            var tempPath = _filePath + ".tmp";

            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer
                    .SerializeAsync(stream, Current, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, overwrite: true);
            _logger.LogDebug("Settings saved to {Path}", _filePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save settings to {Path}", _filePath);
            return;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, Current);
    }

    private void TryPreserveCorruptFile()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var backup = _filePath + ".corrupt";
            File.Copy(_filePath, backup, overwrite: true);
            _logger.LogWarning("Previous settings file preserved as {Backup}", backup);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not preserve the unreadable settings file");
        }
    }
}
