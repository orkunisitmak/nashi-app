using System.Runtime.InteropServices;
namespace Nashi.Linux.Platform;

/// Sets _NET_WM_WINDOW_TYPE on one of our X11 (XWayland) windows before it is mapped. Avalonia makes
/// every window a normal one, and a normal window that appears ends KWin's Show Desktop, so a drag
/// started on the desktop would bring all windows back the moment the gesture layer opens.
internal static class X11WindowType
{
    [DllImport("libX11.so.6")]private static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")]private static extern nint XInternAtom(nint display,string name,bool onlyIfExists);
    [DllImport("libX11.so.6")]private static extern int XChangeProperty(nint display,nint window,nint property,nint type,int format,int mode,nint[] data,int count);
    [DllImport("libX11.so.6")]private static extern int XFlush(nint display);
    private static nint display;private static readonly object gate=new();

    /// kind: "notification", "dock", "utility", … (the part after _NET_WM_WINDOW_TYPE_).
    public static void Set(Avalonia.Controls.Window window,string kind)
    {
        if(window.TryGetPlatformHandle() is not {HandleDescriptor:"XID"} handle)return;
        try
        {
            lock(gate)
            {
                if(display==0)display=XOpenDisplay(0);if(display==0)return;
                var type=XInternAtom(display,"_NET_WM_WINDOW_TYPE",false);var value=XInternAtom(display,"_NET_WM_WINDOW_TYPE_"+kind.ToUpperInvariant(),false);
                XChangeProperty(display,handle.Handle,type,4,32,0,[value],1);   // XA_ATOM, PropModeReplace
                XFlush(display);
            }
        }
        catch(Exception error)when(error is DllNotFoundException or EntryPointNotFoundException){}
    }
}
