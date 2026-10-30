using ImageMagick;
using ImageMagick.Configuration;
using System.Xml;
using System.Xml.Linq;
namespace Nashi.Core;

public sealed record PhotoAdjustments
{
    public double Exposure{get;init;} public double Brightness{get;init;} public double Contrast{get;init;}=1;
    public double Highlights{get;init;} public double Shadows{get;init;} public double Whites{get;init;} public double Blacks{get;init;}
    public double Gamma{get;init;}=1; public double Temperature{get;init;} public double Tint{get;init;}
    public double Saturation{get;init;}=1;public double Vibrance{get;init;} public double Hue{get;init;}
    public double Clarity{get;init;} public double Texture{get;init;} public double Fade{get;init;}
    public double Blur{get;init;} public double Sharpen{get;init;} public double Dehaze{get;init;}
    public double Grain{get;init;} public double NoiseReduction{get;init;} public double Vignette{get;init;}
}
public static class PhotoCodec
{
    static PhotoCodec()
    {
        var config=ConfigurationFiles.Default;
        config.Policy.Data="<policymap><policy domain='delegate' rights='none' pattern='*'/><policy domain='coder' rights='none' pattern='{HTTPS,HTTP,URL,FTP,MSL,EPHEMERAL,PS,PS2,PS3,EPS,PDF,XPS}'/><policy domain='path' rights='none' pattern='@*'/></policymap>";
        MagickNET.Initialize(config);ResourceLimits.Memory=512UL*1024*1024;ResourceLimits.Disk=0;ResourceLimits.Width=30000;ResourceLimits.Height=30000;ResourceLimits.ListLength=500;ResourceLimits.Thread=2;
    }
    public static MagickImage Read(string path)
    {
        if(!FeatureCatalog.IsImage(path)&&FeatureCatalog.Extension(path)!="gif")throw new ArgumentException("Choose an image.");
        if(new FileInfo(path).Length>200_000_000)throw new InvalidDataException("This image exceeds 200 MB.");
        if(FeatureCatalog.Extension(path)=="svg")ValidateSvg(path);
        var ext=FeatureCatalog.Extension(path);var settings=new MagickReadSettings{FrameCount=1,Format=ext=="svg"?MagickFormat.Msvg:Format(ext)};
        using var stream=File.OpenRead(path);var info=new MagickImageInfo(stream,settings);if((ulong)info.Width*info.Height>100_000_000)throw new InvalidDataException("This image exceeds 100 MP.");stream.Position=0;
        var image=new MagickImage(stream,settings);image.AutoOrient();if(image.GetColorProfile() is not null)image.TransformColorSpace(ColorProfiles.SRGB);else image.ColorSpace=ColorSpace.sRGB;return image;
    }
    private static void ValidateSvg(string path)
    {
        using var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20_000_000});var xml=XDocument.Load(reader);
        foreach(var element in xml.Descendants())
        {
            if(element.Name.LocalName is "script" or "foreignObject")throw new InvalidDataException("Use a static SVG without embedded scripts or HTML.");
            foreach(var attribute in element.Attributes())if(attribute.Name.LocalName is "href" or "src"&&!attribute.Value.StartsWith('#')&&!new[]{"data:image/png;base64,","data:image/jpeg;base64,","data:image/webp;base64,"}.Any(p=>attribute.Value.StartsWith(p,StringComparison.Ordinal)))throw new InvalidDataException("SVG external resources are not loaded. Embed the raster image first.");
            if(element.Value.Contains("url(",StringComparison.OrdinalIgnoreCase)&&!element.Value.Contains("url(#",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SVG external styles are not loaded.");
            foreach(var attribute in element.Attributes())if(attribute.Value.Contains("url(",StringComparison.OrdinalIgnoreCase)&&!attribute.Value.Contains("url(#",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SVG external styles are not loaded.");
        }
    }
    public static byte[] Png(string input){using var image=Read(input);return image.ToByteArray(MagickFormat.Png);}
    public static void EncodePng(byte[] pixels,string output,string format,int quality=85,string? encoder=null,CancellationToken token=default)
    {
        using var image=new MagickImage(pixels,new MagickReadSettings{Format=MagickFormat.Png});image.Strip();image.Quality=(uint)Math.Clamp(quality,1,100);
        if(format=="heic"){if(encoder is null)throw new InvalidOperationException("Set up media tools once to enable HEIC encoding on Windows.");var width=(int)image.Width;var height=(int)image.Height;Task.Run(()=>HeifCodec.EncodeAsync(encoder,pixels,output,width,height,quality,token),token).GetAwaiter().GetResult();return;}image.Write(output,Format(format));
    }
    /// Encodes straight (non-premultiplied) BGRA32 pixels without an intermediate PNG.
    public static void EncodePixels(byte[] bgra,int width,int height,string output,string format,int quality=85,string? encoder=null,CancellationToken token=default)
    {
        if(width<1||height<1||bgra.Length<(long)width*height*4)throw new ArgumentException("Choose a nonempty image.");
        MagickImage Read(){var image=new MagickImage();image.ReadPixels(bgra,new PixelReadSettings((uint)width,(uint)height,StorageType.Char,PixelMapping.BGRA));image.ColorSpace=ColorSpace.sRGB;return image;}
        // HEIC goes through EncodePng's own MagickImage; release this pixel cache before that one is created.
        if(format=="heic"){byte[] png;using(var source=Read())png=source.ToByteArray(MagickFormat.Png);EncodePng(png,output,format,quality,encoder,token);return;}
        using var image=Read();image.Strip();image.Quality=(uint)Math.Clamp(quality,1,100);image.Write(output,Format(format));
    }
    public static MagickFormat Format(string format)=>format switch{"jpg"=>MagickFormat.Jpeg,"webp"=>MagickFormat.WebP,"heic"=>MagickFormat.Heic,"avif"=>MagickFormat.Avif,"tiff"=>MagickFormat.Tiff,"bmp"=>MagickFormat.Bmp,"gif"=>MagickFormat.Gif,"png"=>MagickFormat.Png,_=>throw new ArgumentException("Unsupported image format.")};
    public static byte[] Adjust(byte[] png,PhotoAdjustments o)
    {
        foreach(var property in typeof(PhotoAdjustments).GetProperties()){var n=(double)property.GetValue(o)!;if(!double.IsFinite(n)||Math.Abs(n)>100)throw new ArgumentException("Choose finite photo adjustments within their ranges.");}
        if(o.Gamma<=0||o.Contrast<0||o.Saturation<0)throw new ArgumentException("Gamma must be positive; contrast and saturation cannot be negative.");
        using var image=new MagickImage(png,new MagickReadSettings{Format=MagickFormat.Png});
        if(o.Exposure!=0)image.Evaluate(Channels.RGB,EvaluateOperator.Multiply,Math.Pow(2,o.Exposure));
        image.BrightnessContrast(new Percentage(o.Brightness*100),new Percentage((o.Contrast-1)*50));
        if(o.Gamma!=1)image.GammaCorrect(o.Gamma,Channels.RGB);
        if(o.Shadows!=0||o.Highlights!=0||o.Whites!=0||o.Blacks!=0||o.Saturation!=1||o.Vibrance!=0||o.Temperature!=0||o.Tint!=0||o.Fade!=0)
        {
        var pixels=image.GetPixels();
        for(var y=0;y<image.Height;y++)for(var x=0;x<image.Width;x++)
        {
            var p=pixels.GetPixel(x,y);var r=p.GetChannel(0)/255d;var g=p.GetChannel(1)/255d;var b=p.GetChannel(2)/255d;var lum=.2126*r+.7152*g+.0722*b;
            var tone=o.Shadows*Math.Pow(1-lum,2)*.3+o.Highlights*lum*lum*.3+o.Whites*Math.Pow(lum,4)*.3+o.Blacks*Math.Pow(1-lum,4)*.3;
            var saturation=o.Saturation+o.Vibrance*(1-(Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))));
            r=lum+(r-lum)*saturation+tone+o.Temperature*.12+o.Tint*.06;g=lum+(g-lum)*saturation+tone-o.Tint*.1;b=lum+(b-lum)*saturation+tone-o.Temperature*.12+o.Tint*.06;
            var fade=Math.Clamp(o.Fade,0,1)*.3;r=r*(1-fade)+fade;g=g*(1-fade)+fade;b=b*(1-fade)+fade;
            p.SetChannel(0,(byte)Math.Clamp(Math.Round(r*255),0,255));p.SetChannel(1,(byte)Math.Clamp(Math.Round(g*255),0,255));p.SetChannel(2,(byte)Math.Clamp(Math.Round(b*255),0,255));
        }
        pixels.Dispose();
        }
        if(o.Hue!=0)image.Modulate(new Percentage(100),new Percentage(100),new Percentage(100+o.Hue/1.8));
        if(o.Dehaze!=0)image.BrightnessContrast(new Percentage(-o.Dehaze*8),new Percentage(o.Dehaze*25));
        if(o.NoiseReduction>0)image.MedianFilter((uint)Math.Clamp(Math.Ceiling(o.NoiseReduction*2),1,3));
        // Blur is the Gaussian standard deviation, like macOS CIGaussianBlur's radius.
        // GaussianBlur(radius) keeps sigma at 1 (barely blurred, still readable when
        // redacting) and its 2-D kernel is far slower than the separable Blur.
        if(o.Blur>0)image.Blur(0,Math.Min(50,o.Blur));
        if(o.Sharpen>0||o.Clarity>0||o.Texture>0)image.UnsharpMask(0,1+Math.Max(0,o.Clarity)*3,Math.Max(0,o.Sharpen)+Math.Max(0,o.Texture),.02);
        if(o.Grain>0)image.AddNoise(NoiseType.Gaussian,Math.Min(1,o.Grain)*.15,Channels.RGB);
        if(o.Vignette>0)image.Vignette(0,Math.Max(1,Math.Min(image.Width,image.Height)*o.Vignette*.25),0,0);
        return image.ToByteArray(MagickFormat.Png);
    }
    public static IReadOnlyDictionary<string,string> Metadata(string input)
    {
        using var image=Read(input);var values=new SortedDictionary<string,string>(StringComparer.Ordinal);foreach(var name in image.AttributeNames){var value=image.GetAttribute(name);if(!string.IsNullOrEmpty(value))values[name]=value;}
        var exif=image.GetExifProfile();if(exif is not null)foreach(var v in exif.Values)values["EXIF · "+v.Tag]=v.ToString()??"";return values;
    }
}
