using System.Diagnostics;
using Nashi.Core;
namespace Nashi.Linux.UI;

/// freedesktop.org integration: xdg-open, the FileManager1 D-Bus interface that
/// Nautilus, Dolphin, Nemo, Thunar and Caja implement, and the user's terminal.
internal static class Desktop
{
    /// Accepts plain paths and file:// URIs (file managers and %U pass either).
    internal static string? LocalPath(string value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;var text=value.Trim();
        if(text.StartsWith("file://",StringComparison.OrdinalIgnoreCase)&&Uri.TryCreate(text,UriKind.Absolute,out var uri)&&uri.IsFile)text=uri.LocalPath;
        if(text.StartsWith('-'))return null;
        try{var full=Path.GetFullPath(text);return File.Exists(full)||Directory.Exists(full)?full:null;}catch(Exception error)when(error is ArgumentException or NotSupportedException or PathTooLongException){return null;}
    }
    // wait: true reports whether the call itself succeeded (D-Bus methods), not only that it started.
    private static bool Start(string? executable,string[] arguments,bool wait=false)
    {
        if(executable is null)return false;
        try
        {
            var info=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardError=wait,RedirectStandardOutput=wait};foreach(var argument in arguments)info.ArgumentList.Add(argument);
            using var process=Process.Start(info);if(process is null)return false;if(!wait)return true;
            return process.WaitForExit(4000)&&process.ExitCode==0;
        }
        catch(Exception error)when(error is System.ComponentModel.Win32Exception or InvalidOperationException){return false;}
    }
    internal static void Open(string pathOrUrl){if(!Start(SystemTools.Find("xdg-open"),[pathOrUrl]))Start(SystemTools.Find("gio"),["open",pathOrUrl]);}
    internal static void Reveal(string path)
    {
        var uri=new Uri(Path.GetFullPath(path)).AbsoluteUri;
        if(Start(SystemTools.Find("gdbus"),["call","--session","--dest","org.freedesktop.FileManager1","--object-path","/org/freedesktop/FileManager1","--method","org.freedesktop.FileManager1.ShowItems","['"+uri.Replace("'","%27")+"']",""],true))return;
        if(Start(SystemTools.Find("dbus-send"),["--session","--print-reply","--dest=org.freedesktop.FileManager1","--type=method_call","/org/freedesktop/FileManager1","org.freedesktop.FileManager1.ShowItems","array:string:"+uri,"string:"],true))return;
        Open(Directory.Exists(path)?path:Path.GetDirectoryName(path)!);
    }
    /// $TERMINAL first, then the default terminals of the common desktops.
    internal static bool Terminal(string directory)
    {
        var folder=Directory.Exists(directory)?directory:Path.GetDirectoryName(directory)!;
        var preferred=Environment.GetEnvironmentVariable("TERMINAL");
        var candidates=new List<(string Name,string[] Args)>();
        if(!string.IsNullOrWhiteSpace(preferred)&&!preferred.Contains(' '))candidates.Add((preferred,[]));
        candidates.AddRange([("kgx",["--working-directory",folder]),("gnome-terminal",["--working-directory="+folder]),("konsole",["--workdir",folder]),("xfce4-terminal",["--working-directory="+folder]),("kitty",["--directory",folder]),("alacritty",["--working-directory",folder]),("wezterm",["start","--cwd",folder]),("foot",["--working-directory="+folder]),("tilix",["--working-directory="+folder]),("xterm",[])]);
        foreach(var (name,args) in candidates)
        {
            var executable=Path.IsPathFullyQualified(name)?name:SystemTools.Find(name);if(executable is null)continue;
            try{var info=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=folder};foreach(var a in args)info.ArgumentList.Add(a);using var process=Process.Start(info);if(process is not null)return true;}
            catch(Exception error)when(error is System.ComponentModel.Win32Exception or InvalidOperationException){}
        }
        return false;
    }
}
