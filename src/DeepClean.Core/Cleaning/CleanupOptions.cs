namespace DeepClean.Core.Cleaning;

/// <summary>How a clean should behave.</summary>
public sealed class CleanupOptions
{
    /// <summary>
    /// When false, which is the default, everything goes to the Recycle Bin so the user can
    /// still get it back. Only an explicit opt in turns this on.
    /// </summary>
    public bool PermanentlyDelete { get; init; }

    /// <summary>Folders, and everything below them, that must be left alone.</summary>
    public IReadOnlyCollection<string> ExcludedPaths { get; init; } = [];
}
