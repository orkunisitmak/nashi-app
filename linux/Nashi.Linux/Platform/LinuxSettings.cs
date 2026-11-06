using System.Text.Json;
namespace Nashi.Linux;

/// Linux-only settings, kept in their own file (~/.local/share/Nashi/linux.json) so the
/// preferences shared with Windows and macOS keep exactly their own fields.
public sealed record LinuxSettings
{
    public static readonly (string Id,string Title,string Default)[] Actions=
    [
        ("gesture","Gesture wheel: press while dragging, then move left, right, up or down","Meta+Space"),
        ("convert","Convert wheel","Meta+Alt+C"),
        ("tools","Tools wheel","Meta+Alt+T"),
        ("actions","File actions wheel","Meta+Alt+A"),
        ("clipboard","Wheel for copied files","Meta+Alt+V"),
        ("open","Show Nashi","Meta+Alt+N"),
        ("download","Download a copied link","Meta+Alt+D"),
    ];
    public static readonly string[] Directions=["left","right","up","down"];
    public static readonly string[] Wheels=["actions","convert","tools","none"];
    public static readonly string[] Themes=["classic","glass","frosted","brutalist"];

    public bool GlobalShortcuts{get;init;}=true;
    public Dictionary<string,string> Shortcuts{get;init;}=Actions.ToDictionary(a=>a.Id,a=>a.Default);
    /// Which wheel each gesture direction opens.
    public Dictionary<string,string> Gestures{get;init;}=new(){["left"]="actions",["right"]="convert",["up"]="tools",["down"]="none"};
    public string Theme{get;init;}="classic";
    public bool RunInBackground{get;init;}=true;
    /// Conversions and simple tools chosen on the gesture layer run at once with their saved defaults.
    public bool GestureQuickRun{get;init;}=true;
    public bool StartAtLogin{get;init;}

    private static string File=>Path.Combine(AppPaths.Data,"linux.json");
    public static LinuxSettings Load()
    {
        try
        {
            if(!System.IO.File.Exists(File)||new FileInfo(File).Length>100_000)return new();
            var loaded=JsonSerializer.Deserialize<LinuxSettings>(System.IO.File.ReadAllText(File))??new();
            // Unknown or missing entries fall back to defaults, so a newer or older file never breaks startup.
            var shortcuts=Actions.ToDictionary(a=>a.Id,a=>loaded.Shortcuts?.GetValueOrDefault(a.Id) is {} combo&&(combo.Length==0||KeyCombo.ToQt(combo) is not null)?combo:a.Default);
            var gestures=Directions.ToDictionary(d=>d,d=>loaded.Gestures?.GetValueOrDefault(d) is {} wheel&&Wheels.Contains(wheel)?wheel:new LinuxSettings().Gestures[d]);
            return loaded with{Shortcuts=shortcuts,Gestures=gestures,Theme=Themes.Contains(loaded.Theme)?loaded.Theme:"classic"};
        }
        catch(Exception error)when(error is JsonException or IOException or UnauthorizedAccessException){return new();}
    }
    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Data);var temporary=File+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{System.IO.File.WriteAllText(temporary,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));System.IO.File.Move(temporary,File,true);}
        finally{if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);}
        Autostart(StartAtLogin);
    }
    /// ~/.config/autostart/nashi.desktop starts Nashi in the tray at login.
    private static void Autostart(bool enabled)
    {
        var folder=Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is {Length:>0} config?config:Path.Combine(AppPaths.Home,".config"),"autostart");var file=Path.Combine(folder,"nashi.desktop");
        if(!enabled){if(System.IO.File.Exists(file))System.IO.File.Delete(file);return;}
        if(Environment.ProcessPath is not {} executable)return;
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(file,$"[Desktop Entry]\nType=Application\nName=Nashi\nComment=Keeps Nashi's shortcuts and gestures ready\nExec=\"{executable}\" --background\nIcon={Path.Combine(AppContext.BaseDirectory,"packaging","nashi.png")}\nTerminal=false\nX-GNOME-Autostart-enabled=true\nX-KDE-autostart-phase=2\n");
    }
}
