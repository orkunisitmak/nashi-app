using System.Text.RegularExpressions;
namespace Nashi.Core;
/// <summary>
/// Removes what portable updates leave beside the app folder. Staging folders
/// (".nashi-update-ID") are only needed while an update is applied. Previous
/// versions ("Nashi.backup-ID") stay recoverable: the newest one is kept and
/// only older ones are removed. Nothing outside these exact names is touched,
/// links are never followed, and folders a running process uses are skipped.
/// </summary>
public static partial class UpdateLeftovers
{
    [GeneratedRegex("^\\.nashi-update-[0-9a-f]{32}$")]private static partial Regex StageName();
    [GeneratedRegex("^[0-9a-f]{32}$")]private static partial Regex Id();
    public sealed record Result(IReadOnlyList<string> Removed,IReadOnlyList<string> Kept);
    public static bool IsStage(string target,string path)=>Sibling(target,path)&&StageName().IsMatch(Path.GetFileName(path));
    public static bool IsBackup(string target,string path)
    {
        var name=Path.GetFileName(path);var prefix=Path.GetFileName(Path.TrimEndingDirectorySeparator(target))+".backup-";
        return Sibling(target,path)&&name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&Id().IsMatch(name[prefix.Length..]);
    }
    /// <param name="target">The running app folder.</param>
    /// <param name="version">Reads a backup's app version; null when it is not a Nashi folder.</param>
    /// <param name="inUse">True when a running process was started from inside the folder.</param>
    public static Result Clean(string target,Func<string,Version?> version,Func<string,bool> inUse)
    {
        target=Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        var parent=Path.GetDirectoryName(target);if(parent is null||!Directory.Exists(parent))return new([],[]);
        var removed=new List<string>();var kept=new List<string>();
        var folders=new DirectoryInfo(parent).EnumerateDirectories().Where(d=>(d.Attributes&FileAttributes.ReparsePoint)==0).ToArray();
        void Remove(DirectoryInfo folder)
        {
            if(inUse(folder.FullName)){kept.Add(folder.FullName);return;}
            // Directory.Delete removes links inside the folder without entering them.
            try{folder.Delete(true);removed.Add(folder.FullName);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){kept.Add(folder.FullName);}
        }
        foreach(var stage in folders.Where(d=>IsStage(target,d.FullName)))Remove(stage);
        var backups=folders.Where(d=>IsBackup(target,d.FullName)).Select(d=>(Folder:d,Version:Read(version,d.FullName))).Where(b=>b.Version is not null)
            .OrderByDescending(b=>b.Version).ThenByDescending(b=>b.Folder.LastWriteTimeUtc).ToArray();
        if(backups.Length>0)kept.Add(backups[0].Folder.FullName);
        foreach(var backup in backups.Skip(1))Remove(backup.Folder);
        return new(removed,kept);
    }
    private static Version? Read(Func<string,Version?> version,string path){try{return version(path);}catch(Exception error)when(error is IOException or UnauthorizedAccessException or FormatException){return null;}}
    private static bool Sibling(string target,string path)=>string.Equals(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))),Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target))),StringComparison.OrdinalIgnoreCase);
}
