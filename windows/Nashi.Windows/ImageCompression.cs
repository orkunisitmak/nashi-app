using System.Windows.Media.Imaging;
using System.Windows.Media;
using Nashi.Core;
namespace Nashi.Windows;
public static partial class ImageEngine
{
    private static void CompressImage(BitmapSource source,string input,string output,string format,ImageOptions options,CancellationToken token,IProgress<OperationProgress>? progress)
    {
        var original=new FileInfo(input).Length;var target=CompressionTarget(original,options.TargetBytes,options.ReductionPercent);var image=TransformPhoto(source,options);var quality=Math.Clamp(options.Quality,1,100);var lossy=format!="png";var attempt=0;
        void Encode(int q,double scale)
        {
            token.ThrowIfCancellationRequested();if(File.Exists(output))File.Delete(output);
            var frame=scale<1?new TransformedBitmap(image,new ScaleTransform(Math.Max(1,(int)(image.PixelWidth*scale))/(double)image.PixelWidth,Math.Max(1,(int)(image.PixelHeight*scale))/(double)image.PixelHeight)):image;Save(frame,output,format,q,options.EncoderPath,token);
        }
        // Skipping the search is safe only when the untouched source can be kept; resized or re-encoded outputs must be measured.
        var keepsOriginal=image.PixelWidth==source.PixelWidth&&image.PixelHeight==source.PixelHeight&&FeatureCatalog.Extension(input)==format;
        var outcome=target is {} limit?TargetSizeSearch.Run(keepsOriginal?original:long.MaxValue,limit,quality,lossy,true,(q,scale)=>{progress?.Report(new(null,$"Fitting image · attempt {++attempt}…"));Encode(q,scale);return new FileInfo(output).Length;},token):null;
        if(outcome is {Status:TargetSizeStatus.BestEffortAboveTarget})
        {
            if(File.Exists(output))File.Delete(output);
            throw new IOException("The requested image size could not be reached. Choose a larger target.");
        }
        var best=outcome?.Best;if(best is null)Encode(quality,1);else if(new FileInfo(output).Length!=best.Bytes)Encode(best.Quality,best.Scale);
        var originalDimensions=(best?.Scale??1)==1&&image.PixelWidth==source.PixelWidth&&image.PixelHeight==source.PixelHeight;
        if(originalDimensions&&FeatureCatalog.Extension(input)==format&&original<new FileInfo(output).Length)File.Copy(input,output,true);
    }
    internal static long? CompressionTarget(long original,long? bytes,double? percent)
    {
        if(bytes is <=0||percent is {} n&&(!double.IsFinite(n)||n<=0||n>=100))throw new ArgumentException("Choose a positive target size or a reduction between 0 and 100%.");var target=bytes??(percent is {} p?(long)(original*(1-p/100)):null);if(target is <=0)throw new ArgumentException("Choose a larger target size.");return target;
    }
}
