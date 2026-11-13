namespace Nashi.Linux;

/// XDG locations. .NET maps LocalApplicationData to $XDG_DATA_HOME (~/.local/share), so Library
/// and preferences live in ~/.local/share/Nashi, outside the install folder, exactly as on Windows.
public static class AppPaths
{
    // Create: .NET returns "" for a folder that does not exist yet (a fresh account without ~/.local/share),
    // which would put Library and settings in whatever the current directory is.
    public static string Data=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolderOption.Create),"Nashi");
    public static string ToolsRoot=>Path.Combine(Data,"Tools");
    public static string Tools=>Path.Combine(ToolsRoot,"v1");
    public static string Downloads
    {
        get
        {
            // xdg-user-dirs knows the translated name, e.g. ~/İndirilenler on a Turkish desktop.
            var config=Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is {Length:>0} x?x:Path.Combine(Home,".config"),"user-dirs.dirs");
            if(File.Exists(config))
                foreach(var line in File.ReadLines(config))
                    if(line.StartsWith("XDG_DOWNLOAD_DIR=",StringComparison.Ordinal))
                    {
                        var value=line["XDG_DOWNLOAD_DIR=".Length..].Trim('"').Replace("$HOME",Home);
                        if(Path.IsPathFullyQualified(value))return value;
                    }
            return Path.Combine(Home,"Downloads");
        }
    }
    public static string Home=>Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
