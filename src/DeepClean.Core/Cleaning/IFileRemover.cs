namespace DeepClean.Core.Cleaning;

/// <summary>
/// Actually removes something from the disk. Kept behind an interface so the cleaning logic
/// stays testable and so the Recycle Bin, which is a Windows shell feature, lives in the
/// platform project instead of leaking into the core.
/// </summary>
public interface IFileRemover
{
    /// <summary>
    /// Moves the item to the Recycle Bin so it can still be restored. Returns null on success,
    /// otherwise a message explaining what Windows refused to do.
    /// </summary>
    string? MoveToRecycleBin(string path, bool isDirectory);

    /// <summary>
    /// Removes the item without any possibility of recovery. Returns null on success, otherwise
    /// a message explaining the failure.
    /// </summary>
    string? DeletePermanently(string path, bool isDirectory);
}
