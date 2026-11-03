using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Nashi.Core;

/// A portable Windows update swaps the app folder for the new version and keeps the old one beside it as
/// "Name.backup-…", next to the ".nashi-update-…" folder that staged the download. Every update left both behind,
/// so a Nashi kept on the Desktop gathered a backup and about 300 MB of staging per release. After an update the
/// backup of the most recent earlier version stays as the way back; older backups of this app and its finished
/// staging folders are removed. A folder is renamed before it is deleted, and Windows refuses that while a file in it
/// is open, so a copy that is still running is left whole.
public static class UpdateCleanup
{
    private static readonly Regex Leftover=new("^\\.nashi-(update|delete)-[0-9a-f]{32}$",RegexOptions.CultureInvariant);
    private static readonly Regex Backups=new("(\\.backup-[0-9a-f]{32})+$",RegexOptions.CultureInvariant);
    /// The app folder a backup came from, when Nashi runs from one of its backups (a shortcut can still point there).
    public static string? MainFolder(string app)
    {
        var folder=Path.TrimEndingDirectorySeparator(Path.GetFullPath(app));var name=Path.GetFileName(folder);var main=Backups.Replace(name,"");
        var path=Path.Combine(Path.GetDirectoryName(folder)??"",main);return main!=name&&main.Length>0&&File.Exists(Path.Combine(path,"Nashi.exe"))?path:null;
    }
    public static IReadOnlyList<string> Run(string app,DateTime now)
    {
        var folder=Path.TrimEndingDirectorySeparator(Path.GetFullPath(app));var parent=Path.GetDirectoryName(folder);if(parent is null||MainFolder(folder) is not null)return [];
        var name=Path.GetFileName(folder);bool Family(string candidate)=>candidate==name||candidate.StartsWith(name+".backup-",StringComparison.Ordinal)&&Backups.Replace(candidate,"")==name;
        var directories=new DirectoryInfo(parent).EnumerateDirectories().Where(d=>(d.Attributes&FileAttributes.ReparsePoint)==0&&d.FullName!=folder).ToList();
        // The backup with the newest version (then the newest files) is the way back; Nashi.exe marks a folder as a copy of the app.
        var backups=directories.Where(d=>d.Name!=name&&Family(d.Name)&&File.Exists(Path.Combine(d.FullName,"Nashi.exe"))).OrderByDescending(AppVersion).ThenByDescending(d=>File.GetLastWriteTimeUtc(Path.Combine(d.FullName,"Nashi.exe"))).Skip(1);
        // Staging folders go once nothing has touched them for ten minutes, if their request targeted this app (or one of its backups);
        // a download that never got as far as a request goes after a day.
        var stages=directories.Where(d=>Leftover.IsMatch(d.Name)&&now-Touched(d)>TimeSpan.FromMinutes(10)&&(d.Name.StartsWith(".nashi-delete-",StringComparison.Ordinal)||Target(d) is not {} target?now-Touched(d)>TimeSpan.FromDays(1):Path.GetDirectoryName(target)==parent&&Family(Path.GetFileName(target))));
        var removed=new List<string>();foreach(var directory in backups.Concat(stages).ToList())if(Remove(directory,parent))removed.Add(directory.FullName);return removed;
    }
    private static Version AppVersion(DirectoryInfo backup)=>Path.Combine(backup.FullName,"Nashi.dll") is var library&&File.Exists(library)&&Version.TryParse(FileVersionInfo.GetVersionInfo(library).FileVersion,out var version)?version:new();
    private static DateTime Touched(DirectoryInfo directory)=>directory.EnumerateFileSystemInfos().Select(e=>e.LastWriteTimeUtc).Append(directory.LastWriteTimeUtc).Max();
    private static string? Target(DirectoryInfo stage)
    {
        var request=Path.Combine(stage.FullName,"request.json");if(!File.Exists(request)||new FileInfo(request).Length>65536)return null;
        try{using var document=JsonDocument.Parse(File.ReadAllBytes(request));return document.RootElement.TryGetProperty("Target",out var target)&&target.GetString() is {Length:>0} path?Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)):null;}
        catch(Exception error)when(error is JsonException or IOException or ArgumentException or InvalidOperationException or NotSupportedException){return null;}
    }
    private static bool Remove(DirectoryInfo directory,string parent)
    {
        var doomed=directory.Name.StartsWith(".nashi-delete-",StringComparison.Ordinal)?directory.FullName:Path.Combine(parent,".nashi-delete-"+Guid.NewGuid().ToString("N"));
        try{if(doomed!=directory.FullName)Directory.Move(directory.FullName,doomed);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){return false;}
        // An interrupted removal keeps its .nashi-delete name and is finished on a later launch; extracted files may be read-only.
        try{Directory.Delete(doomed,true);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException)
        {
            try{foreach(var file in Directory.EnumerateFiles(doomed,"*",SearchOption.AllDirectories))File.SetAttributes(file,FileAttributes.Normal);Directory.Delete(doomed,true);}catch(Exception again)when(again is IOException or UnauthorizedAccessException){}
        }
        return true;
    }
}
