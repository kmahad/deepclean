using System.Diagnostics;
using System.Security.Principal;

namespace DeepClean.Platform.Security;

/// <summary>Reports and requests Windows administrator rights.</summary>
public interface IElevationService
{
    /// <summary>True when the current process already runs elevated.</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Starts a second, elevated instance of DeepClean and returns true when the user
    /// accepted the UAC prompt. The caller is responsible for shutting the current
    /// instance down afterwards.
    /// </summary>
    bool TryRestartElevated(out string? error);
}

public sealed class ElevationService : IElevationService
{
    public bool IsElevated { get; } = DetectElevation();

    public bool TryRestartElevated(out string? error)
    {
        error = null;

        var executable = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executable))
        {
            error = "The location of the DeepClean executable could not be determined.";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
            };

            foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            // The most common failure is the user dismissing the UAC prompt.
            error = ex.Message;
            return false;
        }
    }

    private static bool DetectElevation()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
