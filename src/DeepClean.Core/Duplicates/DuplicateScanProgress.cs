namespace DeepClean.Core.Duplicates;

/// <summary>Reported while a duplicate scan works through the files it collected.</summary>
/// <param name="Phase">What the scan is doing right now, in words fit for the screen.</param>
/// <param name="FilesHashed">How many files have been read and hashed so far.</param>
/// <param name="BytesRead">How much has been read from the disk so far.</param>
/// <param name="CurrentPath">The file being read when the report was made.</param>
public sealed record DuplicateScanProgress(
    string Phase,
    int FilesHashed,
    long BytesRead,
    string CurrentPath);
