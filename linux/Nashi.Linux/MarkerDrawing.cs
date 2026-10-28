using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
namespace Nashi.Linux;

// Nashi's highlighter uses multiply, not a translucent opaque-colored stroke.
// Draw directly on Avalonia's Skia canvas so preview and RenderTargetBitmap
// export use the same blend without allocating a full-photo intermediate layer.
internal sealed class MarkerDrawing(Point[] points,Color color,double thickness):ICustomDrawOperation
{
    public Rect Bounds{get;}=new Rect(new Point(points.Min(p=>p.X),points.Min(p=>p.Y)),new Point(points.Max(p=>p.X),points.Max(p=>p.Y))).Inflate(thickness/2+1);
    public bool HitTest(Point point)=>false;
    public bool Equals(ICustomDrawOperation? other)=>ReferenceEquals(this,other);
    public void Dispose(){}
    public void Render(ImmediateDrawingContext context)
    {
        var feature=context.TryGetFeature<ISkiaSharpApiLeaseFeature>()??throw new NotSupportedException("The marker requires the bundled Skia renderer.");
        using var lease=feature.Lease();using var path=new SKPath();path.MoveTo((float)points[0].X,(float)points[0].Y);foreach(var point in points.Skip(1))path.LineTo((float)point.X,(float)point.Y);
        using var paint=new SKPaint{Color=new SKColor(color.R,color.G,color.B,(byte)Math.Clamp(Math.Round(color.A*.35*lease.CurrentOpacity),0,255)),Style=SKPaintStyle.Stroke,StrokeWidth=(float)thickness,StrokeCap=SKStrokeCap.Round,StrokeJoin=SKStrokeJoin.Round,IsAntialias=true,BlendMode=SKBlendMode.Multiply};
        lease.SkCanvas.DrawPath(path,paint);
    }
}
