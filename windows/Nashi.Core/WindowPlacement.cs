using System.Text.Json.Serialization;
namespace Nashi.Core;

public enum WindowPlacement
{
    LeftThird, MiddleThird, RightThird, LeftHalf, RightHalf, TopHalf, BottomHalf, LeftTwoThirds, RightTwoThirds, Maximize
}

public readonly record struct WindowFrame(int X,int Y,int Width,int Height)
{
    public int Right=>X+Width;
    public int Bottom=>Y+Height;
}

public static class WindowPlacements
{
    public static string Title(this WindowPlacement placement)=>placement switch
    {
        WindowPlacement.LeftThird=>"Left third", WindowPlacement.MiddleThird=>"Middle third",
        WindowPlacement.RightThird=>"Right third", WindowPlacement.LeftHalf=>"Left half",
        WindowPlacement.RightHalf=>"Right half", WindowPlacement.TopHalf=>"Top half",
        WindowPlacement.BottomHalf=>"Bottom half", WindowPlacement.LeftTwoThirds=>"Left two-thirds",
        WindowPlacement.RightTwoThirds=>"Right two-thirds", WindowPlacement.Maximize=>"Maximize",
        _=>throw new ArgumentOutOfRangeException(nameof(placement))
    };
    public static (double Start,double End) Range(this WindowPlacement placement)=>placement switch
    {
        WindowPlacement.LeftThird=>(0,1d/3), WindowPlacement.MiddleThird=>(1d/3,2d/3),
        WindowPlacement.RightThird=>(2d/3,1), WindowPlacement.LeftHalf=>(0,.5),
        WindowPlacement.RightHalf=>(.5,1), WindowPlacement.LeftTwoThirds=>(0,2d/3),
        WindowPlacement.RightTwoThirds=>(1d/3,1), WindowPlacement.TopHalf or WindowPlacement.BottomHalf or WindowPlacement.Maximize=>(0,1),
        _=>throw new ArgumentOutOfRangeException(nameof(placement))
    };
    /// Measured from the top of the work area.
    public static (double Start,double End) VerticalRange(this WindowPlacement placement)=>placement switch
    {
        WindowPlacement.TopHalf=>(0,.5), WindowPlacement.BottomHalf=>(.5,1), _=>(0,1)
    };
    public static WindowFrame Frame(this WindowPlacement placement,WindowFrame area,int minimumWidth=0,int minimumHeight=0)
    {
        if(area.Width<=0||area.Height<=0)return default;
        var (x,width)=Span(area.X,area.Width,placement.Range(),minimumWidth);
        var (y,height)=Span(area.Y,area.Height,placement.VerticalRange(),minimumHeight);
        return new(x,y,width,height);
    }
    // Adjacent regions share one rounded boundary; a larger minimum keeps the leading/centered/trailing anchor.
    private static (int Origin,int Length) Span(int origin,int length,(double Start,double End) range,int minimum)
    {
        var start=(int)Math.Round(length*range.Start,MidpointRounding.AwayFromZero);
        var end=(int)Math.Round(length*range.End,MidpointRounding.AwayFromZero);
        var size=Math.Min(length,Math.Max(end-start,minimum));
        if(size==end-start)return(origin+start,size);
        return(range.Start==0?origin:range.End==1?origin+length-size:origin+(length-size)/2,size);
    }
    /// The same layout against the opposite edge, for layouts that hug the left or right edge.
    public static WindowPlacement? Mirrored(this WindowPlacement placement)=>placement switch
    {
        WindowPlacement.LeftThird=>WindowPlacement.RightThird, WindowPlacement.RightThird=>WindowPlacement.LeftThird,
        WindowPlacement.LeftHalf=>WindowPlacement.RightHalf, WindowPlacement.RightHalf=>WindowPlacement.LeftHalf,
        WindowPlacement.LeftTwoThirds=>WindowPlacement.RightTwoThirds, WindowPlacement.RightTwoThirds=>WindowPlacement.LeftTwoThirds,
        _=>null
    };
    /// Repeating a left/right layout on a window that already fills it continues on the neighbouring
    /// display, against its facing edge: right half → next display's left half, then its right half.
    public static (WindowPlacement Placement,int Display) Destination(this WindowPlacement placement,WindowFrame window,IReadOnlyList<WindowFrame> displays,int current)
    {
        if(current<0||current>=displays.Count||placement.Mirrored() is not {} mirrored)return(placement,current);
        var area=displays[current];
        // A minimum width or height keeps the anchor, so compare against the frame the app allowed.
        var expected=placement.Frame(area,window.Width,window.Height);
        if(Math.Abs(window.X-expected.X)>=4||Math.Abs(window.Right-expected.Right)>=4||Math.Abs(window.Y-expected.Y)>=4||Math.Abs(window.Bottom-expected.Bottom)>=4)return(placement,current);
        var rightward=placement.Range().End==1;double Middle(WindowFrame f)=>f.X+f.Width/2d;
        var ahead=Enumerable.Range(0,displays.Count).Where(i=>i!=current&&(rightward?Middle(displays[i])>Middle(area):Middle(displays[i])<Middle(area))).ToList();
        // Prefer displays beside this one over ones that are only diagonally further along.
        var beside=ahead.Where(i=>displays[i].Y<area.Bottom&&displays[i].Bottom>area.Y).ToList();
        var pool=beside.Count>0?beside:ahead;if(pool.Count==0)return(placement,current);
        return(mirrored,pool.MinBy(i=>Math.Abs(Middle(displays[i])-Middle(area))));
    }
}

/// A global key combination: RegisterHotKey modifier flags and a Windows virtual-key code.
public sealed record WindowShortcut(int Modifiers,int Key)
{
    public const int Alt=1,Control=2,Shift=4,Win=8;
    /// Shift alone would swallow ordinary typing in every other app.
    [JsonIgnore]public bool IsValid=>(Modifiers&(Alt|Control|Win))!=0&&(Modifiers&~(Alt|Control|Shift|Win))==0&&Key is >0 and <0xFF&&!IsModifierKey(Key);
    public static bool IsModifierKey(int key)=>key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>=0xA0 and <=0xA5);
    [JsonIgnore]public string Text
    {
        get
        {
            var parts=new List<string>();if((Modifiers&Control)!=0)parts.Add("Ctrl");if((Modifiers&Alt)!=0)parts.Add("Alt");if((Modifiers&Shift)!=0)parts.Add("Shift");if((Modifiers&Win)!=0)parts.Add("Win");
            parts.Add(Key switch{0x25=>"←",0x26=>"↑",0x27=>"→",0x28=>"↓",0x0D=>"Enter",0x09=>"Tab",0x20=>"Space",0x08=>"Backspace",0x2E=>"Delete",0x24=>"Home",0x23=>"End",0x21=>"PgUp",0x22=>"PgDn",
                >=0x70 and <=0x87=>"F"+(Key-0x6F),>=0x30 and <=0x39 or >=0x41 and <=0x5A=>((char)Key).ToString(),>=0x60 and <=0x69=>"Num "+(Key-0x60),_=>"0x"+Key.ToString("X2")});
            return string.Join('+',parts);
        }
    }
    /// Ctrl+Shift+D downloads a copied link and cannot be reused.
    public static WindowShortcut DownloadLink{get;}=new(Control|Shift,0x44);
}

/// Every layout has its own global shortcut. A stored null means the user cleared it.
public static class WindowShortcutPreferences
{
    public static WindowShortcut Default(this WindowPlacement placement)
    {
        const int b=WindowShortcut.Control|WindowShortcut.Alt;
        return placement switch
        {
            WindowPlacement.LeftHalf=>new(b,0x25), WindowPlacement.RightHalf=>new(b,0x27),
            WindowPlacement.TopHalf=>new(b,0x26), WindowPlacement.BottomHalf=>new(b,0x28), WindowPlacement.Maximize=>new(b,0x46),
            WindowPlacement.LeftThird=>new(b|WindowShortcut.Win,0x25), WindowPlacement.MiddleThird=>new(b|WindowShortcut.Win,0x28),
            WindowPlacement.RightThird=>new(b|WindowShortcut.Win,0x27),
            WindowPlacement.LeftTwoThirds=>new(b|WindowShortcut.Shift,0x25), WindowPlacement.RightTwoThirds=>new(b|WindowShortcut.Shift,0x27),
            _=>throw new ArgumentOutOfRangeException(nameof(placement))
        };
    }
    public static WindowShortcut? Shortcut(this Preferences preferences,WindowPlacement placement)
        =>preferences.WindowShortcuts.TryGetValue(placement.ToString(),out var stored)?stored:placement.Default();
    /// Returns the updated preferences; otherwise Conflict names the command that owns the
    /// combination, or is null when the combination lacks Ctrl, Alt or Win.
    public static (Preferences? Value,string? Conflict) WithShortcut(this Preferences preferences,WindowPlacement placement,WindowShortcut? shortcut)
    {
        if(shortcut is not null)
        {
            if(!shortcut.IsValid)return(null,null);
            if(shortcut==WindowShortcut.DownloadLink)return(null,"Download Link…");
            foreach(var other in Enum.GetValues<WindowPlacement>())if(other!=placement&&preferences.Shortcut(other)==shortcut)return(null,other.Title());
        }
        return(preferences with{WindowShortcuts=new(preferences.WindowShortcuts){[placement.ToString()]=shortcut}},null);
    }
    internal static void Validate(Dictionary<string,WindowShortcut?> stored)
    {
        if(stored is null||stored.Count>Enum.GetValues<WindowPlacement>().Length||stored.Keys.Any(k=>!Enum.TryParse<WindowPlacement>(k,out _)||int.TryParse(k,out _))||stored.Values.Any(s=>s is not null&&(!s.IsValid||s==WindowShortcut.DownloadLink)))throw new ArgumentException("Choose distinct window shortcuts.");
        var preferences=new Preferences{WindowShortcuts=stored};var active=Enum.GetValues<WindowPlacement>().Select(p=>preferences.Shortcut(p)).OfType<WindowShortcut>().ToList();
        if(active.Distinct().Count()!=active.Count)throw new ArgumentException("Choose distinct window shortcuts.");
    }
}
