using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace Nashi.Windows;
// Explorer hands Nashi a Windows shortcut (.lnk) as the shortcut file itself, which no tool
// can open. Nashi works on the file or folder the shortcut points to. A shortcut whose target
// is missing, or that points to something that is not a file (Control Panel, a printer),
// stays as it is, so nothing silently disappears from the drop.
internal static class ShellShortcut
{
    internal static string[] Resolve(IEnumerable<string> paths)=>paths.Select(Resolve).ToArray();
    internal static string Resolve(string path)
    {
        if(!IsShortcut(path)||!File.Exists(path))return path;
        try{var target=Target(path);return target.Length>0&&(File.Exists(target)||Directory.Exists(target))?Path.GetFullPath(target):path;}
        catch(Exception error)when(error is COMException or InvalidCastException or IOException or UnauthorizedAccessException or ArgumentException){return path;}
    }
    internal static bool IsShortcut(string path)=>string.Equals(Path.GetExtension(path),".lnk",StringComparison.OrdinalIgnoreCase);
    private static string Target(string path)
    {
        var link=(IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path,0);var buffer=new StringBuilder(32768);
            link.GetPath(buffer,buffer.Capacity,IntPtr.Zero,0);return buffer.ToString();
        }
        finally{Marshal.FinalReleaseComObject(link);}
    }
    // Used by the smoke test to create a real shortcut through the same shell API.
    internal static void Create(string shortcut,string target)
    {
        var link=(IShellLinkW)new ShellLink();
        try{link.SetPath(target);((IPersistFile)link).Save(shortcut,true);}
        finally{Marshal.FinalReleaseComObject(link);}
    }
    [ComImport,Guid("00021401-0000-0000-C000-000000000046")]private class ShellLink;
    [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder file,int length,IntPtr data,uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder name,int length);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string name);
        void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder directory,int length);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string directory);
        void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder arguments,int length);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder icon,int length,out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string icon,int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string path,uint reserved);
        void Resolve(IntPtr window,uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)]string file);
    }
}
