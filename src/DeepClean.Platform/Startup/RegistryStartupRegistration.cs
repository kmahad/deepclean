using DeepClean.Core;
using Microsoft.Win32;

namespace DeepClean.Platform.Startup;

/// <summary>Registers the application in the current user's auto start list.</summary>
public interface IStartupRegistration
{
    bool IsEnabled();

    bool TrySetEnabled(bool enabled, out string? error);
}

/// <summary>
/// Uses <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. This needs no
/// administrator rights and is easy for users to inspect or remove.
/// </summary>
public sealed class RegistryStartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = AppPaths.ProductName;

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TrySetEnabled(bool enabled, out string? error)
    {
        error = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key is null)
            {
                error = "The auto start registry key could not be opened.";
                return false;
            }

            if (enabled)
            {
                var executable = Environment.ProcessPath;

                if (string.IsNullOrEmpty(executable))
                {
                    error = "The location of the DeepClean executable could not be determined.";
                    return false;
                }

                key.SetValue(ValueName, $"\"{executable}\" --tray", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
