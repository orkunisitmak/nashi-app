using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Nashi.Core;
namespace Nashi.Windows;
internal static class UpdateInstaller
{
    public static Version Current=>Assembly.GetExecutingAssembly().GetName().Version??new(0,7,0);
    public static void CleanupStale()
    {
        var target=Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var parent=Path.GetDirectoryName(target);if(parent is null)return;
        try
        {
            foreach(var directory in Directory.EnumerateDirectories(parent,".nashi-update-*"))
            {
                try { if(DateTime.UtcNow-Directory.GetLastWriteTimeUtc(directory)>TimeSpan.FromHours(24))Directory.Delete(directory,true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
    public static async Task StageAsync(WindowsUpdate update,IProgress<OperationProgress> progress,CancellationToken token)
    {
        var target=Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);var parent=Path.GetDirectoryName(target)??throw new IOException("Extract Nashi into its own writable folder first.");
        if(target==Path.GetPathRoot(target)||(File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)throw new IOException("Use a regular writable folder for portable updates.");
        var stage=AtomicFile.CreateTemporaryDirectory(".nashi-update-",parent);
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
            token.ThrowIfCancellationRequested();
            var start=new ProcessStartInfo(Path.Combine(worker,"Nashi.exe")){UseShellExecute=false,WorkingDirectory=worker};
            start.ArgumentList.Add("--apply-update");start.ArgumentList.Add(fileName);
            using var installer=Process.Start(start)??throw new IOException("Cannot start update installer.");
        }
        catch
        {
            try { Directory.Delete(stage, true); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            throw;
        }
    }
    public static int Apply(string requestFile)
    {
        var request=JsonSerializer.Deserialize<InstallRequest>(File.ReadAllText(requestFile))??throw new InvalidDataException("No install request.");
        var stage=Path.GetFullPath(request.Stage);var target=Path.GetFullPath(request.Target);var payload=Path.GetFullPath(request.Payload);
        if(!Path.GetFileName(stage).StartsWith(".nashi-update-",StringComparison.Ordinal)||Path.GetDirectoryName(stage)!=Path.GetDirectoryName(target)||payload!=Path.Combine(stage,"payload")||Path.GetFullPath(requestFile)!=Path.Combine(stage,"request.json"))throw new InvalidDataException("Invalid install paths.");
        try{using var old=Process.GetProcessById(request.ProcessId);if(!old.WaitForExit(60000))throw new IOException("Nashi is still running.");}catch(ArgumentException){}
        var backup=target+".backup-"+Guid.NewGuid().ToString("N");
        Directory.Move(target,backup);
        try{Directory.Move(payload,target);}catch(Exception payloadError)
        {
            try
            {
                if(Directory.Exists(target))Directory.Delete(target,true);
                Directory.Move(backup,target);
            }
            catch(Exception restoreError)
            {
                throw new IOException("Nashi could not install the update, and the previous installation could not be restored from "+backup+".",new AggregateException(payloadError,restoreError));
            }
            throw new IOException("Nashi could not install the update; the previous installation was restored.",payloadError);
        }
        Process.Start(new ProcessStartInfo(Path.Combine(target,"Nashi.exe")){UseShellExecute=true,WorkingDirectory=target});
        // Backups remain recoverable; the app never recursively deletes a user's
        // portable directory merely because an update succeeded.
        return 0;
    }
    private sealed record InstallRequest(int ProcessId,string Target,string Payload,string Stage);
}
