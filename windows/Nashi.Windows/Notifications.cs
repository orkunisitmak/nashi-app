using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Input;
using Nashi.Core;
namespace Nashi.Windows;

/// The notification stack in the top-right corner of Nashi's window: running and queued jobs,
/// finished results and short messages. It floats over the page and never takes focus.
internal sealed class Notifications:StackPanel
{
    internal static Notifications Shared{get;}=new();
    internal IEnumerable<NotificationCard> Cards=>Children.OfType<NotificationCard>();
    private Notifications()
    {
        Width=372;HorizontalAlignment=HorizontalAlignment.Right;VerticalAlignment=VerticalAlignment.Top;Margin=new(0,14,18,0);Visibility=Visibility.Collapsed;Focusable=false;
        Panel.SetZIndex(this,100);AutomationProperties.SetName(this,"Notifications");
    }
    /// Puts the stack over a window's content (Nashi's main window).
    internal static void Host(Window window)
    {
        if(Shared.Parent is Panel old)old.Children.Remove(Shared);if(window.Content is not UIElement page)return;
        var layer=new Grid();window.Content=layer;layer.Children.Add(page);layer.Children.Add(Shared);
    }
    /// A card for one job; Cancel calls back until the job finishes.
    internal NotificationCard Job(string title,Action cancel)=>Add(new NotificationCard(title,"circle-notch",cancel));
    internal NotificationCard Message(string title,string? detail=null,string icon="bell-simple")
    {
        var card=Add(new NotificationCard(title,icon,null));if(detail is not null)card.Detail(detail);card.Finish(TimeSpan.FromSeconds(4));return card;
    }
    private NotificationCard Add(NotificationCard card)
    {
        // Newest on top; keep the stack short.
        Children.Insert(0,card);card.Closed+=()=>{Children.Remove(card);if(Children.Count==0)Visibility=Visibility.Collapsed;};
        foreach(var old in Children.OfType<NotificationCard>().Skip(5).Where(c=>c.Finished).ToArray())Children.Remove(old);
        Visibility=Visibility.Visible;card.Enter();return card;
    }
}

internal sealed class NotificationCard:Border
{
    private readonly TextBlock title=new(){FontSize=13,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap},detail=new(){FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new(0,3,0,0)};
    private readonly ProgressBar bar=new(){Minimum=0,Maximum=1,IsIndeterminate=true,Margin=new(0,10,0,2)};
    private readonly StackPanel actions=new(){Orientation=Orientation.Horizontal,Margin=new(0,10,0,0)};
    private readonly Border badge=new(){Width=34,Height=34,CornerRadius=new(17),VerticalAlignment=VerticalAlignment.Top,Margin=new(0,0,12,0)};
    private Action? cancel;private DispatcherTimer? closing;
    internal event Action? Closed;
    internal bool Finished{get;private set;}
    internal string Heading=>title.Text;
    internal NotificationCard(string heading,string icon,Action? cancel)
    {
        this.cancel=cancel;CornerRadius=new(12);BorderThickness=new(1);Padding=new(14,12,10,12);Margin=new(0,0,0,10);
        SetResourceReference(BackgroundProperty,"Surface");SetResourceReference(BorderBrushProperty,"Line");
        Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=18,ShadowDepth=3,Direction=270,Opacity=.22,Color=Color.FromRgb(0x08,0x0A,0x0D)};
        title.SetResourceReference(TextBlock.ForegroundProperty,"Ink");detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");L10n.SetText(title,heading);
        badge.SetResourceReference(Border.BackgroundProperty,"Sage");Icon(icon);
        var close=new Button{Style=(Style)Application.Current.FindResource("IconButton"),Width=24,Height=24,Content=Icons.Create("x",11),VerticalAlignment=VerticalAlignment.Top,Margin=new(8,0,0,0)};AutomationProperties.SetName(close,"Dismiss");L10n.Bind(close,ToolTipProperty,"Dismiss");
        close.Click+=(_,_)=>{if(!Finished&&this.cancel is not null)return;Dismiss();};
        var text=new StackPanel();text.Children.Add(title);text.Children.Add(detail);text.Children.Add(bar);text.Children.Add(actions);
        var row=new DockPanel();DockPanel.SetDock(badge,Dock.Left);row.Children.Add(badge);DockPanel.SetDock(close,Dock.Right);row.Children.Add(close);row.Children.Add(text);Child=row;
        if(cancel is not null)Button("Cancel",()=>{this.cancel?.Invoke();L10n.SetText(detail,"Cancelling…");actions.IsEnabled=false;});else bar.Visibility=Visibility.Collapsed;
        detail.Visibility=Visibility.Collapsed;
        // Hovering keeps a finished card around; leaving restarts its short timer.
        MouseEnter+=(_,_)=>closing?.Stop();MouseLeave+=(_,_)=>closing?.Start();
        AutomationProperties.SetName(this,heading);
    }
    private void Icon(string name){badge.Child=Icons.Create(name,16,"Gold");}
    internal void Detail(string text,bool localize=true){detail.Visibility=Visibility.Visible;if(localize)L10n.SetText(detail,text);else{BindingOperationsClear();detail.Text=text;}}
    private void BindingOperationsClear()=>System.Windows.Data.BindingOperations.ClearBinding(detail,TextBlock.TextProperty);
    internal void Title(string text)=>L10n.SetText(title,text);
    internal void Queued(int position){Icon("list-checks");bar.Visibility=Visibility.Collapsed;Detail(position<=1?"Waiting · next in line":L10n.Format("Waiting · {0} in line",position),false);}
    internal void Start(){Icon("circle-notch");bar.Visibility=Visibility.Visible;bar.IsIndeterminate=true;Detail("Starting…");}
    internal void Update(OperationProgress progress)
    {
        bar.Visibility=Visibility.Visible;bar.IsIndeterminate=progress.Fraction is null;if(progress.Fraction is {} fraction)bar.Value=Math.Clamp(fraction,0,1);
        Detail(progress.Fraction is {} value?L10n.Text(progress.Label)+"  ·  "+value.ToString("P0",System.Globalization.CultureInfo.CurrentCulture):L10n.Text(progress.Label),false);
    }
    internal void Complete(IReadOnlyList<string> outputs,Action<string>? open=null)
    {
        cancel=null;actions.Children.Clear();actions.IsEnabled=true;bar.Visibility=Visibility.Collapsed;Icon("check");
        L10n.SetText(title,outputs.Count==0?"All set. You’re ready to go.":$"Nicely done. {outputs.Count} {(outputs.Count==1?"file is":"files are")} ready.");
        if(outputs.Count>0)
        {
            Detail(string.Join(", ",outputs.Take(3).Select(Path.GetFileName))+(outputs.Count>3?" …":""),false);
            if(open is not null&&File.Exists(outputs[0]))Button("Open",()=>open(outputs[0]));
            Button("Show in Explorer",()=>{if(File.Exists(outputs[0])||Directory.Exists(outputs[0]))MainWindow.Reveal(outputs[0]);});
        }
        Finish(TimeSpan.FromSeconds(6));
    }
    internal void Fail(string message){cancel=null;actions.Children.Clear();bar.Visibility=Visibility.Collapsed;Icon("warning");L10n.SetText(title,"Couldn’t finish");Detail(message,false);Finish(TimeSpan.FromSeconds(12));}
    internal void Cancelled(){cancel=null;actions.Children.Clear();bar.Visibility=Visibility.Collapsed;Icon("x");Detail("Cancelled. Originals kept.");Finish(TimeSpan.FromSeconds(3));}
    /// For setup and update jobs that report their own outcome elsewhere.
    internal void Close()=>Dismiss();
    internal void Finish(TimeSpan after)
    {
        Finished=true;closing?.Stop();closing=new DispatcherTimer{Interval=after};closing.Tick+=(_,_)=>{closing!.Stop();Dismiss();};if(!IsMouseOver)closing.Start();
    }
    private void Button(string text,Action action){var button=MainWindow.Button(text,action);button.Padding=new(12,5,12,5);button.Margin=new(0,0,8,0);actions.Children.Add(button);}
    internal void Enter()
    {
        if(!Motion.On)return;
        Motion.Enter(this,24,0);
    }
    internal void Dismiss()
    {
        closing?.Stop();
        if(!Motion.On){Closed?.Invoke();return;}
        var fade=new DoubleAnimation(0,Motion.Time(180));fade.Completed+=(_,_)=>Closed?.Invoke();BeginAnimation(OpacityProperty,fade);
    }
}
