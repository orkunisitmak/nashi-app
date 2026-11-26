using System.Globalization;
using System.Text;

namespace Nashi.Core;

/// Traces BGRA pixels into a black-and-white vector SVG (laser engraving/cutting, plotters). Mirrors Sources/NashiCore/ImageVectorizer.swift.
/// shortcut: fixed 50% luminance threshold and pixel-edge tracing smoothed by Douglas-Peucker, no Bezier fitting; add a threshold option or curve fitting if users need smoother photo traces.
public static class VectorTrace
{
    public static string Svg(byte[] bgra,int width,int height,double dpi=96,CancellationToken token=default)
    {
        if(width<=0||height<=0||bgra.Length<width*height*4)throw new InvalidDataException("This image could not be vectorized.");
        var black=new bool[width*height];
        for(var i=0;i<black.Length;i++)
        {
            // Transparent areas count as white so logos on transparent backgrounds trace cleanly.
            double a=bgra[i*4+3]/255d;
            double luma=(0.114*bgra[i*4]+0.587*bgra[i*4+1]+0.299*bgra[i*4+2])*a+255*(1-a);
            black[i]=luma<128;
        }
        bool IsBlack(int x,int y)=>x>=0&&y>=0&&x<width&&y<height&&black[y*width+x];
        var stride=width+1;var next=new Dictionary<long,List<long>>();
        void Add(int ax,int ay,int bx,int by){long key=(long)ay*stride+ax;if(!next.TryGetValue(key,out var list))next[key]=list=new List<long>(2);list.Add((long)by*stride+bx);}
        for(var y=0;y<height;y++)
        {
            token.ThrowIfCancellationRequested();
            for(var x=0;x<width;x++)
            {
                if(!black[y*width+x])continue;
                if(!IsBlack(x,y-1))Add(x,y,x+1,y);
                if(!IsBlack(x+1,y))Add(x+1,y,x+1,y+1);
                if(!IsBlack(x,y+1))Add(x+1,y+1,x,y+1);
                if(!IsBlack(x-1,y))Add(x,y+1,x,y);
            }
        }
        // Every vertex has equal in/out degree, so each walk closes. Even-odd fill makes the loop split at touching corners irrelevant.
        var path=new StringBuilder();
        while(next.Count>0)
        {
            token.ThrowIfCancellationRequested();
            long start=next.Keys.First(),v=start;var loop=new List<(double X,double Y)>();
            do
            {
                loop.Add((v%stride,v/stride));
                var outs=next[v];var n=outs[^1];outs.RemoveAt(outs.Count-1);if(outs.Count==0)next.Remove(v);v=n;
            }while(v!=start);
            var points=Simplify(loop,0.75);
            if(points.Count<3)continue;
            path.Append('M').Append(string.Join("L",points.Select(p=>Fmt(p.X)+" "+Fmt(p.Y)))).Append('Z');
        }
        string Mm(int px)=>Fmt(px/dpi*25.4);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            +$"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Mm(width)}mm\" height=\"{Mm(height)}mm\" viewBox=\"0 0 {width} {height}\">\n"
            +$"<path fill=\"#000000\" fill-rule=\"evenodd\" d=\"{path}\"/>\n</svg>\n";
    }

    static string Fmt(double v)=>Math.Round(v,2).ToString("0.##",CultureInfo.InvariantCulture);

    /// Douglas-Peucker on a closed loop: straightens pixel staircases into diagonal lines while keeping corners.
    public static List<(double X,double Y)> Simplify(List<(double X,double Y)> loop,double epsilon)
    {
        if(loop.Count<=4)return loop;
        var far=1;for(var i=2;i<loop.Count;i++)if(Dist2(loop[i],loop[0])>Dist2(loop[far],loop[0]))far=i;
        var a=Dp(loop.GetRange(0,far+1),epsilon);var b=Dp([..loop.GetRange(far,loop.Count-far),loop[0]],epsilon);
        a.RemoveAt(a.Count-1);b.RemoveAt(b.Count-1);a.AddRange(b);return a;
    }

    static List<(double X,double Y)> Dp(List<(double X,double Y)> pts,double eps)
    {
        if(pts.Count<=2)return pts;
        var a=pts[0];var b=pts[^1];double dx=b.X-a.X,dy=b.Y-a.Y,len=Math.Sqrt(dx*dx+dy*dy);
        double best=0;var index=0;
        for(var i=1;i<pts.Count-1;i++)
        {
            var d=len==0?Math.Sqrt(Dist2(pts[i],a)):Math.Abs(dy*pts[i].X-dx*pts[i].Y+b.X*a.Y-b.Y*a.X)/len;
            if(d>best){best=d;index=i;}
        }
        if(best<=eps)return [a,b];
        var left=Dp(pts.GetRange(0,index+1),eps);left.RemoveAt(left.Count-1);left.AddRange(Dp(pts.GetRange(index,pts.Count-index),eps));return left;
    }

    static double Dist2((double X,double Y) p,(double X,double Y) q)=>(p.X-q.X)*(p.X-q.X)+(p.Y-q.Y)*(p.Y-q.Y);
}
