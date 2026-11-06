using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Nashi.Core;
using global::Windows.ApplicationModel.DataTransfer;
using global::Windows.Storage;
using Clipboard=System.Windows.Clipboard;
using FileAttributes=System.IO.FileAttributes;
namespace Nashi.Windows;
public static class FileActions
{
    private static readonly Dictionary<IntPtr,ShareSession> shares=[];
    private sealed class ShareSession(DataTransferManager manager)
    {
        internal readonly DataTransferManager Manager=manager;internal string[] Paths=[];
        internal async void Requested(DataTransferManager sender,DataRequestedEventArgs args)
        {
            var paths=Paths.ToArray();var deferral=args.Request.GetDeferral();try{var items=new List<IStorageItem>();foreach(var path in paths)items.Add(Directory.Exists(path)?await StorageFolder.GetFolderFromPathAsync(path):await StorageFile.GetFileFromPathAsync(path));args.Request.Data.Properties.Title=paths.Length==1?Path.GetFileName(paths[0]):$"{paths.Length} files from Nashi";args.Request.Data.SetStorageItems(items);}catch(Exception error){args.Request.FailWithDisplayText(error.Message);}finally{deferral.Complete();}
        }
    }
    public static void Copy(IReadOnlyList<string> paths){var values=new StringCollection();values.AddRange(paths.ToArray());Clipboard.SetFileDropList(values);}
    public static void CopyPaths(IReadOnlyList<string> paths)=>Clipboard.SetText(string.Join(Environment.NewLine,paths));
    public static void Terminal(string path)
    {
        var folder=Directory.Exists(path)?path:Path.GetDirectoryName(path)!;
        var terminal=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","WindowsApps","wt.exe");
        var info=new ProcessStartInfo(File.Exists(terminal)?terminal:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,WorkingDirectory=folder};
        if(File.Exists(terminal)){info.ArgumentList.Add("-d");info.ArgumentList.Add(folder);}Process.Start(info)?.Dispose();
    }
    public static async Task<IReadOnlyList<string>> MoveAsync(IReadOnlyList<string> inputs,string destination,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);
        var dest=Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var toMove=new List<string>();
        var staged=new List<string>();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in inputs)
        {
            var full=Path.GetFullPath(path);
            if(!File.Exists(full)&&!Directory.Exists(full))throw new FileNotFoundException("Source not found: "+full,full);
            if(Directory.Exists(full)&&(SameDirectory(full,dest)||dest.StartsWith(full.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Choose a destination outside the folder being moved.");
            var parent=Path.GetDirectoryName(full);
            if(parent is not null) parent=Path.TrimEndingDirectorySeparator(parent);
            if(parent is not null&&SameDirectory(parent,dest)){resolved[full]=Path.Combine(dest,Path.GetFileName(full));continue;}
            if(!seen.Add(full))continue;
            var target=SafePaths.Unique(tx.StagingDirectory,Path.GetFileName(full));
            await CopyTree(full,target,token);
            staged.Add(target);
            toMove.Add(full);
            progress?.Report(new(staged.Count/(double)inputs.Count*.95,"Preparing move…"));
        }
        token.ThrowIfCancellationRequested();
        if(staged.Count>0)
        {
            var published=tx.Publish(staged,token);
            for(var i=0;i<toMove.Count;i++)resolved[toMove[i]]=published[i];
            // Only after every copy was published may originals be removed. If a
            // locked original remains, the destination is retained and reported.
            var retained=new List<string>();
            foreach(var orig in toMove)
            {
                var deleted=false;
                for(var attempt=0;attempt<5;attempt++)
                {
                    try{if(Directory.Exists(orig))Directory.Delete(orig,true);else if(File.Exists(orig))File.Delete(orig);deleted=true;break;}
                    catch(Exception ex)when(attempt<4&&(ex is IOException or UnauthorizedAccessException)){await Task.Delay(25*(attempt+1),CancellationToken.None);}
                    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){break;}
                }
                if(!deleted&&(File.Exists(orig)||Directory.Exists(orig)))retained.Add(orig);
            }
            if(retained.Count>0)throw new IOException("Files were copied safely, but some originals could not be removed. New copies are in "+destination+". Check the originals before retrying.");
        }
        return inputs.Select(p=>resolved[Path.GetFullPath(p)]).ToArray();
    }
    private static async Task CopyTree(string source,string target,CancellationToken token,int depth=0)
    {
        token.ThrowIfCancellationRequested();if(depth>64||(File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Move regular files and folders without symbolic links.");
        if(Directory.Exists(source)){Directory.CreateDirectory(target);foreach(var item in Directory.EnumerateFileSystemEntries(source))await CopyTree(item,SafePaths.ArchiveEntry(target,Path.GetFileName(item)),token,depth+1);}
        else{await using var input=File.OpenRead(source);await using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);await input.CopyToAsync(output,token);}
    }
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Auto)]private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string lpFileName,uint dwDesiredAccess,uint dwShareMode,IntPtr lpSecurityAttributes,uint dwCreationDisposition,uint dwFlagsAndAttributes,IntPtr hTemplateFile);
    [StructLayout(LayoutKind.Sequential)]internal struct BY_HANDLE_FILE_INFORMATION{public uint dwFileAttributes;public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime,ftLastAccessTime,ftLastWriteTime;public uint dwVolumeSerialNumber,nFileSizeHigh,nFileSizeLow,nNumberOfLinks,nFileIndexHigh,nFileIndexLow;}
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle hFile,out BY_HANDLE_FILE_INFORMATION lpFileInformation);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Auto)]private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle hFile,[Out]System.Text.StringBuilder lpszFilePath,uint cchFilePath,uint dwFlags);
    private static string? GetFinalPath(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        var sb=new System.Text.StringBuilder(1024);
        var len=GetFinalPathNameByHandle(handle,sb,(uint)sb.Capacity,0);
        if(len>=sb.Capacity)
        {
            sb.EnsureCapacity((int)len+1);
            len=GetFinalPathNameByHandle(handle,sb,(uint)sb.Capacity,0);
        }
        return(len>0&&len<sb.Capacity)?sb.ToString(0,(int)len):null;
    }
    internal static bool CompareDirectoryIdentity(bool infoOkA,in BY_HANDLE_FILE_INFORMATION infoA,bool infoOkB,in BY_HANDLE_FILE_INFORMATION infoB,Func<string?> getFinalA,Func<string?> getFinalB)
    {
        if(infoOkA&&infoOkB)
        {
            if(infoA.dwVolumeSerialNumber!=infoB.dwVolumeSerialNumber)
                return false;
            var hasIdA=infoA.nFileIndexHigh!=0||infoA.nFileIndexLow!=0;
            var hasIdB=infoB.nFileIndexHigh!=0||infoB.nFileIndexLow!=0;
            if(hasIdA&&hasIdB)
                return infoA.nFileIndexHigh==infoB.nFileIndexHigh&&infoA.nFileIndexLow==infoB.nFileIndexLow;
        }
        var finalA=getFinalA();
        var finalB=getFinalB();
        if(finalA is not null&&finalB is not null)
            return string.Equals(finalA,finalB,StringComparison.OrdinalIgnoreCase);
        throw new IOException("Could not determine physical directory identity.");
    }
    internal static bool SameDirectory(string a,string b)
    {
        var normA=Path.TrimEndingDirectorySeparator(Path.GetFullPath(a));
        var normB=Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
        if(string.Equals(normA,normB,StringComparison.OrdinalIgnoreCase))return true;
        try
        {
            using var hA=CreateFile(normA,0x0080,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
            var errA=hA.IsInvalid?Marshal.GetLastWin32Error():0;
            using var hB=CreateFile(normB,0x0080,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
            var errB=hB.IsInvalid?Marshal.GetLastWin32Error():0;
            if(hA.IsInvalid)
            {
                if(errA is not (2 or 3))
                    throw new IOException($"Cannot access directory '{normA}' to verify identity (Win32 error {errA}).");
                if(hB.IsInvalid&&errB is not (0 or 2 or 3))
                    throw new IOException($"Cannot access directory '{normB}' to verify identity (Win32 error {errB}).");
                return false;
            }
            if(hB.IsInvalid)
            {
                if(errB is not (2 or 3))
                    throw new IOException($"Cannot access directory '{normB}' to verify identity (Win32 error {errB}).");
                return false;
            }

            var infoOkA=GetFileInformationByHandle(hA,out var infoA);
            var infoOkB=GetFileInformationByHandle(hB,out var infoB);
            if(infoOkA&&(infoA.dwFileAttributes&0x10)==0||infoOkB&&(infoB.dwFileAttributes&0x10)==0)
                return false;

            return CompareDirectoryIdentity(infoOkA,in infoA,infoOkB,in infoB,()=>GetFinalPath(hA),()=>GetFinalPath(hB));
        }
        catch(Exception ex) when(ex is not IOException)
        {
            throw new IOException($"Error verifying directory identity for '{normA}' and '{normB}': {ex.Message}",ex);
        }
    }
    [ComImport,Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IShareInterop{IntPtr GetForWindow(IntPtr window,ref Guid id);void ShowShareUIForWindow(IntPtr window);}
    public static void Share(Window owner,IReadOnlyList<string> paths)
    {
        var handle=new WindowInteropHelper(owner).Handle;var interop=DataTransferManager.As<IShareInterop>();
        if(!shares.TryGetValue(handle,out var session))
        {
            var id=new Guid("a5caee9b-8708-49d1-8d36-67d25a8da00c");var pointer=interop.GetForWindow(handle,ref id);DataTransferManager manager;try{manager=WinRT.MarshalInterface<DataTransferManager>.FromAbi(pointer);}finally{Marshal.Release(pointer);}
            session=new(manager);manager.DataRequested+=session.Requested;shares[handle]=session;var retained=session;owner.Closed+=(_,_)=>{retained.Manager.DataRequested-=retained.Requested;shares.Remove(handle);};
        }
        session.Paths=paths.ToArray();
        interop.ShowShareUIForWindow(handle);
    }
}
