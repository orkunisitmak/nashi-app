using Tmds.DBus.Protocol;
namespace Nashi.Linux;

/// System-wide shortcuts through KDE's kglobalaccel (the service behind System Settings →
/// Shortcuts). Nashi registers its own component, sets the keys chosen in its Settings and
/// listens for globalShortcutPressed, so the shortcuts work while any app has focus, including
/// in the middle of a drag in Dolphin. Other desktops return null; Nashi then keeps its in-app keys.
public sealed class GlobalShortcuts:IDisposable
{
    private const string Service="org.kde.kglobalaccel",Root="/kglobalaccel",Interface="org.kde.KGlobalAccel";
    // kglobalaccel flags: SetPresent marks the action active, NoAutoloading makes our keys win over stored ones.
    private const uint SetPresent=2,NoAutoloading=4;
    private readonly DBusConnection connection;private readonly string component;private readonly string friendly;
    private readonly HashSet<string> registered=[];
    public event Action<string>? Pressed;
    private GlobalShortcuts(DBusConnection connection,string component,string friendly){this.connection=connection;this.component=component;this.friendly=friendly;}

    public static async Task<GlobalShortcuts?> TryCreateAsync(string component="nashi",string friendly="Nashi")
    {
        if(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is not {Length:>0} address)return null;
        var connection=new DBusConnection(address);
        try
        {
            await connection.ConnectAsync();
            var services=await connection.ListServicesAsync();
            if(!services.Contains(Service)){connection.Dispose();return null;}
            var shortcuts=new GlobalShortcuts(connection,component,friendly);await shortcuts.ListenAsync();return shortcuts;
        }
        catch(Exception error)when(error is DBusExceptionBase or IOException or InvalidOperationException or System.Net.Sockets.SocketException){connection.Dispose();return null;}
    }
    private string[] ActionId(string id,string title)=>[component,id,friendly,title];
    private async Task ListenAsync()
    {
        // The component's object exists once something is registered; its path is predictable.
        await connection.AddMatchAsync(new MatchRule{Type=MessageType.Signal,Interface="org.kde.kglobalaccel.Component",Member="globalShortcutPressed"},
            (Message message,object? _)=>{var reader=message.GetBodyReader();var owner=reader.ReadString();var action=reader.ReadString();return(owner,action);},
            (Notification<(string Owner,string Action)> note)=>{if(note.HasValue&&note.Value.Owner==component)Pressed?.Invoke(note.Value.Action);},
            false,ObserverFlags.None,null);
    }
    /// Binds one action. Returns the combination KDE accepted, or null when it was refused
    /// (for example because another app already owns those keys).
    public async Task<string?> BindAsync(string id,string title,string? combo)
    {
        var action=ActionId(id,title);
        await Call("doRegister","as",(ref MessageWriter w)=>w.WriteArray(action));registered.Add(id);
        var keys=combo is {Length:>0}&&KeyCombo.ToQt(combo) is {} code?new[]{code}:Array.Empty<int>();
        var accepted=await Call("setShortcut","asaiu",(ref MessageWriter w)=>{w.WriteArray(action);w.WriteArray(keys);w.WriteUInt32(SetPresent|NoAutoloading);},m=>m.GetBodyReader().ReadArrayOfInt32());
        return accepted.Length>0&&accepted[0]!=0?KeyCombo.FromQt(accepted[0]):null;
    }
    /// Who already uses these keys (empty when free), e.g. "KWin: Overview".
    public async Task<string?> OwnerAsync(string combo)
    {
        if(KeyCombo.ToQt(combo) is not {} code)return null;
        var owner=await Call("action","i",(ref MessageWriter w)=>w.WriteInt32(code),m=>m.GetBodyReader().ReadArrayOfString());
        return owner.Length>=4&&owner[0]!=component?owner[2]+": "+owner[3]:null;
    }
    /// Keeps the bindings stored in KDE but marks them inactive while Nashi is closed.
    public async Task DeactivateAsync()
    {
        foreach(var id in registered)await Call("setInactive","as",(ref MessageWriter w)=>w.WriteArray(ActionId(id,id)));
    }
    public async Task UnregisterAsync(string id){await Call("unRegister","as",(ref MessageWriter w)=>w.WriteArray(ActionId(id,id)));registered.Remove(id);}
    /// Removes the whole component from KDE once it has no actions (used by the self-check and when shortcuts are turned off).
    public async Task RemoveComponentAsync()
    {
        foreach(var id in registered.ToList())await UnregisterAsync(id);
        var path=await Call("getComponent","s",(ref MessageWriter w)=>w.WriteString(component),m=>m.GetBodyReader().ReadObjectPathAsString());
        await Call("cleanUp","",(ref MessageWriter w)=>{},destination:path,iface:"org.kde.kglobalaccel.Component");
    }
    /// Presses a shortcut the way the keyboard would (used by the self-check).
    public async Task InvokeAsync(string id)
    {
        // kglobalaccel names the object itself (characters invalid in object paths become "_").
        var path=await Call("getComponent","s",(ref MessageWriter w)=>w.WriteString(component),m=>m.GetBodyReader().ReadObjectPathAsString());
        await Call("invokeShortcut","s",(ref MessageWriter w)=>w.WriteString(id),destination:path,iface:"org.kde.kglobalaccel.Component");
    }
    // MessageWriter is a struct: the body must write through a ref, or it writes into a copy.
    private delegate void Body(ref MessageWriter writer);
    private Task Call(string member,string signature,Body body,string? destination=null,string? iface=null)=>Call<bool>(member,signature,body,_=>true,destination,iface);
    private Task<T> Call<T>(string member,string signature,Body body,Func<Message,T> read,string? destination=null,string? iface=null)
    {
        var writer=connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service,destination??Root,iface??Interface,member,signature,MessageFlags.None);body(ref writer);
        var message=writer.CreateMessage();writer.Dispose();
        return connection.CallMethodAsync(message,(Message m,object? _)=>read(m),null);
    }
    public void Dispose()=>connection.Dispose();
}

/// Key combinations as text ("Meta+Alt+C") and as Qt key codes, which kglobalaccel stores.
public static class KeyCombo
{
    private const int Shift=0x02000000,Ctrl=0x04000000,Alt=0x08000000,Meta=0x10000000;
    private static readonly (string Name,int Code)[] Named=
    [
        ("Space",0x20),("Return",0x01000004),("Tab",0x01000001),("Escape",0x01000000),("Backspace",0x01000003),("Delete",0x01000007),("Insert",0x01000006),
        ("Home",0x01000010),("End",0x01000011),("Left",0x01000012),("Up",0x01000013),("Right",0x01000014),("Down",0x01000015),("PgUp",0x01000016),("PgDown",0x01000017),
        (".",0x2e),(",",0x2c),("/",0x2f),(";",0x3b),("'",0x27),("[",0x5b),("]",0x5d),("\\",0x5c),("-",0x2d),("=",0x3d),("`",0x60)
    ];
    public static int? ToQt(string combo)
    {
        int modifiers=0,key=0;
        foreach(var raw in combo.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
        {
            switch(raw.ToLowerInvariant())
            {
                case "meta" or "super" or "win":modifiers|=Meta;continue;
                case "ctrl" or "control":modifiers|=Ctrl;continue;
                case "alt":modifiers|=Alt;continue;
                case "shift":modifiers|=Shift;continue;
            }
            if(key!=0)return null;
            if(raw.Length==1&&char.IsAsciiLetterOrDigit(raw[0]))key=char.ToUpperInvariant(raw[0]);
            else if(raw.Length is 2 or 3&&(raw[0] is 'F' or 'f')&&int.TryParse(raw[1..],out var f)&&f is >=1 and <=24)key=0x01000030+f-1;
            else if(Named.FirstOrDefault(n=>n.Name.Equals(raw,StringComparison.OrdinalIgnoreCase)) is {Code:>0} named)key=named.Code;
            else return null;
        }
        return key==0?null:modifiers|key;
    }
    public static string FromQt(int code)
    {
        var parts=new List<string>();if((code&Meta)!=0)parts.Add("Meta");if((code&Ctrl)!=0)parts.Add("Ctrl");if((code&Alt)!=0)parts.Add("Alt");if((code&Shift)!=0)parts.Add("Shift");
        var key=code&0x01FFFFFF;
        parts.Add(key is >=0x30 and <=0x39 or >=0x41 and <=0x5A?((char)key).ToString():key is >=0x01000030 and <=0x01000047?"F"+(key-0x01000030+1):Named.FirstOrDefault(n=>n.Code==key).Name??$"0x{key:X}");
        return string.Join("+",parts);
    }
    /// From a key press in Nashi's shortcut recorder.
    public static string? FromAvalonia(Avalonia.Input.Key key,Avalonia.Input.KeyModifiers modifiers)
    {
        string? name=key switch
        {
            >=Avalonia.Input.Key.A and <=Avalonia.Input.Key.Z=>key.ToString(),
            >=Avalonia.Input.Key.D0 and <=Avalonia.Input.Key.D9=>((int)(key-Avalonia.Input.Key.D0)).ToString(),
            >=Avalonia.Input.Key.F1 and <=Avalonia.Input.Key.F24=>"F"+(key-Avalonia.Input.Key.F1+1),
            Avalonia.Input.Key.Space=>"Space",Avalonia.Input.Key.Return=>"Return",Avalonia.Input.Key.Tab=>"Tab",Avalonia.Input.Key.Back=>"Backspace",Avalonia.Input.Key.Delete=>"Delete",Avalonia.Input.Key.Insert=>"Insert",
            Avalonia.Input.Key.Home=>"Home",Avalonia.Input.Key.End=>"End",Avalonia.Input.Key.Left=>"Left",Avalonia.Input.Key.Up=>"Up",Avalonia.Input.Key.Right=>"Right",Avalonia.Input.Key.Down=>"Down",
            Avalonia.Input.Key.PageUp=>"PgUp",Avalonia.Input.Key.PageDown=>"PgDown",Avalonia.Input.Key.OemPeriod=>".",Avalonia.Input.Key.OemComma=>",",Avalonia.Input.Key.OemMinus=>"-",Avalonia.Input.Key.OemPlus=>"=",
            _=>null
        };
        if(name is null)return null;
        var parts=new List<string>();if(modifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta))parts.Add("Meta");if(modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))parts.Add("Ctrl");if(modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))parts.Add("Alt");if(modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))parts.Add("Shift");
        // A global shortcut needs at least one modifier, or it would take that key from every app.
        if(parts.Count==0)return null;parts.Add(name);return string.Join("+",parts);
    }
}
