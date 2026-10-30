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

    /// What the setup window's download brings: media tools plus Deno for Pages.
    public static bool DownloadsReady(Toolchain tools) => MediaReady(tools) && Deno(tools) is not null;

    /// Every tool Nashi uses is in place: the downloads and the system LibreOffice.
    public static bool AllReady(Toolchain tools) => DownloadsReady(tools) && Office() is not null;

    /// Deno runs the Pages bridge; Toolchain finds a downloaded one first, then one on PATH.
    public static string? Deno(Toolchain tools)
    {
        try { return tools.Executable("deno"); }
        catch (FileNotFoundException) { return null; }
    }

    /// Where Office() looks for LibreOffice. The checks point it at a test folder.
    public static string? OfficeSearchPath { get; set; } = Environment.GetEnvironmentVariable("PATH");

    /// The system LibreOffice: soffice or libreoffice on PATH. Nashi does not download it (there is no portable Linux build).
    /// Snap and Flatpak builds are skipped: their sandbox has its own /tmp, where each job's private profile lives.
    public static string? Office()
    {
        foreach (var folder in (OfficeSearchPath ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Path.IsPathFullyQualified(folder)) continue;
            foreach (var name in new[] { "soffice", "libreoffice" })
            {
                var path = Path.Combine(folder, name);
                try
                {
                    var target = new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? path;
                    if (target.StartsWith("/snap/", StringComparison.Ordinal) || target.Contains("/flatpak/", StringComparison.Ordinal)) continue;
                    if (ToolInstallation.UsableFile(target) && (File.GetUnixFileMode(target) & UnixFileMode.UserExecute) != 0) return path;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return null;
    }
}
