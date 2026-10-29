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
        // A Setup-installed Nashi must never folder-swap: the replacement
        // payload carries no uninstaller and would leave the registry entry
        // dangling. Installed copies update by silently rerunning Setup.
        if(InstalledBySetup())
        {
            if(update.SetupUrl is null)throw new InvalidDataException("This Nashi was installed with Setup, but the update feed does not carry a Setup package yet. Download the latest Nashi Setup from the release page.");
            await StageSetupAsync(update,progress,token);return;
        }
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
            token.ThrowIfCancellationRequested();var start=new ProcessStartInfo(Path.Combine(worker,"Nashi.exe")){UseShellExecute=false,WorkingDirectory=worker};start.ArgumentList.Add("--apply-update");start.ArgumentList.Add(fileName);_ = Process.Start(start)??throw new IOException("Cannot start update installer.");
        }
        catch{Directory.Delete(stage,true);throw;}
    }
    public static int Apply(string requestFile)
    {
        var request=JsonSerializer.Deserialize<InstallRequest>(File.ReadAllText(requestFile))??throw new InvalidDataException("No install request.");
        var stage=Path.GetFullPath(request.Stage);var target=Path.GetFullPath(request.Target);var payload=Path.GetFullPath(request.Payload);
        if(!Path.GetFileName(stage).StartsWith(".nashi-update-",StringComparison.Ordinal)||Path.GetDirectoryName(stage)!=Path.GetDirectoryName(target)||payload!=Path.Combine(stage,"payload")||Path.GetFullPath(requestFile)!=Path.Combine(stage,"request.json"))throw new InvalidDataException("Invalid install paths.");
        try{var old=Process.GetProcessById(request.ProcessId);if(!old.WaitForExit(60000))throw new IOException("Nashi is still running.");}catch(ArgumentException){}
        var backup=target+".backup-"+Guid.NewGuid().ToString("N");
        Directory.Move(target,backup);
        try{Directory.Move(payload,target);}catch{Directory.Move(backup,target);throw;}
        Process.Start(new ProcessStartInfo(Path.Combine(target,"Nashi.exe")){UseShellExecute=true,WorkingDirectory=target});
        // Backups remain recoverable; the app never recursively deletes a user's
        // portable directory merely because an update succeeded.
        return 0;
    }
    private static bool InstalledBySetup()
    {
        // The Setup marker belongs to the user account, not to this executable:
        // a portable copy on the same machine must keep updating itself. The
        // registered InstallPath has to match the running directory and the
        // uninstaller has to sit beside the app.
        try
        {
            using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Nashi");
            if(!string.Equals(key?.GetValue("InstallMode") as string,"setup",StringComparison.OrdinalIgnoreCase))return false;
            if(key?.GetValue("InstallPath") is not string installedPath||!SameDirectory(installedPath,AppContext.BaseDirectory))return false;
            return File.Exists(Path.Combine(AppContext.BaseDirectory,"uninst.exe"));
        }
        catch(Exception error)when(error is System.Security.SecurityException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException){return false;}
    }
    private static bool SameDirectory(string left,string right)
    {
        string Normalize(string value){var full=Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);return OperatingSystem.IsWindows()?full.ToLowerInvariant():full;}
        return Normalize(left)==Normalize(right);
    }
    private static async Task StageSetupAsync(WindowsUpdate update,IProgress<OperationProgress> progress,CancellationToken token)
    {
        var stage=Path.Combine(Path.GetTempPath(),".nashi-setup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try
        {
            var installer=Path.Combine(stage,"Nashi-Setup-x64.exe");
            await VerifiedDownload.FetchAsync(new(update.SetupUrl!),installer,update.SetupLength,p=>progress.Report(new(p*.9,"Downloading Nashi "+update.Version+"…")),token);
            progress.Report(new(.94,"Verifying update signature…"));await PublicUpdates.VerifyFileAsync(installer,update.SetupLength,update.SetupEdSignature!,token);
            // The signature proves who signed the package, not that it carries
            // the announced version; never launch an older signed Setup.
            var expected=Version.Parse(update.Version);var expectedFileVersion=new Version(expected.Major,expected.Minor,Math.Max(0,expected.Build),Math.Max(0,expected.Revision));
            if(!Version.TryParse(FileVersionInfo.GetVersionInfo(installer).FileVersion,out var parsed)||parsed!=expectedFileVersion)throw new InvalidDataException("The signed Setup package version does not match this update.");
            progress.Report(new(.97,"Installing Nashi "+update.Version+"…"));
            // The Setup runs silently, waits for this app to exit, installs over
            // the registered folder and reopens Nashi.
            var start=new ProcessStartInfo(installer){UseShellExecute=false};start.ArgumentList.Add("/S");
            _ = Process.Start(start)??throw new IOException("Cannot start the Nashi Setup.");
        }
        catch{try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}throw;}
    }
    private sealed record InstallRequest(int ProcessId,string Target,string Payload,string Stage);
}
