using System.Collections.Concurrent;
using Nashi.Core;
namespace Nashi.Linux;

/// A watched-folder rule: new files with these extensions in this folder become `Format`.
public sealed record FolderRule(string Folder,string[] Extensions,string Format,bool Enabled=true);

/// Converts new files that land in watched folders (for example HEIC photos synced into
/// ~/Downloads become JPG) while Nashi runs, in the tray too. Originals are never changed; each
/// result is recorded in Library. Partial downloads, hidden files, Nashi's own staging folders
/// and files Nashi just made are ignored, so a rule can never feed itself.
public sealed class FolderWatcher:IDisposable
{
    private readonly List<FileSystemWatcher> watchers=[];private readonly ConcurrentDictionary<string,byte> seen=new();
    private readonly Func<string,string,CancellationToken,Task<IReadOnlyList<string>>> convert;private readonly Action<FolderRule,IReadOnlyList<string>,string?> done;
    private readonly SemaphoreSlim queue=new(1);private readonly CancellationTokenSource stopping=new();
    public FolderWatcher(IEnumerable<FolderRule> rules,Func<string,string,CancellationToken,Task<IReadOnlyList<string>>> convert,Action<FolderRule,IReadOnlyList<string>,string?> done)
    {
        this.convert=convert;this.done=done;
        foreach(var rule in rules.Where(r=>r.Enabled&&Directory.Exists(r.Folder)))
        {
            var watcher=new FileSystemWatcher(rule.Folder){IncludeSubdirectories=false,NotifyFilter=NotifyFilters.FileName|NotifyFilters.Size|NotifyFilters.LastWrite};
            watcher.Created+=(_,e)=>Consider(rule,e.FullPath);watcher.Renamed+=(_,e)=>Consider(rule,e.FullPath);
            watcher.EnableRaisingEvents=true;watchers.Add(watcher);
        }
    }
    public int Count=>watchers.Count;
    public static bool Matches(FolderRule rule,string path)
    {
        var name=Path.GetFileName(path);
        if(name.StartsWith('.')||name.EndsWith(".part",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".crdownload",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase))return false;
        var ext=FeatureCatalog.Extension(path);
        return ext!=rule.Format&&rule.Extensions.Any(e=>FeatureCatalog.Extension("x."+e.Trim().TrimStart('.'))==ext);
    }
    private void Consider(FolderRule rule,string path)
    {
        if(!Matches(rule,path)||!seen.TryAdd(path,0))return;
        _=Task.Run(async()=>
        {
            try
            {
                // Wait until the file stops growing (a copy or download still in progress).
                long last=-1;for(var n=0;n<120;n++){await Task.Delay(700,stopping.Token);if(!File.Exists(path))return;var size=new FileInfo(path).Length;if(size>0&&size==last)break;last=size;}
                await queue.WaitAsync(stopping.Token);
                try
                {
                    var outputs=await convert(path,rule.Format,stopping.Token);
                    foreach(var output in outputs)seen.TryAdd(output,0);
                    done(rule,outputs,null);
                }
                catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnauthorizedAccessException){done(rule,[],error.Message);}
                finally{queue.Release();}
            }
            catch(OperationCanceledException){}
        });
    }
    public void Dispose(){stopping.Cancel();foreach(var watcher in watchers)watcher.Dispose();watchers.Clear();}
}
