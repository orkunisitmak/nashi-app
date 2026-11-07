using Avalonia.Input;
using Avalonia.Input.Platform;
using Nashi.Core;

namespace Nashi.Linux;

/// The Actions wheel (catalog `fileActions`). Programs start through Desktop.Start and Desktop.Run, with argument lists.
public static class FileActions
{
    /// Resources/FeatureCatalog.json `fileActions`, in order; the checks compare the two.
    public static readonly string[] Ids = ["share", "float", "copy", "move", "copyPath", "terminal"];

    /// Every action is available (Float opens the shelf).
    public static bool Available(string id) => true;

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

    private static class Copy
    {
        public const string NoClipboard = "Couldn’t reach the clipboard.";
    }

    /// What Move did: the files now in the folder, and how many were already there (left alone, not moved).
    public sealed record Moved(IReadOnlyList<string> Files, int AlreadyThere);

    /// How long a terminal gets to fail (a broken alternative exits at once) before it counts as open.
    public static TimeSpan TerminalSettle { get; set; } = TimeSpan.FromSeconds(1.5);

    /// Copies into `folder` with unique names; originals are kept. On a failure the copies made so far, and a
    /// half-written one, are removed.
    public static IReadOnlyList<string> CopyTo(IReadOnlyList<string> files, string folder)
    {
        ValidateTargets(files, folder);
        var made = new List<string>();
        var staging = Path.Combine(folder, ".nashi-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var entries = 0;
        long bytes = 0;
        void CopyTree(string source, string target, int depth)
        {
            if (depth > 64 || ++entries > 10000 || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Copy regular files and folders without links (10,000 item limit).");
            if (Directory.Exists(source))
            {
                Directory.CreateDirectory(target);
                foreach (var child in Directory.EnumerateFileSystemEntries(source)) CopyTree(child, Path.Combine(target, Path.GetFileName(child)), depth + 1);
                File.SetUnixFileMode(target, File.GetUnixFileMode(source));
                Directory.SetLastWriteTimeUtc(target, Directory.GetLastWriteTimeUtc(source));
            }
            else
            {
                bytes = checked(bytes + new FileInfo(source).Length);
                if (bytes > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Copy up to 4 GB per task.");
                CopyFile(source, target);
            }
        }
        try
        {
            foreach (var file in files)
            {
                var target = SafePaths.Unique(folder, Path.GetFileName(file));
                var staged = Path.Combine(staging, Guid.NewGuid().ToString("N"));
                CopyTree(file, staged, 0);
                if (Directory.Exists(staged)) Directory.Move(staged, target); else File.Move(staged, target);
                made.Add(target);
            }
            return made;
        }
        catch
        {
            foreach (var copy in made) RemoveCopy(copy);
            throw;
        }
        finally { if (Directory.Exists(staging)) RemoveCopy(staging); }
    }

    // Copied folders can inherit read-only modes. Restore access on our own copies before rollback.
    static void RemoveCopy(string path)
    {
        if (!Directory.Exists(path)) { File.Delete(path); return; }
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var child in Directory.EnumerateFileSystemEntries(path)) RemoveCopy(child);
        Directory.Delete(path);
    }

    static void ValidateTargets(IReadOnlyList<string> sources, string folder)
    {
        var into = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(into)) throw new DirectoryNotFoundException("Choose an existing destination folder.");
        foreach (var source in sources)
        {
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            if (Directory.Exists(path) && (into == path || into.StartsWith(path + "/", StringComparison.Ordinal)))
                throw new IOException("Choose a destination outside the selected folder.");
        }
    }

    public sealed class PartialMoveException(IReadOnlyList<string> remaining, Exception cause)
        : IOException("Move could not finish or fully roll back. Files remain at: " + string.Join("; ", remaining), cause)
    { public IReadOnlyList<string> Remaining { get; } = remaining; }

    // CreateNew, so a target that appeared meanwhile is never touched, and one this wrote is ours to delete.
    private static void CopyFile(string source, string target)
    {
        using var input = File.OpenRead(source);
        var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920);
        try
        {
            using (output) input.CopyTo(output);
            File.SetUnixFileMode(target, File.GetUnixFileMode(source));
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        }
        catch
        {
            File.Delete(target);
            throw;
        }
    }

    /// Moves into `folder` with unique names, all or nothing: on a failure the files moved so far go back, and a
    /// half-copied target (a move across file systems that failed midway) is removed. A file already in `folder`
    /// stays where it is and counts as already there.
    public static Moved MoveTo(IReadOnlyList<string> files, string folder)
    {
        ValidateTargets(files, folder);
        var moved = new List<(string From, string To)>();
        var into = Path.GetFullPath(folder).TrimEnd('/');
        var already = 0;
        try
        {
            foreach (var file in files.Select(Path.GetFullPath))
            {
                if (Path.GetDirectoryName(file) == into) { already++; continue; }
                var target = SafePaths.Unique(into, Path.GetFileName(file));
                if (Directory.Exists(file)) Directory.Move(file, target);
                else File.Move(file, target); // never delete a target we did not create
                moved.Add((file, target));
            }
            return new(moved.Select(m => m.To).ToList(), already);
        }
        catch (Exception cause)
        {
            var remaining = new List<string>();
            foreach (var (from, to) in Enumerable.Reverse(moved))
                try { if (Directory.Exists(to)) Directory.Move(to, from); else File.Move(to, from); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { remaining.Add(to); }
            if (remaining.Count > 0)
            {
                History.Record("Move needs attention", remaining);
                throw new PartialMoveException(remaining, cause);
            }
            throw;
        }
    }

    /// The full paths, one per line, put on the clipboard as text; returns the text. No clipboard is an error.
    public static async Task<string> CopyPathsAsync(IClipboard? clipboard, IReadOnlyList<string> files)
    {
        if (clipboard is null) throw new InvalidOperationException(Copy.NoClipboard);
        var text = string.Join("\n", files);
        await clipboard.SetTextAsync(text);
        return text;
    }

    /// Files on the clipboard as GNOME file managers paste them (copy, not cut), with a uri-list and the paths as text.
    public static async Task CopyFilesAsync(IClipboard? clipboard, IReadOnlyList<string> files)
    {
        if (clipboard is null) throw new InvalidOperationException(Copy.NoClipboard);
        var uris = files.Select(f => new Uri(f).AbsoluteUri).ToList();
        var item = new DataTransferItem();
        item.Set(DataFormat.CreateStringPlatformFormat("x-special/gnome-copied-files"), "copy\n" + string.Join("\n", uris));
        item.Set(DataFormat.CreateStringPlatformFormat("text/uri-list"), string.Join("\r\n", uris) + "\r\n");
        item.SetText(string.Join("\n", files));
        var data = new DataTransfer();
        data.Add(item);
        await clipboard.SetDataAsync(data);
    }

    /// The system terminal in the file's folder: the first of these that starts and does not fail within
    /// TerminalSettle (blocks for up to that long per candidate; call it off the UI thread).
    public static bool OpenTerminal(string file)
    {
        var folder = Directory.Exists(file) ? Path.GetFullPath(file) : Path.GetDirectoryName(Path.GetFullPath(file))!;
        return Terminal("x-terminal-emulator", [], folder)
            || Terminal("gnome-terminal", ["--working-directory=" + folder], folder)
            || Terminal("konsole", ["--workdir", folder], folder)
            || Terminal("xfce4-terminal", ["--working-directory=" + folder], folder);
    }

    // Still running, or done with 0 (gnome-terminal hands the window to its server and exits), counts as open.
    private static bool Terminal(string program, IReadOnlyList<string> args, string folder)
    {
        var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Desktop.Spawn(program, args, folder, code => exit.TrySetResult(code))) return false;
        return !exit.Task.Wait(TerminalSettle) || exit.Task.Result == 0;
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
