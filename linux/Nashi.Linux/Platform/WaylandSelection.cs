using System.Runtime.InteropServices;
using System.Text;
namespace Nashi.Linux;

/// The files selected in another app, read without a drag. Dolphin, the Plasma desktop and most file
/// managers put the selection in the primary selection (what a middle click pastes). Wayland hands
/// that only to the focused app, but ext-data-control-v1 (KWin, wlroots, Mutter 48+) lets a clipboard
/// tool watch it, so the gesture shortcut can offer the wheel for selected files too.
public sealed unsafe partial class WaylandDropTarget
{
    private sealed record ControlInterfaces(nint Manager,nint Device,nint Offer,nint Source);
    private static readonly Lazy<ControlInterfaces> Control=new(()=>
    {
        var none=Types(0,0,0,0,0,0);
        nint New(){return Marshal.AllocHGlobal(sizeof(WlInterface));}
        var offer=New();var source=New();var device=New();var manager=New();
        *(WlInterface*)offer=new WlInterface{Name=Str("ext_data_control_offer_v1"),Version=1,MethodCount=2,Methods=Messages(("receive","sh",none),("destroy","",none)),EventCount=1,Events=Messages(("offer","s",none))};
        *(WlInterface*)source=new WlInterface{Name=Str("ext_data_control_source_v1"),Version=1,MethodCount=2,Methods=Messages(("offer","s",none),("destroy","",none)),EventCount=2,Events=Messages(("send","sh",none),("cancelled","",none))};
        *(WlInterface*)device=new WlInterface{Name=Str("ext_data_control_device_v1"),Version=1,MethodCount=3,Methods=Messages(("set_selection","?o",Types(source)),("destroy","",none),("set_primary_selection","?o",Types(source))),
            EventCount=4,Events=Messages(("data_offer","n",Types(offer)),("selection","?o",Types(offer)),("finished","",none),("primary_selection","?o",Types(offer)))};
        *(WlInterface*)manager=new WlInterface{Name=Str("ext_data_control_manager_v1"),Version=1,MethodCount=3,Methods=Messages(("create_data_source","n",Types(source)),("get_data_device","no",Types(device,Seat)),("destroy","",none)),EventCount=0,Events=0};
        return new(manager,device,offer,source);
    });
    private nint controlManager,controlDevice,primary,clip;private readonly Dictionary<nint,HashSet<string>> controlOffers=[];
    /// When the primary selection last changed (so a selection from long ago can be told apart).
    public DateTime PrimaryChanged{get;private set;}
    /// When the clipboard last changed while Nashi was running.
    public DateTime ClipboardChanged{get;private set;}

    private static readonly nint ControlDeviceListener=Listener((delegate* unmanaged<nint,nint,nint,void>)&OnControlOffer,(delegate* unmanaged<nint,nint,nint,void>)&OnControlSelection,
        (delegate* unmanaged<nint,nint,void>)&OnControlFinished,(delegate* unmanaged<nint,nint,nint,void>)&OnControlPrimary);
    private static readonly nint ControlOfferListener=Listener((delegate* unmanaged<nint,nint,nint,void>)&OnControlMime);

    /// Called from the registry: bind the manager when the compositor offers it.
    private void BindControl(nint registry,uint name)
    {
        long* a=stackalloc long[4];a[0]=name;a[1]=*(nint*)Control.Value.Manager;a[2]=1;a[3]=0;
        controlManager=wl_proxy_marshal_array_flags(registry,0,Control.Value.Manager,1,0,a);
    }
    /// After the seat is known: watch the selections.
    private void StartControl()
    {
        if(controlManager==0||seat==0)return;long* a=stackalloc long[2];a[0]=0;a[1]=seat;
        controlDevice=wl_proxy_marshal_array_flags(controlManager,1,Control.Value.Device,1,0,a);
        wl_proxy_add_listener(controlDevice,ControlDeviceListener,GCHandle.ToIntPtr(self));
    }
    public bool CanReadSelection=>controlDevice!=0;
    /// The formats the current primary selection offers (for --selection-check).
    internal string PrimaryFormats{get{lock(gate)return primary==0?"(none)":controlOffers.TryGetValue(primary,out var m)?string.Join(", ",m):"?";}}

    [UnmanagedCallersOnly]private static void OnControlOffer(nint data,nint device,nint offer)
    {
        try{var t=Of(data);lock(t.gate)t.controlOffers[offer]=[];wl_proxy_add_listener(offer,ControlOfferListener,data);}catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnControlMime(nint data,nint offer,nint mime)
    {
        try{var t=Of(data);var text=Marshal.PtrToStringUTF8(mime);if(text is null)return;lock(t.gate)if(t.controlOffers.TryGetValue(offer,out var set))set.Add(text);}catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnControlSelection(nint data,nint device,nint offer)
    {
        try{var t=Of(data);lock(t.gate){if(t.clip!=0&&t.clip!=offer&&t.clip!=t.primary)t.DropControlOffer(t.clip);t.clip=offer;t.ClipboardChanged=DateTime.UtcNow;}}catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnControlPrimary(nint data,nint device,nint offer)
    {
        try{var t=Of(data);lock(t.gate){if(t.primary!=0&&t.primary!=offer&&t.primary!=t.clip)t.DropControlOffer(t.primary);t.primary=offer;t.PrimaryChanged=DateTime.UtcNow;}}catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnControlFinished(nint data,nint device){try{var t=Of(data);lock(t.gate)t.controlDevice=0;}catch(Exception error){Fault(error);}}
    private void DropControlOffer(nint offer)
    {
        lock(gate){controlOffers.Remove(offer);long* a=stackalloc long[1];wl_proxy_marshal_array_flags(offer,1,0,1,Destroy,a);wl_display_flush(display);}
    }

    /// The primary selection's URIs (file:// lines), or an empty list.
    public Task<string[]> SelectedUrisAsync(){nint offer;lock(gate)offer=primary;return ReceiveAsync(offer,"text/uri-list",1000).ContinueWith(t=>Lines(t.Result),TaskScheduler.Default);}
    /// The clipboard's URIs (files copied with Ctrl+C), or an empty list.
    public Task<string[]> ClipboardUrisAsync(){nint offer;lock(gate)offer=clip;return ReceiveAsync(offer,"text/uri-list",1000).ContinueWith(t=>Lines(t.Result),TaskScheduler.Default);}
    private static string[] Lines(byte[]? bytes)=>bytes is null?[]:Encoding.UTF8.GetString(bytes).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Where(l=>!l.StartsWith('#')).ToArray();

    /// Asks the offer's owner for one format. A source that does not answer in time is given up on
    /// (poll, so the read never hangs). Null when the format is not offered.
    private Task<byte[]?> ReceiveAsync(nint offer,string mime,int timeout,int limit=32_000_000)
    {
        int readFd;
        lock(gate)
        {
            if(offer==0||!controlOffers.TryGetValue(offer,out var mimes)||!mimes.Contains(mime))return Task.FromResult<byte[]?>(null);
            int* fds=stackalloc int[2];if(pipe2(fds,0x80000)!=0)return Task.FromResult<byte[]?>(null);
            long* a=stackalloc long[2];a[0]=Str(mime);a[1]=fds[1];wl_proxy_marshal_array_flags(offer,0,0,1,0,a);   // receive
            close(fds[1]);wl_display_flush(display);readFd=fds[0];
        }
        return Task.Run<byte[]?>(()=>
        {
            var bytes=new List<byte>();var chunk=new byte[65536];var deadline=Environment.TickCount64+timeout;
            try
            {
                fixed(byte* p=chunk)
                {
                    while(true)
                    {
                        var left=(int)(deadline-Environment.TickCount64);if(left<=0)return null;
                        var poll=new PollFd{Fd=readFd,Events=1};if(Poll(&poll,1,left)<=0)return null;   // POLLIN; 0 = the source never answered
                        var n=read(readFd,p,chunk.Length);if(n<=0)break;bytes.AddRange(chunk.AsSpan(0,(int)n).ToArray());if(bytes.Count>limit)return null;
                    }
                }
            }
            finally{close(readFd);}
            return bytes.ToArray();
        });
    }

    // ---- borrowing the clipboard: save it, let Dolphin copy its selection, put it back ----------
    private static readonly string[] SkipFormats=["TARGETS","MULTIPLE","TIMESTAMP","SAVE_TARGETS","DELETE","INSERT_PROPERTY","INSERT_SELECTION"];
    /// The current clipboard offer and its formats worth saving (none when the clipboard is empty).
    internal (nint Offer,string[] Formats) ClipboardFormats()
    {
        lock(gate){if(clip==0||!controlOffers.TryGetValue(clip,out var mimes))return(0,[]);return(clip,mimes.Where(m=>!SkipFormats.Contains(m)).Take(16).ToArray());}
    }
    internal Task<byte[]?> Receive(nint offer,string mime,int timeout)=>ReceiveAsync(offer,mime,timeout);
    private readonly Dictionary<nint,Dictionary<string,byte[]>> sources=[];
    private static readonly nint SourceListener=Listener((delegate* unmanaged<nint,nint,nint,int,void>)&OnSourceSend,(delegate* unmanaged<nint,nint,void>)&OnSourceCancelled);
    /// Puts saved clipboard contents back (null clears it). Nashi serves them until another app copies.
    public void RestoreClipboard(Dictionary<string,byte[]>? saved)
    {
        lock(gate)
        {
            if(controlManager==0||controlDevice==0)return;long* a=stackalloc long[2];nint source=0;
            if(saved is {Count:>0})
            {
                a[0]=0;source=wl_proxy_marshal_array_flags(controlManager,0,Control.Value.Source,1,0,a);   // create_data_source
                wl_proxy_add_listener(source,SourceListener,GCHandle.ToIntPtr(self));sources[source]=saved;
                foreach(var mime in saved.Keys){a[0]=Str(mime);wl_proxy_marshal_array_flags(source,0,0,1,0,a);}   // offer
            }
            a[0]=source;wl_proxy_marshal_array_flags(controlDevice,0,0,1,0,a);   // set_selection
            wl_display_flush(display);
        }
    }
    [UnmanagedCallersOnly]private static void OnSourceSend(nint data,nint source,nint mime,int fd)
    {
        try
        {
            var t=Of(data);var type=Marshal.PtrToStringUTF8(mime);byte[]? bytes=null;
            lock(t.gate)if(type is not null&&t.sources.TryGetValue(source,out var saved))saved.TryGetValue(type,out bytes);
            // Write off the Wayland thread: a slow reader must not stall the event loop.
            _=Task.Run(()=>{try{if(bytes is not null)using(var stream=new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(fd,true),FileAccess.Write))stream.Write(bytes);else close(fd);}catch(IOException){}});
        }
        catch(Exception error){Fault(error);}
    }
    [UnmanagedCallersOnly]private static void OnSourceCancelled(nint data,nint source)
    {
        try{var t=Of(data);lock(t.gate){t.sources.Remove(source);long* a=stackalloc long[1];wl_proxy_marshal_array_flags(source,1,0,1,Destroy,a);wl_display_flush(t.display);}}catch(Exception error){Fault(error);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct PollFd{public int Fd;public short Events,Revents;}
    [DllImport("libc",EntryPoint="poll",SetLastError=true)]private static extern int Poll(PollFd* fds,ulong count,int timeout);
}

/// The awaiting halves of clipboard borrowing (C# does not allow await inside the unsafe class).
public static class WaylandClipboard
{
    /// Every format of the current clipboard (up to 32 MB in all), or null when it is empty.
    public static async Task<Dictionary<string,byte[]>?> SaveClipboardAsync(this WaylandDropTarget target)
    {
        var (offer,formats)=target.ClipboardFormats();if(offer==0)return null;
        var saved=new Dictionary<string,byte[]>();long total=0;
        foreach(var mime in formats)
        {
            var bytes=await target.Receive(offer,mime,800);if(bytes is null)continue;
            total+=bytes.Length;if(total>32_000_000)break;saved[mime]=bytes;
        }
        return saved;
    }
    /// Waits for the clipboard to change after a moment (Dolphin's Copy), up to the timeout.
    public static async Task<bool> WaitClipboardChangeAsync(this WaylandDropTarget target,DateTime after,int timeout)
    {
        var end=Environment.TickCount64+timeout;
        while(Environment.TickCount64<end){if(target.ClipboardChanged>after)return true;await Task.Delay(15);}
        return false;
    }
}
