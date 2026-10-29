using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

/// In/Out range for Trim Video and Trim Audio. Video shows the frames at both handles,
/// extracted with FFmpeg into a private temporary folder that is removed on close.
/// I and O mark the current handle; Left/Right move it by one second.
internal sealed class TrimDialog:Window
{
    private readonly double duration;
    private readonly Slider start=new(){Minimum=0,SmallChange=1,LargeChange=5},end=new(){Minimum=0,SmallChange=1,LargeChange=5};
    private readonly TextBox startText=new(){Width=96,VerticalAlignment=VerticalAlignment.Center},endText=new(){Width=96,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock length=new();
    private readonly Image startFrame=new(){Width=240,Height=135},endFrame=new(){Width=240,Height=135};
    private readonly string source;
    private readonly string? video,frames;
    private readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private Slider active;
    private int generation;
    public MediaRange? Result{get;private set;}

    public TrimDialog(string title,string input,MediaInfo info)
    {
        source=input;duration=info.Duration;Title=title;SizeToContent=SizeToContent.WidthAndHeight;CanResize=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        if(info.Video is not null&&Operations.Executable("ffmpeg") is {} ffmpeg){video=ffmpeg;frames=Directory.CreateTempSubdirectory("nashi-trim-").FullName;}
        start.Maximum=end.Maximum=duration;end.Value=duration;active=start;
        start.ValueChanged+=(_,_)=>{if(start.Value>end.Value-.1)start.Value=Math.Max(0,end.Value-.1);Changed(start);};
        end.ValueChanged+=(_,_)=>{if(end.Value<start.Value+.1)end.Value=Math.Min(duration,start.Value+.1);Changed(end);};
        start.GotFocus+=(_,_)=>active=start;end.GotFocus+=(_,_)=>active=end;
        startText.LostFocus+=(_,_)=>Typed(startText,start);endText.LostFocus+=(_,_)=>Typed(endText,end);
        debounce.Tick+=async(_,_)=>{debounce.Stop();await Frames();};
        var ok=new Button{Content="Trim",IsDefault=true,FontWeight=FontWeight.SemiBold,Background=NashiTheme.Primary,Foreground=new SolidColorBrush(Color.Parse("#243B2B"))};
        ok.Click+=(_,_)=>{Typed(startText,start);Typed(endText,end);Result=new(start.Value,end.Value);Close();};
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();
        var panel=new StackPanel{Margin=new(20),Spacing=12,Width=520,Children=
        {
            new TextBlock{Text=$"{Path.GetFileName(input)} · {Format(duration)}",FontWeight=FontWeight.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis},
            Row("In",start,startText),Row("Out",end,endText),length,
            new TextBlock{Text="I / O mark the selected handle · ←/→ move it one second · Enter trims",FontSize=12,Opacity=.7},
            new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right,Children={cancel,ok}}
        }};
        if(video is not null)panel.Children.Insert(1,new StackPanel{Orientation=Orientation.Horizontal,Spacing=16,HorizontalAlignment=HorizontalAlignment.Center,Children={startFrame,endFrame}});
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");Content=panel;KeyDown+=OnKey;Opened+=async(_,_)=>{Changed(start);await Frames();};
        Closed+=(_,_)=>{debounce.Stop();generation++;if(frames is not null)try{Directory.Delete(frames,true);}catch(IOException){}catch(UnauthorizedAccessException){}};
    }

    private static Control Row(string label,Slider slider,TextBox text)
    {
        var row=new DockPanel{Children={new TextBlock{Text=label,Width=36,VerticalAlignment=VerticalAlignment.Center},text,slider}};
        DockPanel.SetDock(row.Children[0],Dock.Left);DockPanel.SetDock(text,Dock.Right);slider.Margin=new(8,0);return row;
    }
    private void Changed(Slider slider)
    {
        active=slider;startText.Text=Format(start.Value);endText.Text=Format(end.Value);length.Text=$"Keeps {Format(end.Value-start.Value)} of {Format(duration)}";
        if(video is not null){debounce.Stop();debounce.Start();}
    }
    private void Typed(TextBox text,Slider slider){if(Parse(text.Text) is {} seconds)slider.Value=Math.Clamp(seconds,0,duration);else Changed(slider);}
    private void OnKey(object? sender,KeyEventArgs e)
    {
        if(e.Source is TextBox)return;
        if(e.Key==Key.I){start.Value=active.Value;e.Handled=true;}
        else if(e.Key==Key.O){end.Value=active.Value;e.Handled=true;}
        else if(e.Key is Key.Left or Key.Right&&e.Source is not Slider){active.Value=Math.Clamp(active.Value+(e.Key==Key.Left?-1:1),0,duration);e.Handled=true;}
    }

    /// Only the newest request updates the previews, so fast dragging never shows a stale frame.
    private async Task Frames()
    {
        if(video is null||frames is null)return;var current=++generation;
        foreach(var (image,seconds,name) in new[]{(startFrame,start.Value,"in"),(endFrame,Math.Max(0,end.Value-.05),"out")})
        {
            var file=Path.Combine(frames,$"{name}-{current}.jpg");
            try{await ProcessRunner.RunAsync(video,["-hide_banner","-loglevel","error","-ss",seconds.ToString("0.###",CultureInfo.InvariantCulture),"-i",source,"-frames:v","1","-vf","scale=480:-2","-y",file]);}
            catch(Exception error)when(error is IOException or InvalidOperationException){continue;}
            if(current!=generation)return;
            if(File.Exists(file)){using var stream=File.OpenRead(file);var previous=image.Source as IDisposable;image.Source=new Bitmap(stream);previous?.Dispose();File.Delete(file);}
        }
    }

    public static string Format(double seconds){var t=TimeSpan.FromSeconds(Math.Max(0,seconds));return t.TotalHours>=1?t.ToString(@"h\:mm\:ss\.f",CultureInfo.InvariantCulture):t.ToString(@"m\:ss\.f",CultureInfo.InvariantCulture);}
    /// Accepts 75, 75.5, 1:15 or 1:01:15 (seconds, m:ss, h:mm:ss).
    public static double? Parse(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;var parts=text.Trim().Split(':');if(parts.Length>3)return null;double total=0;
        foreach(var part in parts){if(!double.TryParse(part,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||n<0||!double.IsFinite(n))return null;total=total*60+n;}
        return total;
    }
}
