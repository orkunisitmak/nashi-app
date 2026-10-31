using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
namespace Nashi.Linux;

// Linux rendering of the original ImageAnnotation kinds. Each mark has local
// points inside its own source-pixel rectangle, so moving/resizing never depends
// on the preview's zoom or a control's incidental layout bounds.
internal sealed class AnnotationMark:Control
{
    internal required string Kind{get;init;}
    internal required IBrush Ink{get;init;}
    internal double StrokeWidth{get;init;}=3;
    internal double TextSize{get;init;}=24;
    internal string Text{get;init;}="Note";
    internal string Font{get;init;}="system";
    internal Bitmap? Artwork{get;init;}
    internal Point[] Points{get;set;}=[];
    internal Rect SourceBounds=>new(Canvas.GetLeft(this),Canvas.GetTop(this),Width,Height);
    internal void SetBounds(Rect rect)
    {
        Canvas.SetLeft(this,rect.X);Canvas.SetTop(this,rect.Y);Width=rect.Width;Height=rect.Height;InvalidateVisual();
    }
    internal void SetPath(IReadOnlyList<Point> points)
    {
        var left=points.Min(p=>p.X);var top=points.Min(p=>p.Y);var width=Math.Max(1,points.Max(p=>p.X)-left);var height=Math.Max(1,points.Max(p=>p.Y)-top);
        Points=points.Select(p=>new Point((p.X-left)/width,(p.Y-top)/height)).ToArray();SetBounds(new(left,top,width,height));
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);var rect=new Rect(0,0,Bounds.Width,Bounds.Height);if(rect.Width<=0||rect.Height<=0)return;
        var pen=new Pen(Ink,Kind=="Marker"?Math.Max(12,StrokeWidth):StrokeWidth){LineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
        switch(Kind){
            case "Rectangle":context.DrawRectangle(null,pen,rect);break;
            case "Ellipse":context.DrawEllipse(null,pen,rect.Center,rect.Width/2,rect.Height/2);break;
            case "Marker":
                if(Points.Length>0)context.Custom(new MarkerDrawing(Points.Select(p=>new Point(p.X*rect.Width,p.Y*rect.Height)).ToArray(),Ink is ISolidColorBrush solid?solid.Color:Colors.Black,Math.Max(12,StrokeWidth)));break;
            case "Freehand":case "Line":case "Arrow":
                if(Points.Length==0)break;
                var points=Points.Select(p=>new Point(p.X*rect.Width,p.Y*rect.Height)).ToArray();
                using(context.PushOpacity(1)){
                    var path=new StreamGeometry();using(var geometry=path.Open()){geometry.BeginFigure(points[0],false);foreach(var point in points.Skip(1))geometry.LineTo(point);geometry.EndFigure(false);}context.DrawGeometry(null,pen,path);
                    if(Kind=="Arrow"&&points.Length>1){var end=points[^1];var before=points[^2];var angle=Math.Atan2(end.Y-before.Y,end.X-before.X);var size=Math.Max(10,StrokeWidth*4);context.DrawLine(pen,new(end.X-size*Math.Cos(angle-Math.PI/6),end.Y-size*Math.Sin(angle-Math.PI/6)),end);context.DrawLine(pen,end,new(end.X-size*Math.Cos(angle+Math.PI/6),end.Y-size*Math.Sin(angle+Math.PI/6)));}
                }break;
            case "Text":DrawText(context,rect,Ink,TextSize,false);break;
            case "Number":context.DrawEllipse(Ink,null,rect.Center,rect.Width/2,rect.Height/2);DrawText(context,rect,Brushes.White,Math.Min(rect.Width,rect.Height)*.58,true);break;
            case "Callout":
                var body=new Rect(0,0,rect.Width,rect.Height*.8);context.DrawRectangle(Ink,null,body,8,8);
                var tail=new StreamGeometry();using(var geometry=tail.Open()){geometry.BeginFigure(new(rect.Width*.18,rect.Height*.8-1),true);geometry.LineTo(new(rect.Width*.1,rect.Height));geometry.LineTo(new(rect.Width*.4,rect.Height*.8-1));geometry.EndFigure(true);}context.DrawGeometry(Ink,null,tail);DrawText(context,body,Brushes.White,TextSize,true);break;
            case "Magnifier" when Artwork is not null:
                var source=new Rect(Canvas.GetLeft(this)+rect.Width/4,Canvas.GetTop(this)+rect.Height/4,rect.Width/2,rect.Height/2).Intersect(new Rect(0,0,Artwork.PixelSize.Width,Artwork.PixelSize.Height));
                if(source.Width>0&&source.Height>0)using(context.PushGeometryClip(new EllipseGeometry(rect)))context.DrawImage(Artwork,source,rect);
                context.DrawEllipse(null,pen,rect.Center,rect.Width/2,rect.Height/2);break;
            case "Image" when Artwork is not null:
                var scale=Math.Min(rect.Width/Artwork.PixelSize.Width,rect.Height/Artwork.PixelSize.Height);var width=Artwork.PixelSize.Width*scale;var height=Artwork.PixelSize.Height*scale;context.DrawImage(Artwork,new Rect((rect.Width-width)/2,(rect.Height-height)/2,width,height));break;
        }
    }
    private void DrawText(DrawingContext context,Rect rect,IBrush ink,double size,bool centered)
    {
        var family=Font switch{"system"=>"DejaVu Sans","rounded"=>"ui-rounded, DejaVu Sans","serif"=>"DejaVu Serif","monospaced"=>"DejaVu Sans Mono",_=>Font};
        var text=new FormattedText(Text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(family,FontStyle.Normal,FontWeight.SemiBold),Math.Max(1,size),ink){MaxTextWidth=Math.Max(1,rect.Width),MaxTextHeight=Math.Max(1,rect.Height),TextAlignment=centered?TextAlignment.Center:TextAlignment.Left};
        using(context.PushClip(rect))context.DrawText(text,new Point(rect.X,centered?rect.Y+Math.Max(0,(rect.Height-text.Height)/2):rect.Y));
    }
}
