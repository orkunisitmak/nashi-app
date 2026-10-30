using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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

    /// Starts a program with an argument list in `folder` (null: here) without waiting; false when it is missing.
    /// `exited` (optional) gets its exit code later, on a pool thread. The checks swap in a recorder, so nothing is
    /// ever launched from them.
    public static Func<string, IReadOnlyList<string>, string?, Action<int>?, bool> Spawn { get; set; } = (program, args, folder, exited) =>
    {
        var process = new Process { StartInfo = new ProcessStartInfo(program, args) { UseShellExecute = false, WorkingDirectory = folder ?? "" }, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            using (process) exited?.Invoke(process.ExitCode);
        };
        try { if (process.Start()) return true; }
        catch (Win32Exception) { } // not installed
        process.Dispose();
        return false;
    };

    /// Fire-and-forget Spawn.
    public static bool Start(string program, IReadOnlyList<string> args, string? folder) => Spawn(program, args, folder, null);

    /// Runs a program with an argument list and waits: its stdout, or null when missing or failing (Integration.Run).
    public static Func<string, IReadOnlyList<string>, string?> Run { get; set; } = Integration.Run;

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
        window.Position = BottomCenter(screen.WorkingArea, PixelSize.FromSize(window.Bounds.Size, screen.Scaling), screen.Scaling);
    }

    public static PixelPoint BottomCenter(PixelRect area, PixelSize size, double scaling) =>
        new(area.X + (area.Width - size.Width) / 2, area.Bottom - size.Height - (int)(28 * scaling));

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

    /// The X server time in a startup-notification id ("…_TIME<n>"), or 0 (CurrentTime).
    public static uint StartupTime(string? startupId) =>
        startupId is not null && Regex.Match(startupId, @"_TIME(\d+)$") is { Success: true } m && uint.TryParse(m.Groups[1].Value, out var time) ? time : 0;

    /// X11: asks the window manager to activate `window` as a pager would (_NET_ACTIVE_WINDOW, source 2).
    public static void Activate(Window window, uint timestamp)
    {
        if (X11(window) is not var (display, xid)) return;
        var message = new IntPtr[24]; // XClientMessageEvent inside the 192-byte XEvent union
        message[0] = 33; // ClientMessage
        message[2] = 1; // send_event
        message[3] = display;
        message[4] = xid;
        message[5] = XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
        message[6] = 32; // format
        message[7] = 2; // source indication: pager
        message[8] = (IntPtr)timestamp;
        XSendEvent(display, XDefaultRootWindow(display), false, (1 << 20) | (1 << 19), message); // SubstructureRedirect | SubstructureNotify
        XFlush(display);
    }

    /// X11: a popup-menu keyboard and pointer grab (owner_events) on `window`, so keys reach it even when
    /// focus-stealing prevention refused activation. Returns the release, or null when not X11 or either grab failed
    /// (nothing is left grabbed then). Grabbed events go to the grabbing client, so this uses Avalonia's own connection.
    public static Action? Grab(Window window)
    {
        if (X11(window) is not var (display, xid)) return null;
        const int GrabModeAsync = 1, GrabSuccess = 0;
        var keyboard = XGrabKeyboard(display, xid, true, GrabModeAsync, GrabModeAsync, IntPtr.Zero) == GrabSuccess;
        // ButtonPress | ButtonRelease | EnterWindow | LeaveWindow | PointerMotion
        var pointer = XGrabPointer(display, xid, true, (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5) | (1 << 6), GrabModeAsync, GrabModeAsync, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == GrabSuccess;
        void Release()
        {
            if (keyboard) XUngrabKeyboard(display, IntPtr.Zero);
            if (pointer) XUngrabPointer(display, IntPtr.Zero);
            XFlush(display);
        }
        if (keyboard && pointer) return Release;
        Release();
        return null;
    }

    static (IntPtr Display, IntPtr Window)? X11(Window window)
    {
        if (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland") return null;
        try
        {
            return window.TryGetPlatformHandle() is { HandleDescriptor: "XID", Handle: var xid } && AvaloniaDisplay(window) is { } display && display != IntPtr.Zero
                ? (display, xid) : null;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    // ponytail: Avalonia 11.3 keeps its Display in the internal X11Window._x11.Display; reflection, pinned by the lock file.
    static IntPtr? AvaloniaDisplay(Window window)
    {
        var impl = window.PlatformImpl;
        var info = impl?.GetType().GetField("_x11", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(impl);
        return info?.GetType().GetProperty("Display", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(info) as IntPtr?;
    }

    [DllImport("libX11.so.6")] static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, IntPtr mask, IntPtr[] e);
    [DllImport("libX11.so.6")] static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XGrabKeyboard(IntPtr display, IntPtr window, bool ownerEvents, int pointerMode, int keyboardMode, IntPtr time);
    [DllImport("libX11.so.6")] static extern int XGrabPointer(IntPtr display, IntPtr window, bool ownerEvents, uint mask, int pointerMode, int keyboardMode, IntPtr confineTo, IntPtr cursor, IntPtr time);
    [DllImport("libX11.so.6")] static extern int XUngrabKeyboard(IntPtr display, IntPtr time);
    [DllImport("libX11.so.6")] static extern int XUngrabPointer(IntPtr display, IntPtr time);
    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")]
    static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
}
