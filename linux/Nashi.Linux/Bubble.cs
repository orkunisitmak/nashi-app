using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

/// The Linux stand-in for the macOS/Windows drag wheels. Wayland lets no app watch drags across the
/// desktop, so Nashi keeps a small pear on top of every window instead: drop files on it from any app
/// and the wheel opens around it. Ctrl, Shift or Alt while dropping open Tools, Convert or File
/// first, the same modifiers as the wheels. Drag the pear itself to move it.
internal sealed class Bubble:Window
{
    public const double Small=76,Large=500;
    private static readonly string[] Rings=["tools","convert","actions"];
    private readonly Image pear=new(){Width=60,Height=60,Source=new Bitmap(AssetLoader.Open(new Uri("avares://Nashi/Assets/NashiMark.png")))};
    private readonly Canvas wheel=new(){Width=Large,Height=Large,IsVisible=false};
    private readonly TextBlock note=new(){FontSize=12,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Width=170};
    private readonly Border puck;
    private readonly Func<IReadOnlyList<string>,string,Choice[]> choices;
    private readonly Func<IReadOnlyList<string>,Choice,Task<string>> run;
    private readonly DispatcherTimer hide=new(){Interval=TimeSpan.FromSeconds(5)};
    private string[] held=[];private string ring="tools";private PixelPoint home;
    internal string Ring=>ring;internal IReadOnlyList<string> Held=>held;

    /// `choices(files, ring)` lists a ring's buttons; `run(files, choice)` runs one and returns a status line.
    public Bubble(Func<IReadOnlyList<string>,string,Choice[]> choices,Func<IReadOnlyList<string>,Choice,Task<string>> run)
    {
        this.choices=choices;this.run=run;
        Title="Nashi";WindowDecorations=Avalonia.Controls.WindowDecorations.None;Topmost=true;ShowInTaskbar=false;CanResize=false;
        TransparencyLevelHint=[WindowTransparencyLevel.Transparent];Background=Brushes.Transparent;Width=Height=Small;
        puck=new Border{Width=Small-4,Height=Small-4,CornerRadius=new(38),Child=pear,BoxShadow=BoxShadows.Parse("0 2 10 0 #50000000"),Cursor=new Cursor(StandardCursorType.Hand)};
        puck[!Border.BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Surface");
        ToolTip.SetTip(puck,Localizer.T("Drop files here. Hold Ctrl for tools, Shift to convert, Alt for file actions."));
        Content=new Panel{Children={wheel,puck}};
        puck.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed&&!wheel.IsVisible)BeginMoveDrag(e);};
        puck.DoubleTapped+=(_,_)=>Collapse();
        DragDrop.SetAllowDrop(this,true);
        AddHandler(DragDrop.DragEnterEvent,(_,e)=>{Trace.Log("bubble drag enter: "+string.Join(", ",e.DataTransfer.Formats));puck.BorderThickness=new(3);puck.BorderBrush=new SolidColorBrush(Color.FromRgb(227,184,89));});
        AddHandler(DragDrop.DragLeaveEvent,(_,_)=>puck.BorderThickness=new(0));
        AddHandler(DragDrop.DragOverEvent,(_,e)=>e.DragEffects=e.DataTransfer.Contains(DataFormat.File)?DragDropEffects.Copy:DragDropEffects.None);
        AddHandler(DragDrop.DropEvent,(_,e)=>
        {
            puck.BorderThickness=new(0);
            var files=e.DataTransfer.TryGetFiles()?.Select(f=>f.TryGetLocalPath()).OfType<string>().ToArray()??[];
            Trace.Log($"bubble drop: {files.Length} files, {e.KeyModifiers}");
            if(files.Length>0)Expand(files,e.KeyModifiers);
        });
        // Tunnel, so Tab switches rings instead of moving keyboard focus.
        AddHandler(KeyDownEvent,(_,e)=>{if(e.Key==Key.Escape){Collapse();e.Handled=true;}else if(e.Key==Key.Tab&&wheel.IsVisible){Show(Rings[(Array.IndexOf(Rings,ring)+1)%Rings.Length]);e.Handled=true;}},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Deactivated+=(_,_)=>{if(wheel.IsVisible&&!busy)Collapse();};
        hide.Tick+=(_,_)=>{hide.Stop();ToolTip.SetIsOpen(puck,false);ToolTip.SetTip(puck,Localizer.T("Drop files here. Hold Ctrl for tools, Shift to convert, Alt for file actions."));};
        Opened+=(_,_)=>Dock();
    }

    /// The right edge of the screen, a little above the middle.
    private void Dock()
    {
        if((Screens.ScreenFromWindow(this)??Screens.Primary)?.WorkingArea is not {} area)return;
        var scale=Screens.ScreenFromWindow(this)?.Scaling??1;
        Position=new PixelPoint(area.Right-(int)((Small+12)*scale),area.Y+(int)(area.Height*.4));
    }

    /// Opens the wheel for these files, on the ring the modifier picks.
    public void Expand(string[] files,KeyModifiers keys=KeyModifiers.None)
    {
        held=files;home=Position;var scale=Screens.ScreenFromWindow(this)?.Scaling??1;
        Width=Height=Large;var shift=(int)((Large-Small)/2*scale);Position=new PixelPoint(home.X-shift,home.Y-shift);
        // Keep the open wheel on screen when the pear sits at an edge.
        if(Screens.ScreenFromWindow(this)?.WorkingArea is {} area)Position=new PixelPoint(Math.Clamp(Position.X,area.X,area.Right-(int)(Large*scale)),Math.Clamp(Position.Y,area.Y,area.Bottom-(int)(Large*scale)));
        wheel.IsVisible=true;Show(keys.HasFlag(KeyModifiers.Shift)?"convert":keys.HasFlag(KeyModifiers.Alt)?"actions":keys.HasFlag(KeyModifiers.Control)?"tools":Rings.FirstOrDefault(r=>choices(held,r).Length>0)??"actions");
        Activate();Focus();
    }
    public void Collapse()
    {
        if(!wheel.IsVisible)return;wheel.IsVisible=false;wheel.Children.Clear();held=[];
        Width=Height=Small;Position=home;puck.Margin=new(0);
    }

    private bool busy;
    private const double Inner=98,Outer=240;
    private readonly List<(Avalonia.Controls.Shapes.Path Petal,Choice Choice)> petals=[];
    internal IEnumerable<Control> Segments=>petals.Select(p=>(Control)p.Petal);
    internal Geometry PetalOf(Choice choice)=>petals.First(p=>p.Choice==choice).Petal.Data!;
    internal Task Pick(Choice choice)=>Choose(choice);
    /// Symbols from the Windows wheel, so the rings read the same on every platform.
    internal static string Symbol(string id)=>id switch{"compress"=>"↘","removeMetadata"=>"◇","editImage"=>"☷","annotateImage"=>"✎","frameImage"=>"▣","cropImage" or "cropVideo"=>"⌗","trimAudio" or "trimVideo"=>"✂","muteVideo"=>"♫","normalizeAudio"=>"≋","audioChannels"=>"⇆","audioToVideo"=>"▷","createCollage"=>"▦","redactImage" or "redactVideo" or "redactAudio"=>"▨","readQRCode"=>"▧","mergePDF"=>"▣","splitPDF" or "splitVideo"=>"◫","organizePDF"=>"▤","extractArchive"=>"↗","createPDF"=>"▱","joinVideos"=>"⇥","videoSnapshots"=>"▣","changeVideoSpeed"=>"◴",
        "action:open"=>"◎","action:float"=>"▣","action:copy"=>"▱","action:move"=>"↗","action:path"=>"↪","action:terminal"=>">_","convert"=>"⇄",_=>"↗"};

    /// One ring drawn like the Windows wheel: a petal per choice between two circles, a symbol and an
    /// upper-case name in each, the highlighted petal in sage with a gold edge; the ring name, the file
    /// and the ring tabs sit in the middle with the pear.
    private void Show(string name)
    {
        ring=name;wheel.Children.Clear();petals.Clear();const double c=Large/2;
        var list=choices(held,name);
        var disc=new Ellipse{Width=Large-6,Height=Large-6,Opacity=.97};disc[!Shape.FillProperty]=Resource("Surface");disc[!Shape.StrokeProperty]=Resource("Line");disc.StrokeThickness=1;
        Canvas.SetLeft(disc,3);Canvas.SetTop(disc,3);wheel.Children.Add(disc);
        var count=Math.Max(1,list.Length);var sweep=2*Math.PI/count;var first=count==3?-Math.PI:-Math.PI/2-sweep/2;
        Point P(double r,double a)=>new(c+r*Math.Cos(a),c+r*Math.Sin(a));
        for(var i=0;i<list.Length;i++)
        {
            var choice=list[i];var a=first+i*sweep+.012;var b=first+(i+1)*sweep-.012;if(count==1){a=-Math.PI/2+.001;b=a+2*Math.PI-.002;}
            var geometry=new StreamGeometry();
            using(var g=geometry.Open())
            {
                g.BeginFigure(P(Inner,a),true);g.LineTo(P(Outer,a));g.ArcTo(P(Outer,b),new Size(Outer,Outer),0,b-a>Math.PI,SweepDirection.Clockwise);
                g.LineTo(P(Inner,b));g.ArcTo(P(Inner,a),new Size(Inner,Inner),0,b-a>Math.PI,SweepDirection.CounterClockwise);g.EndFigure(true);
            }
            var petal=new Avalonia.Controls.Shapes.Path{Data=geometry,StrokeThickness=1,Cursor=new Cursor(StandardCursorType.Hand),Tag=choice};
            Paint(petal,false);ToolTip.SetTip(petal,choice.Title);
            petal.PointerEntered+=(_,_)=>Paint(petal,true);petal.PointerExited+=(_,_)=>Paint(petal,false);
            petal.PointerReleased+=async(_,e)=>{if(e.InitialPressMouseButton==MouseButton.Left)await Choose(choice);};
            wheel.Children.Add(petal);petals.Add((petal,choice));
            // The label: symbol over the name, as wide as the petal allows at its middle radius.
            var mid=(a+b)/2;var radius=(Inner+Outer)/2;var width=Math.Clamp(radius*sweep*.82,64,132);
            var label=new StackPanel{Width=width,IsHitTestVisible=false,Spacing=2,Children=
            {
                new TextBlock{Text=Symbol(choice.Id=="convert"&&choice.Format is null?"convert":choice.Id),FontSize=24,TextAlignment=TextAlignment.Center},
                new TextBlock{Text=Localizer.Language=="tr"?Localizer.T(choice.Title).ToUpper(new System.Globalization.CultureInfo("tr-TR")):choice.Title.ToUpperInvariant(),FontSize=11,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,LetterSpacing=.4}
            }};
            label.Measure(new Size(width,double.PositiveInfinity));var at=P(radius,mid);
            Canvas.SetLeft(label,at.X-width/2);Canvas.SetTop(label,at.Y-label.DesiredSize.Height/2);wheel.Children.Add(label);
        }
        // The middle: pear, ring name, the file and the ring tabs.
        var title=new TextBlock{Text=name switch{"convert"=>"Convert","actions"=>"File actions",_=>"Tools"},FontSize=17,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,Width=170};
        note.Text=list.Length==0?Localizer.T("Nothing here for these files."):held.Length==1?System.IO.Path.GetFileName(held[0]):Localizer.Pick($"{held.Length} files",$"{held.Length} dosya");
        note.FontSize=11;note.Opacity=.7;note.Width=150;note.TextTrimming=TextTrimming.CharacterEllipsis;note.TextWrapping=TextWrapping.NoWrap;
        var tabs=new StackPanel{Orientation=Orientation.Horizontal,Spacing=3};
        foreach(var (id,label) in new[]{("tools","Tools"),("convert","Convert"),("actions","File")})
        {
            var tab=new ToggleButton{Content=label,IsChecked=id==ring,FontSize=10,Padding=new(7,2),MinHeight=0,CornerRadius=new(10),IsEnabled=choices(held,id).Length>0};tab.Click+=(_,_)=>Show(id);tabs.Children.Add(tab);
        }
        (note.Parent as Panel)?.Children.Remove(note);
        var middle=new StackPanel{Spacing=3,Width=176,HorizontalAlignment=HorizontalAlignment.Center,Children={title,note,tabs}};tabs.HorizontalAlignment=HorizontalAlignment.Center;
        middle.Measure(Size.Infinity);Canvas.SetLeft(middle,c-88);Canvas.SetTop(middle,c-10);wheel.Children.Add(middle);
        puck.Margin=new(0,0,0,100);
    }
    private static Avalonia.Data.BindingBase Resource(string key)=>new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key);
    private static void Paint(Avalonia.Controls.Shapes.Path petal,bool on)
    {
        petal[!Shape.FillProperty]=Resource(on?"Sage":"Inset");
        if(on){petal.Stroke=new SolidColorBrush(Color.FromRgb(227,184,89));petal.StrokeThickness=2;}else{petal[!Shape.StrokeProperty]=Resource("Line");petal.StrokeThickness=1;}
    }
    private async Task Choose(Choice choice)
    {
        var files=held;busy=true;Collapse();note.Text="";
        try{var result=await run(files,choice);ToolTip.SetTip(puck,result);if(result.Length>0){ToolTip.SetIsOpen(puck,true);hide.Start();}}
        finally{busy=false;}
    }
}
