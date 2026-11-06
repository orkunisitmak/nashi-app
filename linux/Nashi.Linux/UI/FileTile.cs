using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageMagick;
using FontWeight = Avalonia.Media.FontWeight;
using Nashi.Core;
using Nashi.Linux.Engine;
namespace Nashi.Linux.UI;

/// Small previews for the selection and Library: images through Magick, PDFs through PDFium and
/// videos through FFmpeg when it is set up. Built off the UI thread, cached by path and change time.
public static class Thumbnails
{
    private static readonly ConcurrentDictionary<string,Task<Bitmap?>> cache=new();
    private static readonly SemaphoreSlim gate=new(2);
    public static string? Ffmpeg{get;set;}
    public static Task<Bitmap?> GetAsync(string path,int size=160)
    {
        DateTime stamp;try{stamp=File.GetLastWriteTimeUtc(path);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){return Task.FromResult<Bitmap?>(null);}
        return cache.GetOrAdd($"{path}|{stamp.Ticks}|{size}",_=>Task.Run(()=>Make(path,size)));
    }
    private static async Task<Bitmap?> Make(string path,int size)
    {
        await gate.WaitAsync();
        try
        {
            if(!File.Exists(path)||new FileInfo(path).Length>200_000_000)return null;
            byte[]? png=null;var ext=FeatureCatalog.Extension(path);
            if(FeatureCatalog.IsImage(path)||ext=="gif")
            {
                Magick.Ensure();using var image=ext=="gif"?new MagickImage(path,new MagickReadSettings{FrameCount=1}):PhotoCodec.Read(path);image.Thumbnail((uint)size,(uint)size);png=image.ToByteArray(MagickFormat.Png);
            }
            else if(ext=="pdf")png=await LinuxPdf.RenderFirstAsync(path,24);
            else if(FeatureCatalog.IsVideo(path)&&Ffmpeg is {} ffmpeg)
            {
                var frame=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"nashi-thumb-"+Guid.NewGuid().ToString("N")+".png");
                try{await ProcessRunner.RunAsync(ffmpeg,["-v","error","-ss","1","-i",path,"-frames:v","1","-vf",$"scale={size}:-2",frame]);if(File.Exists(frame))png=await File.ReadAllBytesAsync(frame);}
                catch(IOException){}finally{if(File.Exists(frame))File.Delete(frame);}
            }
            if(png is null)return null;
            if(ext=="pdf"){using var page=Magick.FromPng(png);page.Thumbnail((uint)size,(uint)size);png=page.ToByteArray(MagickFormat.Png);}
            return Ui.FromPng(png);
        }
        catch(Exception error)when(error is MagickException or IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or InvalidOperationException){return null;}
        finally{gate.Release();}
    }
    /// A colour per kind, used for the badge when there is no preview.
    public static (string Label,Color Color) Kind(string path)
    {
        var ext=FeatureCatalog.Extension(path);
        if(Directory.Exists(path))return("DIR",Color.Parse("#8A8F82"));
        if(FeatureCatalog.IsImage(path))return(ext.ToUpperInvariant(),Color.Parse("#5BA36C"));
        if(FeatureCatalog.IsVideo(path))return(ext.ToUpperInvariant(),Color.Parse("#8B6CC9"));
        if(FeatureCatalog.IsAudio(path))return(ext.ToUpperInvariant(),Color.Parse("#4A8FD1"));
        if(FeatureCatalog.IsArchive(path))return(ext.ToUpperInvariant(),Color.Parse("#9C8B6E"));
        if(ext=="pdf")return("PDF",Color.Parse("#D0564B"));
        return(ext.Length>0?ext.ToUpperInvariant():"FILE",Color.Parse("#C9962E"));
    }
}

/// One selected file: preview (or kind badge), name and size, with a remove button.
public sealed class FileTile:Border
{
    public FileTile(string path,Action? remove,double width=132,Action? open=null)
    {
        Classes.Add("tile");Width=width;Padding=new(8);Margin=new(0,0,10,10);
        var (label,color)=Thumbnails.Kind(path);
        var badge=new Border{CornerRadius=new(8),Background=new SolidColorBrush(color),Child=new TextBlock{Text=label,Foreground=Brushes.White,FontWeight=FontWeight.Bold,FontSize=label.Length>4?12:15,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        var picture=new Image{Stretch=Stretch.UniformToFill,IsVisible=false};
        var frame=new Border{Height=width*.62,CornerRadius=new(8),ClipToBounds=true,Classes={"tile-frame"},Child=new Panel{Children={badge,picture}}};
        var name=new TextBlock{Text=System.IO.Path.GetFileName(path),FontSize=12,TextWrapping=TextWrapping.Wrap,MaxLines=2,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,6,0,0)};
        string size;try{size=Directory.Exists(path)?L10n.T("Folder"):Ui.Size(new FileInfo(path).Length);}catch(IOException){size="";}
        var meta=Ui.Text(size,10,"muted",false);
        var stack=new StackPanel{Children={frame,name,meta}};
        var grid=new Grid{Children={stack}};
        if(remove is not null)
        {
            var close=new Button{Content="✕",Classes={"tile-remove"},HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,-2,-2,0),IsVisible=false};
            ToolTip.SetTip(close,L10n.T("Remove from Nashi (the file stays where it is)"));close.Click+=(_,_)=>remove();grid.Children.Add(close);
            PointerEntered+=(_,_)=>close.IsVisible=true;PointerExited+=(_,_)=>close.IsVisible=false;
        }
        Child=grid;ToolTip.SetTip(this,path);
        // A click opens the quick look; the ✕ handles its own click.
        if(open is not null){Cursor=new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);PointerReleased+=(_,e)=>{if(e.InitialPressMouseButton==Avalonia.Input.MouseButton.Left&&e.Source is not Button&&(e.Source as Avalonia.Visual)?.FindAncestorOfType<Button>() is null)open();};}
        _=Load(path,picture,badge);
    }
    private static async Task Load(string path,Image picture,Border badge)
    {
        if(await Thumbnails.GetAsync(path) is not {} bitmap)return;
        await Dispatcher.UIThread.InvokeAsync(()=>{picture.Source=bitmap;picture.IsVisible=true;badge.IsVisible=false;});
    }
}

/// A dashed outline that fills its parent (Avalonia's Border has no dashed stroke).
public sealed class DashedOutline:Panel
{
    public DashedOutline(Control child,double radius=18)
    {
        var outline=new Rectangle{RadiusX=radius,RadiusY=radius,StrokeThickness=2,StrokeDashArray=[6,5],Classes={"dashed"}};
        Children.Add(outline);Children.Add(child);
    }
}
