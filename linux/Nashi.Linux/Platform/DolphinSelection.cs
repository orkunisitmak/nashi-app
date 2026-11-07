using Tmds.DBus.Protocol;
namespace Nashi.Linux;

/// The files selected in Dolphin, for the gesture shortcut pressed without a drag. Wayland gives one
/// app no way to read another's selection, and Dolphin does not publish it as the primary selection,
/// but Dolphin exports its window actions on D-Bus. Nashi finds the active Dolphin window and runs its
/// own Copy action (the same as Ctrl+C there, without faking a key press), reads the files from the
/// clipboard and puts the clipboard back as it was.
public static class DolphinSelection
{
    private static DBusConnection? connection;
    private static async Task<DBusConnection?> ConnectionAsync()
    {
        if(connection is not null)return connection;
        if(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is not {Length:>0} address)return null;
        var made=new DBusConnection(address);
        try{await made.ConnectAsync();return connection=made;}
        catch(Exception error)when(error is DBusExceptionBase or IOException or InvalidOperationException or System.Net.Sockets.SocketException){made.Dispose();return null;}
    }
    /// A Dolphin window with focus and something selected (its Copy action is enabled), or null.
    public static async Task<(string Service,string Path)?> ActiveWindowAsync()
    {
        try
        {
            if(await ConnectionAsync() is not {} bus)return null;
            foreach(var service in (await bus.ListServicesAsync()).Where(s=>s.StartsWith("org.kde.dolphin-",StringComparison.Ordinal)))
            {
                // Windows are /dolphin/Dolphin_1, _2, …; numbers of closed windows are not reused.
                for(var n=1;n<=16;n++)
                {
                    var path=$"/dolphin/Dolphin_{n}";
                    try
                    {
                        if(!await Call(bus,service,path,"org.kde.dolphin.MainWindow","isActiveWindow","",(ref MessageWriter w)=>{},m=>m.GetBodyReader().ReadBool()))continue;
                        var copy=await Call(bus,service,path,"org.kde.KMainWindow","actionIsEnabled","s",(ref MessageWriter w)=>w.WriteString("edit_copy"),m=>m.GetBodyReader().ReadBool());
                        return copy?(service,path):null;
                    }
                    catch(DBusExceptionBase){}
                }
            }
        }
        catch(Exception error)when(error is DBusExceptionBase or IOException or InvalidOperationException){}
        return null;
    }
    /// Runs Copy in that Dolphin window.
    public static async Task<bool> CopyAsync((string Service,string Path) window)
    {
        try{return await ConnectionAsync() is {} bus&&await Call(bus,window.Service,window.Path,"org.kde.KMainWindow","activateAction","s",(ref MessageWriter w)=>w.WriteString("edit_copy"),m=>m.GetBodyReader().ReadBool());}
        catch(Exception error)when(error is DBusExceptionBase or IOException or InvalidOperationException){return false;}
    }

    /// The selected files: Dolphin's selection borrowed through the clipboard, or files copied (Ctrl+C)
    /// in any app in the last two minutes. Empty when there are none.
    public static async Task<string[]> FilesAsync(WaylandDropTarget target,(string Service,string Path)? dolphin)
    {
        if(!target.CanReadSelection)return [];
        if(dolphin is {} window)
        {
            var saved=await target.SaveClipboardAsync();var before=DateTime.UtcNow;
            // Some sources (XWayland apps, Electron) may not answer in time; Klipper's copy of the text is the fallback.
            var klipperText=saved is {Count:0}?await KlipperAsync("getClipboardContents",null):null;
            UI.GestureWindow.Log($"dolphin {window.Path}: saved clipboard {(saved is null?"empty":string.Join(",",saved.Select(k=>k.Key+":"+k.Value.Length)))}");
            if(await CopyAsync(window)&&await target.WaitClipboardChangeAsync(before,1000))
            {
                var uris=await target.ClipboardUrisAsync();
                if(klipperText is not null)await KlipperAsync("setClipboardContents",klipperText);else target.RestoreClipboard(saved);
                UI.GestureWindow.Log($"copied {uris.Length} uri(s), clipboard put back");
                _=Task.Delay(700).ContinueWith(_=>UI.GestureWindow.Log("clipboard after: "+string.Join(",",target.ClipboardFormats().Formats)),TaskScheduler.Default);
                if(Paths(uris) is {Length:>0} files)return files;
            }
        }
        if(DateTime.UtcNow-target.ClipboardChanged<TimeSpan.FromMinutes(2)&&Paths(await target.ClipboardUrisAsync()) is {Length:>0} copied)return copied;
        return [];
    }
    /// Klipper (Plasma's clipboard history): read or set the clipboard text. Null when it is not running.
    private static async Task<string?> KlipperAsync(string member,string? text)
    {
        try
        {
            if(await ConnectionAsync() is not {} bus)return null;
            if(text is null)return await Call(bus,"org.kde.klipper","/klipper","org.kde.klipper.klipper",member,"",(ref MessageWriter w)=>{},m=>m.GetBodyReader().ReadString());
            await Call(bus,"org.kde.klipper","/klipper","org.kde.klipper.klipper",member,"s",(ref MessageWriter w)=>w.WriteString(text),_=>true);return text;
        }
        catch(Exception error)when(error is DBusExceptionBase or IOException or InvalidOperationException){return null;}
    }
    private static string[] Paths(string[] uris)=>uris.Where(u=>u.StartsWith("file://",StringComparison.Ordinal)&&Uri.TryCreate(u,UriKind.Absolute,out _)).Select(u=>new Uri(u).LocalPath).Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();

    private delegate void Body(ref MessageWriter writer);
    private static Task<T> Call<T>(DBusConnection bus,string service,string path,string iface,string member,string signature,Body body,Func<Message,T> read)
    {
        var writer=bus.GetMessageWriter();
        writer.WriteMethodCallHeader(service,path,iface,member,signature.Length==0?null:signature,MessageFlags.None);body(ref writer);
        var message=writer.CreateMessage();writer.Dispose();
        return bus.CallMethodAsync(message,(Message m,object? _)=>read(m),null);
    }
}
