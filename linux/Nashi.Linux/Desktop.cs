using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace Nashi.Linux;

/// The desktop around Nashi: the X11 pointer, on-screen placement, and file paths on the clipboard.
public static class Desktop
{
    /// The global pointer position on X11; null on Wayland or without an X display.
    public static PixelPoint? Pointer()
    {
        if (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland") return null;
        try
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return null;
            try { return XQueryPointer(display, XDefaultRootWindow(display), out _, out _, out var x, out var y, out _, out _, out _) != 0 ? new PixelPoint(x, y) : null; }
            finally { XCloseDisplay(display); }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    /// Top-left for a window of `size` centered on `center`, kept fully inside `area`.
    public static PixelPoint Place(PixelPoint center, PixelSize size, PixelRect area) =>
        new(Clamp(center.X - size.Width / 2, area.X, area.Right - size.Width), Clamp(center.Y - size.Height / 2, area.Y, area.Bottom - size.Height));

    static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));

    /// The screen under the pointer, else the primary one (null when there are no screens, as in headless runs).
    public static Screen? ScreenAt(TopLevel top, PixelPoint? pointer) =>
        (pointer is { } p ? top.Screens?.ScreenFromPoint(p) : null) ?? top.Screens?.Primary;

    /// Bottom-center of the screen under the pointer, 28 px above its bottom edge: where the macOS toasts sit.
    public static void PlaceAtBottom(Window window)
    {
        if (ScreenAt(window, Pointer()) is not { } screen) return;
        var area = screen.WorkingArea;
        var size = PixelSize.FromSize(window.Bounds.Size, screen.Scaling);
        window.Position = new PixelPoint(area.X + (area.Width - size.Width) / 2, area.Bottom - size.Height - (int)(28 * screen.Scaling));
    }

    /// Local files on the clipboard: Avalonia's file list, else the GNOME and uri-list text forms, else plain text paths.
    public static async Task<IReadOnlyList<string>> ClipboardFilesAsync(IClipboard clipboard)
    {
        var files = (await clipboard.TryGetFilesAsync())?.Select(i => i.TryGetLocalPath()).OfType<string>().ToList();
        if (files is { Count: > 0 }) return files;
        foreach (var format in new[] { "x-special/gnome-copied-files", "text/uri-list" })
            if (await clipboard.TryGetValueAsync(DataFormat.CreateStringPlatformFormat(format)) is { } text && ClipboardPaths(text) is { Count: > 0 } found) return found;
        return ClipboardPaths(await clipboard.TryGetTextAsync() ?? "");
    }

    /// Paths in clipboard text: file:// URIs (percent-decoded) and absolute paths, one per line.
    /// The "copy"/"cut" header, comments and other URIs are ignored.
    public static IReadOnlyList<string> ClipboardPaths(string text)
    {
        var paths = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r', '\0');
            if (line.StartsWith('/')) paths.Add(line);
            else if (line.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile && uri.Host is "" or "localhost")
                paths.Add(Uri.UnescapeDataString(uri.AbsolutePath));
        }
        return paths;
    }

    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")]
    static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
}
