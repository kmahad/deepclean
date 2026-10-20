using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeepClean.Platform.Storage;

/// <summary>
/// Thin wrapper over the Windows shell file operations used to send things to the Recycle Bin.
/// The shell is the only component that can put a file in the bin with a working "Restore", so
/// this is the default removal path for everything DeepClean cleans.
/// </summary>
internal static class ShellFileOperations
{
    private const uint FO_DELETE = 0x0003;

    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_NORECURSEREPARSE = 0x8000;

    // Shell specific return codes. They are not Win32 error codes, so they get their own names.
    private const int DE_OPCANCELLED = 0x75;
    private const int DE_ACCESSDENIEDSRC = 0x78;
    private const int DE_PATHTOODEEP = 0x79;
    private const int DE_INVALIDFILES = 0x7C;
    private const int DE_FILENAMETOOLONG = 0x81;
    private const int DE_FILE_TOO_LARGE = 0x85;
    private const int DE_ROOTDIR = 0x74;

    /// <summary>Moves one item to the Recycle Bin. Returns null on success, otherwise a message.</summary>
    public static string? MoveToRecycleBin(string path)
    {
        var from = Marshal.StringToHGlobalUni(path + "\0");

        try
        {
            var operation = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = from,
                pTo = IntPtr.Zero,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOCONFIRMMKDIR | FOF_NOERRORUI | FOF_SILENT | FOF_NORECURSEREPARSE,
                fAnyOperationsAborted = false,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = IntPtr.Zero,
            };

            var result = SHFileOperation(ref operation);

            if (operation.fAnyOperationsAborted)
            {
                return "Windows reported that the operation was abandoned.";
            }

            return result == 0 ? null : Describe(result);
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    private static string Describe(int code) => code switch
    {
        DE_OPCANCELLED => "The operation was cancelled.",
        DE_ACCESSDENIEDSRC => "Windows denied access to this item.",
        DE_PATHTOODEEP => "The path is too long for the Recycle Bin.",
        DE_INVALIDFILES => "Windows considered this path invalid.",
        DE_FILENAMETOOLONG => "The file name is too long for the Recycle Bin.",
        DE_FILE_TOO_LARGE => "The item is too large for the Recycle Bin.",
        DE_ROOTDIR => "A drive root cannot be recycled.",
        _ => new Win32Exception(code).Message,
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;

        public uint wFunc;

        public IntPtr pFrom;

        public IntPtr pTo;

        public ushort fFlags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;

        public IntPtr hNameMappings;

        public IntPtr lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
