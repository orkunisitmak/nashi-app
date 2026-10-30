using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
namespace Nashi.Windows;

public sealed record ImageOptions(int? Width=null,int? Height=null,int Rotation=0,Int32Rect? Crop=null,int Quality=85,bool Horizontal=true,BitmapSource? Annotated=null)
{
    public PhotoAdjustments Adjustments{get;init;}=new();public FrameOptions Frame{get;init;}=new();public CollageOptions Collage{get;init;}=new();
    public string RedactionStyle{get;init;}="solid";public Rect[] Redactions{get;init;}=[];
    public long? TargetBytes{get;init;}public double? ReductionPercent{get;init;}
    public bool FlipHorizontal{get;init;}public bool FlipVertical{get;init;}
    // Size presets apply to each image's own dimensions rather than the first image's.
    public double? Scale{get;init;}public int? LongestEdge{get;init;}
    public string? EncoderPath{get;init;}
}
public static partial class ImageEngine
{
    public static readonly string[] Formats=["png","jpg","webp","heic","avif","bmp","gif","tiff","pdf"];
    public static BitmapSource Load(string file)
    {
        if(FeatureCatalog.Extension(file) is "webp" or "heic" or "avif" or "svg")return FromPng(PhotoCodec.Png(file));
        using var stream=File.OpenRead(file);var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var frame=decoder.Frames[0];BitmapSource image=frame;
        if((long)image.PixelWidth*image.PixelHeight>100_000_000)throw new InvalidDataException("This image is too large (100 MP limit).");
        ushort orientation=1;
        if(frame.Metadata is BitmapMetadata metadata)
        {
            foreach(var query in new[]{"/app1/ifd/{ushort=274}","/ifd/{ushort=274}"})
            {
                if(metadata.ContainsQuery(query)&&metadata.GetQuery(query) is ushort value){orientation=value;break;}
            }
        }
        if(frame.ColorContexts is {Count:>0} contexts)image=new ColorConvertedBitmap(image,contexts[0],new ColorContext(PixelFormats.Bgra32),PixelFormats.Bgra32);
        var transforms=new TransformGroup();
        if(orientation is 2 or 5 or 7)transforms.Children.Add(new ScaleTransform(-1,1));
        else if(orientation==4)transforms.Children.Add(new ScaleTransform(1,-1));
        var angle=orientation switch{3=>180,5 or 8=>270,6 or 7=>90,_=>0};
        if(angle!=0)transforms.Children.Add(new RotateTransform(angle));
        if(transforms.Children.Count>0)image=new TransformedBitmap(image,transforms);
        image.Freeze();return image;
    }

    public static IReadOnlyList<string> Apply(string tool,IReadOnlyList<string> inputs,string destination,ImageOptions options,string? format=null,CancellationToken token=default,IProgress<OperationProgress>? progress=null)
    {
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();var completed=0;
        if(tool=="createCollage")
        {
            var result=Collage(inputs,options.Collage,token);var file=Path.Combine(tx.StagingDirectory,"Collage.png");Save(result,file,"png");progress?.Report(new(.99,"Saving collage…"));return tx.Publish([file],token);
        }
        if(tool=="createPDF")
        {
            var file=Path.Combine(tx.StagingDirectory,"Images.pdf");using var pdf=new PdfDocument();
            foreach(var input in inputs){token.ThrowIfCancellationRequested();var source=Load(input);using var png=new MemoryStream(PngBytes(source));using var image=XImage.FromStream(png);var page=pdf.AddPage();page.Width=XUnit.FromPoint(source.PixelWidth*72/96d);page.Height=XUnit.FromPoint(source.PixelHeight*72/96d);using var g=XGraphics.FromPdfPage(page);g.DrawImage(image,0,0,page.Width.Point,page.Height.Point);progress?.Report(new(++completed/(double)inputs.Count*.98,"Adding pages…"));}
            pdf.Save(file);return tx.Publish([file],token);
        }
        foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();BitmapSource image=options.Annotated??Load(input);var ext=(format??Path.GetExtension(input).TrimStart('.')).ToLowerInvariant();if(ext=="jpeg")ext="jpg";if(ext=="tif")ext="tiff";
            if(!Formats.Contains(ext))ext="png";
            switch(tool)
            {
                case "resize":
                    var width=options.Width??(int)Math.Round(image.PixelWidth*(double)(options.Height??image.PixelHeight)/image.PixelHeight);
                    var height=options.Height??(int)Math.Round(image.PixelHeight*(double)width/image.PixelWidth);
                    if(width<1||height<1||width>30000||height>30000||(long)width*height>100_000_000)throw new ArgumentException("Choose dimensions between 1 and 30,000 pixels (100 MP limit).");
                    image=new TransformedBitmap(image,new ScaleTransform((double)width/image.PixelWidth,(double)height/image.PixelHeight));break;
                case "crop":if(options.Crop is not {} crop||crop.Width<1||crop.Height<1||crop.X<0||crop.Y<0||crop.X+crop.Width>image.PixelWidth||crop.Y+crop.Height>image.PixelHeight)throw new ArgumentException("Drag a crop inside the image.");image=new CroppedBitmap(image,crop);break;
                case "rotate":image=Rotate(image,options.Rotation);break;
                case "flip":image=new TransformedBitmap(image,new ScaleTransform(options.Horizontal?-1:1,options.Horizontal?1:-1));break;
                case "editImage":
                    image=FromPng(PhotoCodec.Adjust(PngBytes(image),options.Adjustments));
                    image=TransformPhoto(image,options);break;
                case "frameImage":image=Frame(image,options.Frame);ext="png";break;
                // Like macOS, keep the source format: redacted pixels are replaced before encoding, and a
                // 48 MP JPEG re-saved as PNG took ~5-10 s and grew from 8 MB to 52 MB. Writing HEIC needs the
                // media tools on Windows, so without them a HEIC source keeps the previous PNG output.
                case "redactImage":image=Redact(image,options.Redactions,options.RedactionStyle);if(ext=="heic"&&options.EncoderPath is null)ext="png";break;
                case "convert":case "compress":case "removeMetadata":case "annotate":break;
                default:throw new ArgumentException("Unsupported image tool.");
            }
            if(tool=="compress"&&!new[]{"jpg","png","heic","webp","avif"}.Contains(ext))ext="jpg";
            var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+(tool=="convert"?"":" "+tool)+"."+ext);
            if(tool=="compress")CompressImage(image,input,output,ext,options,token,progress);else Save(image,output,ext,options.Quality,options.EncoderPath,token);outputs.Add(output);progress?.Report(new(++completed/(double)inputs.Count*.98,"Saving images…"));
        }
        return tx.Publish(outputs,token);
    }
    // Flip, turn, then size, as on macOS: Width/Height describe the finished image,
    // so Rotate Left/Right swapping the fields keeps the photo's proportions.
    internal static BitmapSource TransformPhoto(BitmapSource image,ImageOptions options)
    {
        if(options.FlipHorizontal||options.FlipVertical)image=new TransformedBitmap(image,new ScaleTransform(options.FlipHorizontal?-1:1,options.FlipVertical?-1:1));
        image=Rotate(image,options.Rotation);var (ew,eh)=TargetSize(image,options);
        if(ew!=image.PixelWidth||eh!=image.PixelHeight){if(ew<1||eh<1||ew>30000||eh>30000||(long)ew*eh>100_000_000)throw new ArgumentException("Choose valid image dimensions.");image=new TransformedBitmap(image,new ScaleTransform((double)ew/image.PixelWidth,(double)eh/image.PixelHeight));}
        image.Freeze();return image;
    }
    // One named side keeps the aspect ratio; presets scale every image by its own size.
    private static (int Width,int Height) TargetSize(BitmapSource image,ImageOptions options)
    {
        int w=image.PixelWidth,h=image.PixelHeight;
        var factor=options.Scale??(options.LongestEdge is {} edge?(double?)Math.Min(1,(double)edge/Math.Max(w,h)):null);
        if(factor is {} f)return(Math.Max(1,(int)Math.Round(w*f)),Math.Max(1,(int)Math.Round(h*f)));
        return(options.Width,options.Height) switch{({} width,{} height)=>(width,height),({} width,null)=>(width,Math.Max(1,(int)Math.Round(h*(double)width/w))),(null,{} height)=>(Math.Max(1,(int)Math.Round(w*(double)height/h)),height),_=>(w,h)};
    }
    // TransformedBitmap only accepts quarter turns. Other angles are drawn clockwise
    // onto a transparent canvas large enough for the turned corners.
    internal static BitmapSource Rotate(BitmapSource image,int degrees)
    {
        var angle=((degrees%360)+360)%360;if(angle==0)return image;if(angle%90==0)return new TransformedBitmap(image,new RotateTransform(angle));
        var radians=angle*Math.PI/180;var cos=Math.Abs(Math.Cos(radians));var sin=Math.Abs(Math.Sin(radians));
        var width=(int)Math.Ceiling(image.PixelWidth*cos+image.PixelHeight*sin);var height=(int)Math.Ceiling(image.PixelWidth*sin+image.PixelHeight*cos);
        if(width>30000||height>30000||(long)width*height>100_000_000)throw new ArgumentException("Choose valid image dimensions.");
        var visual=new DrawingVisual();using(var context=visual.RenderOpen()){context.PushTransform(new RotateTransform(angle,width/2d,height/2d));context.DrawImage(image,new Rect((width-image.PixelWidth)/2d,(height-image.PixelHeight)/2d,image.PixelWidth,image.PixelHeight));}
        var result=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();return result;
    }
    public static void Save(BitmapSource image,string output,string format,int quality=85,string? encoderPath=null,CancellationToken token=default)
    {
        // BitmapFrame.Create(source) can copy decoder metadata implicitly.
        // Materialize pixels into a fresh source, then create a metadata-free
        // frame explicitly. No comments, EXIF or source color-profile payloads
        // can reach the encoder through a retained decoder frame.
        BitmapSource converted=image.Format==PixelFormats.Bgra32?image:new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
        var stride=checked(converted.PixelWidth*4);var pixels=new byte[checked(stride*converted.PixelHeight)];converted.CopyPixels(pixels,stride,0);
        // JPEG has no alpha and WPF drops it unflattened, turning transparent areas black.
        if(format=="jpg")for(var n=0;n<pixels.Length;n+=4){var alpha=pixels[n+3];if(alpha==255)continue;for(var c=0;c<3;c++)pixels[n+c]=(byte)((pixels[n+c]*alpha+255*(255-alpha)+127)/255);pixels[n+3]=255;}
        image=BitmapSource.Create(converted.PixelWidth,converted.PixelHeight,96,96,PixelFormats.Bgra32,null,pixels,stride);image.Freeze();
        // Hand the raw pixels to Magick: an intermediate WPF PNG of a 48 MP photo took 15-19 s alone.
        if(format is "webp" or "heic" or "avif"){PhotoCodec.EncodePixels(pixels,image.PixelWidth,image.PixelHeight,output,format,quality,encoderPath,token);return;}
        if(format=="pdf")
        {
            using var png=new MemoryStream();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(png);png.Position=0;
            using var pdf=new PdfDocument();var page=pdf.AddPage();page.Width=XUnit.FromPoint(image.PixelWidth*72/96d);page.Height=XUnit.FromPoint(image.PixelHeight*72/96d);
            using var graphic=XGraphics.FromPdfPage(page);using var resource=XImage.FromStream(png);graphic.DrawImage(resource,0,0,page.Width.Point,page.Height.Point);pdf.Save(output);return;
        }
        BitmapEncoder target=format switch{"jpg"=>new JpegBitmapEncoder{QualityLevel=Math.Clamp(quality,1,100)},"bmp"=>new BmpBitmapEncoder(),"gif"=>new GifBitmapEncoder(),"tiff"=>new TiffBitmapEncoder(),_=>new PngBitmapEncoder()};
        // New frames omit source metadata deliberately; originals stay unchanged.
        target.Frames.Add(BitmapFrame.Create(image,null,null,null));using var file=new FileStream(output,FileMode.CreateNew);target.Save(file);
    }
}
