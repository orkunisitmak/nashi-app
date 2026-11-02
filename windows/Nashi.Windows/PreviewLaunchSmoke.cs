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
    }
}
