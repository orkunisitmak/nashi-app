using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Nashi.Linux.Engine;
using PixelRect = Nashi.Linux.Engine.PixelRect;
namespace Nashi.Linux.UI;

public enum RegionMode{Crop,Regions,Draw}
public sealed record Stroke(string Kind,IReadOnlyList<Point> Points,string Color,double Width,string Text="");

/// A preview with direct manipulation. Crop: drag a new box or move the current one (Ctrl+drag
/// starts a new box inside it). Regions: drag any number of boxes. Draw: pen, box and text marks.
/// Every result is reported in source pixels, whatever the preview's size.
public sealed class RegionCanvas:Canvas
{
    private readonly double scale;private readonly int sourceWidth,sourceHeight;private readonly RegionMode mode;
    private readonly List<Rect> regions=[];private readonly List<Stroke> strokes=[];
    private Point? start;private Rect moving;private bool movingCrop;private List<Point>? pen;
    public double? Ratio{get;set;}
    public string Tool{get;set;}="pen";public string Color{get;set;}="#E5484D";public double StrokeWidth{get;set;}=6;public string Text{get;set;}="";public double FontSize{get;set;}=36;
    public event Action? Changed;
    public RegionCanvas(byte[] png,int sourceWidth,int sourceHeight,RegionMode mode,double maxWidth=620,double maxHeight=400)
    {
        this.sourceWidth=sourceWidth;this.sourceHeight=sourceHeight;this.mode=mode;
        var fit=Math.Min(1,Math.Min(maxWidth/sourceWidth,maxHeight/sourceHeight));Width=Math.Max(1,sourceWidth*fit);Height=Math.Max(1,sourceHeight*fit);scale=sourceWidth/Width;
        Background=Brushes.Transparent;ClipToBounds=true;Cursor=new Cursor(StandardCursorType.Cross);
        if(mode==RegionMode.Crop)regions.Add(new Rect(Width*.1,Height*.1,Width*.8,Height*.8));
        PointerPressed+=OnPressed;PointerMoved+=OnMoved;PointerReleased+=OnReleased;
        Children.Add(new Image{Source=Ui.FromPng(png),Width=Width,Height=Height});Render();
    }
    public PixelRect? Crop=>mode==RegionMode.Crop&&regions.Count==1?ToSource(regions[0]):null;
    public PixelRect[] Regions=>regions.Select(ToSource).Where(r=>r.Width>=2&&r.Height>=2).ToArray();
    public IReadOnlyList<Stroke> Strokes=>strokes.Select(s=>s with{Points=s.Points.Select(p=>new Point(p.X*scale,p.Y*scale)).ToList(),Width=s.Width*scale}).ToList();
    public double Scale=>scale;
    public void Undo(){if(mode==RegionMode.Draw&&strokes.Count>0)strokes.RemoveAt(strokes.Count-1);else if(mode==RegionMode.Regions&&regions.Count>0)regions.RemoveAt(regions.Count-1);Render();}
    public void Clear(){strokes.Clear();if(mode!=RegionMode.Crop)regions.Clear();Render();}
    public void SetCrop(PixelRect rect){regions.Clear();regions.Add(new Rect(rect.X/scale,rect.Y/scale,rect.Width/scale,rect.Height/scale));Render();}
    /// Applies the ratio to the current crop, centred on it.
    public void ApplyRatio()
    {
        if(mode!=RegionMode.Crop||Ratio is not {} ratio||regions.Count==0)return;var r=regions[0];
        var w=r.Width;var h=w/ratio;if(h>Height){h=Height;w=h*ratio;}if(w>Width){w=Width;h=w/ratio;}
        regions[0]=Clamp(new Rect(r.Center.X-w/2,r.Center.Y-h/2,w,h));Render();
    }
    private PixelRect ToSource(Rect r)
    {
        var x=(int)Math.Clamp(Math.Round(r.X*scale),0,sourceWidth-1);var y=(int)Math.Clamp(Math.Round(r.Y*scale),0,sourceHeight-1);
        var w=(int)Math.Clamp(Math.Round(r.Width*scale),1,sourceWidth-x);var h=(int)Math.Clamp(Math.Round(r.Height*scale),1,sourceHeight-y);return new(x,y,w,h);
    }
    private Rect Clamp(Rect r)
    {
        var w=Math.Min(r.Width,Width);var h=Math.Min(r.Height,Height);return new Rect(Math.Clamp(r.X,0,Width-w),Math.Clamp(r.Y,0,Height-h),w,h);
    }
    private Point Inside(Point p)=>new(Math.Clamp(p.X,0,Width),Math.Clamp(p.Y,0,Height));
    private void OnPressed(object? sender,PointerPressedEventArgs e)
    {
        if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;var point=Inside(e.GetPosition(this));start=point;e.Pointer.Capture(this);
        if(mode==RegionMode.Crop&&regions.Count==1&&regions[0].Contains(point)&&!e.KeyModifiers.HasFlag(KeyModifiers.Control)){movingCrop=true;moving=regions[0];return;}
        movingCrop=false;
        if(mode==RegionMode.Draw)
        {
            if(Tool=="text"){if(!string.IsNullOrWhiteSpace(Text)){strokes.Add(new("text",[point],Color,StrokeWidth,Text));Render();Changed?.Invoke();}start=null;return;}
            pen=[point];if(Tool=="pen")strokes.Add(new("pen",pen,Color,StrokeWidth));
        }
    }
    private void OnMoved(object? sender,PointerEventArgs e)
    {
        if(start is not {} origin)return;var point=Inside(e.GetPosition(this));
        if(movingCrop){regions[0]=Clamp(moving.Translate(point-origin));Render();return;}
        if(mode==RegionMode.Draw&&Tool=="pen"&&pen is not null){pen.Add(point);strokes[^1]=strokes[^1] with{Points=pen.ToList()};Render();return;}
        var rect=new Rect(origin,point).Normalize();
        // A chosen aspect ratio always holds; Free lets the box take any shape.
        if(mode==RegionMode.Crop&&Ratio is {} ratio)
        {
            var w=rect.Width;var h=w/ratio;if(h>rect.Height&&rect.Height>0){h=rect.Height;w=h*ratio;}
            rect=new Rect(point.X<origin.X?origin.X-w:origin.X,point.Y<origin.Y?origin.Y-h:origin.Y,w,h);
        }
        Preview(rect);
    }
    private Rect? pending;
    private void Preview(Rect rect){pending=rect;Render();}
    private void OnReleased(object? sender,PointerReleasedEventArgs e)
    {
        if(start is null)return;e.Pointer.Capture(null);
        if(pending is {} rect&&rect.Width>=3&&rect.Height>=3)
        {
            if(mode==RegionMode.Crop){regions.Clear();regions.Add(Clamp(rect));}
            else if(mode==RegionMode.Regions)regions.Add(Clamp(rect));
            else if(Tool=="box")strokes.Add(new("box",[rect.TopLeft,rect.BottomRight],Color,StrokeWidth));
        }
        start=null;pending=null;pen=null;movingCrop=false;Render();Changed?.Invoke();
    }
    private void Render()
    {
        while(Children.Count>1)Children.RemoveAt(1);
        var gold=new SolidColorBrush(Avalonia.Media.Color.Parse("#E3B859"));
        if(mode==RegionMode.Crop&&regions.Count==1)
        {
            var r=pending??regions[0];var shade=new SolidColorBrush(Avalonia.Media.Color.FromArgb(130,0,0,0));
            foreach(var part in new[]{new Rect(0,0,Width,r.Y),new Rect(0,r.Bottom,Width,Math.Max(0,Height-r.Bottom)),new Rect(0,r.Y,r.X,r.Height),new Rect(r.Right,r.Y,Math.Max(0,Width-r.Right),r.Height)})
                Add(new Rectangle{Width=Math.Max(0,part.Width),Height=Math.Max(0,part.Height),Fill=shade},part.X,part.Y);
            Add(new Rectangle{Width=r.Width,Height=r.Height,Stroke=gold,StrokeThickness=2},r.X,r.Y);
            Add(new Border{Background=gold,CornerRadius=new(6),Padding=new(6,2),Child=new TextBlock{Text=$"{ToSource(r).Width} × {ToSource(r).Height}",FontSize=11,Foreground=Brushes.Black}},r.X+4,r.Y+4);
            return;
        }
        if(mode==RegionMode.Regions)
        {
            foreach(var r in regions)Add(new Rectangle{Width=r.Width,Height=r.Height,Fill=new SolidColorBrush(Avalonia.Media.Color.FromArgb(170,0,0,0)),Stroke=gold,StrokeThickness=1.5},r.X,r.Y);
            if(pending is {} p)Add(new Rectangle{Width=p.Width,Height=p.Height,Stroke=gold,StrokeThickness=2,StrokeDashArray=[4,3]},p.X,p.Y);
            return;
        }
        foreach(var stroke in strokes)
        {
            var brush=new SolidColorBrush(Avalonia.Media.Color.Parse(stroke.Color));
            if(stroke.Kind=="pen")Children.Add(new Polyline{Points=stroke.Points.ToList(),Stroke=brush,StrokeThickness=stroke.Width,StrokeLineCap=PenLineCap.Round,StrokeJoin=PenLineJoin.Round});
            else if(stroke.Kind=="box"){var r=new Rect(stroke.Points[0],stroke.Points[1]);Add(new Rectangle{Width=r.Width,Height=r.Height,Stroke=brush,StrokeThickness=stroke.Width},r.X,r.Y);}
            else Add(new TextBlock{Text=stroke.Text,Foreground=brush,FontSize=FontSize/scale,FontWeight=FontWeight.Bold},stroke.Points[0].X,stroke.Points[0].Y-FontSize/scale);
        }
        if(pending is {} box&&Tool=="box")Add(new Rectangle{Width=box.Width,Height=box.Height,Stroke=new SolidColorBrush(Avalonia.Media.Color.Parse(Color)),StrokeThickness=StrokeWidth,StrokeDashArray=[4,3]},box.X,box.Y);
    }
    private void Add(Control control,double x,double y){SetLeft(control,x);SetTop(control,y);Children.Add(control);}
}
