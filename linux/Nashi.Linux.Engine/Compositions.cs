using ImageMagick;
using ImageMagick.Drawing;
using Nashi.Core;
namespace Nashi.Windows;
// Same geometry and options as Windows ImageCompositions; independent of WPF drawing APIs.
internal static class Compositions
{
    private static void Validate(int w,int h)
    {if(w<1||h<1||w>30000||h>30000||(long)w*h>100_000_000)throw new ArgumentException("Choose a canvas within 100 MP and 30,000 pixels.");}
    private static void Fitted(MagickImage canvas,IMagickImage<byte> source,double x,double y,double width,double height,bool fill,double radius)
    {
        var w=(uint)Math.Max(1,Math.Round(width));var h=(uint)Math.Max(1,Math.Round(height));
        using var content=source.Clone();content.Resize(new MagickGeometry(w,h){FillArea=fill});
        using var cell=new MagickImage(MagickColors.Transparent,w,h);cell.Composite(content,Gravity.Center,CompositeOperator.Over);
        if(radius>0)
        {
            using var mask=new MagickImage(MagickColors.Transparent,w,h);new Drawables().FillColor(MagickColors.White).StrokeColor(MagickColors.White).RoundRectangle(0,0,w-1,h-1,radius,radius).Draw(mask);
            cell.Alpha(AlphaAction.Set);cell.Composite(mask,CompositeOperator.DstIn);
        }
        canvas.Composite(cell,(int)Math.Round(x),(int)Math.Round(y),CompositeOperator.Over);
    }
    public static MagickImage Frame(MagickImage source,FrameOptions o)
    {
        if(o.Padding<0||o.Padding>4000||!new[]{o.CornerRadius,o.Blur,o.Shadow,o.ShadowOpacity,o.Scale,o.X,o.Y}.All(double.IsFinite)||o.Scale<=0||o.Scale>10)throw new ArgumentException("Choose valid background and image dimensions.");
        var w=o.Width??(int)source.Width+2*o.Padding;var h=o.Height??(int)source.Height+2*o.Padding;Validate(w,h);
        var canvas=new MagickImage(new MagickColor(o.Color),(uint)w,(uint)h);
        try
        {
            if(o.Backdrop=="gradient")canvas.Read("gradient:"+new MagickColor(o.Color)+"-"+new MagickColor(o.GradientEnd),new MagickReadSettings{Width=(uint)w,Height=(uint)h});
            if(o.Backdrop=="image")
            {
                if(o.BackgroundImage is null)throw new ArgumentException("Choose a background image.");using var back=PhotoCodec.Read(o.BackgroundImage);if(o.Blur>0)back.GaussianBlur(Math.Min(50,o.Blur));Fitted(canvas,back,0,0,w,h,true,0);
            }
            var scale=Math.Min(Math.Max(1,w-2*o.Padding)/(double)source.Width,Math.Max(1,h-2*o.Padding)/(double)source.Height)*o.Scale;
            var iw=source.Width*scale;var ih=source.Height*scale;var x=(w-iw)*Math.Clamp(o.X,0,1);var y=(h-ih)*Math.Clamp(o.Y,0,1);
            if(o.Shadow>0)
            {
                var spread=Math.Min(100,o.Shadow);for(var n=8;n>0;n--){var amount=spread*n/8;var color=new MagickColor(0,0,0,(byte)(Math.Clamp(o.ShadowOpacity,0,1)*6));new Drawables().FillColor(color).StrokeColor(color).RoundRectangle(x-amount,y-amount,x+iw+amount,y+ih+amount,o.CornerRadius+amount,o.CornerRadius+amount).Draw(canvas);}
            }
            Fitted(canvas,source,x,y,iw,ih,true,o.CornerRadius);return canvas;
        }catch{canvas.Dispose();throw;}
    }
    public static MagickImage Collage(IReadOnlyList<string> inputs,CollageOptions o,CancellationToken token)
    {
        if(inputs.Count<2||inputs.Count>100)throw new ArgumentException("Choose 2–100 images for a collage.");var order=o.Order??Enumerable.Range(0,inputs.Count).ToArray();
        if(order.Length!=inputs.Count||!order.Order().SequenceEqual(Enumerable.Range(0,inputs.Count)))throw new ArgumentException("The image order must include every image once.");
        if(o.Columns<1||o.Columns>100||o.Padding<0||o.Spacing<0)throw new ArgumentException("Choose valid collage spacing and columns.");
        var columns=o.Layout=="row"?inputs.Count:o.Layout=="column"?1:Math.Min(o.Columns,inputs.Count);var rows=(int)Math.Ceiling(inputs.Count/(double)columns);var width=o.Width;var height=o.Height;Validate(width,height);
        var canvas=new MagickImage(new MagickColor(o.Color),(uint)width,(uint)height);
        try
        {
            var cw=(width-2d*o.Padding-(columns-1)*o.Spacing)/columns;var ch=(height-2d*o.Padding-(rows-1)*o.Spacing)/rows;if(cw<=0||ch<=0)throw new ArgumentException("Reduce padding or spacing to fit the images.");
            for(var n=0;n<order.Length;n++)
            {
                token.ThrowIfCancellationRequested();double x,y,w,h;
                if(o.Layout=="featured")
                {
                    var availableWidth=width-2d*o.Padding;var availableHeight=height-2d*o.Padding;var largeWidth=(availableWidth-o.Spacing)*.62;var smallHeight=(availableHeight-(order.Length-2)*o.Spacing)/(order.Length-1);
                    if(largeWidth<=0||smallHeight<=0)throw new ArgumentException("Reduce spacing to fit the featured layout.");
                    x=n==0?o.Padding:o.Padding+largeWidth+o.Spacing;y=n==0?o.Padding:o.Padding+(n-1)*(smallHeight+o.Spacing);w=n==0?largeWidth:availableWidth-largeWidth-o.Spacing;h=n==0?availableHeight:smallHeight;
                }
                else{x=o.Padding+n%columns*(cw+o.Spacing);y=o.Padding+n/columns*(ch+o.Spacing);w=cw;h=ch;}
                using var image=PhotoCodec.Read(inputs[order[n]]);Fitted(canvas,image,x,y,w,h,o.Fill,o.CornerRadius);
            }
            return canvas;
        }catch{canvas.Dispose();throw;}
    }
}
