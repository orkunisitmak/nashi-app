// Adapted from Filee (MIT, © KnifeLemon and contributors): src/Filee.App/Services/Triggers/GestureDetector.cs,
// src/Filee.Core/Settings/TriggerGesture.cs and src/Filee.Platform.Linux/X11WindowSystem.cs (ProcessNameAt).
// Nashi keeps the drag gesture only, with one configurable key, and reads the pointer by polling X11 instead of a
// global hook: nothing is grabbed, so a drag without the key (or dropped anywhere but the wheel) is untouched.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Nashi.Linux;

/// Modifier keys; left and right count the same.
[Flags]
public enum Modifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Super = 8 }

/// Turns pointer samples into "a drag with the gesture key started" and "that drag ended". Thread-safe; pure.
/// Drag:  press, move past the threshold with exactly the key held, over a drag source, while an XDND drag runs →
///        DragStarted → release → DragEnded. The key may be pressed before the press or during the drag (late, as
///        people do to copy in a file manager); released before the threshold, nothing fires until it is held again.
public sealed class GestureDetector
{
    private readonly object gate = new();
    private (int X, int Y)? press;
    private bool? source;
    private bool active;

    /// The gesture key; None turns gestures off.
    public Modifiers Key { get; set; } = Modifiers.Ctrl;

    /// Physical pixels the pointer must travel from the press before it counts as a drag.
    public int Threshold { get; set; } = 8;

    /// Whether the press point is over a window drags come from (a file manager, the desktop, a browser); asked once
    /// per press.
    public Func<int, int, bool> Source { get; set; } = (_, _) => true;

    /// Whether a drag-and-drop is running right now (asked when everything else fits).
    public Func<bool> Dragging { get; set; } = () => true;

    public bool Paused { get; set; }

    public event Action<int, int>? DragStarted;
    public event Action<int, int>? DragEnded;

    public bool Active { get { lock (gate) return active; } }

    public void Down(int x, int y)
    {
        lock (gate) (press, source, active) = ((x, y), null, false);
    }

    /// The pointer moved with the button held.
    public void Move(int x, int y, Modifiers modifiers)
    {
        var fire = false;
        lock (gate)
        {
            if (press is not { } at || active || Paused || Key == Modifiers.None || modifiers != Key) return;
            if (Math.Abs(x - at.X) <= Threshold && Math.Abs(y - at.Y) <= Threshold) return;
            source ??= Source(at.X, at.Y);
            fire = active = source == true && Dragging();
        }
        if (fire) DragStarted?.Invoke(x, y);
    }

    public void Up(int x, int y)
    {
        bool ended;
        lock (gate)
        {
            ended = active;
            (press, source, active) = (null, null, false);
        }
        if (ended) DragEnded?.Invoke(x, y);
    }

    /// "Ctrl", "Ctrl+Shift", … (the Settings values) as Modifiers; None for "Off" or anything else.
    public static Modifiers Parse(string? value)
    {
        var result = Modifiers.None;
        foreach (var part in (value ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<Modifiers>(part, out var key) || key == Modifiers.None) return Modifiers.None;
            result |= key;
        }
        return result;
    }
}

/// Feeds a GestureDetector from pointer samples (position, X11 button and modifier mask), as a poll of the X server
/// gives them. A Tab press is reported once (the wheel flips sides) while `WatchFlip` is on.
public sealed class PointerSampler(GestureDetector detector)
{
    private bool down, tab;
    public bool WatchFlip { get; set; }
    public event Action? Flip;

    public const uint ShiftMask = 1, ControlMask = 4, Mod1Mask = 8, Mod4Mask = 64, Button1Mask = 256;

    public static Modifiers ModifiersOf(uint mask) =>
        ((mask & ShiftMask) != 0 ? Modifiers.Shift : 0) | ((mask & ControlMask) != 0 ? Modifiers.Ctrl : 0)
        | ((mask & Mod1Mask) != 0 ? Modifiers.Alt : 0) | ((mask & Mod4Mask) != 0 ? Modifiers.Super : 0);

    public void Sample(int x, int y, uint mask, bool tabDown = false)
    {
        var held = (mask & Button1Mask) != 0;
        var modifiers = ModifiersOf(mask);
        if (held && !down) detector.Down(x, y);
        if (held) detector.Move(x, y, modifiers);
        if (!held && down) detector.Up(x, y);
        down = held;
        if (tabDown && !tab && WatchFlip) Flip?.Invoke();
        tab = tabDown;
    }
}

/// The X11 side: polls the pointer every 20 ms on its own connection and thread (no grab, no hook, no synthetic
/// input), and says whether a point is over a window a drag gesture may start from. Off on Wayland.
public sealed class PointerWatch : IDisposable
{
    /// Where a file or browser drag comes from: file managers, the GNOME desktop icons (gjs) and browsers.
    public static readonly HashSet<string> Sources = new(StringComparer.OrdinalIgnoreCase)
    {
        "nautilus", "nemo", "caja", "thunar", "dolphin", "pcmanfm", "pcmanfm-qt", "spacefm", "doublecmd", "krusader", "gjs",
        "chrome", "google-chrome", "chromium", "chromium-browser", "firefox", "firefox-bin", "msedge", "microsoft-edge", "brave", "brave-browser", "vivaldi-bin", "opera",
    };

    private readonly CancellationTokenSource stop = new();
    public PointerSampler Sampler { get; }

    public PointerWatch(GestureDetector detector)
    {
        Sampler = new PointerSampler(detector);
        detector.Source = (x, y) => ProcessNameAt(x, y) is { } name && Sources.Contains(name);
        detector.Dragging = DragRunning;
        new Thread(Run) { IsBackground = true, Name = "Nashi pointer" }.Start();
    }

    public static bool Available => Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") != "wayland"
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    private void Run()
    {
        IntPtr display;
        try { display = XOpenDisplay(IntPtr.Zero); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return; }
        if (display == IntPtr.Zero) return;
        try
        {
            var root = XDefaultRootWindow(display);
            var tabCode = XKeysymToKeycode(display, 0xff09); // XK_Tab
            var keys = new byte[32];
            while (!stop.IsCancellationRequested)
            {
                if (XQueryPointer(display, root, out _, out _, out var x, out var y, out _, out _, out var mask) != 0)
                {
                    var tab = Sampler.WatchFlip && XQueryKeymap(display, keys) != 0 && (keys[tabCode / 8] & (1 << (tabCode % 8))) != 0;
                    Sampler.Sample(x, y, mask, tab);
                }
                stop.Token.WaitHandle.WaitOne(20);
            }
        }
        finally { XCloseDisplay(display); }
    }

    /// An XDND drag is running: some client owns XdndSelection while the button is held.
    private static bool DragRunning() => WithDisplay(display => XGetSelectionOwner(display, XInternAtom(display, "XdndSelection", false)) != IntPtr.Zero);

    /// The process (or WM_CLASS) of the client window under a screen point.
    public static string? ProcessNameAt(int x, int y) => WithDisplay(display =>
    {
        var root = XDefaultRootWindow(display);
        var window = root;
        string? name = null;
        for (var depth = 0; depth < 32; depth++)
        {
            name = ProcessName(display, window) ?? name;
            if (XTranslateCoordinates(display, root, window, x, y, out _, out _, out var child) == 0 || child == IntPtr.Zero) break;
            window = child;
        }
        return name;
    });

    private static T? WithDisplay<T>(Func<IntPtr, T> work)
    {
        try
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return default;
            try { return work(display); }
            finally { XCloseDisplay(display); }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return default; }
    }

    private static string? ProcessName(IntPtr display, IntPtr window)
    {
        if (Property(display, window, "_NET_WM_PID", 1) is { Format: 32, Data: { Length: >= 8 } pid })
        {
            try
            {
                using var process = Process.GetProcessById((int)BitConverter.ToInt64(pid));
                return process.ProcessName;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { }
        }
        return Property(display, window, "WM_CLASS", 256) is { Format: 8, Data: var bytes }
            ? Encoding.UTF8.GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() : null;
    }

    /// A window property: format and raw bytes (format-32 items are C longs, 8 bytes each), or null.
    internal static (int Format, byte[] Data)? Property(IntPtr display, IntPtr window, string name, long length)
    {
        var atom = XInternAtom(display, name, true);
        if (atom == IntPtr.Zero || XGetWindowProperty(display, window, atom, 0, (IntPtr)length, false, IntPtr.Zero, out _, out var format, out var count, out _, out var data) != 0) return null;
        try
        {
            if (data == IntPtr.Zero || count == 0) return null;
            var bytes = new byte[(int)count * (format == 32 ? IntPtr.Size : format / 8)];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return (format, bytes);
        }
        finally { if (data != IntPtr.Zero) XFree(data); }
    }

    public void Dispose() => stop.Cancel();

    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);
    [DllImport("libX11.so.6")] static extern int XQueryKeymap(IntPtr display, byte[] keys);
    [DllImport("libX11.so.6")] static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);
    [DllImport("libX11.so.6")] static extern int XFree(IntPtr data);
    [DllImport("libX11.so.6")]
    static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
    [DllImport("libX11.so.6")]
    static extern int XTranslateCoordinates(IntPtr display, IntPtr source, IntPtr destination, int x, int y, out int destinationX, out int destinationY, out IntPtr child);
    [DllImport("libX11.so.6")]
    static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, long offset, IntPtr length, bool delete, IntPtr type, out IntPtr actualType, out int format, out IntPtr count, out IntPtr after, out IntPtr data);
}
