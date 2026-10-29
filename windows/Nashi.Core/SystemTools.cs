namespace Nashi.Core;

/// Linux desktops use their distribution's maintained packages instead of the
/// Windows upstream archives. Lookups never run a shell; they return only
/// absolute, executable regular files.
public static class SystemTools
{
    public static string ExecutableName(string name)=>OperatingSystem.IsWindows()?name+".exe":name;
    // Desktop launchers often start apps with a shorter PATH than a terminal, so
    // the usual package locations are probed too (Arch keeps exiftool in vendor_perl).
    private static readonly string[] StandardDirectories=["/usr/local/bin","/usr/bin","/bin","/usr/bin/vendor_perl","/usr/local/sbin","/snap/bin"];
    public static string? Find(string name)
    {
        if(OperatingSystem.IsWindows())return null;
        var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path=(Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries);
        foreach(var directory in path.Concat(StandardDirectories).Append(Path.Combine(home,".local","bin")).Append(Path.Combine(home,".deno","bin")).Distinct(StringComparer.Ordinal))
        {
            if(!Path.IsPathFullyQualified(directory))continue;
            var candidate=Path.Combine(directory,name);if(IsExecutable(candidate))return candidate;
        }
        return null;
    }
    public static bool IsExecutable(string path)
    {
        try
        {
            if(!File.Exists(path)||new FileInfo(path).Length==0)return false;if(OperatingSystem.IsWindows())return true;
            var resolved=File.ResolveLinkTarget(path,true)?.FullName??path;
            return File.Exists(resolved)&&(File.GetUnixFileMode(resolved)&(UnixFileMode.UserExecute|UnixFileMode.GroupExecute|UnixFileMode.OtherExecute))!=0;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){return false;}
    }
    /// LibreOffice's soffice wrapper, from a distribution package or the official
    /// /opt tarball install. Flatpak/Snap sandboxes cannot read Nashi's private temp profile.
    public static string? Office()
    {
        if(OperatingSystem.IsWindows())return null;
        var found=Find("soffice")??Find("libreoffice");if(found is not null)return found;
        var fixedPaths=new[]{"/usr/lib/libreoffice/program/soffice","/usr/lib64/libreoffice/program/soffice"};
        var opt=Directory.Exists("/opt")?Directory.EnumerateDirectories("/opt","libreoffice*").OrderDescending(StringComparer.Ordinal).Select(d=>Path.Combine(d,"program","soffice")):[];
        return fixedPaths.Concat(opt).FirstOrDefault(IsExecutable);
    }
    /// yt-dlp needs a JavaScript runtime for some sites. Deno is preferred as on
    /// Windows; distributions without a Deno package usually ship Node.js.
    public static string? JavaScriptRuntime()=>OperatingSystem.IsWindows()?null:Find("deno")??Find("node")??Find("nodejs");
    public static string Downloads()
    {
        var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);var fallback=Path.Combine(home,"Downloads");
        if(OperatingSystem.IsWindows()||OperatingSystem.IsMacOS())return fallback;
        // XDG user directories are localized, e.g. ~/İndirilenler on Turkish desktops.
        var config=Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");if(string.IsNullOrWhiteSpace(config)||!Path.IsPathFullyQualified(config))config=Path.Combine(home,".config");
        var file=Path.Combine(config,"user-dirs.dirs");
        try
        {
            if(!File.Exists(file)||new FileInfo(file).Length>65536)return fallback;
            foreach(var raw in File.ReadLines(file))
            {
                var line=raw.Trim();if(!line.StartsWith("XDG_DOWNLOAD_DIR=",StringComparison.Ordinal))continue;
                var value=line["XDG_DOWNLOAD_DIR=".Length..].Trim().Trim('"');
                if(value.StartsWith("$HOME",StringComparison.Ordinal))value=home+value[5..];
                if(value.Length>0&&Path.IsPathFullyQualified(value)&&Path.GetFullPath(value).TrimEnd('/')!=home.TrimEnd('/'))return Path.GetFullPath(value);
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
        return fallback;
    }
    public static StringComparer PathComparer=>OperatingSystem.IsWindows()||OperatingSystem.IsMacOS()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;

    public enum Family { Arch, Debian, Fedora, Suse, Unknown }
    public static Family Distribution(string? osRelease=null)
    {
        try{osRelease??=File.Exists("/etc/os-release")?File.ReadAllText("/etc/os-release"):"";}catch(Exception error)when(error is IOException or UnauthorizedAccessException){osRelease="";}
        var ids=osRelease.Split('\n').Where(l=>l.StartsWith("ID=",StringComparison.Ordinal)||l.StartsWith("ID_LIKE=",StringComparison.Ordinal)).SelectMany(l=>l[(l.IndexOf('=')+1)..].Trim().Trim('"').Split(' ',StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(ids.Overlaps(["arch","cachyos","manjaro","endeavouros","garuda","artix"]))return Family.Arch;
        if(ids.Overlaps(["debian","ubuntu","linuxmint","pop","elementary","zorin"]))return Family.Debian;
        if(ids.Overlaps(["fedora","rhel","centos","rocky","almalinux","nobara"]))return Family.Fedora;
        if(ids.Overlaps(["suse","opensuse","opensuse-tumbleweed","opensuse-leap","sles"]))return Family.Suse;
        return Family.Unknown;
    }
    /// The command a user can run to install missing tools. Nashi never runs it:
    /// installing system packages needs the user's own administrator approval.
    public static string InstallCommand(IEnumerable<string> tools,Family? family=null)
    {
        var target=family??Distribution();
        var packages=tools.Select(t=>(t,target) switch
        {
            ("ffmpeg" or "ffprobe",_)=>"ffmpeg",
            ("yt-dlp",_)=>"yt-dlp",
            ("deno",Family.Arch)=>"deno",("deno",_)=>"nodejs",
            ("exiftool",Family.Arch)=>"perl-image-exiftool",("exiftool",Family.Debian)=>"libimage-exiftool-perl",("exiftool",Family.Fedora)=>"perl-Image-ExifTool",("exiftool",_)=>"exiftool",
            ("office",Family.Arch)=>"libreoffice-fresh",("office",_)=>"libreoffice",
            _=>t
        }).Distinct(StringComparer.Ordinal).ToArray();
        if(packages.Length==0)return "";
        var list=string.Join(' ',packages);
        return target switch
        {
            Family.Arch=>"sudo pacman -S --needed "+list,
            Family.Debian=>"sudo apt install "+list,
            Family.Fedora=>"sudo dnf install "+list,
            Family.Suse=>"sudo zypper install "+list,
            _=>"Install with your package manager: "+list
        };
    }
}
