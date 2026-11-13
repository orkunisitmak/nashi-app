using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
namespace Nashi.Linux.UI;

/// The wheel inside the Nashi window: Tools, Convert and File actions on a petal wheel.
/// Tab / Shift+Tab switch between them, arrows move, Enter chooses, Escape or a click outside closes.
public sealed class WheelOverlay:Panel
{
    private static readonly (string Id,string Title)[] Modes=[("tools","Tools"),("convert","Convert"),("actions","File actions")];
    private readonly Func<string,IReadOnlyList<ToolChoice>> choices;private readonly Action close;
    private string mode;
    private readonly StackPanel tabs=new(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Spacing=4};
    public PetalWheel Wheel{get;}=new(400);
    public WheelOverlay(Func<string,IReadOnlyList<ToolChoice>> choices,string initial,Func<ToolChoice,Task> choose,Action close)
    {
        this.choices=choices;this.close=close;mode=initial;Focusable=true;
        Background=Application.Current?.FindResource(Application.Current.ActualThemeVariant,"Scrim") as IBrush??new SolidColorBrush(Color.FromArgb(120,0,0,0));
        PointerPressed+=(_,e)=>{if(e.Source==this)close();};
        Wheel.Chosen+=async choice=>await choose(choice);
        var hint=new Border{Classes={"card"},Padding=new(14,6),HorizontalAlignment=HorizontalAlignment.Center,Child=Ui.Text("Tab  Switch  ·  Arrows  Move  ·  Enter  Choose  ·  Esc  Close",11,"muted")};
        Children.Add(new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Spacing=14,Children={tabs,Wheel,hint}});
        Render();
    }
    private string Title(string id)=>L10n.T(Modes.First(m=>m.Id==id).Title);
    private void Render()
    {
        tabs.Children.Clear();
        foreach(var (id,title) in Modes)
        {
            if(choices(id).Count==0)continue;
            var tab=Ui.Button(title,()=>{mode=id;Render();},"chip"+(id==mode?" primary":""));tab.Focusable=false;tabs.Children.Add(tab);
        }
        Wheel.Items=choices(mode);Wheel.CenterTitle=Title(mode);Wheel.Selected=0;
    }
    /// The main window forwards every key here while the wheel is open.
    internal void HandleKey(KeyEventArgs e)
    {
        var count=Wheel.Items.Count;
        switch(e.Key)
        {
            case Key.Escape:close();break;
            case Key.Tab:
                var available=Modes.Where(m=>choices(m.Id).Count>0).Select(m=>m.Id).ToList();var at=available.IndexOf(mode);
                mode=available[(at+(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?available.Count-1:1))%available.Count];Render();break;
            case Key.Right or Key.Down when count>0:Wheel.Selected=(Math.Max(0,Wheel.Selected)+1)%count;break;
            case Key.Left or Key.Up when count>0:Wheel.Selected=(Math.Max(0,Wheel.Selected)+count-1)%count;break;
            case Key.Enter or Key.Space when count>0:Wheel.Choose();break;
            default:return;
        }
        e.Handled=true;
    }
}
