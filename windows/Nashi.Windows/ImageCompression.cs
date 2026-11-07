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
            // Round both sides and keep the shorter one at least a pixel so tiny scale steps keep the aspect ratio (matches the Mac path).
            var fit=Math.Max(scale,1d/Math.Min(image.PixelWidth,image.PixelHeight));var frame=scale<1?new TransformedBitmap(image,new ScaleTransform(Math.Max(1,(int)Math.Round(image.PixelWidth*fit))/(double)image.PixelWidth,Math.Max(1,(int)Math.Round(image.PixelHeight*fit))/(double)image.PixelHeight)):image;Save(frame,output,format,q,options.EncoderPath,token);
        }
        // The source file can stand in for the output only when no edit reached the pixels; flips, 180° turns and annotations keep the size but must still be encoded and measured.
        var keepsOriginal=options.Annotated is null&&!options.FlipHorizontal&&!options.FlipVertical&&((options.Rotation%360)+360)%360==0&&image.PixelWidth==source.PixelWidth&&image.PixelHeight==source.PixelHeight&&FeatureCatalog.Extension(input)==format;
        // Every attempt that fits is kept, so the chosen one is used as measured instead of being re-encoded.
        var fitting=new Dictionary<(int,double),string>();
        try
        {
        var outcome=target is {} limit?TargetSizeSearch.Run(keepsOriginal?original:long.MaxValue,limit,quality,lossy,true,(q,scale)=>{progress?.Report(new(null,$"Fitting image · attempt {++attempt}…"));Encode(q,scale);var bytes=new FileInfo(output).Length;if(bytes<=limit){var kept=output+"."+fitting.Count+".fit";File.Copy(output,kept,true);fitting[(q,scale)]=kept;}return bytes;},token):null;
        if(outcome is {Status:TargetSizeStatus.BestEffortAboveTarget})
        {
            if(File.Exists(output))File.Delete(output);
            throw new IOException("The requested image size could not be reached. Choose a larger target.");
        }
        var best=outcome?.Best;if(best is null)Encode(quality,1);else if(fitting.TryGetValue((best.Quality,best.Scale),out var chosen))File.Copy(chosen,output,true);else{Encode(best.Quality,best.Scale);if(new FileInfo(output).Length>target){File.Delete(output);throw new IOException("The requested image size could not be reached. Choose a larger target.");}}
        if((best?.Scale??1)==1&&keepsOriginal&&original<new FileInfo(output).Length)File.Copy(input,output,true);
        }
        finally{foreach(var kept in fitting.Values)if(File.Exists(kept))File.Delete(kept);}
    }
    internal static long? CompressionTarget(long original,long? bytes,double? percent)
    {
        if(bytes is <=0||percent is {} n&&(!double.IsFinite(n)||n<=0||n>=100))throw new ArgumentException("Choose a positive target size or a reduction between 0 and 100%.");var target=bytes??(percent is {} p?(long)(original*(1-p/100)):null);if(target is <=0)throw new ArgumentException("Choose a larger target size.");return target;
    }
}
