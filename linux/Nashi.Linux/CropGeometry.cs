using Avalonia;
namespace Nashi.Linux;

// Adapted from Sources/NashiApp/CropSelectionGeometry.swift. Coordinates are
// source-image pixels, independent of preview scale and pointer delivery.
internal static class CropGeometry
{
    internal enum Handle{NW,N,NE,E,SE,S,SW,W}
    internal static Point Fraction(Handle handle)=>handle switch{
        Handle.NW=>new(0,0),Handle.N=>new(.5,0),Handle.NE=>new(1,0),Handle.E=>new(1,.5),
        Handle.SE=>new(1,1),Handle.S=>new(.5,1),Handle.SW=>new(0,1),_=>new(0,.5)};
    private static double? Ratio(double? ratio)=>ratio is >0&&double.IsFinite(ratio.Value)?ratio:null;
    internal static Rect Selection(Point anchor,Point point,Size bounds,double? aspect=null)
    {
        var start=new Point(Math.Clamp(anchor.X,0,bounds.Width),Math.Clamp(anchor.Y,0,bounds.Height));
        var dx=point.X-start.X;var dy=point.Y-start.Y;var maxWidth=dx<0?start.X:bounds.Width-start.X;var maxHeight=dy<0?start.Y:bounds.Height-start.Y;
        var width=Math.Min(Math.Abs(dx),maxWidth);var height=Math.Min(Math.Abs(dy),maxHeight);
        if(Ratio(aspect) is {} ratio){width=Math.Min(Math.Max(Math.Abs(dx),Math.Abs(dy)*ratio),Math.Min(maxWidth,maxHeight*ratio));height=width/ratio;}
        return new(dx<0?start.X-width:start.X,dy<0?start.Y-height:start.Y,width,height);
    }
    internal static Rect Resize(Rect rect,Handle handle,Point point,Size bounds,double? aspect=null)
    {
        var h=Fraction(handle);
        if(Ratio(aspect) is not {} ratio){
            var left=rect.Left;var right=rect.Right;var top=rect.Top;var bottom=rect.Bottom;
            if(h.X==0)left=Math.Max(0,Math.Min(right-1,point.X));if(h.X==1)right=Math.Min(bounds.Width,Math.Max(left+1,point.X));
            if(h.Y==0)top=Math.Max(0,Math.Min(bottom-1,point.Y));if(h.Y==1)bottom=Math.Min(bounds.Height,Math.Max(top+1,point.Y));
            return new(left,top,right-left,bottom-top);
        }
        if(h.X!=.5&&h.Y!=.5){
            var anchor=new Point(h.X==0?rect.Right:rect.Left,h.Y==0?rect.Bottom:rect.Top);
            var target=new Point(h.X==0?Math.Min(anchor.X-1,point.X):Math.Max(anchor.X+1,point.X),h.Y==0?Math.Min(anchor.Y-1,point.Y):Math.Max(anchor.Y+1,point.Y));
            return Selection(anchor,target,bounds,ratio);
        }
        if(h.X!=.5){
            var anchor=h.X==0?rect.Right:rect.Left;var available=h.X==0?anchor:bounds.Width-anchor;var maxHeight=2*Math.Min(rect.Center.Y,bounds.Height-rect.Center.Y);
            var width=Math.Min(Math.Max(1,h.X==0?anchor-point.X:point.X-anchor),Math.Min(available,maxHeight*ratio));
            return new(h.X==0?anchor-width:anchor,rect.Center.Y-width/ratio/2,width,width/ratio);
        }
        else{
            var anchor=h.Y==0?rect.Bottom:rect.Top;var available=h.Y==0?anchor:bounds.Height-anchor;var maxWidth=2*Math.Min(rect.Center.X,bounds.Width-rect.Center.X);
            var height=Math.Min(Math.Max(1,h.Y==0?anchor-point.Y:point.Y-anchor),Math.Min(available,maxWidth/ratio));
            return new(rect.Center.X-height*ratio/2,h.Y==0?anchor-height:anchor,height*ratio,height);
        }
    }
    internal static Rect Move(Rect rect,Point start,Point point,Size bounds)=>new(Math.Clamp(rect.X+point.X-start.X,0,Math.Max(0,bounds.Width-rect.Width)),Math.Clamp(rect.Y+point.Y-start.Y,0,Math.Max(0,bounds.Height-rect.Height)),rect.Width,rect.Height);
}
