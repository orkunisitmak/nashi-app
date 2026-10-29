using ImageMagick;
using ImageMagick.Drawing;
using Nashi.Core;
namespace Nashi.Linux;

// Same options and validation as windows/Nashi.Windows/ImageCompositions.cs, drawn with ImageMagick.
public sealed record FrameOptions(int Padding=80,double CornerRadius=24,string Color="#FFFFFF",string Backdrop="solid",string GradientEnd="#FFD85C",string? BackgroundImage=null,double Blur=0,double Shadow=24,double ShadowOpacity=.25,int? Width=null,int? Height=null,double Scale=1,double X=.5,double Y=.5)
{
    /// Pixel lengths for a preview drawn at a fraction of the real size.
    public FrameOptions Scaled(double f)=>this with{Padding=(int)Math.Round(Padding*f),CornerRadius=CornerRadius*f,Blur=Blur*f,Shadow=Shadow*f,Width=Width is {} w?Math.Max(1,(int)Math.Round(w*f)):null,Height=Height is {} h?Math.Max(1,(int)Math.Round(h*f)):null};
}
public sealed record CollageOptions(string Layout="grid",int Columns=2,int Width=2048,int Height=2048,int Spacing=12,int Padding=16,double CornerRadius=0,string Color="#FFFFFF",bool Fill=true,int[]? Order=null)
{
    public CollageOptions Scaled(double f)=>this with{Width=Math.Max(1,(int)Math.Round(Width*f)),Height=Math.Max(1,(int)Math.Round(Height*f)),Spacing=(int)Math.Round(Spacing*f),Padding=(int)Math.Round(Padding*f),CornerRadius=CornerRadius*f};
}

public static class Compositions
{
    public static MagickColor Color(string value)
    {
        try{return new MagickColor(value.Trim());}catch(Exception error)when(error is ArgumentException or MagickException){throw new ArgumentException("Choose a hex background color, such as #FFFFFF or #00FFFFFF.");}
    }

    public static MagickImage Frame(MagickImage image,FrameOptions o,MagickImage? loadedBackground=null)
    {
        if(o.Padding<0||o.Padding>4000||!new[]{o.CornerRadius,o.Blur,o.Shadow,o.ShadowOpacity,o.Scale,o.X,o.Y}.All(double.IsFinite)||o.Scale<=0||o.Scale>10)throw new ArgumentException("Choose valid background and image dimensions.");
        var w=o.Width??(int)image.Width+2*o.Padding;var h=o.Height??(int)image.Height+2*o.Padding;Bounds(w,h);
        var canvas=o.Backdrop=="gradient"?Gradient(Color(o.Color),Color(o.GradientEnd),w,h):new MagickImage(Color(o.Color),(uint)w,(uint)h);
        try
        {
            if(o.Backdrop=="image")
            {
                if(loadedBackground is null&&o.BackgroundImage is null)throw new ArgumentException("Choose a background image.");
                using var back=loadedBackground is null?PhotoCodec.Read(o.BackgroundImage!):(MagickImage)loadedBackground.Clone();if(o.Blur>0)back.GaussianBlur(0,Math.Min(50,o.Blur));Fitted(canvas,back,new(0,0,w,h),true,0);
            }
            var scale=Math.Min(Math.Max(1,w-2*o.Padding)/(double)image.Width,Math.Max(1,h-2*o.Padding)/(double)image.Height)*o.Scale;var iw=image.Width*scale;var ih=image.Height*scale;
            var rect=new Area((w-iw)*Math.Clamp(o.X,0,1),(h-ih)*Math.Clamp(o.Y,0,1),iw,ih);
            if(o.Shadow>0&&o.ShadowOpacity>0)Shadow(canvas,rect,o.CornerRadius,Math.Min(100,o.Shadow),Math.Clamp(o.ShadowOpacity,0,1));
            Fitted(canvas,image,rect,true,o.CornerRadius);return canvas;
        }
        catch{canvas.Dispose();throw;}
    }

    public static MagickImage Collage(IReadOnlyList<MagickImage> images,CollageOptions o,CancellationToken token=default)
    {
        if(images.Count<2||images.Count>100)throw new ArgumentException("Choose 2–100 images for a collage.");var order=o.Order??Enumerable.Range(0,images.Count).ToArray();
        if(order.Length!=images.Count||!order.Order().SequenceEqual(Enumerable.Range(0,images.Count)))throw new ArgumentException("The image order must include every image once.");
        if(o.Columns<1||o.Columns>100||o.Padding<0||o.Spacing<0||!double.IsFinite(o.CornerRadius)||o.CornerRadius<0)throw new ArgumentException("Choose valid collage spacing and columns.");Bounds(o.Width,o.Height);
        var columns=o.Layout=="row"?images.Count:o.Layout=="column"?1:Math.Min(o.Columns,images.Count);var rows=(int)Math.Ceiling(images.Count/(double)columns);
        var cw=(o.Width-2d*o.Padding-(columns-1)*o.Spacing)/columns;var ch=(o.Height-2d*o.Padding-(rows-1)*o.Spacing)/rows;if(cw<=0||ch<=0)throw new ArgumentException("Reduce padding or spacing to fit the images.");
        var canvas=new MagickImage(Color(o.Color),(uint)o.Width,(uint)o.Height);
        try
        {
            for(var n=0;n<order.Length;n++)
            {
                token.ThrowIfCancellationRequested();Area cell;
                if(o.Layout=="featured")
                {
                    var availableWidth=o.Width-2d*o.Padding;var availableHeight=o.Height-2d*o.Padding;var largeWidth=(availableWidth-o.Spacing)*.62;var smallHeight=(availableHeight-(order.Length-2)*o.Spacing)/(order.Length-1);
                    if(largeWidth<=0||smallHeight<=0)throw new ArgumentException("Reduce spacing to fit the featured layout.");
                    cell=n==0?new(o.Padding,o.Padding,largeWidth,availableHeight):new(o.Padding+largeWidth+o.Spacing,o.Padding+(n-1)*(smallHeight+o.Spacing),availableWidth-largeWidth-o.Spacing,smallHeight);
                }
                else cell=new(o.Padding+n%columns*(cw+o.Spacing),o.Padding+n/columns*(ch+o.Spacing),cw,ch);
                Fitted(canvas,images[order[n]],cell,o.Fill,o.CornerRadius);
            }
            return canvas;
        }
        catch{canvas.Dispose();throw;}
    }

    private readonly record struct Area(double X,double Y,double Width,double Height);
    private static void Bounds(int w,int h){if(w<1||h<1||w>30000||h>30000||(long)w*h>100_000_000)throw new ArgumentException("Choose a canvas between 1 and 30,000 pixels (100 MP limit).");}

    /// Fill covers the area and crops the overflow; otherwise the image fits inside. Corners are clipped like WPF's PushClip.
    private static void Fitted(MagickImage canvas,MagickImage source,Area area,bool fill,double radius)
    {
        var aw=(int)Math.Round(area.Width);var ah=(int)Math.Round(area.Height);if(aw<1||ah<1)return;
        var scale=fill?Math.Max(area.Width/source.Width,area.Height/source.Height):Math.Min(area.Width/source.Width,area.Height/source.Height);
        using var tile=(MagickImage)source.Clone();tile.Alpha(AlphaAction.Set);
        tile.Resize(new MagickGeometry((uint)Math.Max(1,Math.Round(source.Width*scale)),(uint)Math.Max(1,Math.Round(source.Height*scale))){IgnoreAspectRatio=true});
        // Center the resized picture in an area-sized transparent layer, cropping what overflows.
        using var layer=new MagickImage(MagickColors.Transparent,(uint)aw,(uint)ah);
        layer.Composite(tile,(int)Math.Round((aw-(double)tile.Width)/2),(int)Math.Round((ah-(double)tile.Height)/2),CompositeOperator.Over);
        if(radius>0)
        {
            using var mask=new MagickImage(MagickColors.Transparent,(uint)aw,(uint)ah);
            new Drawables().FillColor(MagickColors.White).RoundRectangle(0,0,aw-1,ah-1,radius,radius).Draw(mask);
            layer.Composite(mask,CompositeOperator.DstIn);
        }
        canvas.Composite(layer,(int)Math.Round(area.X),(int)Math.Round(area.Y),CompositeOperator.Over);
    }

    private static void Shadow(MagickImage canvas,Area rect,double radius,double spread,double opacity)
    {
        var margin=(int)Math.Ceiling(spread*2);var w=(uint)Math.Max(1,Math.Round(rect.Width))+2*(uint)margin;var h=(uint)Math.Max(1,Math.Round(rect.Height))+2*(uint)margin;
        using var shadow=new MagickImage(MagickColors.Transparent,w,h);
        new Drawables().FillColor(new MagickColor(0,0,0,(byte)Math.Round(opacity*255))).RoundRectangle(margin,margin,w-margin-1,h-margin-1,radius,radius).Draw(shadow);
        shadow.GaussianBlur(0,spread/2);
        canvas.Composite(shadow,(int)Math.Round(rect.X)-margin,(int)Math.Round(rect.Y)-margin,CompositeOperator.Over);
    }

    /// 45° gradient from the top-left to the bottom-right corner, matching WPF's LinearGradientBrush angle.
    private static MagickImage Gradient(MagickColor start,MagickColor end,int w,int h)
    {
        var image=new MagickImage(start,(uint)w,(uint)h);
        using var pixels=image.GetPixels();var row=new byte[w*image.ChannelCount];var channels=(int)image.ChannelCount;
        var a=new[]{start.R,start.G,start.B,start.A};var b=new[]{end.R,end.G,end.B,end.A};
        for(var y=0;y<h;y++)
        {
            for(var x=0;x<w;x++){var t=(x/(double)Math.Max(1,w-1)+y/(double)Math.Max(1,h-1))/2;for(var c=0;c<channels;c++)row[x*channels+c]=(byte)Math.Round(a[c]+(b[c]-a[c])*t);}
            pixels.SetArea(0,y,(uint)w,1,row);
        }
        return image;
    }
}
