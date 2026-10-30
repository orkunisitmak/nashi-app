using Avalonia.Input.Platform;
using Nashi.Core;

namespace Nashi.Linux;

/// The Actions wheel (catalog `fileActions`). Programs start through Desktop.Start and Desktop.Run, with argument lists.
public static class FileActions
{
    /// Resources/FeatureCatalog.json `fileActions`, in order; the checks compare the two.
    public static readonly string[] Ids = ["share", "float", "copy", "move", "copyPath", "terminal"];

    /// Float is not built on Linux yet: its petal stays disabled.
    public static bool Available(string id) => id != "float";

    /// "Share" on Linux (there is no AirDrop); the other titles as macOS has them.
    public static string Title(string id) => id switch
    {
        "share" => "Share",
        "float" => "Float",
        "copy" => "Copy",
        "move" => "Move",
        "copyPath" => "Copy Path",
        _ => "Terminal",
    };

    /// Copies into `folder` with unique names; originals are kept. On a failure the copies made so far are removed.
    public static IReadOnlyList<string> CopyTo(IReadOnlyList<string> files, string folder)
    {
        var made = new List<string>();
        try
        {
            foreach (var file in files)
            {
                var target = SafePaths.Unique(folder, Path.GetFileName(file));
                File.Copy(file, target);
                made.Add(target);
            }
            return made;
        }
        catch
        {
            foreach (var copy in made) File.Delete(copy);
            throw;
        }
    }

    /// Moves into `folder` with unique names, all or nothing: on a failure the files moved so far go back.
    /// A file already in `folder` stays where it is.
    public static IReadOnlyList<string> MoveTo(IReadOnlyList<string> files, string folder)
    {
        var moved = new List<(string From, string To)>();
        var into = Path.GetFullPath(folder).TrimEnd('/');
        try
        {
            foreach (var file in files.Select(Path.GetFullPath))
            {
                if (Path.GetDirectoryName(file) == into) { moved.Add((file, file)); continue; }
                var target = SafePaths.Unique(into, Path.GetFileName(file));
                File.Move(file, target); // across file systems .NET copies, then deletes
                moved.Add((file, target));
            }
            return moved.Select(m => m.To).ToList();
        }
        catch
        {
            // ponytail: a rollback move that itself fails leaves that file in `folder` (never lost); add a report if it matters.
            foreach (var (from, to) in Enumerable.Reverse(moved).Where(m => m.From != m.To))
                try { File.Move(to, from); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// The full paths, one per line, put on the clipboard as text (when there is one); returns the text.
    public static async Task<string> CopyPathsAsync(IClipboard? clipboard, IReadOnlyList<string> files)
    {
        var text = string.Join("\n", files);
        if (clipboard is not null) await clipboard.SetTextAsync(text);
        return text;
    }

    /// The system terminal in the file's folder: the first of these that starts.
    public static bool OpenTerminal(string file)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        return Desktop.Start("x-terminal-emulator", [], folder)
            || Desktop.Start("gnome-terminal", ["--working-directory=" + folder], folder)
            || Desktop.Start("konsole", ["--workdir", folder], folder)
            || Desktop.Start("xfce4-terminal", ["--working-directory=" + folder], folder);
    }

    /// Share, the Linux way: the file manager opens with the files selected (FileManager1.ShowItems), so they can be
    /// dragged into a chat or mail; without that service, the first file's folder opens.
    public static bool Share(IReadOnlyList<string> files)
    {
        // dbus-send splits arrays on commas; EscapeDataString escapes them (and #, %, spaces) in each segment.
        var uris = string.Join(",", files.Select(f => "file://" + string.Join("/", Path.GetFullPath(f).Split('/').Select(Uri.EscapeDataString))));
        return Desktop.Run("dbus-send", ["--session", "--print-reply", "--reply-timeout=2000", "--dest=org.freedesktop.FileManager1", "--type=method_call",
                "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems", "array:string:" + uris, "string:"]) is not null
            || Desktop.Start("xdg-open", [Path.GetDirectoryName(Path.GetFullPath(files[0]))!], null);
    }
}
