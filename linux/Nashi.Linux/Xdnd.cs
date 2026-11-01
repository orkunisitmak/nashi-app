using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Nashi.Linux;

/// The target side of XDND as pure state, fed with the events of one drag after another: which offered types to
/// ask for (in order), the status and finished answers, and what the drag brought. XdndTarget turns X events into
/// these calls and these events back into X messages; the checks inject made-up ones.
public sealed class XdndProtocol(Func<int, int, bool> accepts)
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

    public bool Ready => pending.Count == 0;
    public DropContent Content => Ready ? DropContent.From(data) : DropContent.None;

    public void Enter(IEnumerable<string> offered)
    {
        data.Clear();
        pending.Clear();
        foreach (var type in Wanted.Where(offered.Contains)) pending.Enqueue(type);
        (inside, dropPending, requested) = (true, false, false);
    }

    public void Position(int x, int y)
    {
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
        else Left?.Invoke();
    }
}

/// An XDND drop target over a screen rectangle: an invisible, override-redirect InputOnly window with XdndAware on
/// Nashi's own X connection and thread, so a drag from any app can land on an Avalonia window (Avalonia 11 on X11
/// does not take drops). It exists only while a drag needs it; nothing is grabbed, and a drop it refuses goes back
/// to the source as refused. `accepts` (any thread) says whether a screen point is a place to drop; `hover`,
/// `left` and `dropped` run on the X thread: post to the UI thread from them.
public sealed class XdndTarget : IDisposable
{
    private readonly ConcurrentQueue<Action<IntPtr, IntPtr>> requests = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Func<int, int, bool> accepts;
    private readonly Action<DropContent, int, int> hover;
    private readonly Action left;
    private readonly Action<DropContent, int, int> dropped;

    /// Makes a drop target over `rect` (physical pixels): the real one on X11, nothing elsewhere. The checks swap in a
    /// recorder, so no window is ever made on a display from them.
    public static Func<Avalonia.PixelRect, Func<int, int, bool>, Action<DropContent, int, int>, Action, Action<DropContent, int, int>, IDisposable> Create { get; set; } =
        (rect, accepts, hover, left, dropped) => PointerWatch.Available
            ? new XdndTarget(rect.X, rect.Y, rect.Width, rect.Height, accepts, hover, left, dropped)
            : new CancellationTokenSource();

    public XdndTarget(int x, int y, int width, int height, Func<int, int, bool> accepts, Action<DropContent, int, int> hover, Action left, Action<DropContent, int, int> dropped)
    {
        (this.accepts, this.hover, this.left, this.dropped) = (accepts, hover, left, dropped);
        new Thread(() => Run(x, y, width, height)) { IsBackground = true, Name = "Nashi drop target" }.Start();
    }

    /// Moves the target (the window it covers moved or resized).
    public void Place(int x, int y, int width, int height) =>
        requests.Enqueue((display, window) => XMoveResizeWindow(display, window, x, y, (uint)Math.Max(1, width), (uint)Math.Max(1, height)));

    public void Dispose() => stop.Cancel();

    // XEvent as 24 C longs. ClientMessage: [5] message_type, [6] format, [7..11] data.l; SelectionNotify: [6] target, [7] property.
    private void Run(int x, int y, int width, int height)
    {
        IntPtr display;
        try { display = XOpenDisplay(IntPtr.Zero); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return; }
        if (display == IntPtr.Zero) return;
        IntPtr Atom(string name) => XInternAtom(display, name, false);
        var root = XDefaultRootWindow(display);
        var attributes = new long[14];
        attributes[11] = 1; // override_redirect
        var window = XCreateWindow(display, root, x, y, (uint)Math.Max(1, width), (uint)Math.Max(1, height), 0, 0, 2 /* InputOnly */, IntPtr.Zero, (IntPtr)(1 << 9) /* CWOverrideRedirect */, attributes);
        XChangeProperty(display, window, Atom("XdndAware"), (IntPtr)4 /* XA_ATOM */, 32, 0, [5], 1);
        XMapRaised(display, window);
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
            (message[7], message[8], message[11]) = (window, l1, l4);
            XSendEvent(display, source, false, IntPtr.Zero, message);
            XFlush(display);
        }
        var protocol = new XdndProtocol(accepts);
        protocol.Request += type => XConvertSelection(display, selection, Atom(type), property, window, time);
        protocol.Status += ok => Send(status, ok ? 3 : 2, ok ? copy : 0); // bit 1: keep sending positions
        protocol.Finished += ok => Send(finished, ok ? 1 : 0, 0);
        protocol.Hover += hover;
        protocol.Dropped += dropped;
        protocol.Left += left;

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
    [DllImport("libX11.so.6")] static extern IntPtr XGetAtomName(IntPtr display, IntPtr atom);
    [DllImport("libX11.so.6")]
    static extern IntPtr XCreateWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint border, int depth, uint windowClass, IntPtr visual, IntPtr valueMask, long[] attributes);
    [DllImport("libX11.so.6")] static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, long[] data, int count);
    [DllImport("libX11.so.6")] static extern int XDeleteProperty(IntPtr display, IntPtr window, IntPtr property);
    [DllImport("libX11.so.6")] static extern int XMapRaised(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6")] static extern int XMoveResizeWindow(IntPtr display, IntPtr window, int x, int y, uint width, uint height);
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
