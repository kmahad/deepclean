using System.IO;
using DeepClean.Core.Cleaning;

namespace DeepClean.Platform.Storage;

/// <summary>
/// The default way DeepClean removes something: hand it to the shell so it lands in the
/// Recycle Bin and the user can still get it back. Permanent removal is offered separately and
/// only happens when the user has explicitly asked for it.
/// </summary>
public sealed class RecycleBinFileRemover : IFileRemover
{
    public string? MoveToRecycleBin(string path, bool isDirectory) => ShellFileOperations.MoveToRecycleBin(path);

    public string? DeletePermanently(string path, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ex.Message;
        }
    }
}
