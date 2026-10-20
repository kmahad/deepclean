namespace DeepClean.Core.Cleaning;

/// <summary>Read and empty access to the Windows Recycle Bin.</summary>
public interface IRecycleBinService
{
    /// <summary>Total size of everything currently in the Recycle Bin, or null when unavailable.</summary>
    long? GetSizeBytes();

    /// <summary>Number of items currently in the Recycle Bin, or null when unavailable.</summary>
    long? GetItemCount();

    /// <summary>
    /// Empties the Recycle Bin. Returns null on success, otherwise a message explaining the
    /// failure. Asking the user to confirm is the caller's job, not this method's.
    /// </summary>
    string? Empty();
}
