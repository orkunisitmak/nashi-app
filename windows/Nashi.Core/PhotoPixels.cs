using System.Buffers;
namespace Nashi.Core;

// The photo adjustments as a few fast passes over straight BGRA pixels, on all cores. Exposure and white balance work
// in linear light, the tones through one monotonic curve per channel, then color; local contrast and detail work on
// luminance, then blur, vignette and grain. The order and the meaning of each control follow the macOS editor
// (Core Image). The live preview and the saved photo run the same code: `scale` is the preview's size against the
// original, so radii given in pixels shrink with it and both look alike.
public static class PhotoPixels
{
    private static readonly ArrayPool<float> pool=ArrayPool<float>.Create(1<<27,8);
    private static readonly float[] toLinear=Enumerable.Range(0,256).Select(i=>(float)Linear(i/255d)).ToArray();
    private const int Steps=4096;
    private static readonly float[] toEncoded=Enumerable.Range(0,Steps+1).Select(i=>(float)Encoded(i/(double)Steps)).ToArray();
    private static double Linear(double c)=>c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);
    private static double Encoded(double c)=>c<=.0031308?c*12.92:1.055*Math.Pow(c,1/2.4)-.055;
    public static byte[] Adjust(byte[] bgra,int width,int height,PhotoAdjustments o,double scale=1){var output=new byte[bgra.Length];Adjust(bgra,output,width,height,o,scale);return output;}
    public static void Adjust(byte[] source,byte[] target,int width,int height,PhotoAdjustments o,double scale=1)
    {
        if(width<1||height<1||source.Length<(long)width*height*4||target.Length<(long)width*height*4)throw new ArgumentException("Choose a nonempty image.");
        scale=Math.Clamp(scale,1e-3,1);var count=width*height;
        // Light and color: white balance and exposure as gains in linear light, then one tone curve, saturation and hue.
        var t=Math.Clamp(o.Temperature,-1,1);var u=Math.Clamp(o.Tint,-1,1);double gr=1+.28*t+.08*u,gg=1-.2*u,gb=1-.28*t+.08*u;var exposure=Math.Pow(2,Math.Clamp(o.Exposure,-10,10))/(.2126*gr+.7152*gg+.0722*gb);
        float kr=(float)(gr*exposure),kg=(float)(gg*exposure),kb=(float)(gb*exposure);var curve=Curve(o);var ranges=Ranges(o);var hue=o.Hue%360!=0?Hue(o.Hue):null;
        var saturation=(float)(Math.Max(0,o.Saturation)*(1+Math.Clamp(o.Dehaze,-1,1)*.12));var vibrance=(float)Math.Clamp(o.Vibrance,-1,1);
        Rows(height,count,y=>
        {
            for(int x=0,i=y*width*4;x<width;x++,i+=4)
            {
                float r=Tone(curve,Back(toLinear[source[i+2]]*kr)),g=Tone(curve,Back(toLinear[source[i+1]]*kg)),b=Tone(curve,Back(toLinear[source[i]]*kb));
                if(ranges is not null)
                {
                    // Highlights, shadows, whites and blacks move the lightness and keep the color: the pixel scales with its
                    // luminance, and one that would leave the gamut is pulled toward gray instead of changing its hue.
                    var luma=.2126f*r+.7152f*g+.0722f*b;var goal=Tone(ranges,luma);var k=luma>1e-4f?goal/luma:0;r=luma>1e-4f?r*k:goal;g=luma>1e-4f?g*k:goal;b=luma>1e-4f?b*k:goal;
                    var top=Math.Max(r,Math.Max(g,b));if(top>1){var fit=(1-goal)/Math.Max(1e-4f,top-goal);r=goal+(r-goal)*fit;g=goal+(g-goal)*fit;b=goal+(b-goal)*fit;}
                }
                if(saturation!=1||vibrance!=0)
                {
                    // Vibrance lifts the quiet colors most and leaves the strong ones be (as CIVibrance does).
                    var luma=.2126f*r+.7152f*g+.0722f*b;var chroma=Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b));var quiet=1-Math.Clamp(chroma,0,1);var k=saturation*(1+vibrance*quiet*quiet);
                    r=luma+(r-luma)*k;g=luma+(g-luma)*k;b=luma+(b-luma)*k;
                }
                if(hue is {} m){var hr=m[0]*r+m[1]*g+m[2]*b;var hg=m[3]*r+m[4]*g+m[5]*b;var hb=m[6]*r+m[7]*g+m[8]*b;r=hr;g=hg;b=hb;}
                target[i]=Byte(b);target[i+1]=Byte(g);target[i+2]=Byte(r);target[i+3]=source[i+3];
            }
        });
        Detail(target,width,height,o,scale);
        if(o.Blur>0)Blur(target,width,height,Math.Min(100,o.Blur)*scale,true);
        if(o.Vignette>0||o.Grain>0)Finish(target,width,height,Math.Min(2,o.Vignette),Math.Min(1,o.Grain)*Math.Sqrt(scale));
    }
    private static float Back(float linear)=>toEncoded[(int)(Math.Clamp(linear,0,1)*Steps+.5f)];
    private static byte Byte(float value)=>(byte)(Math.Clamp(value,0,1)*255+.5f);
    private static float Tone(float[] curve,float value){var at=Math.Clamp(value,0,1)*(curve.Length-1);var low=(int)at;if(low>=curve.Length-1)return curve[^1];var share=at-low;return curve[low]+(curve[low+1]-curve[low])*share;}
    // One curve per channel for brightness, contrast, gamma, dehaze and fade; never folding back on itself.
    private static float[] Curve(PhotoAdjustments o)
    {
        var curve=new float[1024];double brightness=Math.Clamp(o.Brightness,-1,1),contrast=Math.Max(0,o.Contrast),gamma=Math.Clamp(o.Gamma,.1,10),dehaze=Math.Clamp(o.Dehaze,-1,1),fade=Math.Clamp(o.Fade,0,1);
        for(var n=0;n<curve.Length;n++)
        {
            var y=n/(curve.Length-1d);
            // Brightness lifts or lowers the middle and leaves black and white where they are.
            y+=brightness*.3*4*y*(1-y);y=(y-.5)*contrast+.5;y=Math.Pow(Math.Clamp(y,0,1),1/gamma);y=(y-.5)*(1+dehaze*.65)+.5-dehaze*.045;y=y*(1-fade*.4)+fade*.15;
            curve[n]=(float)Math.Clamp(y,0,1);if(n>0&&curve[n]<curve[n-1])curve[n]=curve[n-1];
        }
        return curve;
    }
    // The four tonal ranges on luminance (the macOS tone curve's anchors); none when all four are at zero.
    private static float[]? Ranges(PhotoAdjustments o)
    {
        if(o.Highlights==0&&o.Shadows==0&&o.Whites==0&&o.Blacks==0)return null;var curve=new float[1024];
        for(var n=0;n<curve.Length;n++)
        {
            var y=n/(curve.Length-1d);y+=o.Blacks*.25*Math.Pow(1-y,4)+o.Whites*.25*Math.Pow(y,4)+o.Shadows*.5*4*y*Math.Pow(1-y,3)+o.Highlights*.5*4*Math.Pow(y,3)*(1-y);
            curve[n]=(float)Math.Clamp(y,0,1);if(n>0&&curve[n]<curve[n-1])curve[n]=curve[n-1];
        }
        return curve;
    }
    // Turns hues around the color wheel while keeping each pixel's lightness (the SVG/CSS hue-rotate matrix).
    private static float[] Hue(double degrees)
    {
        var a=Math.Cos(degrees*Math.PI/180);var b=Math.Sin(degrees*Math.PI/180);
        return [(float)(.213+a*.787-b*.213),(float)(.715-a*.715-b*.715),(float)(.072-a*.072+b*.928),(float)(.213-a*.213+b*.143),(float)(.715+a*.285+b*.140),(float)(.072-a*.072-b*.283),(float)(.213-a*.213-b*.787),(float)(.715-a*.715+b*.715),(float)(.072+a*.928+b*.072)];
    }
    // Clarity (midtone contrast over a wide radius), texture, sharpening and noise reduction, one after another on luminance.
    private static void Detail(byte[] pixels,int width,int height,PhotoAdjustments o,double scale)
    {
        var clarity=Math.Clamp(o.Clarity,-1,1);var texture=Math.Clamp(o.Texture,-1,1);var sharpen=Math.Clamp(o.Sharpen,0,2);var noise=Math.Clamp(o.NoiseReduction,0,1);
        if(clarity==0&&texture==0&&sharpen==0&&noise==0)return;var count=width*height;var luma=pool.Rent(count);var blurred=pool.Rent(count);var spare=pool.Rent(count);
        try
        {
            Rows(height,count,y=>{for(int x=0,i=y*width;x<width;x++,i++){var p=i*4;luma[i]=(.0722f*pixels[p]+.7152f*pixels[p+1]+.2126f*pixels[p+2])/255;}});
            void Pass(double sigma,Func<float,float,float> step){Plane(luma,blurred,spare,width,height,sigma);Rows(height,count,y=>{for(int x=0,i=y*width;x<width;x++,i++)luma[i]=step(luma[i],blurred[i]);});}
            if(noise>0)Pass(Math.Max(.7,(1+1.5*noise)*scale),(v,soft)=>{var d=(v-soft)/.04f;return v+(soft-v)*(float)noise*MathF.Exp(-d*d);});
            if(clarity!=0)Pass(Math.Max(1.5,.008*Math.Min(width,height)),(v,soft)=>v+(v-soft)*(float)clarity*1.1f*4*v*(1-v));
            if(texture!=0)Pass(Math.Max(.7,1.5*scale),(v,soft)=>texture>0?v+(v-soft)*(float)texture*1.2f:v+(soft-v)*(float)-texture*.9f);
            if(sharpen>0)Pass(Math.Max(.6,scale),(v,soft)=>v+(v-soft)*(float)sharpen*.6f);
            // Each pixel moves by its change in luminance, so color stays as it was.
            Rows(height,count,y=>{for(int x=0,i=y*width;x<width;x++,i++){var p=i*4;var shift=luma[i]*255-(.0722f*pixels[p]+.7152f*pixels[p+1]+.2126f*pixels[p+2]);for(var c=0;c<3;c++)pixels[p+c]=(byte)Math.Clamp(pixels[p+c]+shift+.5f,0,255);}});
        }
        finally{pool.Return(luma);pool.Return(blurred);pool.Return(spare);}
    }
    // Vignette darkens the corners on a soft curve; grain is a fixed fine noise (the same each time, so a preview never flickers).
    private static void Finish(byte[] pixels,int width,int height,double vignette,double grain)
    {
        double cx=width/2d,cy=height/2d;var strength=Math.Min(.95,vignette*.5);
        Rows(height,width*height,y=>
        {
            var dy=(y+.5-cy)/cy;
            for(int x=0,p=y*width*4;x<width;x++,p+=4)
            {
                var dx=(x+.5-cx)/cx;var reach=Math.Sqrt((dx*dx+dy*dy)/2);var edge=Math.Clamp((reach-.3)/.7,0,1);edge=edge*edge*(3-2*edge);var keep=(float)(1-strength*edge*edge);
                var noise=0f;if(grain>0){uint h=(uint)x*0x9E3779B1u^(uint)y*0x85EBCA77u;h^=h>>15;h*=0x2C1B3C6Du;h^=h>>12;h*=0x297A2D39u;h^=h>>15;noise=((h&0xFFFF)+(h>>16))/65535f-1;}
                var luma=(.0722f*pixels[p]+.7152f*pixels[p+1]+.2126f*pixels[p+2])/255;var grainy=(float)(grain*.12*(.6+1.6*luma*(1-luma)))*noise*255;
                for(var c=0;c<3;c++)pixels[p+c]=(byte)Math.Clamp(pixels[p+c]*keep+grainy+.5f,0,255);
            }
        });
    }
    // Three box blurs in a row come within a few percent of a Gaussian of this standard deviation. Lines run on all cores.
    public static void Blur(byte[] pixels,int width,int height,double sigma,bool straight=false)
    {
        if(sigma<.5||width<1||height<1)return;var (low,high,lower)=Boxes(sigma);var buffer=bytes.Rent(width*height*4);
        try
        {
            // Straight colors are premultiplied first, so clear pixels cannot bleed their hidden color into their neighbors.
            var opaque=!straight||Opaque(pixels);if(!opaque)Premultiply(pixels,true);
            for(var pass=0;pass<3;pass++){var radius=((pass<lower?low:high)-1)/2;Box(pixels,buffer,width,height,radius,4,width*4);Box(buffer,pixels,height,width,radius,width*4,4);}
            if(!opaque)Premultiply(pixels,false);
        }
        finally{bytes.Return(buffer);}
    }
    // The working copy of a blur. Window glass, lenses and previews blur pictures of a few megabytes again and again;
    // larger ones (a full photo being saved) get a fresh array that is not kept.
    private static readonly ArrayPool<byte> bytes=ArrayPool<byte>.Create(1<<23,4);
    private static (int Low,int High,int Lower) Boxes(double sigma){var ideal=Math.Sqrt(4*sigma*sigma+1);var low=(int)Math.Floor(ideal);if(low%2==0)low--;var high=low+2;return(low,high,(int)Math.Round((12*sigma*sigma-3d*low*low-12*low-9)/(-4d*low-4)));}
    private static bool Opaque(byte[] pixels){for(var i=3;i<pixels.Length;i+=4)if(pixels[i]!=255)return false;return true;}
    private static void Premultiply(byte[] pixels,bool forward)
    {
        for(var i=0;i<pixels.Length;i+=4){var a=pixels[i+3];if(a==255)continue;for(var c=0;c<3;c++)pixels[i+c]=forward?(byte)((pixels[i+c]*a+127)/255):a==0?(byte)0:(byte)Math.Min(255,(pixels[i+c]*255+a/2)/a);}
    }
    // One box pass along lines of `count` pixels `step` bytes apart; `line` bytes separate the lines. Edges repeat outward.
    // A pixel's four channels sit side by side, so one walk along the line carries all four sums (a column is read once, not four times).
    private static void Box(byte[] source,byte[] target,int count,int lines,int radius,int step,int line)
    {
        var span=2*radius+1;
        void Line(int l)
        {
            var start=l*line;int b=0,g=0,r=0,a=0;
            for(var k=-radius;k<=radius;k++){var p=start+Math.Clamp(k,0,count-1)*step;b+=source[p];g+=source[p+1];r+=source[p+2];a+=source[p+3];}
            for(var i=0;i<count;i++)
            {
                var o=start+i*step;target[o]=(byte)((b+radius)/span);target[o+1]=(byte)((g+radius)/span);target[o+2]=(byte)((r+radius)/span);target[o+3]=(byte)((a+radius)/span);
                var n=start+Math.Min(i+radius+1,count-1)*step;var d=start+Math.Max(i-radius,0)*step;
                b+=source[n]-source[d];g+=source[n+1]-source[d+1];r+=source[n+2]-source[d+2];a+=source[n+3]-source[d+3];
            }
        }
        if((long)lines*count>=65_536)Parallel.For(0,lines,Line);else for(var l=0;l<lines;l++)Line(l);
    }
    // The same Gaussian approximation on one plane of floats (luminance).
    private static void Plane(float[] source,float[] target,float[] spare,int width,int height,double sigma)
    {
        Array.Copy(source,target,width*height);if(sigma<.5)return;var (low,high,lower)=Boxes(sigma);
        for(var pass=0;pass<3;pass++){var radius=((pass<lower?low:high)-1)/2;Box(target,spare,width,height,radius,1,width);Box(spare,target,height,width,radius,width,1);}
    }
    private static void Box(float[] source,float[] target,int count,int lines,int radius,int step,int line)
    {
        var span=1f/(2*radius+1);
        void Line(int l)
        {
            var start=l*line;var sum=0f;for(var k=-radius;k<=radius;k++)sum+=source[start+Math.Clamp(k,0,count-1)*step];
            for(var i=0;i<count;i++){target[start+i*step]=sum*span;sum+=source[start+Math.Min(i+radius+1,count-1)*step]-source[start+Math.Max(i-radius,0)*step];}
        }
        if((long)lines*count>=65_536)Parallel.For(0,lines,Line);else for(var l=0;l<lines;l++)Line(l);
    }
    private static void Rows(int rows,int pixels,Action<int> row){if(pixels>=40_000)Parallel.For(0,rows,row);else for(var y=0;y<rows;y++)row(y);}
}
