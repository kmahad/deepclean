namespace DeepClean.Core.Cleaning;

/// <summary>Reported after every item so the interface can follow along.</summary>
public sealed record CleanupProgress(
    int Completed,
    int Total,
    string CurrentPath,
    long ReclaimedBytes);
