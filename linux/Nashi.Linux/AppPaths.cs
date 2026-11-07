using Nashi.Core;

namespace Nashi.Linux;

public static class AppPaths
{
    /// Where the checks keep settings, the shelf and the library instead of the user's data folder.
    public static string? DataOverride { get; set; }

    public static string Data
    {
        get
        {
            if (DataOverride is { } data) return data;
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

    /// Media tools and Deno for Pages (the Toolchain download).
    public static bool MediaAndDenoReady(Toolchain tools) => MediaReady(tools) && Deno(tools) is not null;

    /// What the setup window's download brings: media tools, Deno for Pages and ExifTool for Metadata.
    public static bool DownloadsReady(Toolchain tools) => MediaAndDenoReady(tools) && Metadata().Executable is not null;

    /// Where ExifTool lives (MetadataService's pinned 13.59 under the tools folder). The checks point it elsewhere.
    public static string? MetadataRoot { get; set; }

    /// ExifTool, for the Metadata editor; Executable is null until setup downloads it.
    public static MetadataService Metadata() => new(MetadataRoot ?? Tools);

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
