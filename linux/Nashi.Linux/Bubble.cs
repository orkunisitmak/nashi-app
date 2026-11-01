using Avalonia;
using Avalonia.Controls;
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
    public const double Small=76,Large=470;
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
    internal IEnumerable<Button> Segments=>wheel.Children.OfType<Button>();

    /// `choices(files, ring)` lists a ring's buttons; `run(files, choice)` runs one and returns a status line.
    public Bubble(Func<IReadOnlyList<string>,string,Choice[]> choices,Func<IReadOnlyList<string>,Choice,Task<string>> run)
    {
        this.choices=choices;this.run=run;
        Title="Nashi";SystemDecorations=SystemDecorations.None;Topmost=true;ShowInTaskbar=false;CanResize=false;
        TransparencyLevelHint=[WindowTransparencyLevel.Transparent];Background=Brushes.Transparent;Width=Height=Small;
        puck=new Border{Width=Small-4,Height=Small-4,CornerRadius=new(38),Child=pear,BoxShadow=BoxShadows.Parse("0 2 10 0 #50000000"),Cursor=new Cursor(StandardCursorType.Hand)};
        puck[!Border.BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Surface");
        ToolTip.SetTip(puck,Localizer.T("Drop files here. Hold Ctrl for tools, Shift to convert, Alt for file actions."));
        Content=new Panel{Children={wheel,puck}};
        wheel.LayoutUpdated+=(_,_)=>Remeasured();
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
        Width=Height=Small;Position=home;
    }

    private bool busy;
    /// One ring: the choices on a circle (two circles when there are many), ring tabs and a short note in the middle.
    private void Show(string name)
    {
        ring=name;wheel.Children.Clear();const double c=Large/2;
        var list=choices(held,name);
        var disc=new Border{Width=Large-8,Height=Large-8,CornerRadius=new(Large/2),BoxShadow=BoxShadows.Parse("0 4 24 0 #60000000"),Opacity=.97};
        disc[!Border.BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");Canvas.SetLeft(disc,4);Canvas.SetTop(disc,4);wheel.Children.Add(disc);
        // Up to seven choices on one circle, more on two; long names wrap to two lines. Buttons are
        // added first and measured with their template, then placed.
        var two=list.Length>7;var inner=two?list.Length/2:list.Length;var placed=new List<(Button Button,double Angle,double Radius)>();
        for(var i=0;i<list.Length;i++)
        {
            var onInner=i<inner;var count=onInner?inner:list.Length-inner;var index=onInner?i:i-inner;
            var radius=two?(onInner?122:186):165d;var angle=-Math.PI/2+2*Math.PI*index/Math.Max(1,count)+(two&&!onInner?Math.PI/Math.Max(1,count):0);
            var choice=list[i];
            var button=new Button{Content=new TextBlock{Text=choice.Title,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,MaxWidth=92,FontSize=12},Padding=new(8,4),Tag=choice};
            ToolTip.SetTip(button,choice.Title);button.Click+=async(_,_)=>await Choose(choice);
            wheel.Children.Add(button);placed.Add((button,angle,radius));
        }
        // Ring tabs under the pear, like the three wheels.
        var tabs=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};
        foreach(var (id,label) in new[]{("tools","Tools"),("convert","Convert"),("actions","File")})
        {
            var tab=new ToggleButton{Content=label,IsChecked=id==ring,FontSize=11,Padding=new(6,2),IsEnabled=choices(held,id).Length>0};tab.Click+=(_,_)=>Show(id);tabs.Children.Add(tab);
        }
        tabs.Measure(Size.Infinity);Canvas.SetLeft(tabs,c-tabs.DesiredSize.Width/2);Canvas.SetTop(tabs,c+42);wheel.Children.Add(tabs);
        note.Text=list.Length==0?Localizer.T("Nothing here for these files."):held.Length==1?Path.GetFileName(held[0]):Localizer.Pick($"{held.Length} files",$"{held.Length} dosya");
        note.Measure(Size.Infinity);Canvas.SetLeft(note,c-85);Canvas.SetTop(note,c-80);wheel.Children.Add(note);
        wheel.Measure(new Size(Large,Large));
        // Nothing may cover the pear, the file name or the ring tabs.
        var middle=new[]{new Rect(c-40,c-40,80,80),new Rect(Canvas.GetLeft(tabs),Canvas.GetTop(tabs),tabs.DesiredSize.Width,tabs.DesiredSize.Height),new Rect(c-85,c-80,170,note.DesiredSize.Height)}.Select(r=>r.Inflate(4)).ToArray();
        layout=(placed,middle);Place();
    }
    private (List<(Button Button,double Angle,double Radius)> Buttons,Rect[] Middle)? layout;
    private readonly Dictionary<Button,Size> sized=[];
    /// Each button takes the first free spot near its place on the circle: inside the disc, clear of the
    /// middle and of the buttons already placed. Runs again once the real (themed) sizes are known.
    private void Place()
    {
        if(layout is not var (buttons,middle))return;const double c=Large/2;var taken=new List<Rect>();sized.Clear();
        Rect Ideal((Button Button,double Angle,double Radius) b){var size=b.Button.Bounds.Width>0?b.Button.Bounds.Size:b.Button.DesiredSize;return new(c+Math.Cos(b.Angle)*b.Radius-size.Width/2,c+Math.Sin(b.Angle)*b.Radius-size.Height/2,size.Width,size.Height);}
        for(var i=0;i<buttons.Count;i++)
        {
            var (button,direction,start)=buttons[i];
            var size=button.Bounds.Width>0?button.Bounds.Size:button.DesiredSize;sized[button]=size;
            // The ideal spots of the buttons still to come stay free if possible, so one that has to move
            // does not push a later one out of its place.
            var reserved=buttons.Skip(i+1).Select(Ideal).Where(r=>!middle.Any(m=>m.Intersects(r))).ToArray();
            Rect Box(double r,double a)=>new(c+Math.Cos(a)*r-size.Width/2,c+Math.Sin(a)*r-size.Height/2,size.Width,size.Height);
            bool Free(double r,double a,bool strict){var box=Box(r,a);return Corner(r,a,size)<=c-12&&!middle.Any(m=>m.Intersects(box))&&!taken.Any(t=>t.Inflate(4).Intersects(box))&&(!strict||!reserved.Any(t=>t.Inflate(4).Intersects(box)));}
            // Every nearby radius and angle; the free spot that moves the button least wins.
            var spot=(R:start,A:direction);var found=false;
            foreach(var strict in new[]{true,false})
            {
                if(found)break;var best=double.MaxValue;
                for(var dr=-120d;dr<=60;dr+=6)for(var da=-1.2;da<=1.2;da+=.03)
                {
                    var r=start+dr;var a=direction+da;if(r<=60)continue;
                    var cost=Math.Abs(dr)+Math.Abs(da)*start;if(cost<best&&Free(r,a,strict)){best=cost;spot=(r,a);found=true;}
                }
            }
            var place=Box(spot.R,spot.A);taken.Add(place);Canvas.SetLeft(button,place.X);Canvas.SetTop(button,place.Y);
            Trace.Log($"wheel {((Choice)button.Tag!).Id}: {place} found={found}");
        }
    }
    private void Remeasured()
    {
        if(layout is not var (buttons,_))return;
        if(buttons.Any(b=>b.Button.Bounds.Width>0&&sized.TryGetValue(b.Button,out var used)&&(Math.Abs(used.Width-b.Button.Bounds.Width)>.5||Math.Abs(used.Height-b.Button.Bounds.Height)>.5)))Place();
    }
    private static double Corner(double radius,double angle,Size size)
    {
        var x=Math.Cos(angle)*radius;var y=Math.Sin(angle)*radius;
        return new[]{(-1,-1),(1,-1),(1,1),(-1,1)}.Max(k=>Math.Sqrt(Math.Pow(x+k.Item1*size.Width/2,2)+Math.Pow(y+k.Item2*size.Height/2,2)));
    }
    private async Task Choose(Choice choice)
    {
        var files=held;busy=true;Collapse();note.Text="";
        try{var result=await run(files,choice);ToolTip.SetTip(puck,result);if(result.Length>0){ToolTip.SetIsOpen(puck,true);hide.Start();}}
        finally{busy=false;}
    }
}
