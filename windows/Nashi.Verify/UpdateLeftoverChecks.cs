using Nashi.Core;
internal static class UpdateLeftoverChecks
{
    internal static void Run(Action<bool,string> check,string root)
    {
        var parent=Path.Combine(root,"Update leftovers");Directory.CreateDirectory(parent);
        try
        {
            string Id()=>Guid.NewGuid().ToString("N");
            string Folder(string name,string? version=null)
            {
                var path=Path.Combine(parent,name);Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"Nashi.exe"),"app");
                if(version is not null)File.WriteAllText(Path.Combine(path,"version.txt"),version);
                return path;
            }
            Version? ReadVersion(string path){var file=Path.Combine(path,"version.txt");return File.Exists(file)?Version.Parse(File.ReadAllText(file)):null;}
            var target=Folder("Nashi");File.WriteAllText(Path.Combine(target,"settings I keep.txt"),"keep");
            var stage=Folder(".nashi-update-"+Id());Directory.CreateDirectory(Path.Combine(stage,"worker"));File.WriteAllText(Path.Combine(stage,"update.zip"),"zip");
            var busyStage=Folder(".nashi-update-"+Id());
            var oldest=Folder("Nashi.backup-"+Id(),"0.7.10.0");var older=Folder("Nashi.backup-"+Id(),"0.7.12.0");var newest=Folder("Nashi.backup-"+Id(),"0.7.13.0");
            var unknown=Folder("Nashi.backup-"+Id());
            var lookalikes=new[]{Folder(".nashi-update-not-a-guid","0.7.1.0"),Folder("Nashi.backup-"+Id()+"-copy","0.7.1.0"),Folder("Other.backup-"+Id(),"0.7.1.0"),Folder(".nashi-update-"+Id().ToUpperInvariant())};
            var outside=Path.Combine(parent+" outside");Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"precious.txt"),"keep");
            string? linkedStage=null;string? linkedBackup=null;
            try
            {
                linkedStage=Path.Combine(parent,".nashi-update-"+Id());Directory.CreateSymbolicLink(linkedStage,outside);
                linkedBackup=Path.Combine(parent,"Nashi.backup-"+Id());Directory.CreateSymbolicLink(linkedBackup,outside);
                // A link inside a removed staging folder is removed without entering it.
                Directory.CreateSymbolicLink(Path.Combine(stage,"worker","link"),outside);
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){linkedStage=linkedBackup=null;/* This account may not create links. */}
            var result=UpdateLeftovers.Clean(target+Path.DirectorySeparatorChar,ReadVersion,path=>path==busyStage);
            check(!Directory.Exists(stage)&&result.Removed.Contains(stage),"Finished update staging folders must be removed");
            check(Directory.Exists(busyStage)&&result.Kept.Contains(busyStage),"A staging folder a running process uses must be kept");
            check(Directory.Exists(newest)&&result.Kept.Contains(newest),"The newest previous version must remain recoverable");
            check(!Directory.Exists(oldest)&&!Directory.Exists(older),"Only the newest backup is kept; older backups must be removed");
            check(Directory.Exists(unknown),"A backup without a readable Nashi version must not be deleted");
            check(lookalikes.All(Directory.Exists),"Folders that only resemble update leftovers must not be deleted");
            check(File.ReadAllText(Path.Combine(target,"settings I keep.txt"))=="keep","Cleaning must not change the running app folder");
            check(File.ReadAllText(Path.Combine(outside,"precious.txt"))=="keep","Cleaning must never follow a link out of the update folders");
            if(linkedStage is not null)check(Directory.Exists(linkedStage)&&Directory.Exists(linkedBackup!),"Linked update-looking folders must be left alone");
            var again=UpdateLeftovers.Clean(target,ReadVersion,_=>false);
            check(!Directory.Exists(busyStage)&&Directory.Exists(newest)&&again.Removed.Count==1,"A skipped staging folder is removed on a later launch, and cleanup is repeatable");
            File.WriteAllText(Path.Combine(older=Folder("Nashi.backup-"+Id(),"0.7.13.0"),"marker"),"tie");Directory.SetLastWriteTimeUtc(newest,DateTime.UtcNow.AddMinutes(-5));Directory.SetLastWriteTimeUtc(older,DateTime.UtcNow);
            UpdateLeftovers.Clean(target,ReadVersion,_=>false);
            check(Directory.Exists(older)&&!Directory.Exists(newest),"Backups of the same version keep the one moved aside last");
            check(UpdateLeftovers.Clean(Path.Combine(root,"missing","Nashi"),ReadVersion,_=>false).Removed.Count==0,"A missing parent folder must be ignored");
            check(UpdateLeftovers.Clean(target,_=>throw new IOException("locked"),_=>false).Kept.Count==0&&Directory.Exists(older),"An unreadable backup version must keep the backup");
        }
        finally
        {
            foreach(var link in Directory.EnumerateDirectories(parent).Where(d=>(File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0))Directory.Delete(link);
            Directory.Delete(parent,true);
            if(Directory.Exists(parent+" outside"))Directory.Delete(parent+" outside",true);
        }
    }
}
