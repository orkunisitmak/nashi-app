using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Nashi.Windows;

// The design's line icons on a 20-point grid, drawn with round strokes.
// Geometry is never a StreamGeometry and icons hold no Canvas, so they can sit inside wheels and editors.
internal static class Icons
{
    // Each part: kind+geometry | fill | stroke | stroke width | opacity | dash. P = path, C = circle, R = rounded rect.
    private static readonly Dictionary<string,string> Data=new()
    {
        ["plus"]="PM 10 4.5 v 11 M 4.5 10 h 11|0|1|||",
        ["paste"]="R5 4 10 13 2|0|1|||;PM 8 3.2 h 4 v 2.4 H 8 z|0|1|||;PM 7.7 10 h 4.6 M 7.7 13 h 3|0|1|||",
        ["link"]="PM 8.6 11.4 a 3 3 0 0 0 4.2 0 l 2.4 -2.4 a 3 3 0 0 0 -4.2 -4.2 l -.9 .9|0|1|||;PM 11.4 8.6 a 3 3 0 0 0 -4.2 0 L 4.8 11 a 3 3 0 0 0 4.2 4.2 l .9 -.9|0|1|||",
        ["search"]="C8.8 8.8 4.9|0|1|||;Pm 12.4 12.4 3.7 3.7|0|1|||",
        ["convert"]="PM 15.6 8.4 A 5.8 5.8 0 0 0 5 6.3|0|1|||;PM 5 3.4 v 3 h 3|0|1|||;PM 4.4 11.6 A 5.8 5.8 0 0 0 15 13.7|0|1|||;PM 15 16.6 v -3 h -3|0|1|||",
        ["sliders"]="PM 3.8 6.2 h 7.4 M 14.6 6.2 h 1.6 M 3.8 13.8 h 1.6 M 8.8 13.8 h 7.4|0|1|||;C12.9 6.2 1.7|0|1|||;C7.1 13.8 1.7|0|1|||",
        ["share"]="PM 16.6 3.4 3.6 8.6 l 5.4 2.4 2.4 5.4 z|0|1|||;Pm 9 11 3.9 -3.9|0|1|||",
        ["folder"]="PM 3.4 6.3 c 0 -.9 .7 -1.6 1.6 -1.6 h 3.1 l 1.6 1.7 h 5.3 c .9 0 1.6 .7 1.6 1.6 v 6.5 c 0 .9 -.7 1.6 -1.6 1.6 H 5 c -.9 0 -1.6 -.7 -1.6 -1.6 z|0|1|||",
        ["eye"]="PM 2.6 10 s 2.7 -4.9 7.4 -4.9 7.4 4.9 7.4 4.9 -2.7 4.9 -7.4 4.9 S 2.6 10 2.6 10 z|0|1|||;C10 10 2.2|0|1|||",
        ["reuse"]="PM 8 6.2 4.6 9.6 8 13|0|1|||;PM 4.9 9.6 h 6.6 a 3.6 3.6 0 0 1 0 7.2 H 10|0|1|||",
        ["x"]="Pm 5.6 5.6 8.8 8.8 M 14.4 5.6 l -8.8 8.8|0|1|||",
        ["check"]="Pm 4.8 10.4 3.3 3.3 7.1 -7.4|0|1|||",
        ["warn"]="PM 8.6 4.3 a 1.6 1.6 0 0 1 2.8 0 l 5.4 9.6 a 1.6 1.6 0 0 1 -1.4 2.4 H 4.6 a 1.6 1.6 0 0 1 -1.4 -2.4 z|0|1|||;PM 10 8.2 v 3.6|0|1|||;C10 14 .45|1|1|||",
        ["lock"]="R5 8.8 10 7.6 2|0|1|||;PM 7.2 8.8 V 6.9 a 2.8 2.8 0 0 1 5.6 0 v 1.9|0|1|||",
        ["crop"]="PM 6 2.8 V 14 h 11.2|0|1|||;PM 2.8 6 H 14 v 11.2|0|1|||",
        ["pencil"]="Pm 12.8 4.2 3 3 -8.6 8.6 -3.8 .8 .8 -3.8 z|0|1|||;Pm 11.2 5.8 3 3|0|1|||",
        ["frame"]="R3 3 14 14 2.6|0|1|||;R6.2 6.2 7.6 7.6 1|0|1|||",
        ["play"]="PM 7 4.8 v 10.4 l 8.2 -5.2 z|1|1|||",
        ["pause"]="PM 7 5 v 10 M 13 5 v 10|0|1|2.3||",
        ["back"]="PM 5 5 v 10|0|1|||;PM 15 5.4 v 9.6 L 8.6 10 z|1|1|||",
        ["fwd"]="PM 15 5 v 10|0|1|||;PM 5 5.4 v 9.6 l 6.4 -4.6 z|1|1|||",
        ["wave"]="PM 4 9 v 2 M 6.6 6.5 v 7 M 9.2 4.5 v 11 M 11.8 7 v 6 M 14.4 8.5 v 3 M 17 9.5 v 1|0|1|||",
        ["doc"]="PM 5.6 3 h 6 l 3.6 3.6 V 16 a 1 1 0 0 1 -1 1 H 5.6 a 1 1 0 0 1 -1 -1 V 4 a 1 1 0 0 1 1 -1 z|0|1|||;PM 11.6 3 v 3.6 h 3.6|0|1|||",
        ["copy"]="R6.6 6.6 9.8 9.8 2|0|1|||;PM 13.4 6.6 V 5.2 c 0 -.8 -.7 -1.6 -1.6 -1.6 H 5.2 c -.9 0 -1.6 .7 -1.6 1.6 v 6.6 c 0 .9 .7 1.6 1.6 1.6 h 1.4|0|1|||",
        ["terminal"]="R2.8 3.8 14.4 12.4 2.2|0|1|||;Pm 6 8 2.4 2 L 6 12 M 10 12.4 h 4|0|1|||",
        ["airdrop"]="C10 11 1.5|0|1|||;PM 7.3 13.7 a 3.9 3.9 0 1 1 5.4 0|0|1|||;PM 5 16 a 7 7 0 1 1 10 0|0|1|||",
        ["float"]="R3.2 6 10.4 10 2|0|1|||;PM 6.4 6 V 5.2 c 0 -1 .8 -1.8 1.8 -1.8 H 15 c 1 0 1.8 .8 1.8 1.8 V 12 c 0 1 -.8 1.8 -1.8 1.8 h -1.4|0|1|||",
        ["path"]="PM 6.6 16 11 4 M 11.6 16 16 4|0|1|||",
        ["chevDown"]="Pm 6 8 4 4 4 -4|0|1|||",
        ["chevRight"]="Pm 8 5.6 4.4 4.4 L 8 14.4|0|1|||",
        ["updown"]="Pm 6.8 7.8 3.2 -3.2 3.2 3.2 M 6.8 12.2 l 3.2 3.2 3.2 -3.2|0|1|||",
        ["compress"]="PM 8.4 3.6 v 4.8 H 3.6 M 11.6 16.4 v -4.8 h 4.8|0|1|||;Pm 3.6 3.6 4.8 4.8 M 16.4 16.4 l -4.8 -4.8|0|1|||",
        ["tag"]="PM 3.4 4.6 v 4.7 l 7.2 7.2 5.9 -5.9 -7.2 -7.2 H 4.6 a 1.2 1.2 0 0 0 -1.2 1.2 z|0|1|||;C7 7.2 1.1|0|1|||",
        ["redact"]="PM 2.6 10 s 2.7 -4.9 7.4 -4.9 c 1.3 0 2.5 .4 3.5 .9 M 17.4 10 s -.8 1.5 -2.4 2.9|0|1|||;PM 8.3 11.7 a 2.2 2.2 0 0 1 3.1 -3.1|0|1|||;Pm 4 16 12 -12|0|1|||",
        ["info"]="C10 10 7|0|1|||;PM 10 9 v 4.6|0|1|||;C10 6.6 .55|1|1|||",
        ["grid"]="R3.4 3.4 5.6 5.6 1.3|0|1|||;R11 3.4 5.6 5.6 1.3|0|1|||;R3.4 11 5.6 5.6 1.3|0|1|||;R11 11 5.6 5.6 1.3|0|1|||",
        ["docs"]="PM 7 5.4 V 4.6 c 0 -.9 .7 -1.6 1.6 -1.6 h 4.6 l 3.2 3.2 v 7.6 c 0 .9 -.7 1.6 -1.6 1.6 H 14|0|1|||;R3.6 5.4 10.4 11.6 1.6|0|1|||",
        ["refresh"]="PM 15.8 10.4 a 5.8 5.8 0 1 1 -1.9 -4.6|0|1|||;PM 14.6 3 v 3.4 h -3.4|0|1|||",
        ["rotL"]="PM 4.6 9.4 a 5.6 5.6 0 1 1 1.6 4.8|0|1|||;PM 3.4 5.6 v 3.8 h 3.8|0|1|||",
        ["flip"]="PM 10 3 v 14|0|1|||1.6 2;PM 7.4 5.6 3.4 14.4 h 4 z M 12.6 5.6 l 4 8.8 h -4 z|0|1|||",
        ["minus"]="PM 5 10 h 10|0|1|||",
        ["ellipsis"]="C5 10 1.15|1|0|||;C10 10 1.15|1|0|||;C15 10 1.15|1|0|||",
        ["list"]="PM 7.6 5.6 h 9 M 7.6 10 h 9 M 7.6 14.4 h 9|0|1|||;C4.2 5.6 .9|1|1|||;C4.2 10 .9|1|1|||;C4.2 14.4 .9|1|1|||",
        ["download"]="PM 10 3.4 v 9.4 M 6.2 9.2 l 3.8 3.8 3.8 -3.8|0|1|||;PM 4 16.4 h 12|0|1|||",
        ["sun"]="C10 10 3.3|0|1|||;PM 10 2.6 v 1.8 M 10 15.6 v 1.8 M 2.6 10 h 1.8 M 15.6 10 h 1.8 M 4.8 4.8 l 1.3 1.3 M 13.9 13.9 l 1.3 1.3 M 4.8 15.2 l 1.3 -1.3 M 13.9 6.1 l 1.3 -1.3|0|1|||",
        ["drop"]="PM 10 2.8 c 2.6 3.2 5 5.9 5 8.7 a 5 5 0 0 1 -10 0 c 0 -2.8 2.4 -5.5 5 -8.7 z|0|1|||",
        ["aperture"]="C10 10 7|0|1|||;Pm 10 3 3.5 6 M 17 10 h -7 M 13.5 16 10 10 M 6.5 16 10 10 M 3 10 h 7 M 6.5 4 10 10|0|1|||",
        ["exposure"]="C10 10 7|0|1|||;PM 5.1 14.9 14.9 5.1|0|1|||;PM 6 7.6 h 3.2 M 7.6 6 v 3.2 M 11 12.6 h 3.2|0|1|||",
        ["contrast"]="C10 10 7|0|1|||;PM 10 3 a 7 7 0 0 1 0 14 z|1|0|||",
        ["highlights"]="C10 10 7|0|1|||;PM 4.3 6 h 11.4 M 3.3 9 h 13.4|0|1|||",
        ["shadows"]="C10 10 7|0|1|||;PM 3.3 11 h 13.4 M 4.3 14 h 11.4|0|1|||",
        ["compare"]="R3 4 14 12 2|0|1|||;PM 10 2.6 v 14.8|0|1|||;PM 10 4 h 5 a 2 2 0 0 1 2 2 v 8 a 2 2 0 0 1 -2 2 h -5 z|1|0||.35|",
        ["chevLeft"]="Pm 12 5.6 -4.4 4.4 L 12 14.4|0|1|||",
        ["trash"]="PM 4.8 6 h 10.4 M 8 6 V 4.6 h 4 V 6|0|1|||;PM 6.2 6 l .8 9.4 a 1.4 1.4 0 0 0 1.4 1.3 h 3.2 a 1.4 1.4 0 0 0 1.4 -1.3 L 13.8 6|0|1|||",
        // Annotation tools.
        ["rect"]="R3.5 5 13 10 2|0|1|||",
        ["arrow"]="PM 4.5 15.5 15.5 4.5|0|1|||;PM 8.6 4.5 h 6.9 v 6.9|0|1|||",
        ["line"]="PM 4.5 15.5 15.5 4.5|0|1|||",
        ["ellipse"]="PM 10 5 a 7 5 0 1 1 0 10 a 7 5 0 1 1 0 -10 z|0|1|||",
        ["text"]="PM 5 5 h 10 M 10 5 v 10.5 M 8 15.5 h 4|0|1|||",
        ["number"]="C10 10 7|0|1|||;PM 8.6 7.6 l 1.8 -1.4 v 7.6|0|1|||",
        ["callout"]="PM 5 4.5 h 10 a 2 2 0 0 1 2 2 v 5 a 2 2 0 0 1 -2 2 h -4.5 l -3.5 3 v -3 H 5 a 2 2 0 0 1 -2 -2 v -5 a 2 2 0 0 1 2 -2 z|0|1|||",
        ["image"]="R3 4 14 12 2|0|1|||;PM 3.6 14.2 l 4 -4 3 3 2 -2 3.8 3.6|0|1|||;C13.2 7.8 1.2|0|1|||",
        ["freehand"]="PM 3.5 13.5 c 1.6 -5 3.6 -6.4 5 -3.2 s 2.6 4.4 4.4 1 1.6 -4.4 3.6 -5.6|0|1|||",
        ["pointer"]="PM 5.2 3.6 v 11.6 l 3.2 -3 2.3 4.8 2.1 -1 -2.3 -4.8 h 4.4 z|0|1|||",
        // Adjust Photo dials beyond the design's five, drawn on the same grid and stroke.
        ["whites"]="C10 10 7|0|1|||;PM 3.6 8.2 a 7 7 0 0 1 12.8 0 z|1|0||.3|",
        ["blacks"]="C10 10 7|0|1|||;PM 3.6 11.8 a 7 7 0 0 0 12.8 0 z|1|0|||",
        ["gamma"]="R3 3 14 14 2.6|0|1|||;PM 5.6 14.4 C 9.4 14.4 10.6 5.6 14.4 5.6|0|1|||",
        ["temperature"]="PM 8.4 4.6 a 1.6 1.6 0 0 1 3.2 0 v 6.6 a 3.4 3.4 0 1 1 -3.2 0 z|0|1|||;PM 10 8.4 v 5.2|0|1|||",
        ["tint"]="PM 10 2.8 c 2.6 3.2 5 5.9 5 8.7 a 5 5 0 0 1 -10 0 c 0 -2.8 2.4 -5.5 5 -8.7 z|0|1|||;PM 10 2.8 c -2.6 3.2 -5 5.9 -5 8.7 a 5 5 0 0 0 5 5 z|1|0||.4|",
        ["saturation"]="PM 10 2.8 c 2.6 3.2 5 5.9 5 8.7 a 5 5 0 0 1 -10 0 c 0 -2.8 2.4 -5.5 5 -8.7 z|1|1||.85|",
        ["vibrance"]="PM 9 3.8 c 2.3 2.9 4.5 5.3 4.5 7.9 a 4.5 4.5 0 0 1 -9 0 c 0 -2.6 2.2 -5 4.5 -7.9 z|0|1|||;PM 15.4 3 v 3.4 M 13.7 4.7 h 3.4|0|1|||",
        ["hue"]="C10 10 7|0|1|||;PM 10 10 V 3 M 10 10 l 6.1 3.5 M 10 10 l -6.1 3.5|0|1|||",
        ["clarity"]="C10 10 7|0|1|||;PM 10 5.8 l 4.2 4.2 -4.2 4.2 -4.2 -4.2 z|0|1|||",
        ["texture"]="C6 6 1|1|0|||;C10 6 1|1|0|||;C14 6 1|1|0|||;C6 10 1|1|0|||;C10 10 1|1|0|||;C14 10 1|1|0|||;C6 14 1|1|0|||;C10 14 1|1|0|||;C14 14 1|1|0|||",
        ["sharpen"]="PM 10 3.6 l 6.4 11.6 H 3.6 z|0|1|||",
        ["dehaze"]="PM 3.6 7 h 12.8 M 2.8 10 h 14.4 M 3.6 13 h 12.8|0|1||.55|;C10 10 3.2|0|1|||",
        ["grain"]="C5.6 6.2 .9|1|0|||;C11 4.8 .9|1|0|||;C14.6 9 .9|1|0|||;C8.4 10.4 .9|1|0|||;C5 14.2 .9|1|0|||;C12.2 14.8 .9|1|0|||;C16 14 .9|1|0|||",
        ["noise"]="C6 6.4 .9|1|0|||;C13.6 5.6 .9|1|0|||;C7.4 13.8 .9|1|0|||;C14.2 13.2 .9|1|0|||;PM 4 16 16 4|0|1|||",
        ["vignette"]="R2.8 4 14.4 12 2.6|0|1|||;PM 10 6.6 a 4.4 3.4 0 1 1 0 6.8 a 4.4 3.4 0 1 1 0 -6.8 z|0|1||.6|",
        ["fade"]="R3.4 3.4 13.2 13.2 2.6|0|1|||;PM 3.4 10 h 13.2 v 4 a 2.6 2.6 0 0 1 -2.6 2.6 H 6 a 2.6 2.6 0 0 1 -2.6 -2.6 z|1|0||.3|",
        ["blur"]="C10 10 7|0|1||.5|1.4 1.8;C10 10 3.6|0|1|||",
        ["wifi"]="PM 3.5 8.4 a 9.6 9.6 0 0 1 13 0 M 5.8 11 a 6.2 6.2 0 0 1 8.4 0 M 8.1 13.6 a 2.8 2.8 0 0 1 3.8 0|0|1|||;C10 15.8 .6|1|1|||",
        ["battery"]="R2.6 6.4 13 7.2 2|0|1|||;R4 7.8 8.6 4.4 1|1|0|||;PM 17.2 8.8 v 2.4|0|1|||",
    };
    private static readonly Dictionary<string,(Geometry Shape,bool Fill,bool Stroke,double? Width,double Opacity,string Dash)[]> parsed=[];
    private static (Geometry,bool,bool,double?,double,string)[] Parts(string name)
    {
        if(parsed.TryGetValue(name,out var known))return known;
        var list=new List<(Geometry,bool,bool,double?,double,string)>();
        if(name=="gear")
        {
            // Eight rounded teeth around a hub, as the design computes them.
            var points=new List<Point>();for(var k=0;k<8;k++)foreach(var (r,d) in new[]{(5.7,-14d),(7.4,-8d),(7.4,8d),(5.7,14d)}){var a=(k*45+d)*Math.PI/180;points.Add(new(10+r*Math.Cos(a),10+r*Math.Sin(a)));}
            var outline=new PathGeometry([new PathFigure(points[0],[new PolyLineSegment(points.Skip(1),true)],true)]);outline.Freeze();var hub=new EllipseGeometry(new Point(10,10),2.5,2.5);hub.Freeze();
            list.Add((outline,false,true,null,1,""));list.Add((hub,false,true,null,1,""));
        }
        else foreach(var part in Data[name].Split(';'))
        {
            var f=part.Split('|');double[] n=f[0][0]=='P'?[]:f[0][1..].Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(v=>double.Parse(v,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Geometry geometry=f[0][0] switch{'C'=>new EllipseGeometry(new Point(n[0],n[1]),n[2],n[2]),'R'=>new RectangleGeometry(new Rect(n[0],n[1],n[2],n[3]),n[4],n[4]),_=>PathGeometry.CreateFromGeometry(Geometry.Parse(f[0][1..]))};geometry.Freeze();
            list.Add((geometry,f[1]=="1",f[2]=="1"&&!(f[1]=="1"&&f[0][0]!='P'),f[3].Length>0?double.Parse(f[3],System.Globalization.CultureInfo.InvariantCulture):null,f[4].Length>0?double.Parse(f[4],System.Globalization.CultureInfo.InvariantCulture):1,f[5]));
        }
        return parsed[name]=[..list];
    }
    // brush: a palette key, or null to follow the surrounding text color (so icons turn ink on coral).
    internal static FrameworkElement Make(string name,double size=16,string? brush=null,double stroke=1.55)
    {
        var grid=new Grid{Width=20,Height=20};
        foreach(var (shape,fill,line,width,opacity,dash) in Parts(name))
        {
            var path=new System.Windows.Shapes.Path{Data=shape,StrokeThickness=width??stroke,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Opacity=opacity};
            if(dash.Length>0)path.StrokeDashArray=new(dash.Split(' ').Select(v=>double.Parse(v,System.Globalization.CultureInfo.InvariantCulture)/path.StrokeThickness));
            foreach(var (enabled,property) in new[]{(line,Shape.StrokeProperty),(fill,Shape.FillProperty)})
            {
                if(!enabled)continue;
                if(brush is null)path.SetBinding(property,new Binding{Path=new PropertyPath(TextElement.ForegroundProperty),RelativeSource=RelativeSource.Self});else path.SetResourceReference(property,brush);
            }
            grid.Children.Add(path);
        }
        return new Viewbox{Width=size,Height=size,Child=grid,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
    }
}
