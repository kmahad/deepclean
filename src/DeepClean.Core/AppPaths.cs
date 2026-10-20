namespace DeepClean.Core;

/// <summary>
/// Well known file system locations used by the application.
/// </summary>
public static class AppPaths
{
    public const string ProductName = "DeepClean";

    /// <summary>Settings, logs and future caches live below this folder.</summary>
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductName);

    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "logs");

    public static string LogFileTemplate { get; } = Path.Combine(LogDirectory, "deepclean-.log");

    public static string SettingsFilePath { get; } = Path.Combine(RootDirectory, "settings.json");

    public static string EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        return path;
    }
}
