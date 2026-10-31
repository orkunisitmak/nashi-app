using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed record DesktopDrag(string[] Files,string Mode,PixelPoint Pointer,long Session);

/// Observes the Xdnd selection and modifier state on a separate X connection.
/// It never grabs keys, changes a source's selection or injects desktop input.
internal sealed class X11DragMonitor:IDisposable
{
    private readonly CancellationTokenSource lifetime=new();
    private readonly Action<DesktopDrag?> changed;
    private readonly Func<Preferences> settings;
    private readonly Task loop;
    internal X11DragMonitor(Action<DesktopDrag?> changed,Func<Preferences>? settings=null){this.changed=changed;this.settings=settings??(()=>new());loop=Task.Run(Run);}
    internal static string? ModeFor(bool ctrl,bool shift,bool alt,Preferences prefs)
    {
        var chord=string.Join("+",new[]{ctrl?"Ctrl":null,shift?"Shift":null,alt?"Alt":null}.OfType<string>());
        return !prefs.Enabled?null:chord==prefs.ToolsModifier?"tools":chord==prefs.ConvertModifier?"convert":chord==prefs.ActionsModifier?"actions":null;
    }
    public void Dispose(){lifetime.Cancel();}
    internal static string[] ParseUris(string text)=>text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
        .Where(line=>!line.StartsWith('#')).Select(line=>Uri.TryCreate(line,UriKind.Absolute,out var uri)&&uri.IsFile&&(uri.Host.Length==0||uri.Host=="localhost")?uri.LocalPath:null)
        .OfType<string>().Where(p=>Path.IsPathFullyQualified(p)&&(File.Exists(p)||Directory.Exists(p))).Distinct(StringComparer.Ordinal).Take(1000).ToArray();
    private async Task Run()
    {
        IntPtr display=IntPtr.Zero;ulong window=0;var shown=false;
        try
        {
            display=XOpenDisplay(IntPtr.Zero);if(display==IntPtr.Zero)return;
            var root=XDefaultRootWindow(display);window=XCreateSimpleWindow(display,root,-1,-1,1,1,0,0,0);
            var selection=XInternAtom(display,"XdndSelection",0);var target=XInternAtom(display,"text/uri-list",0);var property=XInternAtom(display,"_NASHI_DRAG_FILES",0);
            ulong owner=0;var pending=false;var buttonWasDown=false;var pressX=0;var pressY=0;var requestedAt=DateTimeOffset.MinValue;string[] files=[];string? lastMode=null;long session=0;
            while(!lifetime.IsCancellationRequested)
            {
                var currentOwner=XGetSelectionOwner(display,selection);
                var pointer=XQueryPointer(display,root,out _,out _,out var x,out var y,out _,out _,out var mask)!=0;
                // Button1 + a live Xdnd selection distinguishes a file drag from ordinary Ctrl use.
                var buttonDown=pointer&&(mask&(1u<<8))!=0;
                if(buttonDown&&!buttonWasDown){pressX=x;pressY=y;}buttonWasDown=buttonDown;
                var distance=(long)(x-pressX)*(x-pressX)+(long)(y-pressY)*(y-pressY);
                var dragging=buttonDown&&distance>=64&&currentOwner!=0;
                var mode=ModeFor((mask&4)!=0,(mask&1)!=0,(mask&8)!=0,settings());
                if(!dragging||currentOwner!=owner)
                {
                    if(shown){changed(null);shown=false;}owner=currentOwner;files=[];pending=false;lastMode=null;session++;
                }
                if(dragging&&mode is not null&&files.Length==0&&!pending&&DateTimeOffset.UtcNow-requestedAt>TimeSpan.FromMilliseconds(500))
                {XDeleteProperty(display,window,property);XConvertSelection(display,selection,target,property,window,0);XFlush(display);pending=true;requestedAt=DateTimeOffset.UtcNow;}
                while(XPending(display)>0)
                {
                    XNextEvent(display,out var e);if(e.Type!=31||e.Requestor!=window||e.Selection!=selection||e.Target!=target)continue;
                    pending=false;if(!dragging||e.Property==0||XGetSelectionOwner(display,selection)!=owner)continue;
                    var result=XGetWindowProperty(display,window,property,0,65536,1,0,out _,out var format,out var count,out var after,out var bytes);
                    try{if(result==0&&format==8&&after==0&&count<=262144&&bytes!=IntPtr.Zero){var content=new byte[(int)count];Marshal.Copy(bytes,content,0,content.Length);files=ParseUris(Encoding.UTF8.GetString(content));}}
                    finally{if(bytes!=IntPtr.Zero)XFree(bytes);}
                }
                if(pending&&DateTimeOffset.UtcNow-requestedAt>TimeSpan.FromMilliseconds(750))pending=false;
                if(dragging&&mode is not null&&files.Length>0)
                {
                    if(!shown||mode!=lastMode){changed(new(files,mode,new(x,y),session));shown=true;lastMode=mode;}
                }
                else if(shown){changed(null);shown=false;lastMode=null;}
                await Task.Delay(30,lifetime.Token);
            }
        }
        catch(OperationCanceledException){}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
        finally{if(shown)changed(null);if(display!=IntPtr.Zero){if(window!=0)XDestroyWindow(display,window);XCloseDisplay(display);}}
    }
    [StructLayout(LayoutKind.Explicit,Size=192)]private struct XEvent
    {
        [FieldOffset(0)]public int Type;[FieldOffset(32)]public ulong Requestor;[FieldOffset(40)]public ulong Selection;[FieldOffset(48)]public ulong Target;[FieldOffset(56)]public ulong Property;
    }
    [DllImport("libX11.so.6")]private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")]private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")]private static extern ulong XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")]private static extern ulong XCreateSimpleWindow(IntPtr display,ulong parent,int x,int y,uint width,uint height,uint border,ulong borderPixel,ulong background);
    [DllImport("libX11.so.6")]private static extern int XDestroyWindow(IntPtr display,ulong window);
    [DllImport("libX11.so.6")]private static extern ulong XInternAtom(IntPtr display,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,int onlyIfExists);
    [DllImport("libX11.so.6")]private static extern ulong XGetSelectionOwner(IntPtr display,ulong atom);
    [DllImport("libX11.so.6")]private static extern int XConvertSelection(IntPtr display,ulong selection,ulong target,ulong property,ulong requestor,ulong time);
    [DllImport("libX11.so.6")]private static extern int XDeleteProperty(IntPtr display,ulong window,ulong property);
    [DllImport("libX11.so.6")]private static extern int XQueryPointer(IntPtr display,ulong window,out ulong root,out ulong child,out int rootX,out int rootY,out int windowX,out int windowY,out uint mask);
    [DllImport("libX11.so.6")]private static extern int XGetWindowProperty(IntPtr display,ulong window,ulong property,long offset,long length,int delete,ulong type,out ulong actualType,out int format,out ulong count,out ulong remaining,out IntPtr bytes);
    [DllImport("libX11.so.6")]private static extern int XFree(IntPtr value);
    [DllImport("libX11.so.6")]private static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")]private static extern int XPending(IntPtr display);
    [DllImport("libX11.so.6")]private static extern int XNextEvent(IntPtr display,out XEvent e);
}
