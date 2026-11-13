using Nashi.Core;
namespace Nashi.Linux;

/// The Windows FileActions.MoveAsync contract on Linux: copy into a staging folder beside the
/// destination, publish, and only then remove the originals. Linux paths are case-sensitive and
/// folder identity is checked after resolving symbolic links instead of with Win32 file IDs.
public static class FileMove
{
    public static async Task<IReadOnlyList<string>> MoveAsync(IReadOnlyList<string> inputs,string destination,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);var dest=Real(destination);
        var toMove=new List<string>();var staged=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);var resolved=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var path in inputs)
        {
            var full=Path.GetFullPath(path);
            if(!File.Exists(full)&&!Directory.Exists(full))throw new FileNotFoundException("Source not found: "+full,full);
            var real=Real(full);
            if(Directory.Exists(full)&&(real==dest||dest.StartsWith(real.TrimEnd('/')+"/",StringComparison.Ordinal)))throw new ArgumentException("Choose a destination outside the folder being moved.");
            var parent=Path.GetDirectoryName(full);
            if(parent is not null&&Real(parent)==dest){resolved[full]=Path.Combine(dest,Path.GetFileName(full));continue;}
            if(!seen.Add(full))continue;
            var target=SafePaths.Unique(tx.StagingDirectory,Path.GetFileName(full));await CopyTree(full,target,token);staged.Add(target);toMove.Add(full);
            progress?.Report(new(staged.Count/(double)inputs.Count*.95,"Preparing move…"));
        }
        token.ThrowIfCancellationRequested();
        if(staged.Count>0)
        {
            var published=tx.Publish(staged,token);for(var i=0;i<toMove.Count;i++)resolved[toMove[i]]=published[i];
            var retained=new List<string>();
            foreach(var original in toMove)
            {
                try{if(Directory.Exists(original))Directory.Delete(original,true);else File.Delete(original);}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
                if(File.Exists(original)||Directory.Exists(original))retained.Add(original);
            }
            if(retained.Count>0)throw new IOException("Files were copied safely, but some originals could not be removed. New copies are in "+destination+". Check the originals before retrying.");
        }
        return inputs.Select(p=>resolved[Path.GetFullPath(p)]).ToArray();
    }
    private static string Real(string path)
    {
        var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        try{if(Directory.Exists(full)&&new DirectoryInfo(full).ResolveLinkTarget(true) is {} target)return Path.TrimEndingDirectorySeparator(target.FullName);}catch(IOException){}
        return full;
    }
    private static async Task CopyTree(string source,string target,CancellationToken token,int depth=0)
    {
        token.ThrowIfCancellationRequested();
        if(depth>64||(File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Move regular files and folders without symbolic links.");
        if(Directory.Exists(source)){Directory.CreateDirectory(target);foreach(var item in Directory.EnumerateFileSystemEntries(source))await CopyTree(item,SafePaths.ArchiveEntry(target,Path.GetFileName(item)),token,depth+1);}
        else
        {
            await using(var input=File.OpenRead(source))await using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))await input.CopyToAsync(output,token);
            // Keep the permission bits (an executable script stays executable).
            File.SetUnixFileMode(target,File.GetUnixFileMode(source));
        }
    }
}
