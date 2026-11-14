using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Nashi.Core;
namespace Nashi.Windows;
internal static class UpdateInstaller
{
    public static Version Current=>Assembly.GetExecutingAssembly().GetName().Version??new(0,7,0);
    public static async Task StageAsync(WindowsUpdate update,IProgress<OperationProgress> progress,CancellationToken token)
    {
        var target=Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);var parent=Path.GetDirectoryName(target)??throw new IOException("Extract Nashi into its own writable folder first.");
        if(target==Path.GetPathRoot(target)||(File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)throw new IOException("Use a regular writable folder for portable updates.");
        var stage=Path.Combine(parent,".nashi-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try
        {
            var archive=Path.Combine(stage,"update.zip");await VerifiedDownload.FetchAsync(new(update.Url),archive,update.Length,p=>progress.Report(new(p*.85,"Downloading Nashi "+update.Version+"…")),token);
            progress.Report(new(.86,"Verifying update signature…"));await PublicUpdates.VerifyAsync(archive,update,token);
            var payload=Path.Combine(stage,"payload");Directory.CreateDirectory(payload);PublicUpdates.ExtractVerified(archive,payload,token);
            var version=FileVersionInfo.GetVersionInfo(Path.Combine(payload,"Nashi.dll")).FileVersion;
            var expected=Version.Parse(update.Version);var expectedFileVersion=new Version(expected.Major,expected.Minor,Math.Max(0,expected.Build),Math.Max(0,expected.Revision));
            if(!Version.TryParse(version,out var parsed)||parsed!=expectedFileVersion)throw new InvalidDataException("The signed package version does not match this update.");
            var worker=Path.Combine(stage,"worker");Directory.CreateDirectory(worker);
            foreach(var file in Directory.EnumerateFiles(payload))File.Copy(file,Path.Combine(worker,Path.GetFileName(file)));
            var request=new InstallRequest(Environment.ProcessId,target,payload,stage);
            var fileName=Path.Combine(stage,"request.json");await File.WriteAllTextAsync(fileName,JsonSerializer.Serialize(request),token);
            token.ThrowIfCancellationRequested();var start=new ProcessStartInfo(Path.Combine(worker,"Nashi.exe")){UseShellExecute=false,WorkingDirectory=worker};start.ArgumentList.Add("--apply-update");start.ArgumentList.Add(fileName);(Process.Start(start)??throw new IOException("Cannot start update installer.")).Dispose();
        }
        catch{Directory.Delete(stage,true);throw;}
    }
    public static int Apply(string requestFile)
    {
        var request=JsonSerializer.Deserialize<InstallRequest>(File.ReadAllText(requestFile))??throw new InvalidDataException("No install request.");
        var stage=Path.GetFullPath(request.Stage);var target=Path.GetFullPath(request.Target);var payload=Path.GetFullPath(request.Payload);
        if(!Path.GetFileName(stage).StartsWith(".nashi-update-",StringComparison.Ordinal)||Path.GetDirectoryName(stage)!=Path.GetDirectoryName(target)||payload!=Path.Combine(stage,"payload")||Path.GetFullPath(requestFile)!=Path.Combine(stage,"request.json"))throw new InvalidDataException("Invalid install paths.");
        try{using var old=Process.GetProcessById(request.ProcessId);if(!old.WaitForExit(60000))throw new IOException("Nashi is still running.");}catch(ArgumentException){}
        var backup=target+".backup-"+Guid.NewGuid().ToString("N");
        Directory.Move(target,backup);
        // The newest backup is kept by version; the move time breaks ties between equal versions.
        try{Directory.SetLastWriteTimeUtc(backup,DateTime.UtcNow);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
        try{Directory.Move(payload,target);}catch{Directory.Move(backup,target);throw;}
        Process.Start(new ProcessStartInfo(Path.Combine(target,"Nashi.exe")){UseShellExecute=true,WorkingDirectory=target})?.Dispose();
        // The previous version remains recoverable. The next launch removes this
        // staging folder and only backups older than the newest one (see UpdateLeftovers).
        return 0;
    }
    /// <summary>Removes staging folders and older backups once the updated app has settled.</summary>
    public static void CleanLeftoversLater(TimeSpan? delay=null)
    {
        var target=Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if(App.IsSmokeTest||target==Path.GetPathRoot(target))return;
        _=Task.Run(async()=>
        {
            // The update worker starts this process and exits right after; give it time to release its folder.
            await Task.Delay(delay??TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            try{UpdateLeftovers.Clean(target,BackupVersion,InUse);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){/* Retried on the next launch. */}
        });
    }
    internal static Version? BackupVersion(string folder)
    {
        var dll=Path.Combine(folder,"Nashi.dll");if(!File.Exists(dll)||!File.Exists(Path.Combine(folder,"Nashi.exe")))return null;
        return Version.TryParse(FileVersionInfo.GetVersionInfo(dll).FileVersion,out var version)?version:null;
    }
    internal static bool InUse(string folder)
    {
        var prefix=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))+Path.DirectorySeparatorChar;
        foreach(var process in Process.GetProcessesByName("Nashi"))
        {
            using(process)
            {
                // A process whose path cannot be read is treated as using the folder.
                try{if(process.MainModule?.FileName is not {} path||path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return true;}
                catch(Exception error)when(error is System.ComponentModel.Win32Exception or InvalidOperationException){return true;}
            }
        }
        return false;
    }
    private sealed record InstallRequest(int ProcessId,string Target,string Payload,string Stage);
}
