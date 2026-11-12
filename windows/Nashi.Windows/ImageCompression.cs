using System.Windows.Media.Imaging;
using System.Windows.Media;
using Nashi.Core;
namespace Nashi.Windows;
public static partial class ImageEngine
{
    private static void CompressImage(BitmapSource source,string input,string output,string format,ImageOptions options,CancellationToken token,IProgress<OperationProgress>? progress)
    {
        var target=CompressionTarget(new FileInfo(input).Length,options.TargetBytes,options.ReductionPercent);var image=TransformPhoto(source,options);var originalDimensions=image.PixelWidth==source.PixelWidth&&image.PixelHeight==source.PixelHeight;var quality=Math.Clamp(options.Quality,1,100);
        for(var n=0;n<24;n++)
        {
            token.ThrowIfCancellationRequested();if(File.Exists(output))File.Delete(output);progress?.Report(new(null,$"Fitting image · attempt {n+1}…"));if(format=="png"){var straight=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var pixels=new byte[straight.PixelWidth*straight.PixelHeight*4];straight.CopyPixels(pixels,straight.PixelWidth*4,0);Nashi.Core.PhotoCodec.EncodeCompressed(pixels,straight.PixelWidth,straight.PixelHeight,output,format,quality);}else Save(image,output,format,quality,options.EncoderPath,token);
            if(target is null||new FileInfo(output).Length<=target){if(originalDimensions&&FeatureCatalog.Extension(input)==format&&new FileInfo(input).Length<new FileInfo(output).Length)File.Copy(input,output,true);return;}
            if(quality>15&&format!="png")quality=Math.Max(1,(int)(quality*.72));else{image=new TransformedBitmap(image,new ScaleTransform(Math.Max(1,(int)(image.PixelWidth*.8))/(double)image.PixelWidth,Math.Max(1,(int)(image.PixelHeight*.8))/(double)image.PixelHeight));originalDimensions=false;}
        }
        throw new IOException("The requested image size could not be reached. Choose a larger target.");
    }
    internal static long? CompressionTarget(long original,long? bytes,double? percent)
    {
        if(bytes is <=0||percent is {} n&&(!double.IsFinite(n)||n<=0||n>=100))throw new ArgumentException("Choose a positive target size or a reduction between 0 and 100%.");var target=bytes??(percent is {} p?(long)(original*(1-p/100)):null);if(target is <=0)throw new ArgumentException("Choose a larger target size.");return target;
    }
}
