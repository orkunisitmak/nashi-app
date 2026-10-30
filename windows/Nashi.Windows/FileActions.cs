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
        using var tx=new ExportTransaction(destination);var staged=new List<string>();
        foreach(var path in inputs)
        {
            var full=Path.GetFullPath(path);var dest=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
            if(Directory.Exists(full)&&(dest.Equals(full,StringComparison.OrdinalIgnoreCase)||dest.StartsWith(full.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Choose a destination outside the folder being moved.");
            var target=SafePaths.Unique(tx.StagingDirectory,Path.GetFileName(full));await CopyTree(full,target,token);staged.Add(target);progress?.Report(new(staged.Count/(double)inputs.Count*.95,"Preparing move…"));
        }
        token.ThrowIfCancellationRequested();var published=tx.Publish(staged,token);
        // Only after every copy was published may originals be removed. If a
        // locked original remains, the destination is retained and reported.
        var retained=new List<string>();foreach(var input in inputs){try{if(Directory.Exists(input))Directory.Delete(input,true);else File.Delete(input);}catch(IOException){retained.Add(input);}catch(UnauthorizedAccessException){retained.Add(input);}}
        if(retained.Count>0)throw new IOException("Files were copied safely, but some originals could not be removed. New copies are in "+destination+". Check the originals before retrying.");return published;
    }
    private static async Task CopyTree(string source,string target,CancellationToken token,int depth=0)
    {
        token.ThrowIfCancellationRequested();if(depth>64||(File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Move regular files and folders without symbolic links.");
        if(Directory.Exists(source)){Directory.CreateDirectory(target);foreach(var item in Directory.EnumerateFileSystemEntries(source))await CopyTree(item,SafePaths.ArchiveEntry(target,Path.GetFileName(item)),token,depth+1);}
        else{await using var input=File.OpenRead(source);await using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);await input.CopyToAsync(output,token);}
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
