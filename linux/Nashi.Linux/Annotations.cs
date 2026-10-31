using ImageMagick;
using ImageMagick.Drawing;
namespace Nashi.Linux;

/// One mark in image pixel coordinates. Points holds the stroke for "marker" and "freehand";
/// the other kinds use (X1,Y1)→(X2,Y2). Color is "#RRGGBB". Width is the stroke width in pixels.
public sealed record Mark(string Kind,double X1,double Y1,double X2,double Y2,string Color="#FF5200",double Width=6,string Text="",double FontSize=48,IReadOnlyList<(double X,double Y)>? Points=null,byte[]? Picture=null);
public sealed record Annotation(IReadOnlyList<Mark> Marks);

public static class Annotations
{
    public static readonly string[] Kinds=["marker","rectangle","arrow","line","ellipse","text","number","callout","magnifier","image","freehand"];

    public static void Apply(MagickImage image,Annotation annotation)
    {
        if(annotation.Marks==null||annotation.Marks.Count==0)
            throw new ArgumentException("Draw at least one mark.");

        // Magnifiers show the picture as it was before any marks, as the spec asks.
        using var original = (MagickImage)image.Clone();
        foreach(var mark in annotation.Marks)
        {
            string kind = mark.Kind;
            // Validate color format
            if(string.IsNullOrWhiteSpace(mark.Color) || !IsValidHexColor(mark.Color))
                throw new ArgumentException("Choose a color like #FF5200.");
            var baseColor = new MagickColor(mark.Color);
            double left = Math.Min(mark.X1, mark.X2);
            double top = Math.Min(mark.Y1, mark.Y2);
            double w = Math.Abs(mark.X2 - mark.X1);
            double h = Math.Abs(mark.Y2 - mark.Y1);

            switch(kind)
            {
                case "marker":
                    {
                        if(mark.Points==null) break;
                        var semiColor = new MagickColor(baseColor.R, baseColor.G, baseColor.B, 140);
                        var points = mark.Points.Select(p=>new PointD(p.X,p.Y));
                        new Drawables()
                           .StrokeColor(semiColor)
                           .StrokeWidth(mark.Width*4)
                           .StrokeLineCap(LineCap.Round)
                           .StrokeLineJoin(LineJoin.Round)
                           .FillColor(MagickColors.Transparent)
                           .Stroke(points.ToArray(),mark.Kind=="marker"?semiColor:baseColor,mark.Kind=="marker"?mark.Width*4:mark.Width)
                           .Draw(image);
                        break;
                    }
                case "freehand":
                    {
                        if(mark.Points==null) break;
                        var points = mark.Points.Select(p=>new PointD(p.X,p.Y));
                        new Drawables()
                           .StrokeColor(baseColor)
                           .StrokeWidth(mark.Width)
                           .StrokeLineCap(LineCap.Round)
                           .StrokeLineJoin(LineJoin.Round)
                           .FillColor(MagickColors.Transparent)
                           .Stroke(points.ToArray(),baseColor,mark.Width)
                           .Draw(image);
                        break;
                    }
                case "line":
                    new Drawables()
                       .StrokeColor(baseColor)
                       .StrokeWidth(mark.Width)
                       .StrokeLineCap(LineCap.Round)
                       .Line(mark.X1, mark.Y1, mark.X2, mark.Y2)
                       .Draw(image);
                    break;
                case "arrow":
                    {
                        // base line
                        new Drawables()
                           .StrokeColor(baseColor)
                           .StrokeWidth(mark.Width)
                           .StrokeLineCap(LineCap.Round)
                           .Line(mark.X1, mark.Y1, mark.X2, mark.Y2)
                           .Draw(image);
                        // triangle head
                        double headLength = Math.Max(mark.Width*4,12);
                        double halfWidth = headLength*0.5;
                        double dx = mark.X2 - mark.X1;
                        double dy = mark.Y2 - mark.Y1;
                        double len = Math.Sqrt(dx*dx+dy*dy);
                        if(len==0) break;
                        double ux = dx/len;
                        double uy = dy/len;
                        double nx = -uy;
                        double ny = ux;
                        var p1 = new PointD(mark.X2, mark.Y2); // tip
                        var p2 = new PointD(mark.X2 - headLength*ux + halfWidth*nx, mark.Y2 - headLength*uy + halfWidth*ny);
                        var p3 = new PointD(mark.X2 - headLength*ux - halfWidth*nx, mark.Y2 - headLength*uy - halfWidth*ny);
                        new Drawables()
                           .FillColor(baseColor)
                           .StrokeColor(baseColor)
                           .Polygon(p1, p2, p3)
                           .Draw(image);
                        break;
                    }
                case "rectangle":
                    if(w<1||h<1) break;
                    new Drawables()
                       .StrokeColor(baseColor)
                       .StrokeWidth(mark.Width)
                       .FillColor(MagickColors.Transparent)
                       .Rectangle(left, top, left+w, top+h)
                       .Draw(image);
                    break;
                case "ellipse":
                    if(w<1||h<1) break;
                    new Drawables()
                       .StrokeColor(baseColor)
                       .StrokeWidth(mark.Width)
                       .FillColor(MagickColors.Transparent)
                       .Ellipse(left+w/2, top+h/2, w/2, h/2, 0, 360)
                       .Draw(image);
                    break;
                case "text":
                    if(mark.Text.Length==0) break;
                    new Drawables()
                       .FillColor(baseColor)
                       .Typeface(false).FontPointSize(mark.FontSize)
                       .Text(mark.X1, mark.Y1 + mark.FontSize, mark.Text)
                       .Draw(image);
                    break;
                case "number":
                    // circle
                    new Drawables()
                       .FillColor(baseColor)
                       .StrokeColor(MagickColors.Transparent)
                       .Ellipse(mark.X1, mark.Y1, mark.FontSize*0.6, mark.FontSize*0.6, 0, 360)
                       .Draw(image);
                    // number text
                    if(!string.IsNullOrEmpty(mark.Text))
                    {
                        double txtX = mark.X1 - mark.FontSize*0.2*mark.Text.Length;
                        double txtY = mark.Y1 + mark.FontSize*0.25;
                        new Drawables()
                           .FillColor(new MagickColor("#FFFFFF"))
                           .Typeface(true).FontPointSize(mark.FontSize*0.7)
                           .Text(txtX, txtY, mark.Text)
                           .Draw(image);
                    }
                    break;
                case "callout":
                    {
                    if(w<1||h<1) break;
                    double radius = mark.FontSize*0.4;
                    new Drawables()
                       .FillColor(baseColor)
                       .StrokeColor(MagickColors.Transparent)
                       .RoundRectangle(left, top, left+w, top+h, radius, radius)
                       .Draw(image);
                    // text
                    double txtCX = left + mark.FontSize*0.4;
                    double txtCY = top + mark.FontSize*1.1;
                    if(mark.Text.Length>0)
                    new Drawables()
                       .FillColor(new MagickColor("#FFFFFF"))
                       .Typeface(false).FontPointSize(mark.FontSize)
                       .Text(txtCX, txtCY, mark.Text)
                       .Draw(image);
                    break;
                    }
                case "magnifier":
                    {
                        if(w<1||h<1) break;
                        // The dragged spot, kept inside the picture, at twice its size in an oval lens centered on it.
                        var x=(int)Math.Clamp(Math.Round(left),0,original.Width-1);var y=(int)Math.Clamp(Math.Round(top),0,original.Height-1);
                        var cw=(uint)Math.Clamp(Math.Round(w),1,original.Width-x);var ch=(uint)Math.Clamp(Math.Round(h),1,original.Height-y);
                        using var zoom=(MagickImage)original.Clone();zoom.Crop(new MagickGeometry(x,y,cw,ch));zoom.ResetPage();
                        zoom.Resize(new MagickGeometry(cw*2,ch*2){IgnoreAspectRatio=true});zoom.Alpha(AlphaAction.Set);
                        using(var lens=new MagickImage(MagickColors.Transparent,zoom.Width,zoom.Height))
                        {
                            new Drawables().FillColor(MagickColors.White).StrokeColor(MagickColors.Transparent).Ellipse(zoom.Width/2d,zoom.Height/2d,zoom.Width/2d,zoom.Height/2d,0,360).Draw(lens);
                            zoom.Composite(lens,CompositeOperator.DstIn);
                        }
                        double cx=x+cw/2d,cy=y+ch/2d;
                        image.Composite(zoom,(int)Math.Round(cx-zoom.Width/2d),(int)Math.Round(cy-zoom.Height/2d),CompositeOperator.Over);
                        new Drawables()
                           .StrokeColor(baseColor)
                           .StrokeWidth(mark.Width)
                           .FillColor(MagickColors.Transparent)
                           .Ellipse(cx, cy, zoom.Width/2d, zoom.Height/2d, 0, 360)
                           .Draw(image);
                        break;
                    }
                case "image":
                    {
                    if(mark.Picture==null)
                        throw new ArgumentException("Choose an image to place.");
                    if(w<1||h<1) break;
                    using var pic = new MagickImage(mark.Picture);
                    var geom = new MagickGeometry((uint)w,(uint)h) { IgnoreAspectRatio = true };
                    pic.Resize(geom);
                    image.Composite(pic, (int)Math.Round(left), (int)Math.Round(top), CompositeOperator.Over);
                    break;
                    }
                default:
                    throw new ArgumentException("Unknown mark: "+kind);
            }
        }
    }

    private static readonly Lazy<string?> Regular=new(()=>FontFile("sans")),Bold=new(()=>FontFile("sans:bold"));
    /// UTF-8 text in the desktop's sans font (from fontconfig), so Turkish and other letters render.
    private static IDrawables<byte> Typeface(this IDrawables<byte> drawables,bool bold)
    {
        drawables=drawables.TextEncoding(System.Text.Encoding.UTF8);
        return (bold?Bold.Value:Regular.Value) is {} file?drawables.Font(file):drawables;
    }
    private static string? FontFile(string pattern)
    {
        try
        {
            if(Operations.Executable("fc-match") is not {} match)return null;
            var file=ProcessRunnerSync(match,["-f","%{file}",pattern]);return File.Exists(file)?file:null;
        }
        catch(Exception error)when(error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception){return null;}
    }
    private static string ProcessRunnerSync(string exe,string[] args)
    {
        var start=new System.Diagnostics.ProcessStartInfo(exe){RedirectStandardOutput=true,UseShellExecute=false};foreach(var a in args)start.ArgumentList.Add(a);
        using var process=System.Diagnostics.Process.Start(start)!;var output=process.StandardOutput.ReadToEnd();process.WaitForExit(5000);return output.Trim();
    }
    /// ImageMagick needs three points for a polyline: a click becomes a dot, two points a line.
    private static IDrawables<byte> Stroke(this IDrawables<byte> drawables,PointD[] points,MagickColor color,double width)=>points.Length switch
    {
        0=>drawables,
        1=>drawables.FillColor(color).StrokeColor(MagickColors.Transparent).Circle(points[0].X,points[0].Y,points[0].X+width/2,points[0].Y),
        2=>drawables.Line(points[0].X,points[0].Y,points[1].X,points[1].Y),
        _=>drawables.Polyline(points)
    };
    private static bool IsValidHexColor(string s)
    {
        if(string.IsNullOrEmpty(s) || s.Length!=7 || s[0]!='#') return false;
        return s.Skip(1).All(Uri.IsHexDigit);
    }
}
