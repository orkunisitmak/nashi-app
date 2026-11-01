using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Nashi.Linux;

/// The target side of XDND as pure state, fed with the events of one drag after another: which offered types to
/// ask for (in order), the status and finished answers, and what the drag brought. XdndTarget turns X events into
/// these calls and these events back into X messages; the checks inject made-up ones.
/// Positions come in root coordinates; `origin` gives the target window's root position (asked each time, so a
/// moved window stays right), and every callback gets points relative to the window, in physical pixels.
public sealed class XdndProtocol(Func<int, int, bool> accepts, Func<(int X, int Y)>? origin = null)
{
    /// What Nashi reads from a drag, most useful first: local files, then what browsers offer (BrowserDrop).
    public static readonly string[] Wanted =
        ["text/uri-list", "text/x-moz-url", "_NETSCAPE_URL", "application/x-moz-file-promise-url", "application/x-moz-file-promise-dest-filename", "DownloadURL", "text/html", "text/plain"];

    private readonly Dictionary<string, byte[]> data = [];
    private readonly Queue<string> pending = new();
    private bool inside, dropPending, requested;
    private (int X, int Y) at;

    /// Ask the source for this type (XConvertSelection).
    public event Action<string>? Request;
    /// XdndStatus: accept or refuse a drop here.
    public event Action<bool>? Status;
    /// XdndFinished: the drop was taken (copy) or refused.
    public event Action<bool>? Finished;
    /// The drag is over a point (content is None until every type has arrived).
    public event Action<DropContent, int, int>? Hover;
    public event Action<DropContent, int, int>? Dropped;
    /// The drag left, or ended somewhere it was refused.
    public event Action? Left;
    /// A drop came and was refused (off the petals, or nothing Nashi reads): say so, it is not silently lost.
    public event Action? Refused;

    public bool Ready => pending.Count == 0;
    public DropContent Content => Ready ? DropContent.From(data) : DropContent.None;

    public void Enter(IEnumerable<string> offered)
    {
        data.Clear();
        pending.Clear();
        foreach (var type in Wanted.Where(offered.Contains)) pending.Enqueue(type);
        (inside, dropPending, requested) = (true, false, false);
    }

    /// An XdndPosition at root coordinates (x, y).
    public void Position(int rootX, int rootY)
    {
        var (ox, oy) = origin?.Invoke() ?? (0, 0);
        var (x, y) = (rootX - ox, rootY - oy);
        if (!inside) return;
        at = (x, y);
        if (!requested && !Ready) { requested = true; Request?.Invoke(pending.Peek()); }
        var content = Content;
        Status?.Invoke((!Ready || content.Any) && accepts(x, y));
        Hover?.Invoke(content, x, y);
    }

    /// The source's answer for the type asked for last (null: refused, empty, or too large to read).
    public void Reply(byte[]? bytes)
    {
        if (!inside || !pending.TryDequeue(out var type)) return;
        if (bytes is { Length: > 0 }) data[type] = bytes;
        if (!Ready) Request?.Invoke(pending.Peek());
        else if (dropPending) Finish();
    }

    public void Drop()
    {
        if (!inside) return;
        if (Ready) Finish();
        else dropPending = true;
    }

    public void Leave()
    {
        if (!inside) return;
        inside = false;
        Left?.Invoke();
    }

    private void Finish()
    {
        inside = false;
        var content = DropContent.From(data);
        var ok = content.Any && accepts(at.X, at.Y);
        Finished?.Invoke(ok);
        if (ok) Dropped?.Invoke(content, at.X, at.Y);
        else { Left?.Invoke(); Refused?.Invoke(); }
    }
}

/// An XDND drop target for an Avalonia window (Avalonia 11 on X11 takes no drops). The window gets XdndAware and
/// an XdndProxy pointing at a hidden window on Nashi's own X connection and thread, which takes the XDND messages for
/// it: so the source finds the drop target in the window it is really over, stacking aside. It exists only while a
/// drag needs it; nothing is grabbed, and a drop it refuses goes back to the source as refused. `accepts` (any
/// thread) says whether a point (relative to the window, physical pixels) is a place to drop; `hover`, `left`, `dropped` and `refused` (a drop that was
/// refused: the centre, or nothing Nashi reads) run on the X thread: post to the UI thread from them.
public sealed class XdndTarget : IDisposable
{
    private readonly ConcurrentQueue<Action<IntPtr, IntPtr>> requests = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Func<int, int, bool> accepts;
    private readonly Action<DropContent, int, int> hover;
    private readonly Action left;
    private readonly Action<DropContent, int, int> dropped;
    private readonly Action? refused;

    /// Makes the drop target for the toplevel `xid` (Window.TryGetPlatformHandle) covering `rect` (physical pixels):
    /// the real one on X11, nothing elsewhere. The checks swap in a recorder, so nothing ever reaches a display.
    public static Func<IntPtr, Avalonia.PixelRect, Func<int, int, bool>, Action<DropContent, int, int>, Action, Action<DropContent, int, int>, Action?, IDisposable> Create { get; set; } =
        (xid, _, accepts, hover, left, dropped, refused) => PointerWatch.Available && xid != IntPtr.Zero
            ? new XdndTarget(xid, accepts, hover, left, dropped, refused)
            : new CancellationTokenSource();

    public XdndTarget(IntPtr toplevel, Func<int, int, bool> accepts, Action<DropContent, int, int> hover, Action left, Action<DropContent, int, int> dropped, Action? refused = null)
    {
        (this.accepts, this.hover, this.left, this.dropped, this.refused) = (accepts, hover, left, dropped, refused);
        new Thread(() => Run(toplevel)) { IsBackground = true, Name = "Nashi drop target" }.Start();
    }

    public void Dispose() => stop.Cancel();

    /// What an XDND drag running right now carries, asked of its source directly (XdndSelection as text/uri-list,
    /// then the browser types), within `timeout`; null when nothing answers. The gesture fills the wheel with it before
    /// the drag reaches the wheel. The checks swap in their own.
    public static Func<TimeSpan, DropContent?> ReadDrag { get; set; } = ReadSelection;

    private static DropContent? ReadSelection(TimeSpan timeout)
    {
        IntPtr display;
        try { display = XOpenDisplay(IntPtr.Zero); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return null; }
        if (display == IntPtr.Zero) return null;
        var window = XCreateSimpleWindow(display, XDefaultRootWindow(display), 0, 0, 1, 1, 0, IntPtr.Zero, IntPtr.Zero);
        try
        {
            var selection = XInternAtom(display, "XdndSelection", false);
            var property = XInternAtom(display, "NASHI_DRAG", false);
            if (XGetSelectionOwner(display, selection) == IntPtr.Zero) return null;
            var deadline = DateTime.UtcNow + timeout;
            var data = new Dictionary<string, byte[]>();
            var e = new IntPtr[24];
            foreach (var type in new[] { "text/uri-list", "text/x-moz-url", "_NETSCAPE_URL", "text/html" })
            {
                XConvertSelection(display, selection, XInternAtom(display, type, false), property, window, IntPtr.Zero);
                XFlush(display);
                while (DateTime.UtcNow < deadline && !XCheckTypedWindowEvent(display, window, 31 /* SelectionNotify */, e)) Thread.Sleep(5);
                if (DateTime.UtcNow >= deadline && e[0] != 31) break;
                if (e[0] == 31 && e[7] == property && PointerWatch.Property(display, window, "NASHI_DRAG", 16 << 20) is { Format: 8 } read) data[type] = read.Data;
                XDeleteProperty(display, window, property);
                e[0] = 0;
                if (type == "text/uri-list" && DropContent.From(data).Files.Count > 0) break; // files: that is all it needs
            }
            var content = DropContent.From(data);
            return content.Any ? content : null;
        }
        finally
        {
            XDestroyWindow(display, window);
            XCloseDisplay(display);
        }
    }

    // XEvent as 24 C longs. ClientMessage: [5] message_type, [6] format, [7..11] data.l; SelectionNotify: [6] target, [7] property.
    private void Run(IntPtr toplevel)
    {
        IntPtr display;
        try { display = XOpenDisplay(IntPtr.Zero); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return; }
        if (display == IntPtr.Zero) return;
        IntPtr Atom(string name) => XInternAtom(display, name, false);
        var window = XCreateSimpleWindow(display, XDefaultRootWindow(display), -10, -10, 1, 1, 0, IntPtr.Zero, IntPtr.Zero); // never mapped
        var (aware, proxy) = (Atom("XdndAware"), Atom("XdndProxy"));
        // XdndAware version 5 on both; the proxy names itself, as the spec asks, and the toplevel names the proxy.
        foreach (var target in new[] { window, toplevel })
        {
            XChangeProperty(display, target, aware, (IntPtr)4 /* XA_ATOM */, 32, 0, [5], 1);
            XChangeProperty(display, target, proxy, (IntPtr)33 /* XA_WINDOW */, 32, 0, [window], 1);
        }
        XFlush(display);
        var (enter, position, leave, drop) = (Atom("XdndEnter"), Atom("XdndPosition"), Atom("XdndLeave"), Atom("XdndDrop"));
        var (status, finished, copy, selection, property) = (Atom("XdndStatus"), Atom("XdndFinished"), Atom("XdndActionCopy"), Atom("XdndSelection"), Atom("NASHI_DROP"));
        IntPtr source = 0, time = 0;

        void Send(IntPtr type, IntPtr l1, IntPtr l4)
        {
            var message = new IntPtr[24];
            message[0] = 33; // ClientMessage
            message[3] = display;
            message[4] = source;
            message[5] = type;
            message[6] = 32;
            // data.l[0] is the window the source is over (the toplevel), not the proxy: sources match replies by it.
            (message[7], message[8], message[11]) = (toplevel, l1, l4);
            XSendEvent(display, source, false, IntPtr.Zero, message);
            XFlush(display);
        }
        var protocol = new XdndProtocol(accepts, () =>
            XTranslateCoordinates(display, toplevel, XDefaultRootWindow(display), 0, 0, out var x, out var y, out _) != 0 ? (x, y) : (0, 0));
        protocol.Request += type => XConvertSelection(display, selection, Atom(type), property, window, time);
        protocol.Status += ok => Send(status, ok ? 3 : 2, ok ? copy : 0); // bit 1: keep sending positions
        protocol.Finished += ok => Send(finished, ok ? 1 : 0, 0);
        protocol.Hover += hover;
        protocol.Dropped += dropped;
        protocol.Left += left;
        protocol.Refused += () => refused?.Invoke();

        try
        {
            var e = new IntPtr[24];
            while (!stop.IsCancellationRequested)
            {
                while (requests.TryDequeue(out var request)) request(display, window);
                if (XPending(display) == 0)
                {
                    XFlush(display);
                    stop.Token.WaitHandle.WaitOne(10);
                    continue;
                }
                XNextEvent(display, e);
                if (e[0] == 33 && e[5] == enter)
                {
                    source = e[7];
                    time = 0;
                    var types = (e[8] & 1) != 0 ? TypeList(display, source, Atom("XdndTypeList")) : new[] { e[9], e[10], e[11] }.Where(a => a != 0).ToArray();
                    protocol.Enter(types.Select(a => AtomName(display, a)).OfType<string>().ToList());
                }
                else if (e[0] == 33 && e[5] == position && e[7] == source)
                {
                    time = e[10];
                    protocol.Position((int)((long)e[9] >> 16 & 0xFFFF), (int)((long)e[9] & 0xFFFF));
                }
                else if (e[0] == 33 && e[5] == leave && e[7] == source) protocol.Leave();
                else if (e[0] == 33 && e[5] == drop && e[7] == source)
                {
                    time = e[9];
                    protocol.Drop();
                }
                else if (e[0] == 31) // SelectionNotify: [7] is our property, or None when the source refused the type
                {
                    // ponytail: INCR (chunked transfers, format 32) reads as nothing; addresses and file lists are small.
                    var read = e[7] == property ? PointerWatch.Property(display, window, "NASHI_DROP", 16 << 20) : null;
                    if (e[7] == property) XDeleteProperty(display, window, property);
                    protocol.Reply(read is { Format: 8 } r ? r.Data : null);
                }
            }
        }
        finally
        {
            // ponytail: the toplevel may be gone by now; Avalonia's X error handler logs the BadWindow.
            XDeleteProperty(display, toplevel, proxy);
            XDeleteProperty(display, toplevel, aware);
            XDestroyWindow(display, window);
            XCloseDisplay(display);
        }
    }

    private static IntPtr[] TypeList(IntPtr display, IntPtr window, IntPtr atom)
    {
        if (XGetWindowProperty(display, window, atom, 0, 64, false, IntPtr.Zero, out _, out var format, out var count, out _, out var data) != 0 || data == IntPtr.Zero) return [];
        try
        {
            var atoms = new IntPtr[format == 32 ? (int)count : 0];
            if (atoms.Length > 0) Marshal.Copy(data, atoms, 0, atoms.Length);
            return atoms;
        }
        finally { XFree(data); }
    }

    private static string? AtomName(IntPtr display, IntPtr atom)
    {
        var name = XGetAtomName(display, atom);
        if (name == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(name); }
        finally { XFree(name); }
    }

    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")]
    static extern int XTranslateCoordinates(IntPtr display, IntPtr source, IntPtr destination, int x, int y, out int destinationX, out int destinationY, out IntPtr child);
    [DllImport("libX11.so.6")] static extern IntPtr XGetAtomName(IntPtr display, IntPtr atom);
    [DllImport("libX11.so.6")] static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, IntPtr[] data, int count);
    [DllImport("libX11.so.6")] static extern int XDeleteProperty(IntPtr display, IntPtr window, IntPtr property);
    [DllImport("libX11.so.6")] static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint border, IntPtr borderPixel, IntPtr background);
    [DllImport("libX11.so.6")] static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);
    [DllImport("libX11.so.6")] static extern bool XCheckTypedWindowEvent(IntPtr display, IntPtr window, int type, IntPtr[] e);
    [DllImport("libX11.so.6")] static extern int XDestroyWindow(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6")] static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XPending(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XNextEvent(IntPtr display, IntPtr[] e);
    [DllImport("libX11.so.6")] static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, IntPtr mask, IntPtr[] e);
    [DllImport("libX11.so.6")] static extern int XConvertSelection(IntPtr display, IntPtr selection, IntPtr target, IntPtr property, IntPtr requestor, IntPtr time);
    [DllImport("libX11.so.6")] static extern int XFree(IntPtr data);
    [DllImport("libX11.so.6")]
    static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, long offset, IntPtr length, bool delete, IntPtr type, out IntPtr actualType, out int format, out IntPtr count, out IntPtr after, out IntPtr data);
}
