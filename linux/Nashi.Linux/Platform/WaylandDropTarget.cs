using System.Runtime.InteropServices;
using System.Text;
namespace Nashi.Linux;

/// A native Wayland drop target for the gesture layer. Avalonia 11/12 talk to the desktop through
/// XWayland, and KWin's Wayland→XWayland drag bridge can stop passing drags mid-session. This
/// opens a fully transparent wlr-layer-shell overlay surface straight on the compositor while a
/// gesture is open, so a drag from any Wayland app (Dolphin, Files, browsers) arrives directly:
/// enter/motion give the pointer, drop gives the text/uri-list. Avalonia keeps drawing the wheel
/// underneath. When the compositor lacks layer-shell (GNOME) TryCreate returns null and the
/// XWayland path is used alone.
public sealed unsafe class WaylandDropTarget
{
    // ---- libwayland-client ----------------------------------------------------------------
    private const string Lib="libwayland-client.so.0";
    [DllImport(Lib)]private static extern nint wl_display_connect(nint name);
    [DllImport(Lib)]private static extern int wl_display_dispatch(nint display);
    [DllImport(Lib)]private static extern int wl_display_roundtrip(nint display);
    [DllImport(Lib)]private static extern int wl_display_flush(nint display);
    [DllImport(Lib)]private static extern nint wl_proxy_marshal_array_flags(nint proxy,uint opcode,nint iface,uint version,uint flags,long* args);
    [DllImport(Lib)]private static extern int wl_proxy_add_listener(nint proxy,nint implementation,nint data);
    [DllImport(Lib)]private static extern uint wl_proxy_get_version(nint proxy);
    [DllImport(Lib)]private static extern void wl_proxy_destroy(nint proxy);
    [DllImport("libc",SetLastError=true)]private static extern int memfd_create([MarshalAs(UnmanagedType.LPUTF8Str)]string name,uint flags);
    [DllImport("libc",SetLastError=true)]private static extern int ftruncate(int fd,long length);
    [DllImport("libc",SetLastError=true)]private static extern int pipe2(int* fds,int flags);
    [DllImport("libc",SetLastError=true)]private static extern int close(int fd);
    [DllImport("libc",SetLastError=true)]private static extern nint read(int fd,byte* buffer,nint count);
    private const uint Destroy=1;   // WL_MARSHAL_FLAG_DESTROY

    private static nint Interface(string name)=>NativeLibrary.GetExport(NativeLibrary.Load(Lib),name);
    private static readonly nint Registry=Interface("wl_registry_interface"),Compositor=Interface("wl_compositor_interface"),Surface=Interface("wl_surface_interface"),
        Shm=Interface("wl_shm_interface"),ShmPool=Interface("wl_shm_pool_interface"),Buffer=Interface("wl_buffer_interface"),Seat=Interface("wl_seat_interface"),
        DeviceManager=Interface("wl_data_device_manager_interface"),Output=Interface("wl_output_interface");

    // ---- zwlr_layer_shell_v1 / zwlr_layer_surface_v1, built by hand (no generated code) ----
    [StructLayout(LayoutKind.Sequential)]private struct WlMessage{public nint Name,Signature,Types;}
    [StructLayout(LayoutKind.Sequential)]private struct WlInterface{public nint Name;public int Version,MethodCount;public nint Methods;public int EventCount;public nint Events;}
    private static readonly nint LayerShell,LayerSurface;
    private static nint Str(string s)=>Marshal.StringToCoTaskMemUTF8(s);
    private static nint Types(params nint[] types){var p=Marshal.AllocHGlobal(nint.Size*Math.Max(1,types.Length));for(var i=0;i<types.Length;i++)Marshal.WriteIntPtr(p,i*nint.Size,types[i]);return p;}
    private static nint Messages(params (string Name,string Signature,nint Types)[] messages)
    {
        var p=Marshal.AllocHGlobal(sizeof(WlMessage)*Math.Max(1,messages.Length));
        for(var i=0;i<messages.Length;i++)((WlMessage*)p)[i]=new WlMessage{Name=Str(messages[i].Name),Signature=Str(messages[i].Signature),Types=messages[i].Types};
        return p;
    }
    static WaylandDropTarget()
    {
        var none=Types(0,0,0,0,0,0);
        LayerSurface=Marshal.AllocHGlobal(sizeof(WlInterface));LayerShell=Marshal.AllocHGlobal(sizeof(WlInterface));
        *(WlInterface*)LayerSurface=new WlInterface{Name=Str("zwlr_layer_surface_v1"),Version=1,
            MethodCount=8,Methods=Messages(("set_size","uu",none),("set_anchor","u",none),("set_exclusive_zone","i",none),("set_margin","iiii",none),("set_keyboard_interactivity","u",none),("get_popup","o",Types(0)),("ack_configure","u",none),("destroy","",none)),
            EventCount=2,Events=Messages(("configure","uuu",none),("closed","",none))};
        *(WlInterface*)LayerShell=new WlInterface{Name=Str("zwlr_layer_shell_v1"),Version=1,
            MethodCount=1,Methods=Messages(("get_layer_surface","no?ous",Types(LayerSurface,Surface,Output,0,0))),EventCount=0,Events=0};
    }

    // ---- state ----------------------------------------------------------------------------
    private readonly nint display;private nint compositor,shm,seat,manager,shell,device;
    private sealed class Pane{public nint Surface,Layer,Pool,Buffer;public int Width,Height;public Screen? Screen;}
    /// A monitor (wl_output) and its top-left corner in the compositor's global space.
    private sealed class Screen
    {
        public uint Name;public nint Proxy;public int X,Y,ModeWidth,ModeHeight,Scale=1,Transform;
        // Logical size: the current mode divided by the scale, turned for 90°/270° rotations (odd transforms).
        public int Width=>(Transform%2==1?ModeHeight:ModeWidth)/Math.Max(1,Scale);
        public int Height=>(Transform%2==1?ModeWidth:ModeHeight)/Math.Max(1,Scale);
    }
    private readonly List<Screen> screens=[];private readonly List<Pane> overlays=[],edges=[];private string edgeSide="off";private Pane? current;
    private nint offer;private readonly HashSet<string> mimes=[];private readonly Dictionary<nint,HashSet<string>> offers=[];
    private uint serial;private double x,y;private bool inside;
    private readonly GCHandle self;private readonly object gate=new();
    /// Pointer position in the compositor's global space (logical pixels; the layer covers each output).
    public event Action<double,double>? Entered,Moved;
    public event Action? Left;
    /// The dropped URIs (file:// paths or web links) and where they were dropped.
    public event Action<string[],double,double>? Dropped;
    /// The dragged URIs, read while the drag is still going on.
    public event Action<string[]>? Offered;
    /// A drag touched the hot edge strip.
    public event Action<double,double>? EdgeEntered;

    private WaylandDropTarget(nint display){this.display=display;self=GCHandle.Alloc(this);}
    public static WaylandDropTarget? TryCreate()
    {
        if(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not {Length:>0})return null;
        try
        {
            var display=wl_display_connect(0);if(display==0)return null;
            var target=new WaylandDropTarget(display);var args=stackalloc long[1];args[0]=0;
            var registry=wl_proxy_marshal_array_flags(display,1,Registry,wl_proxy_get_version(display),0,args);
            wl_proxy_add_listener(registry,RegistryListener,GCHandle.ToIntPtr(target.self));
            wl_display_roundtrip(display);
            if(target.compositor==0||target.shm==0||target.seat==0||target.manager==0||target.shell==0)return null;
            var device=stackalloc long[2];device[0]=0;device[1]=target.seat;
            target.device=wl_proxy_marshal_array_flags(target.manager,1,Interface("wl_data_device_interface"),wl_proxy_get_version(target.manager),0,device);
            wl_proxy_add_listener(target.device,DeviceListener,GCHandle.ToIntPtr(target.self));
            wl_display_roundtrip(display);
            new Thread(()=>{while(wl_display_dispatch(display)>=0){}}){IsBackground=true,Name="Nashi Wayland drops"}.Start();
            return target;
        }
        catch(Exception error)when(error is DllNotFoundException or EntryPointNotFoundException){return null;}
    }

    /// The layer-shell namespace. KWin turns it into a window type, and an unknown name becomes a
    /// normal window, which Show Desktop hides. As "dock" both surfaces stay in place over the
    /// desktop, where files are usually dragged from after Show Desktop.
    private const string Dock="dock";
    /// Covers the screen with an invisible overlay that accepts drops until Hide.
    public void Show(){lock(gate){if(overlays.Count>0)return;foreach(var screen in Outputs())overlays.Add(Open(15,-1,0,0,(0,0,0,0),Dock,screen));}}
    public void Hide(){lock(gate){foreach(var pane in overlays)Close(pane);overlays.Clear();}}
    internal string Describe(){lock(gate)return string.Join(", ",screens.Select(s=>$"{s.Width}x{s.Height}+{s.X}+{s.Y}"))+$" · {edges.Count} edge strip(s), {overlays.Count} overlay(s)";}
    /// Every monitor, or one null entry (the compositor's current output) when none were announced.
    private IEnumerable<Screen?> Outputs()=>screens.Count>0?screens:new Screen?[]{null};
    /// The hot edge: an invisible 2-pixel strip along one screen edge (corners left free for the
    /// desktop's own corner actions). A drag that touches it raises EdgeEntered, no key needed.
    public void ShowEdge(string side)
    {
        lock(gate)
        {
            edgeSide=side;RebuildEdges();
        }
    }
    /// Strips only on outer edges: a left strip on a monitor with another one to its left would
    /// catch every drag that crosses between the two.
    private void RebuildEdges()
    {
        foreach(var pane in edges)Close(pane);edges.Clear();if(shell==0)return;
        foreach(var screen in Outputs())if(Outer(screen)&&Edge(screen) is {} pane)edges.Add(pane);
    }
    private bool Outer(Screen? screen)
    {
        if(screen is null||screen.Width==0)return true;
        bool Overlaps(int a0,int a1,int b0,int b1)=>a0<b1&&b0<a1;
        return !screens.Any(o=>o!=screen&&o.Width>0&&edgeSide switch
        {
            "left"=>Math.Abs(o.X+o.Width-screen.X)<=1&&Overlaps(o.Y,o.Y+o.Height,screen.Y,screen.Y+screen.Height),
            "right"=>Math.Abs(screen.X+screen.Width-o.X)<=1&&Overlaps(o.Y,o.Y+o.Height,screen.Y,screen.Y+screen.Height),
            "bottom"=>Math.Abs(screen.Y+screen.Height-o.Y)<=1&&Overlaps(o.X,o.X+o.Width,screen.X,screen.X+screen.Width),
            _=>false
        });
    }
    /// Anchors: top 1, bottom 2, left 4, right 8. Margins: top, right, bottom, left.
    private Pane? Edge(Screen? screen)=>edgeSide switch
    {
        "left"=>Open(1|2|4,0,2,0,(48,0,48,0),Dock,screen),
        "right"=>Open(1|2|8,0,2,0,(48,0,48,0),Dock,screen),
        "bottom"=>Open(4|8|2,0,0,2,(0,48,0,48),Dock,screen),
        _=>null
    };
    private Pane Open(uint anchor,int exclusive,uint w,uint h,(int Top,int Right,int Bottom,int Left) margin,string name,Screen? screen)
    {
        var pane=new Pane{Screen=screen};long* a=stackalloc long[6];
        a[0]=0;pane.Surface=wl_proxy_marshal_array_flags(compositor,0,Surface,wl_proxy_get_version(compositor),0,a);
        a[0]=0;a[1]=pane.Surface;a[2]=screen?.Proxy??0;a[3]=3;a[4]=Str(name);   // layer 3 = overlay, output null = current one
        pane.Layer=wl_proxy_marshal_array_flags(shell,0,LayerSurface,1,0,a);
        wl_proxy_add_listener(pane.Layer,LayerListener,GCHandle.ToIntPtr(self));
        a[0]=anchor;wl_proxy_marshal_array_flags(pane.Layer,1,0,1,0,a);
        a[0]=exclusive;wl_proxy_marshal_array_flags(pane.Layer,2,0,1,0,a);   // -1: ignore panels and cover everything
        a[0]=margin.Top;a[1]=margin.Right;a[2]=margin.Bottom;a[3]=margin.Left;wl_proxy_marshal_array_flags(pane.Layer,3,0,1,0,a);
        a[0]=w;a[1]=h;wl_proxy_marshal_array_flags(pane.Layer,0,0,1,0,a);   // 0 = sized by the anchors
        wl_proxy_marshal_array_flags(pane.Surface,6,0,wl_proxy_get_version(pane.Surface),0,a);   // commit, then wait for configure
        wl_display_flush(display);return pane;
    }
    private void Close(Pane pane)
    {
        long* a=stackalloc long[1];if(current==pane)current=null;
        wl_proxy_marshal_array_flags(pane.Layer,7,0,1,Destroy,a);
        if(pane.Buffer!=0)wl_proxy_marshal_array_flags(pane.Buffer,0,0,1,Destroy,a);
        if(pane.Pool!=0)wl_proxy_marshal_array_flags(pane.Pool,1,0,1,Destroy,a);
        wl_proxy_marshal_array_flags(pane.Surface,0,0,wl_proxy_get_version(pane.Surface),Destroy,a);
        wl_display_flush(display);
    }
    private void Configure(nint layerSurface,uint configureSerial,uint w,uint h)
    {
        lock(gate)
        {
            var pane=overlays.Concat(edges).FirstOrDefault(p=>p.Layer==layerSurface);
            if(pane is null||w==0||h==0)return;long* a=stackalloc long[6];
            a[0]=configureSerial;wl_proxy_marshal_array_flags(pane.Layer,6,0,1,0,a);   // ack_configure
            if(pane.Buffer==0||w!=pane.Width||h!=pane.Height)
            {
                if(pane.Buffer!=0)wl_proxy_marshal_array_flags(pane.Buffer,0,0,1,Destroy,a);if(pane.Pool!=0)wl_proxy_marshal_array_flags(pane.Pool,1,0,1,Destroy,a);
                pane.Width=(int)w;pane.Height=(int)h;var size=(long)pane.Width*pane.Height*4;
                // A zero-filled ARGB8888 buffer is fully transparent; ftruncate zero-fills, so no mmap is needed.
                var fd=memfd_create("nashi-drop",1);if(fd<0)return;ftruncate(fd,size);
                a[0]=0;a[1]=fd;a[2]=size;pane.Pool=wl_proxy_marshal_array_flags(shm,0,ShmPool,1,0,a);close(fd);
                a[0]=0;a[1]=0;a[2]=pane.Width;a[3]=pane.Height;a[4]=pane.Width*4;a[5]=0;pane.Buffer=wl_proxy_marshal_array_flags(pane.Pool,0,Buffer,1,0,a);
            }
            a[0]=pane.Buffer;a[1]=0;a[2]=0;wl_proxy_marshal_array_flags(pane.Surface,1,0,wl_proxy_get_version(pane.Surface),0,a);   // attach
            a[0]=0;a[1]=0;a[2]=pane.Width;a[3]=pane.Height;wl_proxy_marshal_array_flags(pane.Surface,2,0,wl_proxy_get_version(pane.Surface),0,a);   // damage
            wl_proxy_marshal_array_flags(pane.Surface,6,0,wl_proxy_get_version(pane.Surface),0,a);   // commit
            wl_display_flush(display);
        }
    }

    // ---- events -----------------------------------------------------------------------------
    /// Never let an exception leave a native callback: it would end the whole app (and its tray icon).
    private static void Fault(Exception error){try{UI.GestureWindow.Log("wayland callback failed: "+error.Message);}catch{}}
    private static WaylandDropTarget Of(nint data)=>(WaylandDropTarget)GCHandle.FromIntPtr(data).Target!;
    private static readonly nint RegistryListener=Listener((delegate* unmanaged<nint,nint,uint,nint,uint,void>)&OnGlobal,(delegate* unmanaged<nint,nint,uint,void>)&OnGlobalRemove);
    private static readonly nint OutputListener=Listener((delegate* unmanaged<nint,nint,int,int,int,int,int,nint,nint,int,void>)&OnOutputGeometry,(delegate* unmanaged<nint,nint,uint,int,int,int,void>)&OnOutputMode,
        (delegate* unmanaged<nint,nint,void>)&OnOutputDone,(delegate* unmanaged<nint,nint,int,void>)&OnOutputScale);
    private static readonly nint DeviceListener=Listener((delegate* unmanaged<nint,nint,nint,void>)&OnDataOffer,(delegate* unmanaged<nint,nint,uint,nint,int,int,nint,void>)&OnEnter,(delegate* unmanaged<nint,nint,void>)&OnLeave,
        (delegate* unmanaged<nint,nint,uint,int,int,void>)&OnMotion,(delegate* unmanaged<nint,nint,void>)&OnDrop,(delegate* unmanaged<nint,nint,nint,void>)&OnNothingOffer);
    private static readonly nint OfferListener=Listener((delegate* unmanaged<nint,nint,nint,void>)&OnOfferMime,(delegate* unmanaged<nint,nint,uint,void>)&OnNothing3,(delegate* unmanaged<nint,nint,uint,void>)&OnNothing3);
    private static readonly nint LayerListener=Listener((delegate* unmanaged<nint,nint,uint,uint,uint,void>)&OnConfigure,(delegate* unmanaged<nint,nint,void>)&OnClosed);
    private static nint Listener(params void*[] functions){var p=Marshal.AllocHGlobal(nint.Size*functions.Length);for(var i=0;i<functions.Length;i++)Marshal.WriteIntPtr(p,i*nint.Size,(nint)functions[i]);return p;}

    [UnmanagedCallersOnly]private static void OnGlobal(nint data,nint registry,uint name,nint iface,uint version)
    {
        try{
        var t=Of(data);var text=Marshal.PtrToStringUTF8(iface);long* a=stackalloc long[4];
        nint Bind(nint target,uint v){a[0]=name;a[1]=*(nint*)target;a[2]=v;a[3]=0;return wl_proxy_marshal_array_flags(registry,0,target,v,0,a);}
        switch(text)
        {
            case "wl_compositor":t.compositor=Bind(Compositor,Math.Min(version,4));break;
            case "wl_shm":t.shm=Bind(Shm,1);break;
            case "wl_seat" when t.seat==0:t.seat=Bind(Seat,Math.Min(version,5));break;
            case "wl_data_device_manager":t.manager=Bind(DeviceManager,Math.Min(version,3));break;
            case "zwlr_layer_shell_v1":t.shell=Bind(LayerShell,1);break;
            case "wl_output":
            {
                var screen=new Screen{Name=name,Proxy=Bind(Output,Math.Min(version,2))};wl_proxy_add_listener(screen.Proxy,OutputListener,data);
                // A monitor plugged in later gets the strips rebuilt once its layout is known (done event).
                lock(t.gate)t.screens.Add(screen);
                break;
            }
        }
    }
        catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnNothing3(nint data,nint proxy,uint value){}
    private void WithScreen(nint output,Action<Screen> change){lock(gate)if(screens.FirstOrDefault(s=>s.Proxy==output) is {} screen)change(screen);}
    [UnmanagedCallersOnly]private static void OnOutputGeometry(nint data,nint output,int x,int y,int pw,int ph,int subpixel,nint make,nint model,int transform)
    {
        try{Of(data).WithScreen(output,s=>{s.X=x;s.Y=y;s.Transform=transform;});}catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnOutputMode(nint data,nint output,uint flags,int w,int h,int refresh)
    {
        try{if((flags&1)!=0)Of(data).WithScreen(output,s=>{s.ModeWidth=w;s.ModeHeight=h;});}catch(Exception error){Fault(error);}   // 1 = current mode
    }
    [UnmanagedCallersOnly]private static void OnOutputScale(nint data,nint output,int factor)
    {
        try{Of(data).WithScreen(output,s=>s.Scale=factor);}catch(Exception error){Fault(error);}
    }
    /// A monitor's layout is complete (first announced, moved or resized): place the strips again.
    [UnmanagedCallersOnly]private static void OnOutputDone(nint data,nint output)
    {
        try{var t=Of(data);lock(t.gate)if(t.edgeSide!="off")t.RebuildEdges();}catch(Exception error){Fault(error);}
    }
    /// A monitor was unplugged: its strips and overlays go with it.
    [UnmanagedCallersOnly]private static void OnGlobalRemove(nint data,nint registry,uint name)
    {
        try
        {
            var t=Of(data);
            lock(t.gate)
            {
                if(t.screens.FirstOrDefault(s=>s.Name==name) is not {} screen)return;
                foreach(var pane in t.overlays.Where(p=>p.Screen==screen).ToList()){t.Close(pane);t.overlays.Remove(pane);}
                t.screens.Remove(screen);wl_proxy_destroy(screen.Proxy);if(t.edgeSide!="off")t.RebuildEdges();
            }
        }
        catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnNothingOffer(nint data,nint proxy,nint offer){}
    [UnmanagedCallersOnly]private static void OnDataOffer(nint data,nint device,nint offer)
    {
        try{
        var t=Of(data);lock(t.gate)t.offers[offer]=[];wl_proxy_add_listener(offer,OfferListener,data);
    }
        catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnOfferMime(nint data,nint offer,nint mime)
    {
        try{
        var t=Of(data);var text=Marshal.PtrToStringUTF8(mime);if(text is null)return;lock(t.gate)if(t.offers.TryGetValue(offer,out var set))set.Add(text);
    }
        catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnEnter(nint data,nint device,uint serial,nint surface,int fx,int fy,nint offer)
    {
        try{
        var t=Of(data);Pane? pane,edge;bool covered;
        lock(t.gate)
        {
            pane=surface==0?null:t.overlays.FirstOrDefault(p=>p.Surface==surface);edge=surface==0?null:t.edges.FirstOrDefault(p=>p.Surface==surface);covered=t.overlays.Count>0;
            t.current=pane;t.serial=serial;t.offer=offer;t.early=null;t.mimes.Clear();if(t.offers.TryGetValue(offer,out var set))t.mimes.UnionWith(set);
        }
        // The hot edge only announces the drag; the full overlay then takes it over.
        if(edge is not null&&!covered){var (ex,ey)=Global(edge,fx,fy);t.EdgeEntered?.Invoke(ex,ey);return;}
        if(pane is null||offer==0)return;
        t.inside=true;(t.x,t.y)=Global(pane,fx,fy);
        long* a=stackalloc long[2];var mime=t.Mime();var m=mime is null?0:Str(mime);
        a[0]=serial;a[1]=m;wl_proxy_marshal_array_flags(offer,0,0,wl_proxy_get_version(offer),0,a);   // accept
        if(wl_proxy_get_version(offer)>=3){a[0]=1;a[1]=1;wl_proxy_marshal_array_flags(offer,4,0,wl_proxy_get_version(offer),0,a);}   // set_actions copy
        wl_display_flush(t.display);t.Entered?.Invoke(t.x,t.y);
        if(mime is not null){t.early=t.ReadAsync(offer,mime);var read=t.early;_=read.ContinueWith(r=>{if(r.IsCompletedSuccessfully&&r.Result.Length>0)t.Offered?.Invoke(r.Result);},TaskScheduler.Default);}
    }
        catch(Exception error){Fault(error);}
    }
    /// Surface-local fixed-point coordinates to the global space (the monitor's corner plus the offset).
    private static (double X,double Y) Global(Pane pane,int fx,int fy)=>((pane.Screen?.X??0)+fx/256.0,(pane.Screen?.Y??0)+fy/256.0);
    [UnmanagedCallersOnly]private static void OnLeave(nint data,nint device)
    {
        try{var t=Of(data);if(!t.inside)return;t.inside=false;t.Left?.Invoke();}
        catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnMotion(nint data,nint device,uint time,int fx,int fy)
    {
        try{var t=Of(data);if(!t.inside||t.current is not {} pane)return;(t.x,t.y)=Global(pane,fx,fy);t.Moved?.Invoke(t.x,t.y);}
        catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnDrop(nint data,nint device)
    {
        try{
        var t=Of(data);if(!t.inside||t.offer==0)return;t.inside=false;var offer=t.offer;var (dx,dy)=(t.x,t.y);var early=t.early;
        _=WaylandDropFlow.FinishAsync(t,offer,early,dx,dy);
    }
        catch(Exception error){Fault(error);}
    }
    /// Ends the transfer (finish + destroy) once the dropped data has been read.
    internal void FinishOffer(nint offer)
    {
        lock(gate)
        {
            long* b=stackalloc long[1];
            if(wl_proxy_get_version(offer)>=3)wl_proxy_marshal_array_flags(offer,3,0,wl_proxy_get_version(offer),0,b);   // finish
            wl_proxy_marshal_array_flags(offer,2,0,wl_proxy_get_version(offer),Destroy,b);offers.Remove(offer);wl_display_flush(display);
        }
    }
    internal void RaiseDropped(string[] uris,double x,double y)=>Dropped?.Invoke(uris,x,y);
    internal string? CurrentMime=>Mime();
    private Task<string[]>? early;
    /// Asks the source for the dragged data. Wayland allows this during the drag, so the wheel can show
    /// the real choices for the files before they are dropped.
    internal Task<string[]> ReadAsync(nint offer,string mime)
    {
        int readFd;
        lock(gate)
        {
            int* fds=stackalloc int[2];if(pipe2(fds,0x80000)!=0)return Task.FromResult(Array.Empty<string>());   // O_CLOEXEC
            long* a=stackalloc long[2];a[0]=Str(mime);a[1]=fds[1];wl_proxy_marshal_array_flags(offer,1,0,wl_proxy_get_version(offer),0,a);   // receive
            close(fds[1]);wl_display_flush(display);readFd=fds[0];
        }
        return Task.Run(()=>
        {
            var bytes=new List<byte>();var chunk=new byte[65536];
            fixed(byte* p=chunk){while(true){var n=read(readFd,p,chunk.Length);if(n<=0)break;bytes.AddRange(chunk.AsSpan(0,(int)n).ToArray());if(bytes.Count>4_000_000)break;}}
            close(readFd);
            return Encoding.UTF8.GetString(bytes.ToArray()).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Where(l=>!l.StartsWith('#')).ToArray();
        });
    }
    [UnmanagedCallersOnly]private static void OnConfigure(nint data,nint layer,uint serial,uint w,uint h){try{Of(data).Configure(layer,serial,w,h);}catch(Exception error){Fault(error);}}
    [UnmanagedCallersOnly]private static void OnClosed(nint data,nint layer){}
    private string? Mime()=>mimes.Contains("text/uri-list")?"text/uri-list":mimes.Contains("text/x-moz-url")?null:mimes.Contains("text/plain;charset=utf-8")?"text/plain;charset=utf-8":mimes.Contains("text/plain")?"text/plain":mimes.Contains("UTF8_STRING")?"UTF8_STRING":null;
}

/// The async tail of a drop, outside the unsafe class (C# does not allow await in unsafe code).
internal static class WaylandDropFlow
{
    internal static async Task FinishAsync(WaylandDropTarget target,nint offer,Task<string[]>? early,double x,double y)
    {
        var uris=early is null?[]:await early;
        if(uris.Length==0&&target.CurrentMime is {} mime)uris=await target.ReadAsync(offer,mime);
        target.FinishOffer(offer);target.RaiseDropped(uris,x,y);
    }
}
