using Nashi.Core;
namespace Nashi.Windows;

public sealed class IncompleteMoveException(IReadOnlyList<string> outputs,IReadOnlyList<string> retained)
    :IOException("Files were copied safely, but some originals could not be removed. Check the retained originals before retrying.")
{
    public IReadOnlyList<string> Outputs{get;}=outputs.ToArray();
    public IReadOnlyList<string> RetainedOriginals{get;}=retained.ToArray();
}

/// Nashi's copy-before-delete move algorithm, with Linux path comparisons.
public static class FileTransfer
{
    public static async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> inputs,string destination,bool move,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);var staged=new List<string>();
        foreach(var path in inputs)
        {
            var full=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);var dest=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
            if(Directory.Exists(full)&&(dest.Equals(full,StringComparison.Ordinal)||dest.StartsWith(full+Path.DirectorySeparatorChar,StringComparison.Ordinal)))throw new ArgumentException("Choose a destination outside the selected folder.");
            var target=SafePaths.Unique(tx.StagingDirectory,Path.GetFileName(full));await CopyTree(full,target,token);staged.Add(target);progress?.Report(new(staged.Count/(double)inputs.Count*.95,"Preparing files…"));
        }
        var published=tx.Publish(staged,token,allowEmptyFiles:true);if(!move)return published;
        var retained=new List<string>();foreach(var input in inputs)
        {try{if(Directory.Exists(input))Directory.Delete(input,true);else File.Delete(input);}catch(IOException){retained.Add(input);}catch(UnauthorizedAccessException){retained.Add(input);}}
        if(retained.Count>0)throw new IncompleteMoveException(published,retained);return published;
    }
    private static async Task CopyTree(string source,string target,CancellationToken token,int depth=0)
    {
        token.ThrowIfCancellationRequested();if(depth>64||(File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Select regular files and folders without symbolic links.");
        if(Directory.Exists(source)){Directory.CreateDirectory(target);foreach(var item in Directory.EnumerateFileSystemEntries(source))await CopyTree(item,Path.Combine(target,Path.GetFileName(item)),token,depth+1);}
        else{await using var input=File.OpenRead(source);await using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);await input.CopyToAsync(output,token);await output.FlushAsync(token);
            if(OperatingSystem.IsLinux())File.SetUnixFileMode(target,File.GetUnixFileMode(source)&~(UnixFileMode.SetUser|UnixFileMode.SetGroup));
            File.SetLastWriteTimeUtc(target,File.GetLastWriteTimeUtc(source));}
    }
}
