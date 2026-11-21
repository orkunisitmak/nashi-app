using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;
public sealed record ToolChoice(string Id,string Title,string Symbol,WindowPlacement? Placement=null);
public sealed class WheelWindow:Window
{
    private readonly List<Button> buttons=[];
    private readonly List<System.Windows.Shapes.Path> sectors=[];
    private IReadOnlyList<ToolChoice> choices;
    private readonly Action<ToolChoice> select;
    private readonly Canvas surface;
    private int selected=-1;
    internal int SelectedIndex=>selected;
    internal void HoverScreen(Point point)
    {
        var local=surface.PointFromScreen(point);var delta=local-new Point(270,270);var distance=delta.Length;
        if(distance<108||distance>250){Highlight(-1);return;}
        var angle=(Math.Atan2(delta.Y,delta.X)*180/Math.PI-WheelPresentation.StartAngle(choices.Count)+360)%360;
        var index=(int)Math.Floor((angle+180d/choices.Count)%360/(360d/choices.Count));
        Highlight(index);
    }
    internal bool Navigate(Key key)
    {
        if(key==Key.Escape){Close();return true;}
        if(buttons.Count==0)return false;
        if(key is Key.Right or Key.Down){Highlight((selected+1)%buttons.Count);buttons[selected].Focus();return true;}
        if(key is Key.Left or Key.Up){Highlight((selected-1+buttons.Count)%buttons.Count);buttons[selected].Focus();return true;}
        if(key is Key.Enter or Key.Space){var choice=choices[Math.Max(0,selected)];Close();select(choice);return true;}
        return false;
    }
    private void Highlight(int index)
    {
        if(selected==index)return;selected=index;
        for(var i=0;i<sectors.Count;i++){sectors[i].Fill=i==index?Theme.Brush("Sage"):Theme.Brush("Inset");sectors[i].Stroke=i==index?Theme.Brush("Gold"):Theme.Brush("Line");sectors[i].StrokeThickness=i==index?2:1;}
        if(index>=0)Feedback.Play();
    }
    private readonly bool externalDrag;private readonly Func<string,IReadOnlyList<ToolChoice>>? modes;private string mode="tools";
    /// Ctrl, Shift and Alt switch between Tools, Convert and File actions (as configured in Settings), like the wheel that opens while dragging.
    internal bool SwitchMode(string modifier,IReadOnlyCollection<string> held)
    {
        if(modes is null)return false;var preferences=WheelPresentation.Preferences;string? best=null;var size=0;
        foreach(var (kind,combo) in new[]{("tools",preferences.ToolsModifier),("convert",preferences.ConvertModifier),("actions",preferences.ActionsModifier)})
        {var set=combo.Split('+');if(set.Contains(modifier)&&set.All(m=>m==modifier||held.Contains(m))&&set.Length>size){best=kind;size=set.Length;}}
        if(best is null||best==mode)return false;var next=modes(best);if(next.Count==0)return false;mode=best;Populate(next);return true;
    }
    internal string Mode=>mode;
    private static string? ModifierName(Key key)=>key switch{Key.LeftCtrl or Key.RightCtrl=>"Ctrl",Key.LeftShift or Key.RightShift=>"Shift",Key.LeftAlt or Key.RightAlt=>"Alt",_=>null};
    private static List<string> HeldModifiers()
    {
        var held=new List<string>();if(Keyboard.IsKeyDown(Key.LeftCtrl)||Keyboard.IsKeyDown(Key.RightCtrl))held.Add("Ctrl");if(Keyboard.IsKeyDown(Key.LeftShift)||Keyboard.IsKeyDown(Key.RightShift))held.Add("Shift");if(Keyboard.IsKeyDown(Key.LeftAlt)||Keyboard.IsKeyDown(Key.RightAlt))held.Add("Alt");return held;
    }
    public WheelWindow(Window? owner,IReadOnlyList<ToolChoice> tools,Action<ToolChoice> select,bool externalDrag=false,Func<string,IReadOnlyList<ToolChoice>>? modes=null,string initialMode="tools")
    {
        mode=initialMode;this.choices=tools;this.select=select;this.externalDrag=externalDrag;this.modes=modes;if(owner is not null)Owner=owner;Title=externalDrag?"Nashi Window Management":"Nashi tools";Width=WheelPresentation.Diameter*WheelPresentation.Preferences.WheelScale;Height=Width;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=externalDrag?WindowStartupLocation.Manual:WindowStartupLocation.CenterOwner;
        if(externalDrag){ShowActivated=false;Topmost=true;Focusable=false;}
        var canvas=new Canvas{Width=540,Height=540};surface=canvas;KeyboardNavigation.SetTabNavigation(canvas,KeyboardNavigationMode.Cycle);Content=new Viewbox{Child=canvas,Stretch=Stretch.Uniform};
        var disc=new Ellipse{Width=530,Height=530,Fill=WheelPresentation.Backdrop};Canvas.SetLeft(disc,5);Canvas.SetTop(disc,5);canvas.Children.Add(disc);
        Populate(tools);
        PreviewKeyDown+=(_,e)=>{var key=e.Key==Key.System?e.SystemKey:e.Key;if(modes is not null&&ModifierName(key) is {} modifier){var held=HeldModifiers();if(!held.Contains(modifier))held.Add(modifier);if(SwitchMode(modifier,held)){e.Handled=true;return;}}e.Handled=Navigate(e.Key);};
        Loaded+=(_,_)=>{WheelPresentation.Animate(this);if(!externalDrag&&buttons.Count>0)buttons[0].Focus();};bool closing=false;Closing+=(_,_)=>closing=true;Deactivated+=(_,_)=>{if(!externalDrag&&!closing&&IsVisible)Close();};
    }
    // Builds the segments and the centre label for one set of choices; a mode switch calls it again on the same canvas.
    private void Populate(IReadOnlyList<ToolChoice> tools)
    {
        choices=tools;selected=-1;buttons.Clear();sectors.Clear();var canvas=surface;while(canvas.Children.Count>1)canvas.Children.RemoveAt(1);
        var count=Math.Min(12,tools.Count);
        for(var i=0;i<count;i++)
        {
            var choice=tools[i];var angle=(WheelPresentation.StartAngle(count)+360d*i/count)*Math.PI/180;var half=(180d/count-1)*Math.PI/180;var a=angle-half;var b=angle+half;
            Point P(double r,double v)=>new(270+r*Math.Cos(v),270+r*Math.Sin(v));
            var geometry=new StreamGeometry();using(var g=geometry.Open()){g.BeginFigure(P(108,a),true,true);g.LineTo(P(250,a),true,false);g.ArcTo(P(250,b),new Size(250,250),0,b-a>Math.PI,SweepDirection.Clockwise,true,false);g.LineTo(P(108,b),true,false);g.ArcTo(P(108,a),new Size(108,108),0,b-a>Math.PI,SweepDirection.Counterclockwise,true,false);}geometry.Freeze();
            var path=new System.Windows.Shapes.Path{Data=geometry,Fill=Theme.Brush("Inset"),Stroke=Theme.Brush("Line"),StrokeThickness=1};canvas.Children.Add(path);sectors.Add(path);
            var (label,location)=WheelPresentation.Caption(choice,geometry,angle,half,count);
            var button=new Button{Style=(Style)Application.Current.FindResource("WheelLabelButton"),Content=label,Width=label.DesiredSize.Width,Height=label.DesiredSize.Height,Background=Brushes.Transparent,Foreground=Theme.Brush("Ink"),Padding=new(0),BorderThickness=new(0)};Canvas.SetLeft(button,location.X-button.Width/2);Canvas.SetTop(button,location.Y-button.Height/2);canvas.Children.Add(button);buttons.Add(button);var index=i;
            button.Click+=(_,_)=>{Close();select(choice);};button.MouseEnter+=(_,_)=>Highlight(index);button.GotKeyboardFocus+=(_,_)=>Highlight(index);
            path.MouseLeftButtonUp+=(_,_)=>{Close();select(choice);};path.MouseEnter+=(_,_)=>Highlight(index);
        }
        var centerLabel=new StackPanel();centerLabel.Children.Add(L10n.Label(new TextBlock{Text=externalDrag?"Arrange window":tools.All(t=>t.Id.StartsWith("convert:"))?"Convert":tools.Any(t=>t.Id=="float")?"File actions":"Tools",FontSize=externalDrag?17:20,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center}));centerLabel.Children.Add(L10n.Label(new TextBlock{Text=externalDrag?"Release to place · Esc cancels":"Esc to close",FontSize=externalDrag?9:11,TextAlignment=TextAlignment.Center,Foreground=Theme.Brush("Muted"),Margin=new(0,6,0,0)}));
        var center=new Button{Style=(Style)Application.Current.FindResource("WheelLabelButton"),Focusable=false,Content=centerLabel,Width=180,Height=180,Background=Brushes.Transparent,Foreground=Theme.Brush("Ink")};Canvas.SetLeft(center,180);Canvas.SetTop(center,180);canvas.Children.Add(center);center.Click+=(_,_)=>Close();
        if(IsLoaded&&!externalDrag&&buttons.Count>0)buttons[0].Focus();
    }
}
internal static class Feedback
{
    internal static bool Enabled{get;set;}=true;
    private static MediaPlayer? player;private static DateTime last;
    internal static void Stop(){player?.Stop();player?.Close();player=null;}
    public static void Play()
    {
        if(!Enabled||(DateTime.UtcNow-last).TotalMilliseconds<70)return;last=DateTime.UtcNow;
        try
        {
            var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi");Directory.CreateDirectory(directory);var file=Path.Combine(directory,"WheelSelection.wav");
            if(!File.Exists(file)){using var source=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WheelSelection.wav")).Stream;using var target=File.Create(file);source.CopyTo(target);}
            if(player is null){var created=new MediaPlayer{Volume=.65};player=created;created.MediaOpened+=(_,_)=>{if(Enabled&&ReferenceEquals(player,created))created.Play();};created.Open(new Uri(file));}else{player.Position=TimeSpan.Zero;player.Play();}

        }catch(IOException){}catch(InvalidOperationException){}
    }
}
