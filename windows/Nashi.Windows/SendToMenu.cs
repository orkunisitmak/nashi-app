namespace Nashi.Windows;
/// <summary>
/// Explorer › right-click › Send to › Nashi. The Send to menu is made of shortcuts in a folder of the
/// user's profile, so this needs no registry entry and no administrator rights: turning the option on
/// creates one shortcut, turning it off removes it. Explorer passes every selected file to one launch,
/// and Nashi opens the tools wheel for them.
/// </summary>
internal static class SendToMenu
{
    internal const string Argument="--send-to";
    // A pipe message element that can never be a path, so an older running Nashi simply ignores it.
    internal const string Marker="nashi:send-to";
    internal const string Title="Show Nashi in Explorer's Send to menu";
    internal const string Hint="Select files in Explorer, right-click, then Send to › Nashi to open the tools wheel. No registry change; turning this off removes the shortcut.";
    internal static string Folder{get;set;}=Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
    internal static string Shortcut=>Path.Combine(Folder,"Nashi.lnk");

    /// <summary>On when Nashi's own shortcut is in the Send to folder.</summary>
    internal static bool Enabled=>File.Exists(Shortcut)&&Ours(Shortcut);

    internal static void Set(bool enabled,string? executable=null)
    {
        executable??=Environment.ProcessPath??Path.Combine(AppContext.BaseDirectory,"Nashi.exe");
        if(enabled){Directory.CreateDirectory(Folder);ShellShortcut.Create(Shortcut,executable,Argument,executable);return;}
        // Only Nashi's own shortcut is removed; another "Nashi.lnk" the user made stays.
        if(File.Exists(Shortcut)&&Ours(Shortcut))File.Delete(Shortcut);
    }

    /// <summary>Points an existing shortcut at this copy of Nashi, for example after the folder moved.</summary>
    internal static void Refresh(string? executable=null)
    {
        executable??=Environment.ProcessPath;
        if(executable is null||!Enabled)return;
        if(!string.Equals(ShellShortcut.Resolve(Shortcut),executable,StringComparison.OrdinalIgnoreCase))Set(true,executable);
    }

    /// <summary>Separates the Send to request from the paths a launch received.</summary>
    internal static (bool Wheel,string[] Paths) Split(IEnumerable<string> items)
    {
        var list=items.ToList();var wheel=list.RemoveAll(i=>i is Argument or Marker)>0;return (wheel,list.ToArray());
    }

    private static bool Ours(string shortcut)
    {
        var target=ShellShortcut.Resolve(shortcut);
        return !string.Equals(target,shortcut,StringComparison.OrdinalIgnoreCase)&&string.Equals(Path.GetFileName(target),"Nashi.exe",StringComparison.OrdinalIgnoreCase)&&ShellShortcut.Arguments(shortcut)==Argument;
    }
}
