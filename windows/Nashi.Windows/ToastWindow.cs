using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace Nashi.Windows;
// A small floating pill under the owner window: what is happening, how far along, and one action.
public sealed class ToastWindow:Window
{
    private readonly TextBlock label=new(){FontSize=13,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly TextBlock detail=new(){FontSize=11.5,Margin=new(0,2,0,0),TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly ProgressBar bar=new(){Height=4,Minimum=0,Maximum=1,IsIndeterminate=true,Margin=new(0,7,0,0)};
    private readonly Grid tile=new(){Width=40,Height=40,Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
    private readonly Button action=new(){Content="Cancel",MinHeight=28,FontSize=12};private readonly Action? cancel;private bool complete;
    public ToastWindow(Window owner,Action? cancel,string? subject=null)
    {
        Theme.Attach(this);this.cancel=cancel;L10n.Content(action);Owner=owner;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;SizeToContent=SizeToContent.Height;Width=432;
        detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");action.Style=Ui.Style("IconPlain");action.Margin=new(10,0,0,0);action.VerticalAlignment=VerticalAlignment.Center;Ui.SetIcon(action,"x",14);Ui.SetIconOnly(action,true);L10n.Bind(action,ToolTipProperty,"Cancel");Show(subject);
        var meta=new StackPanel{VerticalAlignment=VerticalAlignment.Center};meta.Children.Add(label);meta.Children.Add(detail);meta.Children.Add(bar);
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.Children.Add(tile);Grid.SetColumn(meta,1);grid.Children.Add(meta);Grid.SetColumn(action,2);grid.Children.Add(action);
        // The shadow sits on its own layer so the text keeps crisp rendering.
        var shadow=new Border{CornerRadius=new(22),Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=24,ShadowDepth=6,Direction=270,Opacity=.18}};shadow.SetResourceReference(Border.BackgroundProperty,"Menu");
        // Frosted like the design's toast: what is really behind it, blurred, under a panel tint (the shadow layer stays solid if that cannot be read).
        var card=new Border{CornerRadius=new(22),BorderThickness=new(1),Padding=new(10,10,10,10),Child=grid};card.SetResourceReference(Border.BorderBrushProperty,"Rim");
        var root=new Grid{Margin=new(16)};root.Children.Add(shadow);root.Children.Add(new Backdrop{Tint="Panel",Radius=22});root.Children.Add(card);Content=root;
        Loaded+=(_,_)=>{Left=Math.Max(8,owner.Left+(owner.ActualWidth-Width)/2);Top=Math.Max(8,owner.Top+owner.ActualHeight-ActualHeight-14);};
        action.Click+=(_,_)=>{if(!complete){this.cancel?.Invoke();action.IsEnabled=false;}};Update(new(null,"Working…"));
    }
    private void Show(string? path){tile.Children.Clear();tile.Children.Add(path is not null&&(File.Exists(path)||Directory.Exists(path))?Ui.Thumb(path,40):Ui.TileIcon("doc"));}
    public void Update(Nashi.Core.OperationProgress progress){L10n.SetText(label,progress.Label);bar.IsIndeterminate=progress.Fraction is null;if(progress.Fraction is {} fraction)bar.Value=Math.Clamp(fraction,0,1);L10n.SetText(detail,progress.Fraction is {} value?$"{value:P0}":"Working…");}
    public void Complete(IReadOnlyList<string> outputs)
    {
        complete=true;L10n.SetText(label,outputs.Count==0?"Done":outputs.Count==1?"1 file is ready":$"{outputs.Count} files are ready");detail.Text=outputs.Count>0?Path.GetFileName(outputs[0]):"";bar.Visibility=Visibility.Collapsed;
        // Done: the tile turns into a solid check.
        Show(outputs.FirstOrDefault());var check=new Border{Width=18,Height=18,CornerRadius=new(9),BorderThickness=new(2),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(0,0,-4,-4),Child=Icons.Make("check",10,"OnSolid",2.6)};check.SetResourceReference(Border.BackgroundProperty,"Solid");check.BorderBrush=new SolidColorBrush(Color.FromArgb(179,255,255,255));tile.Children.Add(check);
        action.Style=Ui.Style("Chip");Ui.SetIconOnly(action,false);action.ClearValue(Ui.IconProperty);action.Margin=new(10,0,0,0);L10n.Bind(action,ContentControl.ContentProperty,"Show in Explorer");action.IsEnabled=true;
        // Replace the cancel handler by disabling its callback after completion.
        action.Click+=(_,_)=>{if(outputs.Count>0&&(File.Exists(outputs[0])||Directory.Exists(outputs[0])))MainWindow.Reveal(outputs[0]);};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};timer.Tick+=(_,_)=>{timer.Stop();if(WheelPresentation.Preferences.Animations&&SystemParameters.ClientAreaAnimation){var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(200));fade.Completed+=(_,_)=>Close();BeginAnimation(OpacityProperty,fade);}else Close();};timer.Start();
    }
}
