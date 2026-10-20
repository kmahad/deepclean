using System.IO;
using Microsoft.Extensions.Logging;

namespace DeepClean.Core.Cleaning;

/// <summary>
/// The one place where something is actually taken off the disk. The junk cleaner and the
/// duplicate finder both go through here, so the Recycle Bin default, the safety re-check and the
/// handling of Windows refusals stay the same no matter which part of the app asked.
/// </summary>
internal static class ItemRemoval
{
    /// <summary>
    /// Removes one item, after checking it again against the folder it was found in. The item is
    /// never removed when a check refuses it, and a file another program is holding is skipped
    /// rather than forced.
    /// </summary>
    public static CleanupOutcome Remove(
        string path,
        string rootPath,
        bool isDirectory,
        CleanupOptions options,
        IFileRemover remover,
        ILogger? logger,
        out string? message) =>
        Remove(path, rootPath, isDirectory, options, remover, logger, forDuplicates: false, personalFolders: null, out message);

    /// <summary>
    /// Removes one item. When <paramref name="forDuplicates"/> is set the check also allows copies
    /// that sit in one of the user's own folders, but such a copy always goes to the Recycle Bin,
    /// whatever the options ask for, so it can still be restored. <paramref name="personalFolders"/>
    /// overrides which folders count as the user's own; null uses the ones Windows reports.
    /// </summary>
    public static CleanupOutcome Remove(
        string path,
        string rootPath,
        bool isDirectory,
        CleanupOptions options,
        IFileRemover remover,
        ILogger? logger,
        bool forDuplicates,
        IReadOnlyCollection<string>? personalFolders,
        out string? message)
    {
        message = null;

        var allowed = true;
        var reason = string.Empty;
        var mustRecycle = false;

        if (forDuplicates)
        {
            var duplicateCheck = SafetyGuard.CheckDuplicate(path, rootPath, options.ExcludedPaths, personalFolders);

            allowed = duplicateCheck.IsAllowed;
            reason = duplicateCheck.Reason;
            mustRecycle = duplicateCheck.MustRecycle;
        }
        else
        {
            var check = SafetyGuard.Check(path, rootPath, options.ExcludedPaths);

            allowed = check.IsAllowed;
            reason = check.Reason;
        }

        if (!allowed)
        {
            message = reason;
            return CleanupOutcome.Skipped;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            message = "It was already gone.";
            return CleanupOutcome.Skipped;
        }

        var deletePermanently = options.PermanentlyDelete && !mustRecycle;

        try
        {
            var error = deletePermanently
                ? remover.DeletePermanently(path, isDirectory)
                : remover.MoveToRecycleBin(path, isDirectory);

            if (error is null)
            {
                return deletePermanently ? CleanupOutcome.Deleted : CleanupOutcome.Recycled;
            }

            message = error;
            logger?.LogWarning("Could not remove {Path}: {Reason}", path, error);
            return CleanupOutcome.Failed;
        }
        catch (IOException ex)
        {
            // Sharing violations mean another program is holding the file.
            message = $"In use by another program ({ex.Message}).";
            logger?.LogDebug("Skipped {Path}: {Reason}", path, ex.Message);
            return CleanupOutcome.Skipped;
        }
        catch (UnauthorizedAccessException ex)
        {
            message = ex.Message;
            logger?.LogWarning("Access denied for {Path}: {Reason}", path, ex.Message);
            return CleanupOutcome.Failed;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            logger?.LogWarning(ex, "Removing {Path} failed", path);
            return CleanupOutcome.Failed;
        }
    }
}
