namespace Nashi.Core;
/// <summary>
/// Local background removal for solid and near-solid backgrounds (#617): product shots on white,
/// logos on a colour, screenshots, scans. The dominant colour along the image border is the
/// background; only pixels of that colour connected to the border become transparent, so the same
/// colour inside the subject (white eyes, a white logo letter) stays. No model, upload or download:
/// the result is the same on every machine. Not a general AI subject segmentation.
/// </summary>
public static class BackgroundRemoval
{
    public const string NoSolidBackground="This image has no solid background to remove. It works best on product shots, logos and scans on a plain color.";
    /// <param name="Tolerance">How far a colour may differ from the background, 0–100 (percent of the RGB range).</param>
    /// <param name="Feather">Width in pixels of the soft edge around the subject, 0–10.</param>
    /// <param name="Fill">A colour that replaces the background (a white product background, a passport photo
    /// colour); null keeps it transparent. Soft edges blend into it, so any output format can be used.</param>
    public sealed record Options(int Tolerance=12,int Feather=1,(byte R,byte G,byte B)? Fill=null)
    {
        public Options Clamped=>this with{Tolerance=Math.Clamp(Tolerance,0,100),Feather=Math.Clamp(Feather,0,10)};
    }
    /// <summary>"#RRGGBB" or "RRGGBB" (also "#RGB"); null when it is not a colour.</summary>
    public static (byte R,byte G,byte B)? ParseColor(string? text)
    {
        var hex=(text??"").Trim().TrimStart('#');
        if(hex.Length==3)hex=string.Concat(hex.Select(c=>new string(c,2)));
        if(hex.Length!=6||!int.TryParse(hex,System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out var value))return null;
        return ((byte)(value>>16),(byte)(value>>8),(byte)value);
    }
    /// <summary>Blends every pixel over a solid colour and makes the image opaque.</summary>
    public static void Flatten(byte[] bgra,(byte R,byte G,byte B) fill)
    {
        for(var i=0;i<bgra.Length;i+=4)
        {
            var alpha=bgra[i+3];if(alpha==255)continue;
            bgra[i]=(byte)((bgra[i]*alpha+fill.B*(255-alpha)+127)/255);bgra[i+1]=(byte)((bgra[i+1]*alpha+fill.G*(255-alpha)+127)/255);bgra[i+2]=(byte)((bgra[i+2]*alpha+fill.R*(255-alpha)+127)/255);bgra[i+3]=255;
        }
    }
    public sealed record Result(int Removed,byte Red,byte Green,byte Blue);

    /// <summary>The background colour, or null when the border has no dominant colour.</summary>
    public static (byte R,byte G,byte B)? Background(byte[] bgra,int width,int height)
    {
        Validate(bgra,width,height);
        // Border pixels vote in 32-level buckets; one bucket must hold at least 40% of the opaque border.
        var votes=new Dictionary<int,(int Count,long B,long G,long R)>();var opaque=0;
        foreach(var index in Border(width,height))
        {
            var i=index*4;if(bgra[i+3]<16)continue;opaque++;
            var key=(bgra[i]>>3)<<10|(bgra[i+1]>>3)<<5|bgra[i+2]>>3;
            votes.TryGetValue(key,out var v);votes[key]=(v.Count+1,v.B+bgra[i],v.G+bgra[i+1],v.R+bgra[i+2]);
        }
        if(opaque==0)return null;
        var best=votes.Values.MaxBy(v=>v.Count);
        // Neighbouring buckets of a gradient or JPEG noise still belong to the same background.
        var near=votes.Where(v=>Math.Abs((v.Key>>10)-((int)(best.B/best.Count)>>3))<=1&&Math.Abs((v.Key>>5&31)-((int)(best.G/best.Count)>>3))<=1&&Math.Abs((v.Key&31)-((int)(best.R/best.Count)>>3))<=1).Select(v=>v.Value).ToArray();
        var count=near.Sum(v=>v.Count);
        if(count<opaque*.4)return null;
        return ((byte)(near.Sum(v=>v.R)/count),(byte)(near.Sum(v=>v.G)/count),(byte)(near.Sum(v=>v.B)/count));
    }

    /// <summary>Makes the border-connected background transparent in place (straight BGRA).</summary>
    public static Result Remove(byte[] bgra,int width,int height,Options options,CancellationToken token=default)
    {
        options=options.Clamped;
        var background=Background(bgra,width,height)??throw new InvalidDataException(NoSolidBackground);
        var (r,g,b)=background;// Percent of the RGB range (√3·255); 0 keeps only the exact colour.
        var limit=Math.Max(.5,options.Tolerance*4.4167);
        double Distance(int i){double db=bgra[i]-b,dg=bgra[i+1]-g,dr=bgra[i+2]-r;return Math.Sqrt(db*db+dg*dg+dr*dr);}
        bool Matches(int pixel){var i=pixel*4;return bgra[i+3]<16||Distance(i)<=limit;}
        var count=width*height;var removed=new bool[count];var queue=new Queue<int>();
        foreach(var pixel in Border(width,height))if(!removed[pixel]&&Matches(pixel)){removed[pixel]=true;queue.Enqueue(pixel);}
        var steps=0;
        while(queue.TryDequeue(out var pixel))
        {
            if((++steps&0xFFFF)==0)token.ThrowIfCancellationRequested();
            var x=pixel%width;var y=pixel/width;
            void Visit(int next){if(!removed[next]&&Matches(next)){removed[next]=true;queue.Enqueue(next);}}
            if(x>0)Visit(pixel-1);if(x<width-1)Visit(pixel+1);if(y>0)Visit(pixel-width);if(y<height-1)Visit(pixel+width);
        }
        token.ThrowIfCancellationRequested();
        var total=0;
        // A soft edge: subject pixels next to the removed area that are close to the background colour
        // become partly transparent (anti-aliased outlines), fading with distance from the background.
        if(options.Feather>0)
        {
            var distance=new int[count];Array.Fill(distance,int.MaxValue);var edge=new Queue<int>();
            for(var p=0;p<count;p++)if(removed[p]){distance[p]=0;edge.Enqueue(p);}
            while(edge.TryDequeue(out var p))
            {
                var d=distance[p]+1;if(d>options.Feather)continue;var x=p%width;var y=p/width;
                void Reach(int n){if(distance[n]>d){distance[n]=d;edge.Enqueue(n);}}
                if(x>0)Reach(p-1);if(x<width-1)Reach(p+1);if(y>0)Reach(p-width);if(y<height-1)Reach(p+width);
            }
            var soft=limit*2.5+24;
            for(var p=0;p<count;p++)
            {
                if(removed[p]||distance[p]==int.MaxValue)continue;
                var i=p*4;var colour=Distance(i);if(colour>=soft)continue;
                var keep=Math.Clamp((colour-limit)/(soft-limit),0,1);keep=Math.Max(keep,(distance[p]-1)/(double)options.Feather);
                bgra[i+3]=(byte)Math.Round(bgra[i+3]*keep);
            }
        }
        for(var p=0;p<count;p++)if(removed[p]){var i=p*4;bgra[i]=bgra[i+1]=bgra[i+2]=bgra[i+3]=0;total++;}
        if(options.Fill is {} fill)Flatten(bgra,fill);
        return new(total,r,g,b);
    }

    private static IEnumerable<int> Border(int width,int height)
    {
        for(var x=0;x<width;x++){yield return x;if(height>1)yield return (height-1)*width+x;}
        for(var y=1;y<height-1;y++){yield return y*width;if(width>1)yield return y*width+width-1;}
    }
    private static void Validate(byte[] bgra,int width,int height)
    {
        if(width<1||height<1||(long)width*height>100_000_000||bgra.Length!=(long)width*height*4)throw new ArgumentException("Choose an image between 1 pixel and 100 MP.");
    }
}
