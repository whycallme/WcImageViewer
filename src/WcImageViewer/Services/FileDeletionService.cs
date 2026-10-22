using System.IO;
using System.Runtime.InteropServices;

namespace WcImageViewer.Services;

public static class FileDeletionService
{
    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;

    /// <summary>Deletes a file, optionally moving it to the Recycle Bin.</summary>
    public static void Delete(string path, bool useRecycleBin)
    {
        if (!useRecycleBin)
        {
            File.Delete(path);
            return;
        }

        Recycle(path);
    }

    private static void Recycle(string path)
    {
        var operation = new ShFileOpStruct
        {
            wFunc = FoDelete,
            pFrom = Path.GetFullPath(path) + '\0',
            fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi
        };

        var result = SHFileOperation(ref operation);
        if (result != 0 || operation.fAnyOperationsAborted)
        {
            throw new IOException("파일을 휴지통으로 이동하지 못했습니다.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct lpFileOp);
}
