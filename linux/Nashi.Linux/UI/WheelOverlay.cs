using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
namespace Nashi.Linux.UI;

/// The Nashi wheel: tools, conversions or file actions around a ring. Tab / Shift+Tab switch
/// between the three, arrows move, Enter chooses, Escape or a click outside closes.
public sealed class WheelOverlay:Panel
{
    private static readonly (string Id,string Title)[] Modes=[("tools","Tools"),("convert","Convert"),("actions","File actions")];
    private readonly Func<string,IReadOnlyList<ToolChoice>> choices;private readonly Func<ToolChoice,Task> choose;private readonly Action close;
    private string mode;private int selected;private readonly Canvas ring=new(){Width=Diameter,Height=Diameter};private readonly TextBlock caption=new(){FontSize=15,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Width=150};
    private readonly StackPanel tabs=new(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Spacing=4};
    private List<Button> buttons=[];
    private const double Diameter=440,Item=74;
    public WheelOverlay(Func<string,IReadOnlyList<ToolChoice>> choices,string initial,Func<ToolChoice,Task> choose,Action close)
    {
        this.choices=choices;this.choose=choose;this.close=close;mode=initial;Focusable=true;
        Background=Application.Current?.FindResource("Scrim") as IBrush??new SolidColorBrush(Color.FromArgb(120,0,0,0));
        PointerPressed+=(_,e)=>{if(e.Source==this)close();};
        var disc=new Border{Width=Diameter+40,Height=Diameter+40,CornerRadius=new((Diameter+40)/2),Classes={"card"},Padding=new(20),Child=ring};
        var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Spacing=14,Children={tabs,disc,Ui.Text("Tab  Switch  ·  Arrows  Move  ·  Enter  Choose  ·  Esc  Close",11,"muted").Also(t=>{t.TextAlignment=TextAlignment.Center;t.Foreground=Brushes.White;})}};
        // Focus can only land once the buttons are laid out; keys work before that too (see HandleKey).
        Children.Add(stack);Render();AttachedToVisualTree+=(_,_)=>Dispatcher.UIThread.Post(()=>Select(selected),DispatcherPriority.Loaded);
    }
    private void Render()
    {
        tabs.Children.Clear();
        foreach(var (id,title) in Modes)
        {
            var count=choices(id).Count;if(count==0)continue;var tab=Ui.Button(title,()=>{mode=id;selected=0;Render();Select(0);},"chip"+(id==mode?" primary":""));tab.Focusable=false;tabs.Children.Add(tab);
        }
        ring.Children.Clear();buttons=[];var items=choices(mode);var radius=(Diameter-Item)/2;var center=Diameter/2;
        var size=items.Count>12?Item*.8:Item;
        for(var n=0;n<items.Count;n++)
        {
            var choice=items[n];var angle=-Math.PI/2+2*Math.PI*n/items.Count;var index=n;
            var content=new StackPanel{Spacing=0,HorizontalAlignment=HorizontalAlignment.Center,Children={new TextBlock{Text=choice.Symbol,FontSize=size*.3,HorizontalAlignment=HorizontalAlignment.Center},new TextBlock{Text=Short(L10n.T(choice.Title)),FontSize=10,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=size-8,HorizontalAlignment=HorizontalAlignment.Center}}};
            var button=new Button{Width=size,Height=size,Content=content,Classes={"wheel"}};ToolTip.SetTip(button,L10n.T(choice.Title));
            button.Click+=async(_,_)=>await choose(choice);button.GotFocus+=(_,_)=>Mark(index);button.PointerEntered+=(_,_)=>Mark(index);
            Canvas.SetLeft(button,center+radius*Math.Cos(angle)-size/2);Canvas.SetTop(button,center+radius*Math.Sin(angle)-size/2);ring.Children.Add(button);buttons.Add(button);
        }
        caption.Text=items.Count>0?L10n.T(items[Math.Min(selected,items.Count-1)].Title):"";
        var mark=new Image{Source=Ui.Asset("Mark"),Width=64,Height=64,HorizontalAlignment=HorizontalAlignment.Center};
        // The caption is reused across modes; take it out of the previous centre before adding it again.
        (caption.Parent as Panel)?.Children.Remove(caption);
        var middle=new StackPanel{Width=150,Spacing=6,Children={mark,caption}};Canvas.SetLeft(middle,center-75);Canvas.SetTop(middle,center-58);ring.Children.Add(middle);
    }
    /// Highlights an item (gold, like the focused one) and names it in the middle.
    private void Mark(int index)
    {
        if(buttons.Count==0)return;selected=Math.Clamp(index,0,buttons.Count-1);
        for(var n=0;n<buttons.Count;n++)buttons[n].Classes.Set("selected",n==selected);
        caption.Text=L10n.T(choices(mode)[selected].Title);
    }
    private void Select(int index){Mark(index);buttons.ElementAtOrDefault(selected)?.Focus();}
    private static string Short(string title)=>title.Length>22?title[..21]+"…":title;
    /// The main window forwards every key here while the wheel is open.
    internal void HandleKey(KeyEventArgs e)
    {
        var count=buttons.Count;
        switch(e.Key)
        {
            case Key.Escape:close();break;
            case Key.Tab:
                var available=Modes.Where(m=>choices(m.Id).Count>0).Select(m=>m.Id).ToList();var at=available.IndexOf(mode);
                mode=available[(at+(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?available.Count-1:1))%available.Count];selected=0;Render();Select(0);break;
            case Key.Right or Key.Down when count>0:Select((selected+1)%count);break;
            case Key.Left or Key.Up when count>0:Select((selected+count-1)%count);break;
            case Key.Enter or Key.Space when count>0:_=choose(choices(mode)[selected]);break;
            default:return;
        }
        e.Handled=true;
    }
}
