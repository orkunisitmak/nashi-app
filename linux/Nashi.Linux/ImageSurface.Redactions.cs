using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed partial class ImageSurface
{
    private string redactionStyle="solid";
    private bool originalView;
    public string RedactionStyle{get=>redactionStyle;set{redactionStyle=value;foreach(var region in RegionShapes)region.Fill=RedactionBrush;}}
    private IBrush RedactionBrush=>RedactionStyle=="solid"?Brushes.Black:Brushes.Transparent;
    private Rectangle[] RegionShapes=>action=="redactImage"?canvas.Children.OfType<Rectangle>().ToArray():[];
    internal Rect[] RegionBounds=>RegionShapes.Select(MarkBounds).ToArray();
    internal int SelectedRegion=>active is Rectangle rectangle?Array.IndexOf(RegionShapes,rectangle):-1;
    internal void ShowOriginal(bool value)
    {
        originalView=value;foreach(var mark in canvas.Children.Where(c=>c!=original))mark.IsVisible=!value;handles.IsVisible=!value;
    }
    internal void SelectRegion(int index)
    {
        ReleasePointer();var regions=RegionShapes;active=index>=0&&index<regions.Length?regions[index]:null;if(active is not null)changed(MarkBounds(active));RefreshHandles();SceneChanged?.Invoke();
    }
    private Rectangle CreateRegion(Rect rect)
    {
        var shape=new Rectangle{Stroke=NashiTheme.Gold,StrokeThickness=2,Fill=RedactionBrush,IsHitTestVisible=false,IsVisible=!originalView};Canvas.SetLeft(shape,rect.X);Canvas.SetTop(shape,rect.Y);shape.Width=rect.Width;shape.Height=rect.Height;return shape;
    }
    internal void SetRegionBounds(Rect rect)
    {
        if(action!="redactImage")throw new InvalidOperationException("Not a redaction surface.");
        if(!double.IsFinite(rect.X)||!double.IsFinite(rect.Y)||!double.IsFinite(rect.Width)||!double.IsFinite(rect.Height)||rect.X<0||rect.Y<0||rect.Width<1||rect.Height<1||rect.Right>PixelWidth+1e-7||rect.Bottom>PixelHeight+1e-7)throw new ArgumentException("Choose a region inside the source image.");
        if(active is Rectangle existing&&CloseBounds(MarkBounds(existing),rect))return;
        if(gesturePointer is null)Remember(CaptureState());
        if(active is not Rectangle){active=CreateRegion(rect);canvas.Children.Add(active);}else{Canvas.SetLeft(active,rect.X);Canvas.SetTop(active,rect.Y);active.Width=rect.Width;active.Height=rect.Height;}
        changed(rect);RefreshHandles();SceneChanged?.Invoke();
    }
    internal int AddRegions(IEnumerable<MediaRegion> regions)
    {
        if(action!="redactImage")throw new InvalidOperationException("Not a redaction surface.");ReleasePointer();
        var existing=Regions.Select(r=>new Rect(r.X,r.Y,r.Width,r.Height)).ToHashSet();var added=new List<Rect>();
        foreach(var region in regions){
            if(!double.IsFinite(region.X)||!double.IsFinite(region.Y)||!double.IsFinite(region.Width)||!double.IsFinite(region.Height)||region.X<0||region.Y<0||region.Width<=0||region.Height<=0||region.X+region.Width>PixelWidth||region.Y+region.Height>PixelHeight)throw new ArgumentException("Choose a region inside the source image.");
            var x=Math.Floor(region.X);var y=Math.Floor(region.Y);var rect=new Rect(x,y,Math.Ceiling(region.X+region.Width)-x,Math.Ceiling(region.Y+region.Height)-y);if(existing.Add(rect))added.Add(rect);
        }
        if(added.Count==0)return 0;Remember(CaptureState());
        foreach(var rect in added){var region=CreateRegion(rect);canvas.Children.Add(region);active=region;}
        changed(MarkBounds(active!));RefreshHandles();SceneChanged?.Invoke();return added.Count;
    }
    private bool BeginRegion(Point point)
    {
        var tolerance=HandleSize*1.5;var selected=RegionShapes.Reverse().FirstOrDefault(region=>MarkBounds(region).Inflate(tolerance).Contains(point));if(selected is null)return false;
        active=selected;previous=MarkBounds(selected);resizeHandle=HitHandle(previous,point,tolerance);moving=resizeHandle is null;changed(previous);RefreshHandles();SceneChanged?.Invoke();return true;
    }
}
