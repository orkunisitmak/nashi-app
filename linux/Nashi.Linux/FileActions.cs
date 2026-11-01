using System.Diagnostics;
using Nashi.Core;
namespace Nashi.Linux;

/// Copy, Move, Copy path and Terminal from the shared catalog's file actions. Share and Float are
/// Windows/macOS desktop features without a common Linux counterpart.
public static class FileActions
{
    /// File managers (Nautilus, Dolphin, Thunar) paste file:// URI lists.
    public static string UriList(IReadOnlyList<string> paths)=>string.Join("\r\n",paths.Select(p=>new Uri(Path.GetFullPath(p)).AbsoluteUri));
    public static string PathList(IReadOnlyList<string> paths)=>string.Join("\n",paths.Select(Path.GetFullPath));

    /// Opens the user's terminal in the file's folder: xdg-terminal-exec, then common desktop terminals.
    public static bool Terminal(string path)
    {
        var folder=Directory.Exists(path)?path:Path.GetDirectoryName(Path.GetFullPath(path))!;
        foreach(var (name,args) in new (string,string[])[]{("xdg-terminal-exec",[]),("kgx",[]),("gnome-terminal",["--working-directory="+folder]),("konsole",["--workdir",folder]),("xfce4-terminal",["--working-directory="+folder]),("alacritty",["--working-directory",folder]),("kitty",["--directory",folder]),("foot",["--working-directory="+folder]),("xterm",[])})
        {
            if(Operations.Executable(name) is not {} exe)continue;
            try{var start=new ProcessStartInfo(exe){UseShellExecute=false,WorkingDirectory=folder};foreach(var a in args)start.ArgumentList.Add(a);Process.Start(start)?.Dispose();return true;}
            catch(Exception error)when(error is System.ComponentModel.Win32Exception or InvalidOperationException){}
        }
        return false;
    }

    /// Like Windows: every item is copied into the destination's staging folder first and published
    /// together; originals are removed only after that, so a failure never loses a file.
    public static async Task<IReadOnlyList<string>> MoveAsync(IReadOnlyList<string> inputs,string destination,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);
        var dest=Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var toMove=new List<string>();var staged=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);var resolved=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var path in inputs)
        {
            var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if(!File.Exists(full)&&!Directory.Exists(full))throw new FileNotFoundException("Source not found: "+full,full);
            if(Directory.Exists(full)&&(full==dest||dest.StartsWith(full+Path.DirectorySeparatorChar,StringComparison.Ordinal)))throw new ArgumentException("Choose a destination outside the folder being moved.");
            if(Path.GetDirectoryName(full) is {} parent&&Path.TrimEndingDirectorySeparator(parent)==dest){resolved[full]=full;continue;}
            if(!seen.Add(full))continue;
            var target=SafePaths.Unique(tx.StagingDirectory,Path.GetFileName(full));
            await Task.Run(()=>CopyTree(full,target,token),token);staged.Add(target);toMove.Add(full);
            progress?.Report(new(staged.Count/(double)inputs.Count*.95,"Preparing move…"));
        }
        token.ThrowIfCancellationRequested();
        if(staged.Count>0)
        {
            var published=tx.Publish(staged,token);for(var i=0;i<toMove.Count;i++)resolved[toMove[i]]=published[i];
            var retained=new List<string>();
            foreach(var original in toMove)
            {
                try{if(Directory.Exists(original)&&(File.GetAttributes(original)&FileAttributes.ReparsePoint)==0)Directory.Delete(original,true);else File.Delete(original);}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException){retained.Add(original);}
            }
            if(retained.Count>0)throw new IOException("Files were copied safely, but some originals could not be removed. New copies are in "+destination+". Check the originals before retrying.");
        }
        return inputs.Select(p=>resolved[Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))]).ToArray();
    }
    /// Symbolic links are copied as links, never followed, so a link cannot pull in files from elsewhere.
    private static void CopyTree(string source,string target,CancellationToken token,int depth=0)
    {
        token.ThrowIfCancellationRequested();if(depth>64)throw new IOException("This folder is nested too deeply to move.");
        var info=new FileInfo(source);
        if(info.LinkTarget is {} link){File.CreateSymbolicLink(target,link);return;}
        if(Directory.Exists(source))
        {
            Directory.CreateDirectory(target);
            foreach(var entry in Directory.EnumerateFileSystemEntries(source))CopyTree(entry,Path.Combine(target,Path.GetFileName(entry)),token,depth+1);
            return;
        }
        File.Copy(source,target);File.SetLastWriteTimeUtc(target,File.GetLastWriteTimeUtc(source));
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(target,File.GetUnixFileMode(source));
    }
}
