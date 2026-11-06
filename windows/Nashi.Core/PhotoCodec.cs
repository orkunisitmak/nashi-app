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
    /// PNG made small the way image optimizers do it: at most 256 colors (transparency kept), maximum compression.
    public static void EncodePalettePng(byte[] bgra,int width,int height,string output,int colors)
    {
        if(width<1||height<1||bgra.Length<(long)width*height*4)throw new ArgumentException("Choose a nonempty image.");
        using var image=new MagickImage();image.ReadPixels(bgra,new PixelReadSettings((uint)width,(uint)height,StorageType.Char,PixelMapping.BGRA));image.ColorSpace=ColorSpace.sRGB;image.Strip();
        image.Quantize(new QuantizeSettings{Colors=(uint)Math.Clamp(colors,2,256),DitherMethod=DitherMethod.FloydSteinberg});image.Settings.SetDefine(MagickFormat.Png,"compression-level","9");image.Write(output,MagickFormat.Png);
    }
    internal static int PaletteColors(int quality)=>quality>=85?256:quality>=70?160:96;
    public static void EncodeCompressed(byte[] bgra,int width,int height,string output,string format,int quality){if(format=="png")EncodePalettePng(bgra,width,height,output,PaletteColors(quality));else EncodePixels(bgra,width,height,output,format,quality);}
    public static MagickFormat Format(string format)=>format switch{"jpg"=>MagickFormat.Jpeg,"webp"=>MagickFormat.WebP,"heic"=>MagickFormat.Heic,"avif"=>MagickFormat.Avif,"tiff"=>MagickFormat.Tiff,"bmp"=>MagickFormat.Bmp,"gif"=>MagickFormat.Gif,"png"=>MagickFormat.Png,_=>throw new ArgumentException("Unsupported image format.")};
    public static byte[] Adjust(byte[] png,PhotoAdjustments o)
    {
        Validate(o);using var image=new MagickImage(png,new MagickReadSettings{Format=MagickFormat.Png});int width=(int)image.Width,height=(int)image.Height;
        byte[] pixels;using(var read=image.GetPixels())pixels=read.ToByteArray(PixelMapping.BGRA)??throw new InvalidDataException("The photo has no pixels.");
        var adjusted=PhotoPixels.Adjust(pixels,width,height,o);var opaque=!image.HasAlpha;
        using var output=new MagickImage();output.ReadPixels(adjusted,new PixelReadSettings((uint)width,(uint)height,StorageType.Char,PixelMapping.BGRA));output.ColorSpace=ColorSpace.sRGB;if(opaque)output.HasAlpha=false;return output.ToByteArray(MagickFormat.Png);
    }
    /// The same adjustments on straight BGRA pixels (the live preview passes its size against the original as `scale`).
    public static byte[] AdjustPixels(byte[] bgra,int width,int height,PhotoAdjustments o){Validate(o);return PhotoPixels.Adjust(bgra,width,height,o);}
    public static void AdjustPixels(byte[] bgra,byte[] output,int width,int height,PhotoAdjustments o,double scale){Validate(o);PhotoPixels.Adjust(bgra,output,width,height,o,scale);}
    private static void Validate(PhotoAdjustments o)
    {
        foreach(var property in typeof(PhotoAdjustments).GetProperties()){var n=(double)property.GetValue(o)!;if(!double.IsFinite(n)||Math.Abs(n)>100)throw new ArgumentException("Choose finite photo adjustments within their ranges.");}
        if(o.Gamma<=0||o.Contrast<0||o.Saturation<0)throw new ArgumentException("Gamma must be positive; contrast and saturation cannot be negative.");
    }
    public static IReadOnlyDictionary<string,string> Metadata(string input)
    {
        using var image=Read(input);var values=new SortedDictionary<string,string>(StringComparer.Ordinal);foreach(var name in image.AttributeNames){var value=image.GetAttribute(name);if(!string.IsNullOrEmpty(value))values[name]=value;}
        var exif=image.GetExifProfile();if(exif is not null)foreach(var v in exif.Values)values["EXIF · "+v.Tag]=v.ToString()??"";return values;
    }
}
