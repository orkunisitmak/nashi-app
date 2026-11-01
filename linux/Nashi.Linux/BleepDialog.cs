using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Core;
namespace Nashi.Linux;

/// Bleep Audio: drag across the waveform to select a word, play it to check, add it to the list.
/// Space plays the selection, A adds it, Delete removes the selected range.
/// Playback uses `ffplay` when the FFmpeg package ships it; everything else works without it.
internal sealed class BleepDialog:Window
{
    private static readonly (string Label,string Mode)[] Modes=[("Bleep tone","bleep"),("Silence","mute")];
    private readonly WaveCanvas wave;
    private readonly string source;
    private readonly string? player,folder;
    private readonly ComboBox mode=new(){ItemsSource=Modes.Select(m=>m.Label).ToArray(),SelectedIndex=0,MinWidth=120};
    private readonly TextBox startText=new(){Width=90},endText=new(){Width=90};
    private readonly ListBox list=new(){Height=130};
    private readonly Button play=new(){Content="Play selection"};
    private readonly TextBlock state=new(){FontSize=12,Opacity=.75,VerticalAlignment=VerticalAlignment.Center};
    private CancellationTokenSource? playing;
    private bool refreshing;
    public (MediaRange[] Ranges,string Mode)? Result{get;private set;}

    public BleepDialog(string title,string input,MediaInfo info)
    {
        source=input;Title=title;Width=900;Height=560;MinWidth=640;MinHeight=460;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        player=Operations.Executable("ffplay");
        if(Operations.Ffmpeg is not null)folder=Directory.CreateTempSubdirectory("nashi-wave-").FullName;
        wave=new(info.Duration){Height=150,Focusable=true};wave.Changed+=Changed;
        startText.LostFocus+=(_,_)=>Typed();endText.LostFocus+=(_,_)=>Typed();
        play.IsEnabled=player is not null;if(player is null)ToolTip.SetTip(play,"Install ffplay (part of FFmpeg) to listen.");
        play.Click+=async(_,_)=>await Play();
        var add=new Button{Content="Add range"};add.Click+=(_,_)=>Add();
        var remove=new Button{Content="Remove"};remove.Click+=(_,_)=>Remove();
        // Refreshing the list raises SelectionChanged; only a user's pick moves the selection.
        list.SelectionChanged+=(_,_)=>{if(!refreshing&&list.SelectedIndex>=0&&list.SelectedIndex!=wave.Selected)wave.Select(list.SelectedIndex);};
        var ok=new Button{Content="Bleep",IsDefault=true,FontWeight=Avalonia.Media.FontWeight.SemiBold,Background=NashiTheme.Primary,Foreground=new SolidColorBrush(Color.Parse("#243B2B"))};
        ok.Click+=(_,_)=>Accept();
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();
        var selection=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,Children=
        {
            Label("Selection"),startText,Label("–"),endText,play,add,state
        }};
        var side=new StackPanel{Spacing=8,Margin=new(10,0,0,0),Children={remove,Label("Replace with"),mode}};
        var ranges=new DockPanel{Children={side,list}};DockPanel.SetDock(side,Dock.Right);
        var panel=new DockPanel{Margin=new(16,12),Children=
        {
            new TextBlock{Text=$"{Path.GetFileName(input)} · {TrimDialog.Format(info.Duration)} · drag across the waveform, Space plays, A adds, Delete removes",FontSize=12,Opacity=.75,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,0,0,8)},
            new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,10,0,0),Children={cancel,ok}},
            new StackPanel{Spacing=10,Children=
            {
                new Border{Child=wave,CornerRadius=new(8),ClipToBounds=true},selection,
                ranges
            }}
        }};
        DockPanel.SetDock(panel.Children[0],Dock.Top);DockPanel.SetDock(panel.Children[1],Dock.Bottom);
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");Content=panel;
        KeyDown+=OnKey;Opened+=async(_,_)=>{wave.Focus();Changed();await Waveform();};
        Closed+=(_,_)=>{playing?.Cancel();wave.Wave?.Dispose();if(folder is not null)try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}};
    }
    private static TextBlock Label(string text)=>new(){Text=text,VerticalAlignment=VerticalAlignment.Center};

    private void Changed()
    {
        startText.Text=wave.Selection is {} s?TrimDialog.Format(s.Start):"";endText.Text=wave.Selection is {} e?TrimDialog.Format(e.End):"";
        refreshing=true;
        try{list.ItemsSource=wave.Ranges.Select((r,i)=>$"{i+1} · {TrimDialog.Format(r.Start)} – {TrimDialog.Format(r.End)}").ToArray();list.SelectedIndex=wave.Selected;}
        finally{refreshing=false;}
    }
    private void Typed()
    {
        // The fields show the selection rounded down to 0.1 s; an untouched field keeps the exact dragged value.
        if(wave.Selection is {} current&&startText.Text==TrimDialog.Format(current.Start)&&endText.Text==TrimDialog.Format(current.End)){Changed();return;}
        var exact=wave.Selection;
        var a=exact is {} e1&&startText.Text==TrimDialog.Format(e1.Start)?e1.Start:TrimDialog.Parse(startText.Text);var b=exact is {} e2&&endText.Text==TrimDialog.Format(e2.End)?e2.End:TrimDialog.Parse(endText.Text);
        if(a is {} start&&b is {} end&&end>start)wave.Selection=new(Math.Clamp(start,0,wave.Duration),Math.Clamp(end,0,wave.Duration));
        Changed();
    }
    private void Add(){Typed();if(wave.Selection is not {} s||s.End-s.Start<.02)return;wave.Ranges.Add(s);wave.Ranges.Sort((a,b)=>a.Start.CompareTo(b.Start));wave.Selection=null;wave.Select(wave.Ranges.IndexOf(s));state.Text="";}
    private void Remove(){if(wave.Selected<0)return;wave.Ranges.RemoveAt(wave.Selected);wave.Select(-1);}
    private void Accept(){if(wave.Ranges.Count==0){if(wave.Selection is null){state.Text="Add at least one range.";return;}Add();}Result=(wave.Ranges.ToArray(),Modes[Math.Max(0,mode.SelectedIndex)].Mode);Close();}
    private void OnKey(object? sender,KeyEventArgs e)
    {
        if(e.Source is TextBox)return;
        if(e.Key==Key.Space){_=Play();e.Handled=true;}
        else if(e.Key==Key.A){Add();e.Handled=true;}
        else if(e.Key is Key.Delete or Key.Back){Remove();e.Handled=true;}
    }

    /// Plays the selection (or the selected range); a second press stops it.
    private async Task Play()
    {
        if(playing is not null){playing.Cancel();return;}
        var range=wave.Selection??(wave.Selected>=0?wave.Ranges[wave.Selected]:null);if(player is null||range is null)return;
        playing=new();play.Content="Stop";
        try{await ProcessRunner.RunAsync(player,["-nodisp","-autoexit","-loglevel","error","-ss",Number(range.Start),"-t",Number(range.End-range.Start),source],playing.Token);}
        catch(OperationCanceledException){}
        catch(Exception error)when(error is IOException or InvalidOperationException){state.Text="Could not play the audio.";}
        finally{playing.Dispose();playing=null;play.Content="Play selection";}
    }
    private async Task Waveform()
    {
        if(folder is null||Operations.Ffmpeg is not {} ffmpeg)return;var file=Path.Combine(folder,"wave.png");
        try{await ProcessRunner.RunAsync(ffmpeg,["-hide_banner","-loglevel","error","-i",source,"-filter_complex","aformat=channel_layouts=mono,showwavespic=s=1600x300:colors=0xE3B859","-frames:v","1","-y",file]);}
        catch(Exception error)when(error is IOException or InvalidOperationException){return;}
        if(!IsVisible||!File.Exists(file))return;using var stream=File.OpenRead(file);wave.Wave=new Bitmap(stream);
    }
    private static string Number(double value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
}

internal sealed class WaveCanvas(double duration):Control
{
    public double Duration{get;}=duration;
    public List<MediaRange> Ranges{get;}=[];
    public int Selected{get;private set;}=-1;
    public event Action? Changed;
    private Bitmap? wave;
    private MediaRange? selection;
    private double? anchor;
    public Bitmap? Wave{get=>wave;set{wave=value;InvalidateVisual();}}
    public MediaRange? Selection{get=>selection;set{selection=value;Update();}}
    public void Select(int index){Selected=index;if(index>=0)selection=null;Update();}
    private void Update(){Changed?.Invoke();InvalidateVisual();}
    private double Time(Point p)=>Math.Clamp(p.X/Math.Max(1,Bounds.Width),0,1)*Duration;
    private double X(double t)=>t/Duration*Bounds.Width;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;Focus();
        var t=Time(e.GetPosition(this));var hit=Ranges.FindIndex(r=>t>=r.Start&&t<=r.End);
        if(hit>=0){Select(hit);return;}
        anchor=t;Selected=-1;selection=null;Update();e.Pointer.Capture(this);e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);if(anchor is not {} a)return;var t=Time(e.GetPosition(this));
        if(Math.Abs(t-a)>=.02){selection=new(Math.Min(a,t),Math.Max(a,t));Update();}
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);anchor=null;e.Pointer.Capture(null);}

    public override void Render(DrawingContext context)
    {
        var all=new Rect(Bounds.Size);context.FillRectangle(new SolidColorBrush(Color.FromRgb(28,36,31)),all);
        if(wave is not null)context.DrawImage(wave,new Rect(wave.Size),all);
        else context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120,227,184,89)),1),new(0,all.Height/2),new(all.Width,all.Height/2));
        for(var n=0;n<Ranges.Count;n++)
        {
            var r=new Rect(X(Ranges[n].Start),0,Math.Max(2,X(Ranges[n].End)-X(Ranges[n].Start)),all.Height);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(n==Selected?150:95),214,72,62)),r);
            if(n==Selected)context.DrawRectangle(new Pen(Brushes.White,1.5),r);
        }
        if(selection is {} s)
        {
            var r=new Rect(X(s.Start),0,Math.Max(2,X(s.End)-X(s.Start)),all.Height);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(80,227,184,89)),r);
            var edge=new Pen(new SolidColorBrush(Color.FromRgb(227,184,89)),2);context.DrawLine(edge,r.TopLeft,r.BottomLeft);context.DrawLine(edge,r.TopRight,r.BottomRight);
        }
    }
}
