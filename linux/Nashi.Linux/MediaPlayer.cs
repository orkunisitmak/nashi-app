using System.Runtime.InteropServices;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Platform;
namespace Nashi.Linux;

/// Embeds the distribution's libmpv, with no shell commands or external player window.
internal sealed class MediaPlayer(string path):NativeControlHost
{
    static MediaPlayer()=>NativeLibrary.SetDllImportResolver(typeof(MediaPlayer).Assembly,(name,assembly,path)=>
    {
        if(name!="nashi-mpv")return IntPtr.Zero;
        foreach(var library in new[]{"libmpv.so.2","libmpv.so.1"})if(NativeLibrary.TryLoad(library,assembly,path,out var loaded))return loaded;
        return IntPtr.Zero;
    });
    private IntPtr player;
    private readonly Dictionary<string,string> properties=[];
    public bool Available=>player!=IntPtr.Zero;
    public string? Error{get;private set;}
    public double Position=>Number("time-pos");
    public double Duration=>Number("duration");
    internal string? LoadedPath=>Get("path");
    public bool Paused=>Get("pause")!="no";
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle=base.CreateNativeControlCore(parent);
        try
        {
            player=mpv_create();if(player==IntPtr.Zero)throw new InvalidOperationException("Media preview could not start.");
            Option("wid",handle.Handle.ToInt64().ToString(CultureInfo.InvariantCulture));Option("config","no");Option("terminal","no");Option("input-default-bindings","no");Option("input-vo-keyboard","no");Option("idle","yes");Option("pause","yes");Option("keep-open","yes");
            // Hosted UI checks have no audio device; retain decoding/timing with mpv's null sink.
            if(Environment.GetCommandLineArgs().Contains("--smoke-test"))Option("ao","null");
            if(mpv_initialize(player)<0)throw new InvalidOperationException("Media preview could not initialize.");
            foreach(var property in new[]{"time-pos","duration","pause","path"})if(mpv_observe_property(player,0,property,1)<0)throw new InvalidOperationException("Media property observation failed: "+property);
            Command("loadfile",System.IO.Path.GetFullPath(path));
        }
        catch(Exception error)when(error is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {Error=error.Message;DisposePlayer();}
        return handle;
    }
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {DisposePlayer();base.DestroyNativeControlCore(control);}
    private void DisposePlayer(){if(player!=IntPtr.Zero){mpv_terminate_destroy(player);player=IntPtr.Zero;}}
    private void Option(string key,string value){if(mpv_set_option_string(player,key,value)<0)throw new InvalidOperationException("Unsupported media preview option: "+key);}
    public void Load(string file){if(Available){properties.Clear();Error=null;Pause();Command("loadfile",System.IO.Path.GetFullPath(file));}}
    public void Toggle(){if(Available)Command("cycle","pause");}
    public void Seek(double seconds){if(Available&&double.IsFinite(seconds))Command("seek",Math.Clamp(seconds,0,Math.Max(Duration,0)).ToString(CultureInfo.InvariantCulture),"absolute+exact");}
    public void SetSpeed(double speed){if(Available&&double.IsFinite(speed))Command("set","speed",Math.Clamp(speed,.05,32).ToString(CultureInfo.InvariantCulture));}
    public void Pause(){if(Available)Command("set","pause","yes");}
    private double Number(string key)=>double.TryParse(Get(key),CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:0;
    private string? Get(string key)
    {
        if(!Available)return null;PollEvents();return properties.GetValueOrDefault(key);
    }
    private void PollEvents()
    {
        // Never wait on the playback core from the Avalonia thread. It may be creating
        // a video output while another file replaces an audio-only preview.
        for(var i=0;i<256;i++){
            var pointer=mpv_wait_event(player,0);if(pointer==IntPtr.Zero)return;var item=Marshal.PtrToStructure<MpvEvent>(pointer);if(item.Id==0)return;
            if(item.Id==22&&item.Data!=IntPtr.Zero){
                var property=Marshal.PtrToStructure<MpvProperty>(item.Data);var name=Marshal.PtrToStringUTF8(property.Name);if(name is null)continue;
                if(property.Format==1&&property.Data!=IntPtr.Zero){var value=Marshal.ReadIntPtr(property.Data);if(value!=IntPtr.Zero)properties[name]=Marshal.PtrToStringUTF8(value)??"";}
                else properties.Remove(name);
            }else if(item.Id==5&&item.Error<0)Error="Media command failed: "+(Marshal.PtrToStringUTF8(mpv_error_string(item.Error))??item.Error.ToString(CultureInfo.InvariantCulture));
        }
    }
    [StructLayout(LayoutKind.Sequential)]private struct MpvEvent{public int Id;public int Error;public ulong UserData;public IntPtr Data;}
    [StructLayout(LayoutKind.Sequential)]private struct MpvProperty{public IntPtr Name;public int Format;public IntPtr Data;}
    private void Command(params string[] args)
    {
        if(!Available)return;var strings=args.Select(Marshal.StringToCoTaskMemUTF8).ToArray();var pointers=Marshal.AllocHGlobal((strings.Length+1)*IntPtr.Size);
        try{for(var i=0;i<strings.Length;i++)Marshal.WriteIntPtr(pointers,i*IntPtr.Size,strings[i]);Marshal.WriteIntPtr(pointers,strings.Length*IntPtr.Size,IntPtr.Zero);if(mpv_command_async(player,0,pointers)<0)throw new InvalidOperationException("Media preview command failed.");}
        finally{Marshal.FreeHGlobal(pointers);foreach(var value in strings)Marshal.FreeCoTaskMem(value);}
    }
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr mpv_create();
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_initialize(IntPtr handle);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_set_option_string(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_observe_property(IntPtr handle,ulong userdata,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,int format);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr mpv_wait_event(IntPtr handle,double timeout);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_command_async(IntPtr handle,ulong userdata,IntPtr args);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr mpv_error_string(int error);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern void mpv_terminate_destroy(IntPtr handle);
}
