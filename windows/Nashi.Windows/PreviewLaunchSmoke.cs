namespace Nashi.Windows;

/// Preview instance must be independent of an installed Nashi application.
internal static class PreviewLaunchSmoke
{
    internal static void Run(Action<bool,string> check)
    {
        if(!PreviewLaunch.Enabled)return;
        check(PreviewLaunch.InstanceName.StartsWith("Nashi.FluentPreview.",StringComparison.Ordinal) &&
              !PreviewLaunch.InstanceName.Contains("Desktop",StringComparison.Ordinal),
              "Visual preview owns an independent single-instance channel");
        check(PreviewLaunch.DataDirectory.EndsWith("Nashi.FluentPreview",StringComparison.Ordinal) &&
              PreviewLaunch.DataDirectory!=Path.Combine(Environment.GetFolderPath(
                  Environment.SpecialFolder.LocalApplicationData),"Nashi"),
              "Visual preview stores Library and preferences away from the installed app");
        var installedTools=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools");
        check(PreviewLaunch.ToolsDirectory==Path.Combine(PreviewLaunch.DataDirectory,"Tools") &&
              !string.Equals(PreviewLaunch.ToolsDirectory,installedTools,StringComparison.OrdinalIgnoreCase),
              "Visual preview isolates downloaded media, office and metadata tools from official Nashi");
        // A synthetic installed tool directory on PATH must never satisfy preview readiness.
        var root=Path.Combine(Path.GetTempPath(),"Nashi-Preview-PATH-"+Guid.NewGuid().ToString("N"));
        var installed=Path.Combine(root,"installed");
        var isolated=Path.Combine(root,"preview");
        Directory.CreateDirectory(installed);
        var previous=Environment.GetEnvironmentVariable("PATH");
        try
        {
            foreach(var name in new[]{"ffmpeg","ffprobe","yt-dlp","deno"})
                File.WriteAllBytes(Path.Combine(installed,name+".exe"),[0x4d,0x5a,0x01]);
            Environment.SetEnvironmentVariable("PATH",installed+Path.PathSeparator+previous);
            var previewTools=PreviewLaunch.CreateMediaToolchain(isolated);
            check(!previewTools.Ready,
                "Visual preview must not reuse unrelated media executables found on PATH");
            // Guard the actual installer entry point; no network or real update is needed.
            Exception? updateFailure=null;
            try { UpdateInstaller.StageAsync(null!,null!,CancellationToken.None).GetAwaiter().GetResult(); }
            catch(Exception error) { updateFailure=error; }
            check(updateFailure is InvalidOperationException{Message:"Updates are disabled in Fluent Preview."},
                "Visual preview must refuse staging any official app update before touching files");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH",previous);
            Directory.Delete(root,true);
        }
    }
}
