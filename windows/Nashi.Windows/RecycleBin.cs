using System.IO;
using System.Runtime.InteropServices;

namespace Nashi.Windows;

// Sends files to the Recycle Bin so a delete can always be undone.
internal static class RecycleBin
{
    private const uint Delete = 3;
    private const ushort Silent = 0x0004, AllowUndo = 0x0040, NoErrorUi = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileOperation
    {
        public IntPtr Owner;
        public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref FileOperation operation);

    internal static bool TryRecycle(IEnumerable<string> paths, IntPtr owner)
    {
        var targets = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath).ToArray();
        if (targets.Length == 0) return true;
        var operation = new FileOperation
        {
            Owner = owner, Function = Delete, From = string.Join("\0", targets) + "\0\0",
            Flags = AllowUndo | NoErrorUi | Silent
        };
        var result = SHFileOperation(ref operation);
        return result == 0 && !operation.Aborted && targets.All(p => !File.Exists(p) && !Directory.Exists(p));
    }
}
