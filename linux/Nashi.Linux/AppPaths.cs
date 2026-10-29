using Nashi.Core;

namespace Nashi.Linux;

public static class AppPaths
{
    public static string Data
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var root = !string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg) ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(root, "Nashi");
        }
    }

    public static string Tools => Path.Combine(Data, "tools");

    /// Downloaded tools first, then whatever is on PATH, so a system ffmpeg is reused.
    public static Toolchain CreateToolchain() => new(Tools, Environment.GetEnvironmentVariable("PATH"));

    /// What media work needs (OperationRunner): ffmpeg and ffprobe, not the full Toolchain.Ready set.
    public static bool MediaReady(Toolchain tools)
    {
        try { tools.Executable("ffmpeg"); tools.Executable("ffprobe"); return true; }
        catch (FileNotFoundException) { return false; }
    }
}
