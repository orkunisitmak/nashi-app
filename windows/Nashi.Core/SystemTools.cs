namespace Nashi.Core;

/// Resolves executables without a shell; current-directory PATH entries are ignored.
public static class SystemTools
{
    public static string? Find(string name,string? searchPath=null)
    {
        if (name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            throw new ArgumentException("Use an executable name.", nameof(name));
        foreach (var directory in (searchPath??Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            if (OperatingSystem.IsWindows() || (File.GetUnixFileMode(path) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0) return path;
        }
        return null;
    }
}
