using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
namespace Nashi.Linux;

/// Options on the right, a small live preview on the left. Previews use downscaled
/// copies with every pixel length scaled, so a 2048 px collage renders in a moment.
internal abstract class PreviewDialog:Window
{
    protected const uint PreviewEdge=720;
    protected readonly StackPanel Options=new(){Spacing=8,Margin=new(0,0,12,0)};
    private readonly Image preview=new(){Stretch=Avalonia.Media.Stretch.Uniform};
    private readonly TextBlock state=new(){FontSize=12,Opacity=.75,VerticalAlignment=VerticalAlignment.Center,TextWrapping=Avalonia.Media.TextWrapping.Wrap,MaxWidth=420};
    private readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private int generation;private bool rendering;
    public bool Accepted{get;private set;}

    protected PreviewDialog(string title,string subtitle)
    {
        Title=title;Width=1000;Height=700;MinWidth=760;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var ok=new Button{Content="Save",IsDefault=true,FontWeight=Avalonia.Media.FontWeight.SemiBold,Background=NashiTheme.Primary,Foreground=new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#243B2B"))};
        ok.Click+=(_,_)=>{if(Validate() is {} problem){state.Text=problem;return;}Accepted=true;Close();};
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();
        var bar=new DockPanel{Margin=new(16,10),Children={new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,Children={cancel,ok}},state}};DockPanel.SetDock(bar.Children[0],Dock.Right);
        var side=new ScrollViewer{Content=Options,Width=340};
        var body=new DockPanel{Margin=new(16,12,16,0),Children={side,new Border{Child=preview,Margin=new(0,0,16,0)}}};DockPanel.SetDock(side,Dock.Right);
        var root=new DockPanel{Children={bar,new TextBlock{Text=subtitle,Margin=new(16,10,16,0),FontSize=12,Opacity=.75,TextTrimming=Avalonia.Media.TextTrimming.CharacterEllipsis},body}};
        DockPanel.SetDock(bar,Dock.Bottom);DockPanel.SetDock(root.Children[1],Dock.Top);
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");Content=root;
        debounce.Tick+=async(_,_)=>{debounce.Stop();await Render();};Opened+=(_,_)=>Queue();
        Closed+=(_,_)=>{debounce.Stop();generation++;(preview.Source as IDisposable)?.Dispose();};
    }
    /// Reads the controls on the UI thread and returns the drawing work for a background thread.
    protected abstract Func<MagickImage> Snapshot();
    protected virtual string? Validate(){try{using var _=Snapshot()();return null;}catch(Exception error)when(error is ArgumentException or MagickException){return error.Message;}}
    protected void Queue(){debounce.Stop();debounce.Start();}
    private async Task Render()
    {
        var current=++generation;if(rendering)return;rendering=true;state.Text="Updating…";
        try
        {
            var draw=Snapshot();var png=await Task.Run(()=>{using var image=draw();return Picture.Encode(image);});
            if(current!=generation)return;using var stream=new MemoryStream(png);var previous=preview.Source;preview.Source=new Bitmap(stream);(previous as IDisposable)?.Dispose();state.Text="";
        }
        catch(Exception error)when(error is ArgumentException or MagickException or InvalidDataException or IOException or ObjectDisposedException){if(current==generation&&IsVisible)state.Text=error.Message;}
        finally{rendering=false;if(current!=generation&&IsVisible)Queue();}
    }

    protected static MagickImage Small(string input,uint edge=PreviewEdge){var image=PhotoCodec.Read(input);if(Math.Max(image.Width,image.Height)>edge)image.Resize(new MagickGeometry(edge,edge));return image;}
    protected void Heading(string text)=>Options.Children.Add(new TextBlock{Text=text,FontWeight=Avalonia.Media.FontWeight.SemiBold,FontSize=15,Margin=new(0,10,0,0)});
    private Control Row(string label,Control control){var row=new DockPanel{Children={new TextBlock{Text=label,Width=130,VerticalAlignment=VerticalAlignment.Center},control}};DockPanel.SetDock(row.Children[0],Dock.Left);Options.Children.Add(row);return control;}
    protected NumericUpDown Number(string label,double value,double min,double max,double step=1,string format="0")
    {
        var box=new NumericUpDown{Value=(decimal)value,Minimum=(decimal)min,Maximum=(decimal)max,Increment=(decimal)step,FormatString=format,ClipValueToMinMax=true};box.ValueChanged+=(_,_)=>Queue();Row(label,box);return box;
    }
    protected TextBox Text(string label,string value){var box=new TextBox{Text=value};box.TextChanged+=(_,_)=>Queue();Row(label,box);return box;}
    protected ComboBox Choice(string label,string[] items,int selected=0){var box=new ComboBox{ItemsSource=items,SelectedIndex=selected,HorizontalAlignment=HorizontalAlignment.Stretch};box.SelectionChanged+=(_,_)=>Queue();Row(label,box);return box;}
    protected CheckBox Check(string label,bool value){var box=new CheckBox{Content=label,IsChecked=value};box.IsCheckedChanged+=(_,_)=>Queue();Options.Children.Add(box);return box;}
    protected static double Value(NumericUpDown box)=>(double)(box.Value??box.Minimum);
}

internal sealed class FrameDialog:PreviewDialog
{
    private static readonly string[] Backdrops=["solid","gradient","image"];
    private readonly MagickImage small;private readonly double factor;private readonly int sourceWidth,sourceHeight;
    private readonly ComboBox backdrop;private readonly TextBox color,gradientEnd;private readonly TextBlock backgroundName=new(){Opacity=.75,TextTrimming=Avalonia.Media.TextTrimming.CharacterEllipsis};
    private readonly NumericUpDown padding,corner,shadow,shadowOpacity,blur,scale,x,y,width,height;
    private readonly CheckBox custom;
    private string? background;private MagickImage? backgroundSmall;private bool closed;

    private FrameDialog(string title,string input,MagickImage small,int w,int h):base(title,$"{Path.GetFileName(input)} · {w} × {h}")
    {
        this.small=small;sourceWidth=w;sourceHeight=h;factor=small.Width/(double)w;
        Heading("Background");backdrop=Choice("Style",["Solid color","Gradient","Image"]);color=Text("Color","#FFFFFF");gradientEnd=Text("Gradient end","#FFD85C");
        var choose=new Button{Content="Choose background image…"};choose.Click+=async(_,_)=>await ChooseBackground();Options.Children.Add(choose);Options.Children.Add(backgroundName);
        blur=Number("Background blur",0,0,50);
        Heading("Image");padding=Number("Padding",80,0,4000,8);corner=Number("Corner radius",24,0,2000,2);shadow=Number("Shadow",24,0,100,2);shadowOpacity=Number("Shadow opacity",.25,0,1,.05,"0.00");
        scale=Number("Image scale",1,.1,10,.05,"0.00");x=Number("Position X",.5,0,1,.05,"0.00");y=Number("Position Y",.5,0,1,.05,"0.00");
        Heading("Canvas");custom=Check("Custom canvas size",false);width=Number("Canvas width",w+160,1,30000,10);height=Number("Canvas height",h+160,1,30000,10);
        // A render may still be drawing on a worker thread; it holds this lock while it does.
        Closed+=(_,_)=>{lock(small){closed=true;small.Dispose();backgroundSmall?.Dispose();}};
    }
    public static async Task<FrameDialog> CreateAsync(string title,string input)
    {
        var (image,w,h)=await Task.Run(()=>{using var full=PhotoCodec.Read(input);var w=(int)full.Width;var h=(int)full.Height;var image=(MagickImage)full.Clone();if(Math.Max(w,h)>PreviewEdge)image.Resize(new MagickGeometry(PreviewEdge,PreviewEdge));return (image,w,h);});
        return new(title,input,image,w,h);
    }
    private async Task ChooseBackground()
    {
        var picked=await StorageProvider.OpenFilePickerAsync(new(){Title="Choose background image",FileTypeFilter=[new("Images"){Patterns=["*.png","*.jpg","*.jpeg","*.webp","*.heic","*.avif","*.tiff","*.bmp"]}]});
        if(picked.FirstOrDefault()?.TryGetLocalPath() is not {} path)return;
        try{var loaded=await Task.Run(()=>Small(path));lock(small){backgroundSmall?.Dispose();backgroundSmall=loaded;}background=path;backgroundName.Text=Path.GetFileName(path);backdrop.SelectedIndex=2;Queue();}
        catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or MagickException){backgroundName.Text=error.Message;}
    }
    public FrameOptions Result=>new((int)Value(padding),Value(corner),color.Text??"",Backdrops[Math.Max(0,backdrop.SelectedIndex)],gradientEnd.Text??"",background,Value(blur),Value(shadow),Value(shadowOpacity),custom.IsChecked==true?(int)Value(width):null,custom.IsChecked==true?(int)Value(height):null,Value(scale),Value(x),Value(y));
    protected override string? Validate()=>Backdrops[Math.Max(0,backdrop.SelectedIndex)]=="image"&&background is null?"Choose a background image.":base.Validate();
    protected override Func<MagickImage> Snapshot()
    {
        // The preview draws the small background copy; Save reads the original file.
        var options=Result.Scaled(factor);var back=options.Backdrop=="image"?backgroundSmall:null;
        return ()=>{lock(small){ObjectDisposedException.ThrowIf(closed,this);return Compositions.Frame(small,options,back);}};
    }
}

internal sealed class CollageDialog:PreviewDialog
{
    private static readonly string[] Layouts=["grid","row","column","featured"];
    private readonly IReadOnlyList<string> inputs;private readonly MagickImage[] small;private bool closed;
    private readonly ComboBox layout;private readonly NumericUpDown columns,width,height,spacing,padding,corner;private readonly TextBox color;private readonly CheckBox fill;
    private readonly ListBox order=new(){MaxHeight=200};
    private readonly List<int> indices;

    private CollageDialog(string title,IReadOnlyList<string> inputs,MagickImage[] small):base(title,$"{inputs.Count} images · the first image is featured in the Featured layout")
    {
        this.inputs=inputs;this.small=small;indices=Enumerable.Range(0,inputs.Count).ToList();
        Heading("Layout");layout=Choice("Layout",["Grid","Row","Column","Featured"]);columns=Number("Columns",2,1,100);
        Heading("Canvas");width=Number("Width",2048,16,30000,16);height=Number("Height",2048,16,30000,16);spacing=Number("Spacing",12,0,2000);padding=Number("Padding",16,0,2000);corner=Number("Corner radius",0,0,2000);color=Text("Background","#FFFFFF");
        fill=Check("Fill cells (crop to fit)",true);
        Heading("Order");Options.Children.Add(order);
        var up=new Button{Content="Move up"};var down=new Button{Content="Move down"};up.Click+=(_,_)=>Move(-1);down.Click+=(_,_)=>Move(1);
        Options.Children.Add(new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,Children={up,down}});
        order.KeyDown+=(_,e)=>{if(e.KeyModifiers==KeyModifiers.Alt&&e.Key is Key.Up or Key.Down){Move(e.Key==Key.Up?-1:1);e.Handled=true;}};
        RefreshOrder();Closed+=(_,_)=>{lock(small){closed=true;foreach(var image in small)image.Dispose();}};
    }
    public static async Task<CollageDialog> CreateAsync(string title,IReadOnlyList<string> inputs)
    {
        var small=await Task.Run(()=>inputs.Select(i=>Small(i,640)).ToArray());return new(title,inputs,small);
    }
    private void Move(int delta)
    {
        var at=order.SelectedIndex;var to=at+delta;if(at<0||to<0||to>=indices.Count)return;(indices[at],indices[to])=(indices[to],indices[at]);RefreshOrder();order.SelectedIndex=to;Queue();
    }
    private void RefreshOrder()=>order.ItemsSource=indices.Select((index,n)=>$"{n+1}. {Path.GetFileName(inputs[index])}").ToArray();
    public CollageOptions Result=>new(Layouts[Math.Max(0,layout.SelectedIndex)],(int)Value(columns),(int)Value(width),(int)Value(height),(int)Value(spacing),(int)Value(padding),Value(corner),color.Text??"",fill.IsChecked==true,indices.ToArray());
    protected override Func<MagickImage> Snapshot()
    {
        var result=Result;var factor=Math.Min(1,PreviewEdge/(double)Math.Max(result.Width,result.Height));var options=result.Scaled(factor);
        return ()=>{lock(small){ObjectDisposedException.ThrowIf(closed,this);return Compositions.Collage(small,options);}};
    }
}
