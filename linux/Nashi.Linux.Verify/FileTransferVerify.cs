using Nashi.Windows;

internal static class FileTransferVerify
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var source=Path.Combine(root,"transfer-source");Directory.CreateDirectory(source);
        var empty=Path.Combine(source,"empty.txt");await File.WriteAllBytesAsync(empty,[]);
        using(var transaction=new Nashi.Core.ExportTransaction(Path.Combine(root,"empty-engine-output"))){
            var invalid=Path.Combine(transaction.StagingDirectory,"empty.png");await File.WriteAllBytesAsync(invalid,[]);
            try{transaction.Publish([invalid],default);throw new Exception("Empty engine export was accepted");}
            catch(InvalidDataException){check(true,"engine exports still reject empty files unless explicitly allowed");}
        }
        var copied=await FileTransfer.RunAsync([empty],Path.Combine(root,"empty-copy"),false,null,default);
        check(File.Exists(copied.Single())&&new FileInfo(copied.Single()).Length==0&&File.Exists(empty),"file copy preserves empty regular files");
        var moved=await FileTransfer.RunAsync([empty],Path.Combine(root,"empty-move"),true,null,default);
        check(File.Exists(moved.Single())&&!File.Exists(empty),"file move publishes an empty file before removing source");
        using(var cancellation=new CancellationTokenSource()){
            cancellation.Cancel();var destination=Path.Combine(root,"cancelled-file-move");
            try{await FileTransfer.RunAsync([moved.Single()],destination,true,null,cancellation.Token);throw new Exception("Cancelled move succeeded");}
            catch(OperationCanceledException){check(File.Exists(moved.Single())&&!Directory.EnumerateFileSystemEntries(destination).Any(),"cancelled file move preserves source and publishes nothing");}
        }
        if(!OperatingSystem.IsLinux())return;
        var script=Path.Combine(source,"run.sh");await File.WriteAllTextAsync(script,"#!/bin/sh\nexit 0\n");
        var mode=UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|UnixFileMode.GroupRead|UnixFileMode.GroupExecute;
        File.SetUnixFileMode(script,mode);var timestamp=new DateTime(2020,1,2,3,4,5,DateTimeKind.Utc);File.SetLastWriteTimeUtc(script,timestamp);
        copied=await FileTransfer.RunAsync([script],Path.Combine(root,"executable-copy"),false,null,default);
        check(File.GetUnixFileMode(copied.Single())==mode,"Linux file copy preserves executable and access permissions");
        check(File.GetLastWriteTimeUtc(copied.Single())==timestamp,"Linux file copy preserves modification time");
        if(Environment.UserName=="root"){Console.WriteLine("SKIP deletion denial fixture: root bypasses directory permissions");return;}
        var protectedDirectory=Path.Combine(root,"protected-original");Directory.CreateDirectory(protectedDirectory);
        var retained=Path.Combine(protectedDirectory,"retained.txt");await File.WriteAllTextAsync(retained,"keep original");
        var removable=Path.Combine(source,"removable.txt");await File.WriteAllTextAsync(removable,"move original");
        var directoryMode=File.GetUnixFileMode(protectedDirectory);File.SetUnixFileMode(protectedDirectory,UnixFileMode.UserRead|UnixFileMode.UserExecute);
        try{
            try{await FileTransfer.RunAsync([removable,retained],Path.Combine(root,"partial-move"),true,null,default);throw new Exception("Expected incomplete move");}
            catch(IncompleteMoveException error){
                check(error.Outputs.Count==2&&error.Outputs.All(File.Exists),"partial move reports every safely published output");
                check(error.RetainedOriginals.SequenceEqual([retained])&&File.ReadAllText(retained)=="keep original","partial move identifies and preserves undeletable originals");
                check(!File.Exists(removable),"partial move distinguishes successfully removed originals");
            }
        }finally{File.SetUnixFileMode(protectedDirectory,directoryMode);}
    }
}
