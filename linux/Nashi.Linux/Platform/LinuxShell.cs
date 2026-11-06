using System.Diagnostics;
using Nashi.Core;
namespace Nashi.Linux;

/// Desktop integration through freedesktop.org conventions, so it works the same on
/// GNOME, KDE Plasma, Xfce, Cinnamon and other desktops, on X11 and Wayland.
public static class LinuxShell
{
    /// KWin's "Legacy applications (X11): apply scaling themselves" sets [Xwayland] Scale in kwinrc;
    /// X11 coordinates are then logical pixels times that. Other compositors: 1.
    public static double XwaylandScale()
    {
        try
        {
            var path=Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is {Length:>0} c?c:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".config"),"kwinrc");
            if(!File.Exists(path))return 1;var inside=false;
            foreach(var line in File.ReadLines(path))
            {
                var text=line.Trim();if(text.StartsWith('[')){inside=text=="[Xwayland]";continue;}
                if(inside&&text.StartsWith("Scale=",StringComparison.Ordinal)&&double.TryParse(text[6..],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var scale)&&scale>0)return scale;
            }
        }
        catch(IOException){}
        return 1;
    }
    public static string? FindInPath(string name)
    {
        foreach(var entry in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            if(!Path.IsPathFullyQualified(entry))continue;var path=Path.Combine(entry,name);
            if(File.Exists(path)&&ToolInstallation.CanExecute(path))return path;
        }
        return null;
    }
    private static void Start(string executable,params string[] arguments)
    {
        var info=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in arguments)info.ArgumentList.Add(argument);
        var process=Process.Start(info)??throw new IOException("Could not start "+Path.GetFileName(executable)+".");
        // Drain and reap in the background so no zombie or blocked pipe is left behind.
        _=Task.Run(async()=>{try{await process.StandardOutput.ReadToEndAsync();await process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();}finally{process.Dispose();}});
    }
    /// Opens a file, folder or http(s) link with the user's default application.
    public static void Open(string target)
    {
        var opener=FindInPath("xdg-open")??throw new InvalidOperationException("Install xdg-utils to open files from Nashi.");
        Start(opener,target);
    }
    /// Shows the file selected in the file manager (Files, Dolphin, Nemo, Thunar…) when it supports
    /// org.freedesktop.FileManager1; otherwise opens its folder.
    public static void Reveal(string path)
    {
        var full=Path.GetFullPath(path);
        if(FindInPath("gdbus") is {} gdbus)
        {
            try
            {
                using var process=Process.Start(new ProcessStartInfo(gdbus){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,ArgumentList={"call","--session","--dest","org.freedesktop.FileManager1","--object-path","/org/freedesktop/FileManager1","--method","org.freedesktop.FileManager1.ShowItems","['"+new Uri(full).AbsoluteUri.Replace("'","%27")+"']",""}});
                if(process is not null&&process.WaitForExit(3000)&&process.ExitCode==0)return;
            }
            catch(Exception error)when(error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception){}
        }
        Open(Directory.Exists(full)?full:Path.GetDirectoryName(full)!);
    }
    /// Opens a terminal in the folder. $TERMINAL wins, then the desktop's own terminal.
    public static void Terminal(string path)
    {
        var folder=Directory.Exists(path)?path:Path.GetDirectoryName(Path.GetFullPath(path))!;
        (string Name,string[] Arguments)[] candidates=
        [
            (Environment.GetEnvironmentVariable("TERMINAL")??"",[]),
            ("kgx",["--working-directory="+folder]),("gnome-terminal",["--working-directory="+folder]),("konsole",["--workdir",folder]),
            ("xfce4-terminal",["--working-directory="+folder]),("mate-terminal",["--working-directory="+folder]),("tilix",["--working-directory="+folder]),
            ("ptyxis",["--working-directory="+folder]),("kitty",["--directory",folder]),("alacritty",["--working-directory",folder]),("wezterm",["start","--cwd",folder]),
            ("foot",["--working-directory="+folder]),("x-terminal-emulator",[]),("xterm",[])
        ];
        foreach(var (name,arguments) in candidates)
        {
            if(string.IsNullOrWhiteSpace(name))continue;
            var executable=Path.IsPathFullyQualified(name)?name:FindInPath(name);if(executable is null)continue;
            var info=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=folder};foreach(var argument in arguments)info.ArgumentList.Add(argument);
            Process.Start(info)?.Dispose();return;
        }
        throw new InvalidOperationException("No terminal app was found. Set the TERMINAL environment variable to your terminal.");
    }
    /// "sudo apt install …" for the running distribution, used in setup hints.
    public static string InstallCommand(params string[] packages)
    {
        var release=ReadOsRelease();var ids=(release.GetValueOrDefault("ID","")+" "+release.GetValueOrDefault("ID_LIKE","")).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        string Join(IEnumerable<string> names)=>string.Join(' ',names);
        if(ids.Any(i=>i is "arch"))return "sudo pacman -S "+Join(packages.Select(p=>p switch{"tesseract-ocr"=>"tesseract tesseract-data-eng tesseract-data-tur","perl-image-exiftool"=>"perl-image-exiftool",_=>p}));
        if(ids.Any(i=>i is "fedora" or "rhel" or "centos"))return "sudo dnf install "+Join(packages.Select(p=>p switch{"tesseract-ocr"=>"tesseract tesseract-langpack-tur","perl-image-exiftool"=>"perl-Image-ExifTool",_=>p}));
        if(ids.Any(i=>i is "opensuse" or "suse"))return "sudo zypper install "+Join(packages.Select(p=>p switch{"tesseract-ocr"=>"tesseract-ocr tesseract-ocr-traineddata-turkish","perl-image-exiftool"=>"exiftool",_=>p}));
        return "sudo apt install "+Join(packages.Select(p=>p switch{"tesseract-ocr"=>"tesseract-ocr tesseract-ocr-tur","perl-image-exiftool"=>"libimage-exiftool-perl",_=>p}));
    }
    private static Dictionary<string,string> ReadOsRelease()
    {
        var values=new Dictionary<string,string>();
        foreach(var file in new[]{"/etc/os-release","/usr/lib/os-release"})
        {
            if(!File.Exists(file))continue;
            foreach(var line in File.ReadLines(file)){var split=line.IndexOf('=');if(split>0)values[line[..split]]=line[(split+1)..].Trim('"').ToLowerInvariant();}
            break;
        }
        return values;
    }
}

/// LibreOffice from the distribution. The Windows build downloads its own copy; on Linux every
/// distribution packages LibreOffice, and Nashi only runs its headless --convert-to filters.
/// Snap and Flatpak builds are skipped: their sandbox cannot read Nashi's private temp folders.
public sealed class LinuxOffice
{
    public string? Executable
    {
        get
        {
            foreach(var name in new[]{"soffice","libreoffice"})
                if(LinuxShell.FindInPath(name) is {} path&&!path.StartsWith("/snap/",StringComparison.Ordinal)&&!path.Contains("/flatpak/",StringComparison.Ordinal))return path;
            string[] fixedPaths=["/usr/lib/libreoffice/program/soffice","/usr/lib64/libreoffice/program/soffice","/usr/local/lib/libreoffice/program/soffice"];
            var opt=Directory.Exists("/opt")?Directory.GetDirectories("/opt","libreoffice*").OrderDescending().Select(d=>Path.Combine(d,"program","soffice")):[];
            return fixedPaths.Concat(opt).FirstOrDefault(p=>File.Exists(p)&&ToolInstallation.CanExecute(p));
        }
    }
    public static string InstallHint=>LinuxShell.InstallCommand("libreoffice");
}
