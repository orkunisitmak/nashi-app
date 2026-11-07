using Avalonia;
namespace Nashi.Linux;

/// Editor geometry for annotation marks: hit boxes, moving and preview scaling.
internal static class MarkGeometry
{
    /// The area a mark covers in image pixels, used to pick and outline it.
    public static Rect Bounds(Mark m)
    {
        switch(m.Kind)
        {
            case "marker" or "freehand" when m.Points is {Count:>0} points:
                {var pad=m.Kind=="marker"?m.Width*2:m.Width/2;var xs=points.Select(p=>p.X).ToArray();var ys=points.Select(p=>p.Y).ToArray();return new Rect(xs.Min()-pad,ys.Min()-pad,xs.Max()-xs.Min()+pad*2,ys.Max()-ys.Min()+pad*2);}
            case "text":return new(m.X1,m.Y1,Math.Max(m.FontSize*.6*Math.Max(1,m.Text.Length),m.FontSize),m.FontSize*1.3);
            case "number":{var r=m.FontSize*.6;return new(m.X1-r,m.Y1-r,r*2,r*2);}
            default:{var pad=m.Width/2+(m.Kind=="arrow"?Math.Max(m.Width*4,12)*.5:0);return new Rect(Math.Min(m.X1,m.X2)-pad,Math.Min(m.Y1,m.Y2)-pad,Math.Abs(m.X2-m.X1)+pad*2,Math.Abs(m.Y2-m.Y1)+pad*2);}
        }
    }
    public static Mark Moved(Mark m,double dx,double dy)=>m with{X1=m.X1+dx,Y1=m.Y1+dy,X2=m.X2+dx,Y2=m.Y2+dy,Points=m.Points?.Select(p=>(p.X+dx,p.Y+dy)).ToArray()};
    /// Every length scaled, for drawing on a smaller preview of the same picture.
    public static Annotation Scaled(Annotation a,double scale)=>Math.Abs(scale-1)<1e-9?a:new(a.Marks.Select(m=>m with{X1=m.X1*scale,Y1=m.Y1*scale,X2=m.X2*scale,Y2=m.Y2*scale,Width=Math.Max(1,m.Width*scale),FontSize=Math.Max(4,m.FontSize*scale),Points=m.Points?.Select(p=>(p.X*scale,p.Y*scale)).ToArray()}).ToArray());
}
