using Nashi.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Windows;
namespace Nashi.Linux;

/// Edits in source-pixel coordinates; the viewbox scales the entire surface, including input.
internal sealed class ImageSurface:Viewbox
{
    private readonly Canvas canvas;
    private readonly Image original;
    private readonly Stack<Control> undo=[];
    private Control? active;
    private Point origin;
    private Point last;
    private Rect previous;
    private bool moving;
    private readonly string action;
    private readonly Action<Rect> changed;
    private int markNumber=1;
    public string DrawingTool{get;set;}="Marker";
    public string Ink{get;set;}="#FF5200";
    public double StrokeWidth{get;set;}=3;
    public double FontSize{get;set;}=24;
    public string AnnotationText{get;set;}="Note";
    public string? InsertImage{get;set;}
    public MediaRegion[] Regions=>canvas.Children.OfType<Rectangle>().Where(r=>r.Width>0&&r.Height>0).Select(r=>new MediaRegion(Math.Floor(Canvas.GetLeft(r)),Math.Floor(Canvas.GetTop(r)),Math.Ceiling(Canvas.GetLeft(r)+r.Width)-Math.Floor(Canvas.GetLeft(r)),Math.Ceiling(Canvas.GetTop(r)+r.Height)-Math.Floor(Canvas.GetTop(r)))).ToArray();
    public int PixelWidth{get;} public int PixelHeight{get;}
    public ImageSurface(byte[] bytes,string action,Action<Rect> changed)
    {
        this.action=action;this.changed=changed;using var stream=new MemoryStream(bytes);var bitmap=new Bitmap(stream);PixelWidth=bitmap.PixelSize.Width;PixelHeight=bitmap.PixelSize.Height;
        original=new Image{Source=bitmap,Width=PixelWidth,Height=PixelHeight};canvas=new Canvas{Width=PixelWidth,Height=PixelHeight,Background=Brushes.Transparent,ClipToBounds=true};canvas.Children.Add(original);Child=canvas;Stretch=Stretch.Uniform;MaxHeight=460;
        canvas.PointerPressed+=Begin;canvas.PointerMoved+=Move;canvas.PointerReleased+=End;Focusable=true;
        KeyDown+=(_,e)=>{if(e.Key==Key.Z&&e.KeyModifiers.HasFlag(KeyModifiers.Control)){Undo();e.Handled=true;}if(e.Key==Key.Delete&&active is not null){canvas.Children.Remove(active);active=null;e.Handled=true;}};
    }
    public void Reset(){while(canvas.Children.Count>1)canvas.Children.RemoveAt(1);undo.Clear();active=null;markNumber=1;}
    public void Undo(){if(undo.TryPop(out var shape)){canvas.Children.Remove(shape);active=null;}}
    public byte[] Png()
    {
        using var bitmap=new RenderTargetBitmap(new PixelSize(PixelWidth,PixelHeight),new Vector(96,96));bitmap.Render(canvas);using var output=new MemoryStream();bitmap.Save(output,new PngBitmapEncoderOptions());return output.ToArray();
    }
    private Point Position(PointerEventArgs e){var p=e.GetPosition(canvas);return new(Math.Clamp(p.X,0,PixelWidth),Math.Clamp(p.Y,0,PixelHeight));}
    private void Begin(object? sender,PointerPressedEventArgs e)
    {
        if(!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed)return;
        if(action is not ("cropImage" or "redactImage" or "annotateImage"))return;
        origin=Position(e);last=origin;Focus();e.Pointer.Capture(canvas);
        if(action=="cropImage"&&active is Rectangle crop&&new Rect(Canvas.GetLeft(crop),Canvas.GetTop(crop),crop.Width,crop.Height).Contains(origin))
        {moving=true;previous=new(Canvas.GetLeft(crop),Canvas.GetTop(crop),crop.Width,crop.Height);return;}
        if(action=="annotateImage"&&DrawingTool=="Select / Move")
        {
            active=canvas.Children.Reverse().FirstOrDefault(c=>c!=original&&new Rect(Canvas.GetLeft(c),Canvas.GetTop(c),double.IsNaN(c.Width)?c.Bounds.Width:c.Width,double.IsNaN(c.Height)?c.Bounds.Height:c.Height).Contains(origin));
            if(active is not null){previous=new(Canvas.GetLeft(active),Canvas.GetTop(active),active.Bounds.Width,active.Bounds.Height);moving=true;}return;
        }
        moving=false;
        if(action=="cropImage"&&active is not null){canvas.Children.Remove(active);undo.Clear();}
        var brush=NashiTheme.Brush(Ink);
        if(action=="annotateImage")
        {
            active=DrawingTool switch
            {
                "Marker" or "Freehand"=>new Polyline{Stroke=brush,StrokeThickness=DrawingTool=="Marker"?StrokeWidth*4:StrokeWidth,Opacity=DrawingTool=="Marker"?.5:1,Points=new Points{origin}},
                "Ellipse"=>new Ellipse{Stroke=brush,StrokeThickness=StrokeWidth},
                "Text" or "Number" or "Callout"=>new TextBlock{Text=DrawingTool=="Number"?(markNumber++).ToString():AnnotationText,FontSize=FontSize,Foreground=brush,Background=DrawingTool=="Callout"?Brushes.White:Brushes.Transparent},
                "Line" or "Arrow"=>new Polyline{Stroke=brush,StrokeThickness=StrokeWidth,Points=new Points{origin,origin}},
                "Image" when InsertImage is {} path=>new Image{Source=new Bitmap(path),Stretch=Stretch.Fill},
                "Magnifier"=>Magnifier(origin),
                _=>new Rectangle{Stroke=brush,StrokeThickness=StrokeWidth}
            };
        }
        else active=new Rectangle{Stroke=NashiTheme.Gold,StrokeThickness=2,Fill=action=="redactImage"?Brushes.Black:NashiTheme.Brush("#22333333")};
        if(active is not Polyline){Canvas.SetLeft(active,origin.X);Canvas.SetTop(active,origin.Y);}
        canvas.Children.Add(active);undo.Push(active);e.Handled=true;
    }
    private Control Magnifier(Point point)
    {
        using var source=new ImageMagick.MagickImage(Png());var size=(uint)Math.Max(1,Math.Min(80,Math.Min(PixelWidth-point.X,PixelHeight-point.Y)));
        source.Crop(new ImageMagick.MagickGeometry((int)point.X,(int)point.Y,size,size));source.ResetPage();using var stream=new MemoryStream(source.ToByteArray(ImageMagick.MagickFormat.Png));return new Image{Source=new Bitmap(stream),Stretch=Stretch.Fill};
    }
    private void Move(object? sender,PointerEventArgs e)
    {
        if(e.Pointer.Captured!=canvas||active is null)return;var point=Position(e);
        if(moving)
        {
            var x=Math.Clamp(previous.X+point.X-origin.X,0,Math.Max(0,PixelWidth-active.Bounds.Width));var y=Math.Clamp(previous.Y+point.Y-origin.Y,0,Math.Max(0,PixelHeight-active.Bounds.Height));Canvas.SetLeft(active,x);Canvas.SetTop(active,y);
            if(action=="cropImage")changed(new(x,y,active.Width,active.Height));return;
        }
        if(active is Polyline line)
        {
            if(DrawingTool is "Marker" or "Freehand")line.Points!.Add(point);
            else if(DrawingTool=="Arrow")
            {var angle=Math.Atan2(point.Y-origin.Y,point.X-origin.X);var length=Math.Max(12,StrokeWidth*4);line.Points=new Points{origin,point,new(point.X-length*Math.Cos(angle-.5),point.Y-length*Math.Sin(angle-.5)),point,new(point.X-length*Math.Cos(angle+.5),point.Y-length*Math.Sin(angle+.5))};}
            else line.Points=new Points{origin,point};return;
        }
        if(active is TextBlock)return;
        var dx=point.X-origin.X;var dy=point.Y-origin.Y;
        if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)){var side=Math.Min(Math.Abs(dx),Math.Abs(dy));dx=Math.Sign(dx)*side;dy=Math.Sign(dy)*side;}
        var rect=new Rect(new Point(origin.X+Math.Min(0,dx),origin.Y+Math.Min(0,dy)),new Size(Math.Max(1,Math.Abs(dx)),Math.Max(1,Math.Abs(dy))));
        Canvas.SetLeft(active,rect.X);Canvas.SetTop(active,rect.Y);active.Width=rect.Width;active.Height=rect.Height;last=point;
        if(action is "cropImage" or "redactImage")changed(rect);
    }
    private void End(object? sender,PointerReleasedEventArgs e){e.Pointer.Capture(null);moving=false;e.Handled=true;}
}
