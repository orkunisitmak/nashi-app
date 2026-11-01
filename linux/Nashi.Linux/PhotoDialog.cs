using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
namespace Nashi.Linux;

public sealed record PhotoEdit(PhotoAdjustments Adjustments,int Rotation=0,bool FlipHorizontal=false,bool FlipVertical=false)
{
    /// Geometry first, then the shared adjustment engine, as on Windows. EXIF survives; pixels are sRGB already.
    public void Apply(MagickImage image)
    {
        if(Rotation%360!=0)image.Rotate(Rotation);if(FlipHorizontal)image.Flop();if(FlipVertical)image.Flip();
        if(Adjustments==new PhotoAdjustments())return;
        var exif=image.GetExifProfile();image.Read(PhotoCodec.Adjust(image.ToByteArray(MagickFormat.Png),Adjustments),new MagickReadSettings{Format=MagickFormat.Png});
        if(exif is not null){exif.SetValue(ExifTag.Orientation,(ushort)1);image.SetProfile(exif);}
    }
}

/// Edit Photo: the same Light/Color/Detail controls as Windows over a bounded live preview.
/// Only the newest preview request is shown, so dragging a slider never flickers back.
internal sealed class PhotoDialog:Window
{
    private static readonly (string Group,(string Name,double Min,double Max,double Default)[] Controls)[] Groups=
    [
        ("Light",[("Exposure",-3,3,0),("Brightness",-1,1,0),("Contrast",0,2,1),("Highlights",-1,1,0),("Shadows",-1,1,0),("Whites",-1,1,0),("Blacks",-1,1,0),("Gamma",.2,3,1)]),
        // Hue stays within the engine's ±100 validation.
        ("Color",[("Temperature",-1,1,0),("Tint",-1,1,0),("Saturation",0,2,1),("Vibrance",-1,1,0),("Hue",-100,100,0)]),
        ("Detail",[("Clarity",0,1,0),("Texture",0,1,0),("Sharpen",0,2,0),("Dehaze",0,1,0),("Grain",0,1,0),("NoiseReduction",0,1,0),("Vignette",0,2,0),("Fade",0,1,0),("Blur",0,30,0)]),
    ];
    private readonly byte[] source;
    private readonly Image preview=new(){Stretch=Avalonia.Media.Stretch.Uniform};
    private readonly Dictionary<string,Slider> sliders=[];
    private readonly ToggleButton original=new(){Content="Show original"};
    private readonly CheckBox flipH=new(){Content="Flip horizontally"},flipV=new(){Content="Flip vertically"};
    private readonly TextBlock state=new(){FontSize=12,Opacity=.75};
    private readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(120)};
    // The shared adjustment engine walks every pixel (~0.4 s at 600 px, ~1.5 s at 1200 px on a laptop CPU),
    // so the live preview uses a smaller copy and runs one render at a time.
    private const uint PreviewEdge=640;
    private int rotation,generation;
    private bool rendering;
    private Bitmap? originalBitmap;
    public PhotoEdit? Result{get;private set;}

    private PhotoDialog(string title,string input,byte[] previewPng)
    {
        source=previewPng;Title=title;Width=1000;Height=700;MinWidth=760;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var controls=new StackPanel{Spacing=6,Margin=new(0,0,12,0)};
        var turn=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,Children={Button("Rotate left",()=>{rotation=(rotation+270)%360;Queue();}),Button("Rotate right",()=>{rotation=(rotation+90)%360;Queue();})}};
        controls.Children.Add(Heading("Geometry"));controls.Children.Add(turn);controls.Children.Add(flipH);controls.Children.Add(flipV);
        flipH.IsCheckedChanged+=(_,_)=>Queue();flipV.IsCheckedChanged+=(_,_)=>Queue();
        foreach(var (group,items) in Groups)
        {
            controls.Children.Add(Heading(group));
            foreach(var (name,min,max,initial) in items)
            {
                var value=new TextBlock{Width=48,TextAlignment=Avalonia.Media.TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
                var slider=new Slider{Minimum=min,Maximum=max,Value=initial,Tag=initial};sliders[name]=slider;
                slider.ValueChanged+=(_,_)=>{value.Text=slider.Value.ToString(max>10?"0":"0.00",CultureInfo.InvariantCulture);Queue();};value.Text=initial.ToString(max>10?"0":"0.00",CultureInfo.InvariantCulture);
                var label=new TextBlock{Text=Words(name),Width=110,VerticalAlignment=VerticalAlignment.Center};
                // Double-click a name to reset that control.
                label.DoubleTapped+=(_,_)=>slider.Value=(double)slider.Tag!;
                var row=new DockPanel{Children={label,value,slider}};DockPanel.SetDock(label,Dock.Left);DockPanel.SetDock(value,Dock.Right);controls.Children.Add(row);
            }
        }
        original.IsCheckedChanged+=async(_,_)=>{if(original.IsChecked==true)preview.Source=originalBitmap;else await Render();};
        var reset=Button("Reset",()=>{foreach(var s in sliders.Values)s.Value=(double)s.Tag!;rotation=0;flipH.IsChecked=flipV.IsChecked=false;Queue();});
        var ok=new Button{Content="Save",IsDefault=true,FontWeight=Avalonia.Media.FontWeight.SemiBold,Background=NashiTheme.Primary,Foreground=new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#243B2B"))};
        ok.Click+=(_,_)=>{Result=Current();Close();};
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();
        var bar=new DockPanel{Margin=new(16,10),Children={new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,Children={cancel,ok}},new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=10,Children={original,reset,state}}}};
        DockPanel.SetDock(bar.Children[0],Dock.Right);state.VerticalAlignment=VerticalAlignment.Center;
        var side=new ScrollViewer{Content=controls,Width=340};
        var body=new DockPanel{Margin=new(16,12,16,0),Children={side,new Border{Child=preview,Margin=new(0,0,16,0)}}};DockPanel.SetDock(side,Dock.Right);
        var root=new DockPanel{Children={bar,new TextBlock{Text=Path.GetFileName(input)+" · double-click a name to reset it · the full-size photo is processed on Save",Margin=new(16,10,16,0),FontSize=12,Opacity=.75},body}};
        DockPanel.SetDock(bar,Dock.Bottom);DockPanel.SetDock(root.Children[1],Dock.Top);
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");Content=root;
        debounce.Tick+=async(_,_)=>{debounce.Stop();await Render();};
        using(var stream=new MemoryStream(source))originalBitmap=new Bitmap(stream);preview.Source=originalBitmap;
        KeyDown+=(_,e)=>{if(e.Key==Key.Enter&&e.Source is not Slider){Result=Current();Close();e.Handled=true;}};
        Closed+=(_,_)=>{debounce.Stop();generation++;if(preview.Source!=originalBitmap)(preview.Source as IDisposable)?.Dispose();originalBitmap.Dispose();};
    }
    public static async Task<PhotoDialog> CreateAsync(string title,string input)
    {
        var png=await Task.Run(()=>{using var image=PhotoCodec.Read(input);if(Math.Max(image.Width,image.Height)>PreviewEdge)image.Resize(new MagickGeometry(PreviewEdge,PreviewEdge));return image.ToByteArray(MagickFormat.Png);});
        return new(title,input,png);
    }

    public PhotoEdit Current()
    {
        var values=sliders.ToDictionary(p=>p.Key,p=>Math.Round(p.Value.Value,3));
        var a=new PhotoAdjustments
        {
            Exposure=values["Exposure"],Brightness=values["Brightness"],Contrast=values["Contrast"],Highlights=values["Highlights"],Shadows=values["Shadows"],Whites=values["Whites"],Blacks=values["Blacks"],Gamma=values["Gamma"],
            Temperature=values["Temperature"],Tint=values["Tint"],Saturation=values["Saturation"],Vibrance=values["Vibrance"],Hue=values["Hue"],
            Clarity=values["Clarity"],Texture=values["Texture"],Sharpen=values["Sharpen"],Dehaze=values["Dehaze"],Grain=values["Grain"],NoiseReduction=values["NoiseReduction"],Vignette=values["Vignette"],Fade=values["Fade"],Blur=values["Blur"],
        };
        return new(a,rotation,flipH.IsChecked==true,flipV.IsChecked==true);
    }
    private void Queue(){original.IsChecked=false;debounce.Stop();debounce.Start();}
    private async Task Render()
    {
        var current=++generation;if(rendering)return;rendering=true;var edit=Current();state.Text="Updating…";
        try
        {
            var png=await Task.Run(()=>{using var image=new MagickImage(source,new MagickReadSettings{Format=MagickFormat.Png});edit.Apply(image);return Picture.Encode(image);});
            if(current!=generation||original.IsChecked==true)return;
            using var stream=new MemoryStream(png);var previous=preview.Source;preview.Source=new Bitmap(stream);if(previous!=originalBitmap)(previous as IDisposable)?.Dispose();state.Text="";
        }
        catch(Exception error)when(error is ArgumentException or MagickException){if(current==generation)state.Text=error.Message;}
        finally{rendering=false;if(current!=generation&&IsVisible)Queue();}
    }
    private static TextBlock Heading(string text)=>new(){Text=text,FontWeight=Avalonia.Media.FontWeight.SemiBold,FontSize=15,Margin=new(0,10,0,2)};
    private static Button Button(string text,Action click){var button=new Button{Content=text};button.Click+=(_,_)=>click();return button;}
    private static string Words(string name)=>name=="NoiseReduction"?"Noise reduction":name;
}
