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
    private readonly Canvas handles=new(){IsHitTestVisible=false};
    private CropGeometry.Handle? resizeHandle;
    private double? cropRatio;
    public double? CropRatio{get=>cropRatio;set{cropRatio=value;if(action=="cropImage"&&value is >0){var width=Math.Min(PixelWidth,PixelHeight*value.Value);SetCrop(new((PixelWidth-width)/2,(PixelHeight-width/value.Value)/2,width,width/value.Value));}}}
    internal Rect? CropBounds=>action=="cropImage"&&active is Rectangle r&&r.Width>=1&&r.Height>=1?new Rect(Canvas.GetLeft(r),Canvas.GetTop(r),r.Width,r.Height):null;
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
        original=new Image{Source=bitmap,Width=PixelWidth,Height=PixelHeight};canvas=new Canvas{Width=PixelWidth,Height=PixelHeight,Background=Brushes.Transparent,ClipToBounds=true};canvas.Children.Add(original);var layers=new Grid();layers.Children.Add(canvas);layers.Children.Add(handles);Child=layers;Stretch=Stretch.Uniform;MaxHeight=460;
        canvas.PointerPressed+=Begin;canvas.PointerMoved+=Move;canvas.PointerReleased+=End;Focusable=true;
        SizeChanged+=(_,_)=>{if(CropBounds is {} crop)ShowCropHandles(crop);};
        canvas.PointerCaptureLost+=(_,_)=>{moving=false;resizeHandle=null;};
        KeyDown+=(_,e)=>{if(e.Key==Key.Z&&e.KeyModifiers.HasFlag(KeyModifiers.Control)){Undo();e.Handled=true;}if(e.Key==Key.Delete&&active is not null){canvas.Children.Remove(active);active=null;e.Handled=true;}};
    }
    public void SetPreview(byte[] bytes){using var stream=new MemoryStream(bytes);var bitmap=new Bitmap(stream);var old=original.Source as Bitmap;original.Source=bitmap;original.Width=canvas.Width=bitmap.PixelSize.Width;original.Height=canvas.Height=bitmap.PixelSize.Height;old?.Dispose();}
    public void Reset(){while(canvas.Children.Count>1)canvas.Children.RemoveAt(1);undo.Clear();active=null;handles.Children.Clear();markNumber=1;}
    public void Undo(){if(undo.TryPop(out var shape)){canvas.Children.Remove(shape);active=null;handles.Children.Clear();}}
    public byte[] Png()
    {
        using var bitmap=new RenderTargetBitmap(new PixelSize(PixelWidth,PixelHeight),new Vector(96,96));bitmap.Render(canvas);using var output=new MemoryStream();bitmap.Save(output,new PngBitmapEncoderOptions());return output.ToArray();
    }
    private Point Position(PointerEventArgs e){var p=e.GetPosition(canvas);return new(Math.Clamp(p.X,0,PixelWidth),Math.Clamp(p.Y,0,PixelHeight));}
    private void Begin(object? sender,PointerPressedEventArgs e)
    {
        if(!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed)return;
        if(action is not ("cropImage" or "redactImage" or "annotateImage"))return;
        origin=Position(e);last=origin;Focus();e.Pointer.Capture(canvas);e.Handled=true;
        resizeHandle=null;
        if(action=="cropImage"&&CropBounds is {} crop){
            previous=crop;var tolerance=HandleSize*1.5;
            var nearest=Enum.GetValues<CropGeometry.Handle>().Select(handle=>{var f=CropGeometry.Fraction(handle);return (Handle:handle,Distance:Math.Sqrt(Math.Pow(crop.X+f.X*crop.Width-origin.X,2)+Math.Pow(crop.Y+f.Y*crop.Height-origin.Y,2)));}).OrderBy(h=>h.Distance).First();
            if(nearest.Distance<=tolerance){resizeHandle=nearest.Handle;return;}
            if(crop.Contains(origin)){moving=true;return;}
        }
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
        canvas.Children.Add(active);undo.Push(active);
        if(action=="cropImage"){origin=new(Math.Min(origin.X,PixelWidth-1),Math.Min(origin.Y,PixelHeight-1));SetCrop(new(origin,new Size(1,1)));}
        e.Handled=true;
    }
    private Control Magnifier(Point point)
    {
        using var source=new ImageMagick.MagickImage(Png());var size=(uint)Math.Max(1,Math.Min(80,Math.Min(PixelWidth-point.X,PixelHeight-point.Y)));
        source.Crop(new ImageMagick.MagickGeometry((int)point.X,(int)point.Y,size,size));source.ResetPage();using var stream=new MemoryStream(source.ToByteArray(ImageMagick.MagickFormat.Png));return new Image{Source=new Bitmap(stream),Stretch=Stretch.Fill};
    }
    private void Move(object? sender,PointerEventArgs e)
    {
        if(e.Pointer.Captured!=canvas||active is null)return;var point=Position(e);e.Handled=true;
        if(action=="cropImage"&&CropBounds is not null&&(moving||resizeHandle is not null)){
            SetCrop(resizeHandle is {} handle?CropGeometry.Resize(previous,handle,point,new(PixelWidth,PixelHeight),CropRatio??(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?previous.Width/previous.Height:null)):CropGeometry.Move(previous,origin,point,new(PixelWidth,PixelHeight)));return;
        }
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
        if(action=="cropImage"){
            var selection=CropGeometry.Selection(origin,point,new(PixelWidth,PixelHeight),CropRatio??(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?1:null));
            if(selection.Width>=1&&selection.Height>=1)SetCrop(selection);return;
        }
        var dx=point.X-origin.X;var dy=point.Y-origin.Y;
        if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)){var side=Math.Min(Math.Abs(dx),Math.Abs(dy));dx=Math.Sign(dx)*side;dy=Math.Sign(dy)*side;}
        var rect=new Rect(new Point(origin.X+Math.Min(0,dx),origin.Y+Math.Min(0,dy)),new Size(Math.Max(1,Math.Abs(dx)),Math.Max(1,Math.Abs(dy))));
        Canvas.SetLeft(active,rect.X);Canvas.SetTop(active,rect.Y);active.Width=rect.Width;active.Height=rect.Height;last=point;
        if(action is "cropImage" or "redactImage")changed(rect);
    }
    private double HandleSize=>Math.Clamp(8*PixelWidth/Math.Max(1,Bounds.Width),2,40);
    internal void SetCrop(Rect rect)
    {
        if(action!="cropImage")throw new InvalidOperationException("Not a crop surface.");
        if(active is not Rectangle){active=new Rectangle{Stroke=NashiTheme.Gold,StrokeThickness=2,Fill=NashiTheme.Brush("#22333333")};canvas.Children.Add(active);undo.Push(active);}
        Canvas.SetLeft(active,rect.X);Canvas.SetTop(active,rect.Y);active.Width=rect.Width;active.Height=rect.Height;
        ShowCropHandles(rect);changed(rect);
    }
    private void ShowCropHandles(Rect rect)
    {
        handles.Children.Clear();var size=HandleSize;
        foreach(var handle in Enum.GetValues<CropGeometry.Handle>()){var f=CropGeometry.Fraction(handle);var mark=new Rectangle{Width=size,Height=size,Fill=Brushes.White,Stroke=NashiTheme.Gold,StrokeThickness=Math.Max(1,size/5)};Canvas.SetLeft(mark,rect.X+f.X*rect.Width-size/2);Canvas.SetTop(mark,rect.Y+f.Y*rect.Height-size/2);handles.Children.Add(mark);}
    }
    private void End(object? sender,PointerReleasedEventArgs e){e.Pointer.Capture(null);moving=false;resizeHandle=null;e.Handled=true;}
}
