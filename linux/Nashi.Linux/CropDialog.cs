using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Core;
namespace Nashi.Linux;

/// Crop selection in source pixels. Several images share one selection relative to their size.
public sealed record ImageEdit(double X,double Y,double Width,double Height);

/// Drag to select, drag inside to move, drag a corner to resize; Shift locks the aspect,
/// arrows nudge (Shift: 10 px), Enter crops, Escape cancels.
internal sealed class CropDialog:Window
{
    private static readonly (string Label,double? Ratio)[] Aspects=[("Free",null),("Original",-1),("1:1",1),("4:3",4/3d),("3:4",3/4d),("16:9",16/9d),("9:16",9/16d),("3:2",3/2d)];
    private readonly CropCanvas canvas;
    private readonly TextBlock size=new(){VerticalAlignment=VerticalAlignment.Center,MinWidth=110};
    public ImageEdit? Result{get;private set;}

    private CropDialog(string title,string input,Bitmap preview,PixelSize pixels)
    {
        Title=title;Width=820;Height=640;MinWidth=520;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        canvas=new(preview,pixels){Focusable=true};canvas.Changed+=()=>size.Text=$"{canvas.Selection.Width:0} × {canvas.Selection.Height:0} px";
        var aspect=new ComboBox{ItemsSource=Aspects.Select(a=>a.Label).ToArray(),SelectedIndex=0,MinWidth=110};
        aspect.SelectionChanged+=(_,_)=>{var ratio=Aspects[aspect.SelectedIndex].Ratio;canvas.SetAspect(ratio==-1?pixels.Width/(double)pixels.Height:ratio);canvas.Focus();};
        var reset=new Button{Content="Reset"};reset.Click+=(_,_)=>{aspect.SelectedIndex=0;canvas.Reset();canvas.Focus();};
        var ok=new Button{Content="Crop",IsDefault=true,FontWeight=Avalonia.Media.FontWeight.SemiBold,Background=NashiTheme.Primary,Foreground=new SolidColorBrush(Color.Parse("#243B2B"))};ok.Click+=(_,_)=>Accept();
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();
        var bar=new DockPanel{Margin=new(16,10),Children=
        {
            new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={cancel,ok}},
            new StackPanel{Orientation=Orientation.Horizontal,Spacing=10,Children={new TextBlock{Text="Aspect",VerticalAlignment=VerticalAlignment.Center},aspect,reset,size}}
        }};
        DockPanel.SetDock(bar.Children[0],Dock.Right);
        var root=new DockPanel{Children={bar,new TextBlock{Text=$"{Path.GetFileName(input)} · {pixels.Width} × {pixels.Height} · drag to select, Shift locks the aspect, Enter crops",Margin=new(16,10,16,0),FontSize=12,Opacity=.75,TextTrimming=TextTrimming.CharacterEllipsis},canvas}};
        DockPanel.SetDock(bar,Dock.Bottom);DockPanel.SetDock(root.Children[1],Dock.Top);canvas.Margin=new(16);
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");Content=root;
        Opened+=(_,_)=>canvas.Focus();Closed+=(_,_)=>preview.Dispose();
        KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Accept();e.Handled=true;}};
    }
    private void Accept()
    {
        var s=canvas.Selection;var p=canvas.Pixels;if(s.Width<1||s.Height<1)return;
        Result=new(s.X/p.Width,s.Y/p.Height,s.Width/p.Width,s.Height/p.Height);Close();
    }

    /// Decodes with the shared reader (orientation, color profile, HEIC/AVIF/SVG) and keeps a bounded preview.
    public static async Task<CropDialog> CreateAsync(string title,string input)
    {
        var (png,width,height)=await Task.Run(()=>
        {
            using var image=PhotoCodec.Read(input);var w=(int)image.Width;var h=(int)image.Height;
            if(Math.Max(w,h)>2048)image.Resize(new ImageMagick.MagickGeometry(2048,2048));
            return (image.ToByteArray(ImageMagick.MagickFormat.Png),w,h);
        });
        using var stream=new MemoryStream(png);return new(title,input,new Bitmap(stream),new(width,height));
    }
}

internal sealed class CropCanvas(Bitmap preview,PixelSize pixels):Control
{
    private enum Mode{None,Create,Move,Resize}
    private const double Handle=10,Minimum=4;
    public PixelSize Pixels{get;}=pixels;
    public Rect Selection{get;private set;}=new(0,0,pixels.Width,pixels.Height);
    public event Action? Changed;
    private double? aspect;
    private Mode mode;
    private Point anchor,origin;
    private Rect start;

    public void Reset(){aspect=null;Selection=new(0,0,Pixels.Width,Pixels.Height);Update();}
    public void SetAspect(double? ratio)
    {
        aspect=ratio;if(ratio is not {} r){Update();return;}
        // Largest centered rectangle of that aspect inside the current selection.
        var s=Selection;var w=Math.Min(s.Width,s.Height*r);var h=w/r;
        Selection=Clamp(new(s.X+(s.Width-w)/2,s.Y+(s.Height-h)/2,w,h));Update();
    }
    private void Update(){Changed?.Invoke();InvalidateVisual();}
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e){base.OnAttachedToVisualTree(e);Changed?.Invoke();}

    private Rect Image
    {
        get{var scale=Math.Min(Bounds.Width/Pixels.Width,Bounds.Height/Pixels.Height);var w=Pixels.Width*scale;var h=Pixels.Height*scale;return new((Bounds.Width-w)/2,(Bounds.Height-h)/2,w,h);}
    }
    private double Scale=>Image.Width/Pixels.Width;
    private Point ToPixels(Point p){var i=Image;return new(Math.Clamp((p.X-i.X)/Scale,0,Pixels.Width),Math.Clamp((p.Y-i.Y)/Scale,0,Pixels.Height));}
    private Rect ToScreen(Rect r){var i=Image;return new(i.X+r.X*Scale,i.Y+r.Y*Scale,r.Width*Scale,r.Height*Scale);}
    private Rect Clamp(Rect r)
    {
        var w=Math.Clamp(r.Width,1,Pixels.Width);var h=Math.Clamp(r.Height,1,Pixels.Height);
        return new(Math.Round(Math.Clamp(r.X,0,Pixels.Width-w)),Math.Round(Math.Clamp(r.Y,0,Pixels.Height-h)),Math.Round(w),Math.Round(h));
    }
    private Point[] Corners(Rect r)=>[r.TopLeft,r.TopRight,r.BottomRight,r.BottomLeft];

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        Focus();var p=e.GetPosition(this);var screen=ToScreen(Selection);start=Selection;origin=ToPixels(p);
        var corner=Array.FindIndex(Corners(screen),c=>Math.Abs(c.X-p.X)<=Handle&&Math.Abs(c.Y-p.Y)<=Handle);
        if(corner>=0){mode=Mode.Resize;anchor=Corners(Selection)[(corner+2)%4];}
        else if(screen.Contains(p))mode=Mode.Move;
        else{mode=Mode.Create;anchor=origin;}
        e.Pointer.Capture(this);e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);var p=e.GetPosition(this);
        if(mode==Mode.None){Cursor=new(Corners(ToScreen(Selection)).Any(c=>Math.Abs(c.X-p.X)<=Handle&&Math.Abs(c.Y-p.Y)<=Handle)?StandardCursorType.SizeAll:ToScreen(Selection).Contains(p)?StandardCursorType.Hand:StandardCursorType.Cross);return;}
        var point=ToPixels(p);
        if(mode==Mode.Move){Selection=Clamp(start.Translate(new Vector(point.X-origin.X,point.Y-origin.Y)));Update();return;}
        var ratio=aspect??((e.KeyModifiers&KeyModifiers.Shift)!=0?(mode==Mode.Resize?start.Width/start.Height:1):null);
        var dx=point.X-anchor.X;var dy=point.Y-anchor.Y;
        if(ratio is {} r)
        {
            // Follow the larger movement and keep the selection inside the image.
            var w=Math.Max(Math.Abs(dx),Math.Abs(dy)*r);var maxW=dx>=0?Pixels.Width-anchor.X:anchor.X;var maxH=dy>=0?Pixels.Height-anchor.Y:anchor.Y;
            w=Math.Min(w,Math.Min(maxW,maxH*r));dx=Math.Sign(dx==0?1:dx)*w;dy=Math.Sign(dy==0?1:dy)*w/r;
        }
        if(Math.Abs(dx)<Minimum||Math.Abs(dy)<Minimum)return;
        Selection=Clamp(new Rect(anchor,new Point(anchor.X+dx,anchor.Y+dy)).Normalize());Update();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);mode=Mode.None;e.Pointer.Capture(null);}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);var step=(e.KeyModifiers&KeyModifiers.Shift)!=0?10:1;
        var move=e.Key switch{Key.Left=>new Vector(-step,0),Key.Right=>new Vector(step,0),Key.Up=>new Vector(0,-step),Key.Down=>new Vector(0,step),_=>(Vector?)null};
        if(move is {} v){Selection=Clamp(Selection.Translate(v));Update();e.Handled=true;}
    }

    public override void Render(DrawingContext context)
    {
        var image=Image;context.DrawImage(preview,new Rect(preview.Size),image);
        var s=ToScreen(Selection);var shade=new SolidColorBrush(Color.FromArgb(140,0,0,0));
        context.FillRectangle(shade,new(image.X,image.Y,image.Width,s.Y-image.Y));
        context.FillRectangle(shade,new(image.X,s.Bottom,image.Width,image.Bottom-s.Bottom));
        context.FillRectangle(shade,new(image.X,s.Y,s.X-image.X,s.Height));
        context.FillRectangle(shade,new(s.Right,s.Y,image.Right-s.Right,s.Height));
        var thirds=new Pen(new SolidColorBrush(Color.FromArgb(110,255,255,255)),1);
        for(var n=1;n<3;n++){context.DrawLine(thirds,new(s.X+s.Width*n/3,s.Y),new(s.X+s.Width*n/3,s.Bottom));context.DrawLine(thirds,new(s.X,s.Y+s.Height*n/3),new(s.Right,s.Y+s.Height*n/3));}
        context.DrawRectangle(new Pen(Brushes.White,1.5),s);
        var gold=new SolidColorBrush(Color.FromRgb(227,184,89));
        foreach(var c in Corners(s))context.DrawRectangle(gold,new Pen(Brushes.White,1),new Rect(c.X-5,c.Y-5,10,10));
    }
}
