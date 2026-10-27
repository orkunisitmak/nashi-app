using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Linux.Engine;
using PixelRect=Nashi.Linux.Engine.PixelRect;
namespace Nashi.Linux.UI;

/// Drag rectangles on an oriented preview. Regions are kept in source pixels,
/// the same space PhotoCodec and FFmpeg's auto-rotated frames use.
internal sealed class RegionPicker:Control
{
    private readonly Bitmap bitmap;private readonly bool multiple;private readonly List<PixelRect> regions=[];
    private Point? start;private Rect? dragging;
    private static readonly IPen Outline=new Pen(new SolidColorBrush(Color.FromRgb(227,184,89)),2);
    private static readonly IBrush Fill=new SolidColorBrush(Color.FromArgb(70,227,184,89));
    public IReadOnlyList<PixelRect> Regions=>regions;
    public PixelSize Pixels=>bitmap.PixelSize;
    public event Action? Changed;
    public RegionPicker(Bitmap bitmap,bool multiple)
    {
        this.bitmap=bitmap;this.multiple=multiple;Height=340;ClipToBounds=true;Focusable=true;Cursor=new Cursor(StandardCursorType.Cross);
        Avalonia.Automation.AutomationProperties.SetName(this,L10n.T(multiple?"Drag on the preview to add areas. Right-click removes the last one.":"Drag on the preview to select an area."));
    }
    private Rect ImageRect
    {
        get
        {
            var size=bitmap.PixelSize;if(size.Width==0||size.Height==0||Bounds.Width<=0||Bounds.Height<=0)return default;
            var scale=Math.Min(Bounds.Width/size.Width,Bounds.Height/size.Height);var w=size.Width*scale;var h=size.Height*scale;
            return new((Bounds.Width-w)/2,(Bounds.Height-h)/2,w,h);
        }
    }
    private double Scale=>ImageRect.Width/Math.Max(1,bitmap.PixelSize.Width);
    private Rect ToView(PixelRect r){var area=ImageRect;var s=Scale;return new(area.X+r.X*s,area.Y+r.Y*s,r.Width*s,r.Height*s);}
    private Point Clamp(Point p){var area=ImageRect;return new(Math.Clamp(p.X,area.Left,area.Right),Math.Clamp(p.Y,area.Top,area.Bottom));}
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent,new Rect(Bounds.Size));
        context.DrawImage(bitmap,new Rect(bitmap.Size),ImageRect);
        foreach(var region in regions)context.DrawRectangle(Fill,Outline,ToView(region));
        if(dragging is {} live)context.DrawRectangle(Fill,Outline,live);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);Focus();
        if(e.GetCurrentPoint(this).Properties.IsRightButtonPressed){if(regions.Count>0){regions.RemoveAt(regions.Count-1);InvalidateVisual();Changed?.Invoke();}return;}
        start=Clamp(e.GetPosition(this));dragging=null;e.Pointer.Capture(this);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);if(start is not {} origin)return;var current=Clamp(e.GetPosition(this));
        dragging=new Rect(new Point(Math.Min(origin.X,current.X),Math.Min(origin.Y,current.Y)),new Point(Math.Max(origin.X,current.X),Math.Max(origin.Y,current.Y)));InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);e.Pointer.Capture(null);
        if(dragging is {} live&&live.Width>=4&&live.Height>=4)
        {
            var area=ImageRect;var s=Scale;var size=bitmap.PixelSize;
            var x=(int)Math.Round((live.X-area.X)/s);var y=(int)Math.Round((live.Y-area.Y)/s);
            var w=Math.Min(size.Width-x,(int)Math.Round(live.Width/s));var h=Math.Min(size.Height-y,(int)Math.Round(live.Height/s));
            if(w>=2&&h>=2){if(!multiple)regions.Clear();regions.Add(new(x,y,w,h));Changed?.Invoke();}
        }
        start=null;dragging=null;InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.Key is Key.Delete or Key.Back&&regions.Count>0){regions.RemoveAt(regions.Count-1);InvalidateVisual();Changed?.Invoke();e.Handled=true;}
    }
}
