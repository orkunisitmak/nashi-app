using System.Text.Json;
using Nashi.Core;
internal static class UpdateCleanupChecks
{
    internal static void Run(Action<bool,string> check,string root)
    {
        static string Hex(char c)=>new(c,32);var now=DateTime.UtcNow;
        string App(string name,DateTime written){var folder=Path.Combine(root,name);Directory.CreateDirectory(folder);var exe=Path.Combine(folder,"Nashi.exe");File.WriteAllText(exe,"app");File.SetLastWriteTimeUtc(exe,written);return folder;}
        string Stage(string name,string? target,DateTime touched)
        {
            var folder=Path.Combine(root,name);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"update.zip"),"zip");
            if(target is not null)File.WriteAllText(Path.Combine(folder,"request.json"),JsonSerializer.Serialize(new{ProcessId=1,Target=target,Payload=Path.Combine(folder,"payload"),Stage=folder}));
            foreach(var entry in Directory.EnumerateFileSystemEntries(folder))File.SetLastWriteTimeUtc(entry,touched);Directory.SetLastWriteTimeUtc(folder,touched);return folder;
        }
        // Three updates in a row, one of them applied to a backup a shortcut still opened, as a Desktop install collected them.
        var app=App("Nashi",now);var oldest=App("Nashi.backup-"+Hex('a'),now.AddDays(-30));var newest=App("Nashi.backup-"+Hex('b'),now.AddDays(-2));var nested=App("Nashi.backup-"+Hex('b')+".backup-"+Hex('c'),now.AddDays(-10));
        var empty=Path.Combine(root,"Nashi.backup-"+Hex('d'));Directory.CreateDirectory(empty);var other=App("Other.backup-"+Hex('e'),now.AddDays(-30));var similar=App("Nashi Copy.backup-"+Hex('f'),now.AddDays(-30));
        var done=Stage(".nashi-update-"+Hex('1'),app,now.AddHours(-1));var fromBackup=Stage(".nashi-update-"+Hex('2'),newest,now.AddHours(-1));var running=Stage(".nashi-update-"+Hex('3'),app,now.AddMinutes(-2));
        var foreign=Stage(".nashi-update-"+Hex('4'),Path.Combine(root,"Other"),now.AddHours(-1));var abandoned=Stage(".nashi-update-"+Hex('5'),null,now.AddDays(-2));var recent=Stage(".nashi-update-"+Hex('6'),null,now.AddHours(-2));
        var odd=Path.Combine(root,".nashi-update-not-ours");Directory.CreateDirectory(odd);
        check(UpdateCleanup.MainFolder(newest)==app&&UpdateCleanup.MainFolder(nested)==app&&UpdateCleanup.MainFolder(app) is null&&UpdateCleanup.MainFolder(other) is null&&UpdateCleanup.Run(newest,now).Count==0,"A Nashi backup was not recognised, or cleaned up from inside a backup");
        var removed=UpdateCleanup.Run(app,now);
        check(Directory.Exists(app)&&Directory.Exists(newest)&&!Directory.Exists(oldest)&&!Directory.Exists(nested),"Update cleanup did not keep exactly the newest backup");
        check(!Directory.Exists(done)&&!Directory.Exists(fromBackup)&&!Directory.Exists(abandoned)&&Directory.Exists(running)&&Directory.Exists(foreign)&&Directory.Exists(recent),"Update cleanup removed staging still in use or another app's, or kept a finished one");
        check(Directory.Exists(empty)&&Directory.Exists(other)&&Directory.Exists(similar)&&Directory.Exists(odd)&&removed.Count==5&&!Directory.EnumerateDirectories(root,".nashi-delete-*").Any(),"Update cleanup touched folders that are not this app's leftovers: "+string.Join(", ",removed.Select(Path.GetFileName)));
        if(OperatingSystem.IsWindows())
        {
            // Windows refuses to rename a folder with an open file, so a Nashi still running from a backup keeps it whole.
            var busy=App("Nashi.backup-"+Hex('9'),now.AddDays(-40));
            using(new FileStream(Path.Combine(busy,"Nashi.exe"),FileMode.Open,FileAccess.Read,FileShare.Read))check(UpdateCleanup.Run(app,now).Count==0&&File.Exists(Path.Combine(busy,"Nashi.exe")),"Update cleanup removed a backup that was still in use");
            check(UpdateCleanup.Run(app,now).Count==1&&!Directory.Exists(busy)&&Directory.Exists(newest),"Update cleanup did not remove the backup once it was free");
        }
    }
}
