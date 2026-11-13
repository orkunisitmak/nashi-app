using Tmds.DBus.Protocol;
namespace Nashi.Linux;

/// Desktop notifications (org.freedesktop.Notifications: KDE, GNOME, Xfce, Cinnamon…). Used when
/// work finishes while the Nashi window is hidden, for example after a gesture from the tray.
public sealed class Notifications:IDisposable
{
    private const string Service="org.freedesktop.Notifications",Path="/org/freedesktop/Notifications";
    // Written from the caller of ShowAsync, read from the D-Bus thread; an entry goes when its notification is used or closed.
    private readonly DBusConnection connection;private readonly System.Collections.Concurrent.ConcurrentDictionary<uint,Action<string>> handlers=new();
    private Notifications(DBusConnection connection)=>this.connection=connection;
    public static async Task<Notifications?> TryCreateAsync()
    {
        if(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is not {Length:>0} address)return null;
        var connection=new DBusConnection(address);
        try
        {
            await connection.ConnectAsync();
            var notifications=new Notifications(connection);
            await connection.AddMatchAsync(new MatchRule{Type=MessageType.Signal,Interface=Service,Member="ActionInvoked"},
                (Message m,object? _)=>{var r=m.GetBodyReader();return(r.ReadUInt32(),r.ReadString());},
                (Notification<(uint Id,string Action)> note)=>{if(note.HasValue&&notifications.handlers.TryRemove(note.Value.Id,out var handler))Avalonia.Threading.Dispatcher.UIThread.Post(()=>handler(note.Value.Action));},
                false,ObserverFlags.None,null);
            await connection.AddMatchAsync(new MatchRule{Type=MessageType.Signal,Interface=Service,Member="NotificationClosed"},
                (Message m,object? _)=>m.GetBodyReader().ReadUInt32(),
                (Notification<uint> note)=>{if(note.HasValue)notifications.handlers.TryRemove(note.Value,out _);},
                false,ObserverFlags.None,null);
            return notifications;
        }
        catch(Exception error)when(error is DBusExceptionBase or IOException or InvalidOperationException or System.Net.Sockets.SocketException){connection.Dispose();return null;}
    }
    private delegate void Body(ref MessageWriter writer);
    /// Shows (or replaces) a notification. actions are (key, label) pairs; onAction gets the key.
    public async Task<uint> ShowAsync(string summary,string body,(string Key,string Label)[]? actions=null,Action<string>? onAction=null,uint replaces=0)
    {
        var icon=System.IO.Path.Combine(AppContext.BaseDirectory,"packaging","nashi.png");
        var writer=connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service,Path,Service,"Notify","susssasa{sv}i",MessageFlags.None);
        writer.WriteString("Nashi");writer.WriteUInt32(replaces);writer.WriteString(File.Exists(icon)?icon:"");writer.WriteString(summary);writer.WriteString(body);
        writer.WriteArray((actions??[]).SelectMany(a=>new[]{a.Key,a.Label}).ToArray());
        var hints=writer.WriteDictionaryStart();writer.WriteDictionaryEntryStart();writer.WriteString("desktop-entry");writer.WriteVariantString("nashi");writer.WriteDictionaryEnd(hints);
        writer.WriteInt32(-1);
        var message=writer.CreateMessage();writer.Dispose();
        try
        {
            var id=await connection.CallMethodAsync(message,(Message m,object? _)=>m.GetBodyReader().ReadUInt32(),null);
            if(onAction is not null)handlers[id]=onAction;return id;
        }
        catch(DBusExceptionBase){return 0;}
    }
    public void Dispose()=>connection.Dispose();
}
