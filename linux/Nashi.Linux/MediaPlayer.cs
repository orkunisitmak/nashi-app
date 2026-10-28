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
            if(mpv_initialize(player)<0)throw new InvalidOperationException("Media preview could not initialize.");
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
    public void Load(string file){if(Available){Pause();Command("loadfile",System.IO.Path.GetFullPath(file));}}
    public void Toggle(){if(Available)Command("cycle","pause");}
    public void Seek(double seconds){if(Available&&double.IsFinite(seconds))Command("seek",Math.Clamp(seconds,0,Math.Max(Duration,0)).ToString(CultureInfo.InvariantCulture),"absolute+exact");}
    public void SetSpeed(double speed){if(Available&&double.IsFinite(speed))mpv_set_property_string(player,"speed",Math.Clamp(speed,.05,32).ToString(CultureInfo.InvariantCulture));}
    public void Pause(){if(Available)mpv_set_property_string(player,"pause","yes");}
    private double Number(string key)=>double.TryParse(Get(key),CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:0;
    private string? Get(string key)
    {
        if(!Available)return null;var value=mpv_get_property_string(player,key);if(value==IntPtr.Zero)return null;
        try{return Marshal.PtrToStringUTF8(value);}finally{mpv_free(value);}
    }
    private void Command(params string[] args)
    {
        if(!Available)return;var strings=args.Select(Marshal.StringToCoTaskMemUTF8).ToArray();var pointers=Marshal.AllocHGlobal((strings.Length+1)*IntPtr.Size);
        try{for(var i=0;i<strings.Length;i++)Marshal.WriteIntPtr(pointers,i*IntPtr.Size,strings[i]);Marshal.WriteIntPtr(pointers,strings.Length*IntPtr.Size,IntPtr.Zero);if(mpv_command(player,pointers)<0)throw new InvalidOperationException("Media preview command failed.");}
        finally{Marshal.FreeHGlobal(pointers);foreach(var value in strings)Marshal.FreeCoTaskMem(value);}
    }
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr mpv_create();
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_initialize(IntPtr handle);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_set_option_string(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_set_property_string(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr mpv_get_property_string(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string name);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern int mpv_command(IntPtr handle,IntPtr args);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern void mpv_free(IntPtr value);
    [DllImport("nashi-mpv",CallingConvention=CallingConvention.Cdecl)]private static extern void mpv_terminate_destroy(IntPtr handle);
}
