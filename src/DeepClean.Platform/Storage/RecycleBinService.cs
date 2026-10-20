using System.ComponentModel;
using System.Runtime.InteropServices;
using DeepClean.Core.Cleaning;

namespace DeepClean.Platform.Storage;

/// <summary>
/// Reads the size of the Recycle Bin and can empty it. Emptying is never called on its own:
/// the interface has to ask the user first and explain that the contents become unrecoverable.
/// </summary>
public sealed class RecycleBinService : IRecycleBinService
{
    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    public long? GetSizeBytes() => Query()?.Size;

    public long? GetItemCount() => Query()?.Items;

    public string? Empty()
    {
        var result = SHEmptyRecycleBin(
            IntPtr.Zero,
            null,
            SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

        if (result == 0)
        {
            return null;
        }

        var message = Marshal.GetExceptionForHR(result)?.Message;

        return string.IsNullOrWhiteSpace(message)
            ? new Win32Exception(result).Message
            : message;
    }

    private static RecycleBinInfo? Query()
    {
        var info = new SHQUERYRBINFO
        {
            cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>(),
        };

        var result = SHQueryRecycleBin(null, ref info);

        return result == 0 ? new RecycleBinInfo(info.i64Size, info.i64NumItems) : null;
    }

    private readonly record struct RecycleBinInfo(long Size, long Items);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public uint cbSize;

        public long i64Size;

        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}
