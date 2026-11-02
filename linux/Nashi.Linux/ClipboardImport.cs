using System.Text;
using Avalonia.Input.Platform;

namespace Nashi.Linux;

public static class ClipboardImport
{
    /// File references stay in place. Pasted content without a source gets a private, persistent import file.
    public static async Task<(IReadOnlyList<string> Files, string? Url)> ReadAsync(IClipboard clipboard)
    {
        var files = (await Desktop.ClipboardFilesAsync(clipboard)).Where(p => File.Exists(p) || Directory.Exists(p)).Distinct(StringComparer.Ordinal).ToArray();
        if (files.Length > 0) return (files, null);
        using var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is not null)
        {
            if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 25_000_000) throw new InvalidDataException("Paste an image smaller than 25 MP.");
            var folder = ImportFolder();
            var file = Path.Combine(folder, "Clipboard image " + Guid.NewGuid().ToString("N") + ".png");
            try { bitmap.Save(file); File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite); return ([file], null); }
            catch { File.Delete(file); throw; }
        }
        var text = await clipboard.TryGetTextAsync() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return ([], null);
        if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") return ([], uri.AbsoluteUri);
        return ([SaveText(text)], null);
    }

    internal static string SaveText(string text)
    {
        if (text.Length > 1_000_000) throw new InvalidDataException("Paste at most one million text characters.");
        var file = Path.Combine(ImportFolder(), "Clipboard text " + Guid.NewGuid().ToString("N") + ".txt");
        using var output = new FileStream(file, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        using var writer = new StreamWriter(output, new UTF8Encoding(false)); writer.Write(text); return file;
    }
    static string ImportFolder()
    {
        var folder = Path.Combine(AppPaths.Data, "Imports");
        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return folder;
    }
}
