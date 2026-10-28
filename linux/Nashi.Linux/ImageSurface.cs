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
internal sealed partial class ImageSurface:Viewbox
{
    private readonly Canvas canvas;
    private readonly Image original;
    private readonly Canvas handles=new(){IsHitTestVisible=false};
    private CropGeometry.Handle? resizeHandle;
    private double? cropRatio;
    public double? CropRatio{get=>cropRatio;set{cropRatio=value;if(action=="cropImage"&&value is >0){var width=Math.Min(PixelWidth,PixelHeight*value.Value);SetCrop(new((PixelWidth-width)/2,(PixelHeight-width/value.Value)/2,width,width/value.Value));}}}
    internal Rect? CropBounds=>action=="cropImage"&&active is Rectangle r&&r.Width>=1&&r.Height>=1?new Rect(Canvas.GetLeft(r),Canvas.GetTop(r),r.Width,r.Height):null;
    private Control? active;
    private Point origin;
    private Point last;
    private Rect previous;
    private bool moving;
    private readonly string action;
    private readonly Action<Rect> changed;
    private int markNumber=1;
    public string Ink{get;set;}="#FF5200";
    public double StrokeWidth{get;set;}=3;
    public double FontSize{get;set;}=24;
    public string AnnotationText{get;set;}="Note";
    public string? InsertImage{get;set;}
    public MediaRegion[] Regions=>RegionBounds.Select(r=>{var x=Math.Floor(r.X+1e-7);var y=Math.Floor(r.Y+1e-7);return new MediaRegion(x,y,Math.Ceiling(r.Right-1e-7)-x,Math.Ceiling(r.Bottom-1e-7)-y);}).ToArray();
    public int PixelWidth{get;} public int PixelHeight{get;}
    public ImageSurface(byte[] bytes,string action,Action<Rect> changed)
    {
        this.action=action;this.changed=changed;using var stream=new MemoryStream(bytes);var bitmap=new Bitmap(stream);PixelWidth=bitmap.PixelSize.Width;PixelHeight=bitmap.PixelSize.Height;
        original=new Image{Source=bitmap,Width=PixelWidth,Height=PixelHeight};canvas=new Canvas{Width=PixelWidth,Height=PixelHeight,Background=Brushes.Transparent,ClipToBounds=true};canvas.Children.Add(original);var layers=new Grid();layers.Children.Add(canvas);layers.Children.Add(handles);Child=layers;Stretch=Stretch.Uniform;MaxHeight=460;
        canvas.PointerPressed+=Begin;canvas.PointerMoved+=Move;canvas.PointerReleased+=End;Focusable=true;
        SizeChanged+=(_,_)=>RefreshHandles();
        canvas.PointerCaptureLost+=(_,_)=>{moving=false;resizeHandle=null;gesturePointer=null;CommitGesture();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Z&&e.KeyModifiers.HasFlag(KeyModifiers.Control)){if(e.KeyModifiers.HasFlag(KeyModifiers.Shift))Redo();else Undo();e.Handled=true;}if(e.Key==Key.Y&&e.KeyModifiers.HasFlag(KeyModifiers.Control)){Redo();e.Handled=true;}if(e.Key is Key.Delete or Key.Back&&active is not null){DeleteSelected();e.Handled=true;}};
    }
    public void SetPreview(byte[] bytes){using var stream=new MemoryStream(bytes);var bitmap=new Bitmap(stream);var old=original.Source as Bitmap;original.Source=bitmap;original.Width=canvas.Width=bitmap.PixelSize.Width;original.Height=canvas.Height=bitmap.PixelSize.Height;old?.Dispose();}
    public byte[] Png()
    {
        canvas.UpdateLayout();
        using var bitmap=new RenderTargetBitmap(new PixelSize(PixelWidth,PixelHeight),new Vector(96,96));bitmap.Render(canvas);using var output=new MemoryStream();bitmap.Save(output,new PngBitmapEncoderOptions());return output.ToArray();
    }
    private Point Position(PointerEventArgs e){var p=e.GetPosition(canvas);return new(Math.Clamp(p.X,0,PixelWidth),Math.Clamp(p.Y,0,PixelHeight));}
    private void Begin(object? sender,PointerPressedEventArgs e)
    {
        if(!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed)return;
        if(originalView||action is not ("cropImage" or "redactImage" or "annotateImage"))return;
        origin=Position(e);last=origin;Focus();moving=false;resizeHandle=null;e.Handled=true;
        if(action=="annotateImage"){
            if(!BeginAnnotation(origin))return;
            gesturePointer=e.Pointer;e.Pointer.Capture(canvas);return;
        }
        beforeGesture=CaptureState();gesturePointer=e.Pointer;e.Pointer.Capture(canvas);
        if(action=="redactImage"&&BeginRegion(origin))return;
        if(action=="cropImage"&&CropBounds is {} crop){
            previous=crop;resizeHandle=HitHandle(crop,origin,HandleSize*1.5);
            if(resizeHandle is not null)return;
            if(crop.Contains(origin)){moving=true;return;}
        }
        if(action=="cropImage"&&active is not null)canvas.Children.Remove(active);
        active=new Rectangle{Width=1,Height=1,Stroke=NashiTheme.Gold,StrokeThickness=2,Fill=action=="redactImage"?RedactionBrush:NashiTheme.Brush("#22333333"),IsHitTestVisible=false};
        origin=new(Math.Min(origin.X,PixelWidth-1),Math.Min(origin.Y,PixelHeight-1));Canvas.SetLeft(active,origin.X);Canvas.SetTop(active,origin.Y);canvas.Children.Add(active);
        if(action=="cropImage")SetCrop(new(origin,new Size(1,1)));else{changed(MarkBounds(active));RefreshHandles();SceneChanged?.Invoke();}
    }
    private void Move(object? sender,PointerEventArgs e)
    {
        if(e.Pointer.Captured!=canvas||active is null)return;var point=Position(e);e.Handled=true;
        if(action=="cropImage"&&CropBounds is not null&&(moving||resizeHandle is not null)){
            SetCrop(resizeHandle is {} handle?CropGeometry.Resize(previous,handle,point,new(PixelWidth,PixelHeight),CropRatio??(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?previous.Width/previous.Height:null)):CropGeometry.Move(previous,origin,point,new(PixelWidth,PixelHeight)));return;
        }
        if(action=="redactImage"&&(moving||resizeHandle is not null)){
            SetRegionBounds(resizeHandle is {} handle?CropGeometry.Resize(previous,handle,point,new(PixelWidth,PixelHeight),e.KeyModifiers.HasFlag(KeyModifiers.Shift)?previous.Width/previous.Height:null):CropGeometry.Move(previous,origin,point,new(PixelWidth,PixelHeight)));return;
        }
        if(action=="annotateImage"){MoveAnnotation(point,e.KeyModifiers);return;}
        if(action=="cropImage"){
            var selection=CropGeometry.Selection(origin,point,new(PixelWidth,PixelHeight),CropRatio??(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?1:null));
            if(selection.Width>=1&&selection.Height>=1)SetCrop(selection);return;
        }
        var dx=point.X-origin.X;var dy=point.Y-origin.Y;
        if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)){var side=Math.Min(Math.Abs(dx),Math.Abs(dy));dx=Math.Sign(dx)*side;dy=Math.Sign(dy)*side;}
        var rect=new Rect(new Point(origin.X+Math.Min(0,dx),origin.Y+Math.Min(0,dy)),new Size(Math.Max(1,Math.Abs(dx)),Math.Max(1,Math.Abs(dy))));
        SetRegionBounds(rect);last=point;
    }
    private double HandleSize=>Math.Clamp(8*PixelWidth/Math.Max(1,Bounds.Width),2,40);
    internal void SetCrop(Rect rect)
    {
        if(action!="cropImage")throw new InvalidOperationException("Not a crop surface.");
        if(active is not Rectangle){active=new Rectangle{Stroke=NashiTheme.Gold,StrokeThickness=2,Fill=NashiTheme.Brush("#22333333")};canvas.Children.Add(active);}
        Canvas.SetLeft(active,rect.X);Canvas.SetTop(active,rect.Y);active.Width=rect.Width;active.Height=rect.Height;
        ShowCropHandles(rect);changed(rect);
    }
    private void ShowCropHandles(Rect rect,bool outline=false)
    {
        handles.Children.Clear();var size=HandleSize;
        if(outline){var border=new Rectangle{Width=rect.Width,Height=rect.Height,Stroke=NashiTheme.Gold,StrokeThickness=Math.Max(1,size/5)};Canvas.SetLeft(border,rect.X);Canvas.SetTop(border,rect.Y);handles.Children.Add(border);}
        foreach(var handle in Enum.GetValues<CropGeometry.Handle>()){var f=CropGeometry.Fraction(handle);var mark=new Rectangle{Width=size,Height=size,Fill=Brushes.White,Stroke=NashiTheme.Gold,StrokeThickness=Math.Max(1,size/5)};Canvas.SetLeft(mark,rect.X+f.X*rect.Width-size/2);Canvas.SetTop(mark,rect.Y+f.Y*rect.Height-size/2);handles.Children.Add(mark);}
    }
    private void End(object? sender,PointerReleasedEventArgs e){CommitGesture();e.Pointer.Capture(null);moving=false;resizeHandle=null;gesturePointer=null;RefreshHandles();e.Handled=true;}
}
