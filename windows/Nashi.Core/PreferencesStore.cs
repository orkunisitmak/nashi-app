using System.Text.Json;
namespace Nashi.Core;
public sealed record OutputProfile(double Quality=.85,bool PreserveMetadata=true,int AudioBitrate=256,string SampleDepth="matchSource",int MaxVideoHeight=0,int GifFrameRate=15,int GifWidth=960,string Compression="balanced",string Layout="editable",string EpubLayout="reflowable",int Dpi=144);
public sealed record Preferences
{
    public string Language{get;init;}="en";
    public bool AutomaticUpdateChecks{get;init;}=true;
    public bool Enabled{get;init;}=true;public bool Feedback{get;init;}=true;public bool Animations{get;init;}=true;public bool KeepInTray{get;init;}=true;public bool RevealFinished{get;init;}
    public bool PillModeEnabled{get;init;}
    public string PillVisibilityMode{get;init;}="shortcut";
    public string PillHotkey{get;init;}="Ctrl+Alt+N";
    public bool PillOnboardingCompleted{get;init;}
    public string PillAccentColor{get;init;}="gold";
    public double PillScale{get;init;}=1;
    public string PillStartupBehavior{get;init;}="shortcut";
    public double WheelScale{get;init;}=1;public string WheelFinish{get;init;}="glass";public string FloatBehavior{get;init;}="createNew";
    public string ToolsModifier{get;init;}="Ctrl";public string ConvertModifier{get;init;}="Shift";public string ActionsModifier{get;init;}="Alt";
    public string OutputMode{get;init;}="beside";public string OutputFolder{get;init;}="";public string DownloadFolder{get;init;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");
    public Dictionary<string,OutputProfile> Formats{get;init;}=[];
}
public sealed class PreferencesStore
{
    private readonly string file;public Preferences Value{get;private set;}=new();public string? RecoveryNotice{get;private set;}
    public PreferencesStore(string directory)
    {
        file=Path.Combine(directory,"preferences.json");if(!File.Exists(file))return;try{if(new FileInfo(file).Length>1_000_000)throw new InvalidDataException();Value=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file))??new();Value=Value with{Language=AppLocalization.Normalize(Value.Language),PillVisibilityMode=Value.PillVisibilityMode=="automatic"?"shortcut":Value.PillVisibilityMode};Validate(Value);}catch(Exception error)when(error is JsonException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){RecoveryNotice="Settings could not be loaded. Defaults are in use.";Value=new();}
    }
    private static void Validate(Preferences p)
    {
        if(p.Language is not ("en" or "tr")||p.Formats is null||p.Formats.Count>100||p.Formats.Any(pair=>pair.Value is null)||p.WheelFinish is not ("glass" or "solid")||p.FloatBehavior is not ("createNew" or "addToExisting")||p.OutputFolder is null||p.DownloadFolder is null||p.PillVisibilityMode is not ("always" or "shortcut")||!new[]{"Ctrl+Alt+N","Ctrl+Shift+N","Ctrl+Alt+Space"}.Contains(p.PillHotkey)||p.PillAccentColor is not ("gold" or "blue" or "violet" or "green")||p.PillStartupBehavior is not ("shortcut" or "expanded")||!double.IsFinite(p.PillScale)||p.PillScale<.85||p.PillScale>1.2)throw new ArgumentException("Invalid preferences.");
        var shortcuts=new[]{p.ToolsModifier,p.ConvertModifier,p.ActionsModifier};if(shortcuts.Distinct().Count()!=3||shortcuts.Any(s=>!new[]{"Ctrl","Shift","Alt","Ctrl+Shift","Ctrl+Alt","Shift+Alt"}.Contains(s))||!double.IsFinite(p.WheelScale)||p.WheelScale<.85||p.WheelScale>1.4||p.OutputMode is not ("beside" or "ask" or "folder"))throw new ArgumentException("Choose distinct drag shortcuts and valid output settings.");
        foreach(var profile in p.Formats.Values)if(!double.IsFinite(profile.Quality)||profile.Quality<.1||profile.Quality>1||profile.AudioBitrate<8||profile.AudioBitrate>512||profile.Dpi<36||profile.Dpi>600||profile.GifFrameRate<1||profile.GifFrameRate>60||profile.GifWidth<16||profile.GifWidth>8192||profile.MaxVideoHeight<0||profile.MaxVideoHeight>8192||profile.SampleDepth is not ("matchSource" or "pcm16" or "pcm24" or "pcm32" or "float32")||profile.Layout is not ("editable" or "preserve")||profile.EpubLayout is not ("reflowable" or "fixed"))throw new ArgumentException("Choose supported export defaults.");
    }
    public void Save(Preferences value)
    {
        Validate(value);Directory.CreateDirectory(Path.GetDirectoryName(file)!);var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temporary,JsonSerializer.Serialize(value));File.Move(temporary,file,true);Value=value;}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public OutputProfile Profile(string format)=>Value.Formats.GetValueOrDefault(format,new());
    public void SetProfile(string format,OutputProfile profile){var formats=new Dictionary<string,OutputProfile>(Value.Formats){[format]=profile};Save(Value with{Formats=formats});}
}
