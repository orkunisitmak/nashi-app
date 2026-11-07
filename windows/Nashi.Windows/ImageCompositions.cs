using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
namespace Nashi.Windows;
public sealed record FrameOptions(int Padding=80,double CornerRadius=24,string Color="#FFFFFF",string Backdrop="solid",string GradientEnd="#FFD85C",string? BackgroundImage=null,double Blur=0,double Shadow=24,double ShadowOpacity=.25,int? Width=null,int? Height=null,double Scale=1,double X=.5,double Y=.5,double ShadowX=0,double ShadowY=0);
public sealed record CollageOptions(string Layout="grid",int Columns=2,int Width=2048,int Height=2048,int Spacing=12,int Padding=16,double CornerRadius=0,string Color="#FFFFFF",bool Fill=true,int[]? Order=null);
public static partial class ImageEngine
{
    internal static byte[] PngBytes(BitmapSource source){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();}
    internal static BitmapSource FromPng(byte[] png){using var stream=new MemoryStream(png);var bitmap=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);bitmap.Freeze();return bitmap;}
    private static void CheckCanvas(int width,int height){if(width<1||height<1||width>30000||height>30000||(long)width*height>100_000_000)throw new ArgumentException("Choose a canvas within 100 MP and 30,000 pixels.");}
    private static BitmapSource Render(int width,int height,Action<DrawingContext> draw)
    {
        CheckCanvas(width,height);
        var visual=new DrawingVisual();using(var context=visual.RenderOpen())draw(context);var result=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();return result;
    }
    internal static Brush ColorBrush(string color){try{return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));}catch(FormatException){throw new ArgumentException("Choose a hex background color, such as #FFFFFF or #00FFFFFF.");}}
    private static void Fitted(DrawingContext g,BitmapSource source,Rect area,bool fill,double radius=0)
    {
        var scale=fill?Math.Max(area.Width/source.PixelWidth,area.Height/source.PixelHeight):Math.Min(area.Width/source.PixelWidth,area.Height/source.PixelHeight);var w=source.PixelWidth*scale;var h=source.PixelHeight*scale;
        g.PushClip(new RectangleGeometry(area,radius,radius));g.DrawImage(source,new Rect(area.X+(area.Width-w)/2,area.Y+(area.Height-h)/2,w,h));g.Pop();
    }
    internal static BitmapSource Frame(BitmapSource image,FrameOptions o){var (drawing,w,h)=FrameDrawing(image,o);return Render(w,h,g=>g.DrawDrawing(drawing));}
    // The editor shows the same composition live, drawn by the graphics card while a value moves.
    internal static (DrawingImage Image,int Width,int Height) FrameLive(BitmapSource image,FrameOptions o){var (drawing,w,h)=FrameDrawing(image,o);var live=new DrawingImage(drawing);live.Freeze();return(live,w,h);}
    private static (Drawing Drawing,int Width,int Height) FrameDrawing(BitmapSource image,FrameOptions o)
    {
        if(o.Padding<0||o.Padding>4000||!new[]{o.CornerRadius,o.Blur,o.Shadow,o.ShadowOpacity,o.Scale,o.X,o.Y}.All(double.IsFinite)||o.Scale<=0||o.Scale>10)throw new ArgumentException("Choose valid background and image dimensions.");
        var w=o.Width??image.PixelWidth+2*o.Padding;var h=o.Height??image.PixelHeight+2*o.Padding;CheckCanvas(w,h);var group=new DrawingGroup();
        using(var g=group.Open())
        {
            var bounds=new Rect(0,0,w,h);Brush background=o.Backdrop=="gradient"?new LinearGradientBrush(((SolidColorBrush)ColorBrush(o.Color)).Color,((SolidColorBrush)ColorBrush(o.GradientEnd)).Color,45):ColorBrush(o.Color);g.DrawRectangle(background,null,bounds);
            if(o.Backdrop=="image"){if(o.BackgroundImage is null)throw new ArgumentException("Choose a background image.");var (cover,area)=Cover(o.BackgroundImage,o.Blur,w,h);g.DrawImage(cover,area);}
            var scale=Math.Min(Math.Max(1,w-2*o.Padding)/(double)image.PixelWidth,Math.Max(1,h-2*o.Padding)/(double)image.PixelHeight)*o.Scale;var iw=image.PixelWidth*scale;var ih=image.PixelHeight*scale;var rect=new Rect((w-iw)*Math.Clamp(o.X,0,1),(h-ih)*Math.Clamp(o.Y,0,1),iw,ih);
            if(o.Shadow>0&&o.ShadowOpacity>0){var (mask,step)=Shadow(rect,o,w,h);g.DrawImage(mask,new Rect(0,0,mask.PixelWidth*step,mask.PixelHeight*step));}
            Fitted(g,image,rect,true,o.CornerRadius);
        }
        group.ClipGeometry=new RectangleGeometry(new Rect(0,0,w,h));group.Freeze();return(group,w,h);
    }
    // A real soft shadow, as CSS draws one: the picture's rounded shape in black at the chosen strength, blurred
    // (the radius is two standard deviations) and moved by the offset. It is worked out small and stretched back.
    private static (BitmapSource Mask,double Step) Shadow(Rect shape,FrameOptions o,int width,int height)
    {
        var sigma=Math.Max(.5,o.Shadow/2);var step=Math.Max(1,sigma/5);int mw=Math.Max(1,(int)Math.Ceiling(width/step)),mh=Math.Max(1,(int)Math.Ceiling(height/step));
        double hw=shape.Width/step/2,hh=shape.Height/step/2,cx=(shape.X+o.ShadowX)/step+hw,cy=(shape.Y+o.ShadowY)/step+hh;var radius=Math.Clamp(o.CornerRadius/step,0,Math.Min(hw,hh));
        var alpha=255*Math.Clamp(o.ShadowOpacity,0,1);var pixels=new byte[mw*mh*4];
        for(var y=0;y<mh;y++)for(var x=0;x<mw;x++)
        {
            // How much of this cell the rounded rectangle covers (signed distance, half a cell of antialiasing).
            var qx=Math.Abs(x+.5-cx)-hw+radius;var qy=Math.Abs(y+.5-cy)-hh+radius;var outside=Math.Sqrt(Math.Max(qx,0)*Math.Max(qx,0)+Math.Max(qy,0)*Math.Max(qy,0))+Math.Min(Math.Max(qx,qy),0)-radius;
            var cover=Math.Clamp(.5-outside,0,1);if(cover>0)pixels[(y*mw+x)*4+3]=(byte)Math.Round(alpha*cover);
        }
        Behind.Blur(pixels,mw,mh,sigma/step);var mask=BitmapSource.Create(mw,mh,96,96,PixelFormats.Pbgra32,null,pixels,mw*4);mask.Freeze();return(mask,step);
    }
    // The picture behind, covering the canvas. Its blur is worked out on the picture itself (σ on the canvas is σ/scale
    // there) with three quick box passes, as fast at 40 px as at 2. While an editor is open, a copy of the picture at
    // most 2048 px (enough for its preview), a 512 px copy and the last blurred one are kept; ForgetCovers lets them go.
    private static (string Path,BitmapSource Image)? lastSource;private static (BitmapSource Source,double Sigma,BitmapSource Image)? lastBlurred;private static (BitmapSource Source,BitmapSource Copy)? lastThumb;private static readonly object coverGate=new();
    internal static void ForgetCovers(){lock(coverGate){lastSource=null;lastBlurred=null;lastThumb=null;}}
    private static (BitmapSource Image,Rect Area) Cover(string path,double blur,int width,int height)
    {
        var source=Source(path,Math.Max(width,height));var scale=Math.Max(width/(double)source.PixelWidth,height/(double)source.PixelHeight);
        var area=new Rect((width-source.PixelWidth*scale)/2,(height-source.PixelHeight*scale)/2,source.PixelWidth*scale,source.PixelHeight*scale);
        return(blur>0?Blurred(source,blur/scale):source,area);
    }
    private static BitmapSource Blurred(BitmapSource source,double sigma)
    {
        lock(coverGate)if(lastBlurred is {} known&&known.Sigma==sigma&&ReferenceEquals(known.Source,source))return known.Image;
        // A wide blur needs few pixels: shrink the picture until σ is about three of its pixels, blur that, and let drawing
        // stretch it back. When the result is that small anyway, shrinking starts from the 512 px copy (reading megapixels is the slow part).
        var k=Math.Max(1,sigma/3);var longSide=Math.Max(source.PixelWidth,source.PixelHeight);var basis=source;var shrink=k;
        if(longSide>512&&longSide/k<=512){basis=Thumb(source);shrink=k*Math.Max(basis.PixelWidth,basis.PixelHeight)/longSide;}
        BitmapSource shrunk=shrink>1?new TransformedBitmap(basis,new ScaleTransform(1/shrink,1/shrink)):basis;
        var straight=new FormatConvertedBitmap(shrunk,PixelFormats.Pbgra32,null,0);int w=straight.PixelWidth,h=straight.PixelHeight;var pixels=new byte[w*h*4];straight.CopyPixels(pixels,w*4,0);Behind.Blur(pixels,w,h,sigma/k);
        var blurred=BitmapSource.Create(w,h,96,96,PixelFormats.Pbgra32,null,pixels,w*4);blurred.Freeze();lock(coverGate)lastBlurred=(source,sigma,blurred);return blurred;
    }
    private static BitmapSource Thumb(BitmapSource source)
    {
        lock(coverGate)if(lastThumb is {} known&&ReferenceEquals(known.Source,source))return known.Copy;
        var f=512d/Math.Max(source.PixelWidth,source.PixelHeight);var copy=new WriteableBitmap(new TransformedBitmap(source,new ScaleTransform(f,f)));copy.Freeze();lock(coverGate)lastThumb=(source,copy);return copy;
    }
    // A preview canvas gets the kept copy; a saved canvas larger than 2048 px reads the full picture (never kept).
    private static BitmapSource Source(string path,int need)
    {
        if(need>2048)return Load(path);
        lock(coverGate)if(lastSource is {} held&&held.Path==path)return held.Image;
        var full=Load(path);var side=Math.Max(full.PixelWidth,full.PixelHeight);BitmapSource kept=full;
        if(side>2048){var copy=new WriteableBitmap(new TransformedBitmap(full,new ScaleTransform(2048d/side,2048d/side)));copy.Freeze();kept=copy;}
        lock(coverGate)lastSource=(path,kept);return kept;
    }
    internal static BitmapSource Collage(IReadOnlyList<string> inputs,CollageOptions o,CancellationToken token)
    {
        if(inputs.Count<2||inputs.Count>100)throw new ArgumentException("Choose 2–100 images for a collage.");var order=o.Order??Enumerable.Range(0,inputs.Count).ToArray();if(order.Length!=inputs.Count||!order.Order().SequenceEqual(Enumerable.Range(0,inputs.Count)))throw new ArgumentException("The image order must include every image once.");
        if(o.Columns<1||o.Columns>100||o.Padding<0||o.Spacing<0)throw new ArgumentException("Choose valid collage spacing and columns.");
        var columns=o.Layout=="row"?inputs.Count:o.Layout=="column"?1:Math.Min(o.Columns,inputs.Count);var rows=(int)Math.Ceiling(inputs.Count/(double)columns);var width=o.Width;var height=o.Height;
        return Render(width,height,g=>
        {
            g.DrawRectangle(ColorBrush(o.Color),null,new Rect(0,0,width,height));var cw=(width-2d*o.Padding-(columns-1)*o.Spacing)/columns;var ch=(height-2d*o.Padding-(rows-1)*o.Spacing)/rows;if(cw<=0||ch<=0)throw new ArgumentException("Reduce padding or spacing to fit the images.");
            for(var n=0;n<order.Length;n++)
            {
                token.ThrowIfCancellationRequested();Rect cell;
                if(o.Layout=="featured")
                {
                    var availableWidth=width-2d*o.Padding;var availableHeight=height-2d*o.Padding;var largeWidth=(availableWidth-o.Spacing)*.62;var smallHeight=(availableHeight-(order.Length-2)*o.Spacing)/(order.Length-1);
                    if(largeWidth<=0||smallHeight<=0)throw new ArgumentException("Reduce spacing to fit the featured layout.");
                    cell=n==0?new(o.Padding,o.Padding,largeWidth,availableHeight):new(o.Padding+largeWidth+o.Spacing,o.Padding+(n-1)*(smallHeight+o.Spacing),availableWidth-largeWidth-o.Spacing,smallHeight);
                }
                else cell=new(o.Padding+n%columns*(cw+o.Spacing),o.Padding+n/columns*(ch+o.Spacing),cw,ch);
                Fitted(g,Load(inputs[order[n]]),cell,o.Fill,o.CornerRadius);
            }
        });
    }
    internal static BitmapSource Redact(BitmapSource image,Rect[] regions,string style)
    {
        if(regions.Length==0)throw new ArgumentException("Draw at least one area to redact.");
        // Load can return a lazy EXIF-rotated chain; materialize it once so drawing and every crop reuse the pixels.
        // Check the canvas first so an oversized source is rejected before its pixels are cached.
        CheckCanvas(image.PixelWidth,image.PixelHeight);
        if(image is not (BitmapFrame or CachedBitmap)){image=new CachedBitmap(image,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);image.Freeze();}
        return Render(image.PixelWidth,image.PixelHeight,g=>
        {
            g.DrawImage(image,new Rect(0,0,image.PixelWidth,image.PixelHeight));foreach(var r in regions)
            {
                if(r.Width<1||r.Height<1||r.X<0||r.Y<0||r.Right>image.PixelWidth||r.Bottom>image.PixelHeight)throw new ArgumentException("Keep redaction areas inside the image.");
                if(style=="solid")g.DrawRectangle(Brushes.Black,null,r);else g.DrawImage(RedactionPatch(image,r,style),r);
            }
        });
    }
    // What covers one area: blur or blocks sized to the area, so text of any size stays unreadable (the editor shows the same patch).
    internal static BitmapSource RedactionPatch(BitmapSource image,Rect r,string style)
    {
        var crop=new CroppedBitmap(image,new Int32Rect((int)r.X,(int)r.Y,Math.Max(1,(int)r.Width),Math.Max(1,(int)r.Height)));var shortSide=Math.Max(1,Math.Min(r.Width,r.Height));
        // A much smaller copy does the work: blocks for pixelate, a softened copy stretched back for blur — milliseconds, unreadable at any size.
        int w=crop.PixelWidth,h=crop.PixelHeight;var cell=style=="pixelate"?Math.Max(10,shortSide/7):Math.Max(6,shortSide/9);int sw=Math.Max(1,(int)Math.Ceiling(w/cell)),sh=Math.Max(1,(int)Math.Ceiling(h/cell));
        var small=new FormatConvertedBitmap(new TransformedBitmap(crop,new ScaleTransform(sw/(double)w,sh/(double)h)),PixelFormats.Bgra32,null,0);int tw=small.PixelWidth,th=small.PixelHeight;var px=new byte[tw*th*4];small.CopyPixels(px,tw*4,0);
        if(style!="pixelate"){Behind.Blur(px,tw,th,1.6);var soft=BitmapSource.Create(tw,th,96,96,PixelFormats.Bgra32,null,px,tw*4);var done=new WriteableBitmap(new TransformedBitmap(soft,new ScaleTransform(w/(double)tw,h/(double)th)));done.Freeze();return done;}
        var full=new byte[w*h*4];for(var y=0;y<h;y++){var sy=Math.Min(th-1,y*th/h);for(var x=0;x<w;x++)Buffer.BlockCopy(px,(sy*tw+Math.Min(tw-1,x*tw/w))*4,full,(y*w+x)*4,4);}
        var blocks=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,full,w*4);blocks.Freeze();return blocks;
    }
}
