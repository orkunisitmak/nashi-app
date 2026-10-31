using System.Runtime.InteropServices;
namespace Nashi.Windows;

/// Reports when any mouse button goes down or up anywhere on the desktop, so the global drag
/// controller can sleep while no button is held instead of polling all the time.
///
/// The low-level hook runs on its own thread with its own message loop. Windows waits for a
/// low-level hook before it delivers mouse input to every other application, so the hook must
/// never wait for Nashi's UI thread. The callback only looks at the message number and posts a
/// notification; it never reads keys, pointer positions or window contents.
internal sealed class MouseButtonHook : IDisposable
{
    private const int WhMouseLowLevel = 14;
    private const int WmQuit = 0x0012;
    private const int WmLButtonDown = 0x0201, WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204, WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207, WmMButtonUp = 0x0208;
    private const int WmXButtonDown = 0x020B, WmXButtonUp = 0x020C;

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hook, LowLevelMouseProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    private readonly Action changed;
    // Kept in a field so the garbage collector never frees the delegate while Windows holds it.
    private readonly LowLevelMouseProc callback;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private IntPtr hook;
    private uint threadId;
    private int disposed;

    private MouseButtonHook(Action changed)
    {
        this.changed = changed;
        callback = Callback;
        thread = new Thread(Run) { IsBackground = true, Name = "Nashi mouse button hook" };
    }

    /// Starts the hook, or returns null when Windows refuses it. Callers then fall back to polling.
    /// `changed` runs on the hook thread and must return quickly (for example by posting to a dispatcher).
    public static MouseButtonHook? TryStart(Action changed)
    {
        var instance = new MouseButtonHook(changed);
        instance.thread.Start();
        if (!instance.ready.Wait(TimeSpan.FromSeconds(5)) || instance.hook == IntPtr.Zero)
        {
            instance.Dispose();
            return null;
        }
        return instance;
    }

    private void Run()
    {
        threadId = GetCurrentThreadId();
        // Create this thread's message queue before anyone can post WM_QUIT to it.
        PeekMessage(out _, IntPtr.Zero, 0x0400, 0x0400, 0);
        hook = SetWindowsHookEx(WhMouseLowLevel, callback, GetModuleHandle(null), 0);
        ready.Set();
        if (hook == IntPtr.Zero) return;
        if (Volatile.Read(ref disposed) != 0)
        {
            // TryStart gave up waiting before the hook was ready.
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            return;
        }
        try
        {
            // Low-level hooks are called through this thread's message loop.
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }
        }
        finally
        {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
    }

    private IntPtr Callback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Volatile.Read(ref disposed) == 0 && IsButton(message.ToInt32()))
        {
            try
            {
                changed();
            }
            catch (InvalidOperationException)
            {
                // The dispatcher is shutting down; there is nothing left to notify.
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    internal static bool IsButton(int message) => message is WmLButtonDown or WmLButtonUp or WmRButtonDown
        or WmRButtonUp or WmMButtonDown or WmMButtonUp or WmXButtonDown or WmXButtonUp;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        // The background thread may still be starting; it removes the hook as soon as its loop ends.
        if (ready.IsSet && threadId != 0 && PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero))
            thread.Join(TimeSpan.FromSeconds(2));
    }
}
