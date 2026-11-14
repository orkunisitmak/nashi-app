using System.Text;
namespace Nashi.Linux;

/// Clipboard text as files, a link or a new text file, the same rules as on Windows.
public static class ClipboardImport
{
    /// File managers such as Nautilus and Dolphin copy file:// URIs (or plain paths) as text.
    public static string[] Paths(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return [];
        var lines=text.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Where(l=>l is not ("copy" or "cut")).ToArray();
        var paths=lines.Select(l=>Uri.TryCreate(l,UriKind.Absolute,out var uri)&&uri.IsFile?uri.LocalPath:l).ToArray();
        return paths.Length>0&&paths.All(p=>Path.IsPathFullyQualified(p)&&(File.Exists(p)||Directory.Exists(p)))?paths:[];
    }
    public static string? Link(string? text)=>text?.Trim() is {} t&&!t.Contains('\n')&&Uri.TryCreate(t,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http"?uri.AbsoluteUri:null;
    public static string SaveText(string text)
    {
        if(Encoding.UTF8.GetByteCount(text)>16*1024*1024)throw new InvalidOperationException("This text is too large to paste. Choose a text file instead.");
        var path=Destination("txt");File.WriteAllText(path,text,new UTF8Encoding(false));return path;
    }
    public static string Destination(string extension)
    {
        var folder=Path.Combine(LinuxPaths.DataDirectory,"Imports");Directory.CreateDirectory(folder);
        return Path.Combine(folder,$"Clipboard {DateTime.Now:yyyy-MM-dd HH-mm-ss}-{Guid.NewGuid().ToString("N")[..6]}.{extension}");
    }
}
