using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
namespace Nashi.Windows;
public sealed record FrameOptions(int Padding=80,double CornerRadius=24,string Color="#FFFFFF",string Backdrop="solid",string GradientEnd="#FFD85C",string? BackgroundImage=null,double Blur=0,double Shadow=24,double ShadowOpacity=.25,int? Width=null,int? Height=null,double Scale=1,double X=.5,double Y=.5);
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
    internal static BitmapSource Frame(BitmapSource image,FrameOptions o)
    {
        if(o.Padding<0||o.Padding>4000||!new[]{o.CornerRadius,o.Blur,o.Shadow,o.ShadowOpacity,o.Scale,o.X,o.Y}.All(double.IsFinite)||o.Scale<=0||o.Scale>10)throw new ArgumentException("Choose valid background and image dimensions.");
        var w=o.Width??image.PixelWidth+2*o.Padding;var h=o.Height??image.PixelHeight+2*o.Padding;
        return Render(w,h,g=>
        {
            var bounds=new Rect(0,0,w,h);Brush background=o.Backdrop=="gradient"?new LinearGradientBrush(((SolidColorBrush)ColorBrush(o.Color)).Color,((SolidColorBrush)ColorBrush(o.GradientEnd)).Color,45):ColorBrush(o.Color);g.DrawRectangle(background,null,bounds);
            if(o.Backdrop=="image"){if(o.BackgroundImage is null)throw new ArgumentException("Choose a background image.");var back=Load(o.BackgroundImage);if(o.Blur>0)back=FromPng(Nashi.Core.PhotoCodec.Adjust(PngBytes(back),new(){Blur=o.Blur}));Fitted(g,back,bounds,true);}
            var scale=Math.Min(Math.Max(1,w-2*o.Padding)/(double)image.PixelWidth,Math.Max(1,h-2*o.Padding)/(double)image.PixelHeight)*o.Scale;var iw=image.PixelWidth*scale;var ih=image.PixelHeight*scale;var rect=new Rect((w-iw)*Math.Clamp(o.X,0,1),(h-ih)*Math.Clamp(o.Y,0,1),iw,ih);
            if(o.Shadow>0){var spread=Math.Min(100,o.Shadow);for(var n=8;n>0;n--){var r=rect;r.Inflate(spread*n/8,spread*n/8);g.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(o.ShadowOpacity,0,1)*6),0,0,0)),null,r,o.CornerRadius+spread*n/8,o.CornerRadius+spread*n/8);}}
            Fitted(g,image,rect,true,o.CornerRadius);
        });
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
                if(style=="solid")g.DrawRectangle(Brushes.Black,null,r);
                else
                {
                    var crop=new CroppedBitmap(image,new Int32Rect((int)r.X,(int)r.Y,(int)r.Width,(int)r.Height));BitmapSource effect;
                    if(style=="pixelate"){effect=new TransformedBitmap(crop,new ScaleTransform(Math.Max(1,r.Width/16)/r.Width,Math.Max(1,r.Height/16)/r.Height));var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.NearestNeighbor);using(var c=visual.RenderOpen())c.DrawImage(effect,new Rect(0,0,r.Width,r.Height));var pixels=new RenderTargetBitmap((int)r.Width,(int)r.Height,96,96,PixelFormats.Pbgra32);pixels.Render(visual);effect=pixels;}
                    else effect=FromPng(Nashi.Core.PhotoCodec.Adjust(PngBytes(crop),new(){Blur=24}));g.DrawImage(effect,r);
                }
            }
        });
    }
}
