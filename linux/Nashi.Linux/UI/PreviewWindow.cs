using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux.UI;

/// Quick look at the selected files: a large preview with the essentials (size, pixels, length).
/// ← → move through the selection, Space or Escape closes, Enter opens in the default app.
public sealed class PreviewWindow:Window
{
    private readonly IReadOnlyList<string> paths;private int index;private readonly MainWindow main;
    private readonly Image picture=new(){Stretch=Stretch.Uniform};private readonly TextBlock title=Ui.Title("",20);private readonly TextBlock details=Ui.Text("",12,"muted",false);
    private readonly Border badge=new(){CornerRadius=new(18),Width=220,Height=160,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    public PreviewWindow(MainWindow main,IReadOnlyList<string> paths,int start)
    {
        this.main=main;this.paths=paths;index=Math.Clamp(start,0,paths.Count-1);
        Width=880;Height=700;MinWidth=420;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var open=Ui.Primary("Open",()=>LinuxShell.Open(paths[index]));var reveal=Ui.Button("Show in Folder",()=>LinuxShell.Reveal(paths[index]));
        var previous=Ui.Button("←",()=>Go(-1),"chip");var next=Ui.Button("→",()=>Go(1),"chip");previous.IsVisible=next.IsVisible=paths.Count>1;
        var head=new DockPanel{Margin=new(22,18,22,8)};var nav=Ui.Row(0,previous,next);DockPanel.SetDock(nav,Dock.Right);head.Children.Add(nav);head.Children.Add(Ui.Stack(2,title,details));DockPanel.SetDock(head,Dock.Top);
        var foot=Ui.Row(0,open,reveal,Ui.Button("Close",Close));foot.Margin=new(22,8,22,18);DockPanel.SetDock(foot,Dock.Bottom);
        Content=new DockPanel{Children={head,foot,new Border{Classes={"inset"},Margin=new(22,0),Padding=new(10),Child=new Panel{Children={badge,picture}}}}};
        // Tunnel first: buttons would otherwise take the arrow keys for focus moves.
        AddHandler(KeyDownEvent,(_,e)=>{switch(e.Key){case Key.Escape or Key.Space:Close();break;case Key.Left:Go(-1);break;case Key.Right:Go(1);break;case Key.Enter:LinuxShell.Open(paths[index]);break;default:return;}e.Handled=true;},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Show(index);
    }
    private void Go(int by){if(paths.Count<2)return;Show((index+by+paths.Count)%paths.Count);}
    private void Show(int at)
    {
        index=at;var path=paths[index];Title=Path.GetFileName(path)+" · Nashi";title.Text=Path.GetFileName(path);
        var (kind,color)=Thumbnails.Kind(path);badge.Background=new SolidColorBrush(color);badge.Child=new TextBlock{Text=kind,FontSize=34,FontWeight=FontWeight.Bold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        badge.IsVisible=true;picture.Source=null;
        var facts=new List<string>();try{facts.Add(Directory.Exists(path)?L10n.T("Folder"):Ui.Size(new FileInfo(path).Length));facts.Add(File.GetLastWriteTime(path).ToString("g"));}catch(IOException){}
        details.Text=string.Join("  ·  ",facts)+(paths.Count>1?$"  ·  {index+1}/{paths.Count}":"");
        _=Task.Run(async()=>
        {
            var bitmap=await Thumbnails.GetAsync(path,1400);var extra=await Facts(path);
            await Dispatcher.UIThread.InvokeAsync(()=>{if(paths[index]!=path)return;if(bitmap is not null){picture.Source=bitmap;badge.IsVisible=false;}if(extra.Length>0)details.Text=extra+"  ·  "+details.Text;});
        });
    }
    /// Pixels for images, length and resolution for audio and video.
    private async Task<string> Facts(string path)
    {
        try
        {
            if(FeatureCatalog.IsImage(path)){var info=new ImageMagick.MagickImageInfo(path);return $"{info.Width} × {info.Height}";}
            if((FeatureCatalog.IsVideo(path)||FeatureCatalog.IsAudio(path))&&main.HasMedia)
            {
                var media=await new MediaEngine(main.tools.Executable("ffmpeg"),main.tools.Executable("ffprobe")).InspectAsync(path);
                var length=TimeSpan.FromSeconds(media.Duration).ToString(media.Duration>=3600?@"h\:mm\:ss":@"m\:ss");
                return media.Video is {Width:{} w,Height:{} h}?$"{length}  ·  {w} × {h}":length;
            }
        }
        catch(Exception error)when(error is IOException or ImageMagick.MagickException or InvalidDataException or ArgumentException){}
        return "";
    }
}
