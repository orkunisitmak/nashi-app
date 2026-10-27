using ImageMagick;
using ImageMagick.Drawing;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
namespace Nashi.Linux.Engine;

public readonly record struct PixelRect(int X,int Y,int Width,int Height)
{
    public int Right=>X+Width;public int Bottom=>Y+Height;
}
public sealed record FrameOptions(int Padding=80,double CornerRadius=24,string Color="#FFFFFF",string Backdrop="solid",string GradientEnd="#FFD85C",double Shadow=24,double ShadowOpacity=.25);
public sealed record CollageOptions(string Layout="grid",int Columns=2,int Width=2048,int Height=2048,int Spacing=12,int Padding=16,string Color="#FFFFFF",bool Fill=true);
public sealed record ImageOptions
{
    public int? Width{get;init;}public int? Height{get;init;}public int Rotation{get;init;}public bool Horizontal{get;init;}=true;
    public PixelRect? Crop{get;init;}public int Quality{get;init;}=85;public PhotoAdjustments Adjustments{get;init;}=new();
    public FrameOptions Frame{get;init;}=new();public CollageOptions Collage{get;init;}=new();
    public string RedactionStyle{get;init;}="solid";public PixelRect[] Regions{get;init;}=[];
    public string AnnotationText{get;init;}="";public string AnnotationColor{get;init;}="#E5484D";
    public long? TargetBytes{get;init;}public double? ReductionPercent{get;init;}
    public string? EncoderPath{get;init;}
}

/// Magick.NET implementation of the Windows WPF ImageEngine. Pixels are always
/// decoded through PhotoCodec (auto-orientation, sRGB, size limits, safe SVG) and
/// every output is a fresh, metadata-free encode; originals are never touched.
public static class LinuxImages
{
    public static readonly string[] Formats=["png","jpg","webp","heic","avif","bmp","gif","tiff","pdf"];
    static LinuxImages(){PhotoCodec.Format("png");}
    public static MagickImage Load(string file)=>PhotoCodec.Read(file);
    public static MagickImage FromPng(byte[] png){PhotoCodec.Format("png");return new MagickImage(png,new MagickReadSettings{Format=MagickFormat.Png});}
    public readonly record struct PngSize(int PixelWidth,int PixelHeight);
    public static PngSize Size(byte[] png){var info=new MagickImageInfo(png);return new((int)info.Width,(int)info.Height);}
    public static byte[] PngFromBgra(byte[] pixels,int width,int height)
    {
        PhotoCodec.Format("png");using var image=new MagickImage(pixels,new PixelReadSettings((uint)width,(uint)height,StorageType.Char,PixelMapping.BGRA));
        image.Alpha(AlphaAction.Off);return image.ToByteArray(MagickFormat.Png);
    }

    public static IReadOnlyList<string> Apply(string tool,IReadOnlyList<string> inputs,string destination,ImageOptions options,string? format=null,CancellationToken token=default,IProgress<OperationProgress>? progress=null)
    {
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();var completed=0;
        if(tool=="createCollage")
        {
            using var result=Collage(inputs,options.Collage,token);var file=Path.Combine(tx.StagingDirectory,"Collage.png");Save(result,file,"png");progress?.Report(new(.99,"Saving collage…"));return tx.Publish([file],token);
        }
        if(tool=="createPDF")
        {
            var file=Path.Combine(tx.StagingDirectory,"Images.pdf");using var pdf=new PdfDocument();
            foreach(var input in inputs){token.ThrowIfCancellationRequested();using var source=Load(input);AddPdfPage(pdf,source);progress?.Report(new(++completed/(double)inputs.Count*.98,"Adding pages…"));}
            pdf.Save(file);return tx.Publish([file],token);
        }
        foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();using var image=Load(input);var ext=(format??FeatureCatalog.Extension(input)).ToLowerInvariant();if(!Formats.Contains(ext))ext="png";
            switch(tool)
            {
                case "resize":
                    var width=options.Width??(int)Math.Round(image.Width*(double)(options.Height??(int)image.Height)/image.Height);
                    var height=options.Height??(int)Math.Round(image.Height*(double)width/image.Width);
                    if(width<1||height<1||width>30000||height>30000||(long)width*height>100_000_000)throw new ArgumentException("Choose dimensions between 1 and 30,000 pixels (100 MP limit).");
                    image.Resize(new MagickGeometry((uint)width,(uint)height){IgnoreAspectRatio=true});break;
                case "crop":
                    if(options.Crop is not {} crop||crop.Width<1||crop.Height<1||crop.X<0||crop.Y<0||crop.Right>image.Width||crop.Bottom>image.Height)throw new ArgumentException("Drag a crop inside the image.");
                    image.Crop(new MagickGeometry(crop.X,crop.Y,(uint)crop.Width,(uint)crop.Height));image.ResetPage();break;
                case "rotate":image.Rotate(options.Rotation);break;
                case "flip":if(options.Horizontal)image.Flop();else image.Flip();break;
                case "editImage":
                    using(var adjusted=FromPng(PhotoCodec.Adjust(image.ToByteArray(MagickFormat.Png),options.Adjustments))){image.CopyPixels(adjusted);}
                    Transform(image,options);break;
                case "frameImage":using(var framed=Frame(image,options.Frame)){Save(framed,Output(tx,input,tool,"png"),"png",options.Quality,options.EncoderPath,token,outputs);}progress?.Report(new(++completed/(double)inputs.Count*.98,"Saving images…"));continue;
                case "redactImage":Redact(image,options.Regions,options.RedactionStyle);ext="png";break;
                case "annotate":Annotate(image,options);break;
                case "convert":case "compress":case "removeMetadata":break;
                default:throw new ArgumentException("Unsupported image tool.");
            }
            if(tool=="compress"&&!new[]{"jpg","png","heic","webp","avif"}.Contains(ext))ext="jpg";
            var output=Output(tx,input,tool,ext);
            if(tool=="compress")Compress(image,input,output,ext,options,token,progress);else Save(image,output,ext,options.Quality,options.EncoderPath,token);
            outputs.Add(output);progress?.Report(new(++completed/(double)inputs.Count*.98,"Saving images…"));
        }
        return tx.Publish(outputs,token);
    }
    private static string Output(ExportTransaction tx,string input,string tool,string ext)=>SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+(tool=="convert"?"":" "+tool)+"."+ext);
    private static void Save(MagickImage image,string output,string format,int quality,string? encoder,CancellationToken token,List<string> outputs){Save(image,output,format,quality,encoder,token);outputs.Add(output);}
    private static void Transform(MagickImage image,ImageOptions options)
    {
        if(options.Width is {} w&&options.Height is {} h&&(w!=image.Width||h!=image.Height)){if(w<1||h<1||w>30000||h>30000||(long)w*h>100_000_000)throw new ArgumentException("Choose valid image dimensions.");image.Resize(new MagickGeometry((uint)w,(uint)h){IgnoreAspectRatio=true});}
        if(options.Rotation%360!=0)image.Rotate(options.Rotation);
    }
    public static void Save(MagickImage image,string output,string format,int quality=85,string? encoder=null,CancellationToken token=default)
    {
        // Strip comments, EXIF and source profiles; metadata is copied back only
        // when the user keeps it, through ExifTool, exactly as on Windows.
        image.Strip();
        if(format is "webp" or "heic" or "avif"){PhotoCodec.EncodePng(image.ToByteArray(MagickFormat.Png),output,format,quality,encoder,token);return;}
        if(format=="pdf"){using var pdf=new PdfDocument();AddPdfPage(pdf,image);pdf.Save(output);return;}
        image.Quality=(uint)Math.Clamp(quality,1,100);
        using var file=new FileStream(output,FileMode.CreateNew);image.Write(file,PhotoCodec.Format(format));
    }
    private static void AddPdfPage(PdfDocument pdf,MagickImage source)
    {
        // PDFsharp reads only truecolor PNG; palette and 1-bit PNGs (QR codes, scans) are widened first.
        using var png=new MemoryStream(source.ToByteArray(MagickFormat.Png32));using var image=XImage.FromStream(png);var page=pdf.AddPage();
        page.Width=XUnit.FromPoint(source.Width*72/96d);page.Height=XUnit.FromPoint(source.Height*72/96d);using var g=XGraphics.FromPdfPage(page);g.DrawImage(image,0,0,page.Width.Point,page.Height.Point);
    }
    private static void Compress(MagickImage source,string input,string output,string format,ImageOptions options,CancellationToken token,IProgress<OperationProgress>? progress)
    {
        var target=CompressionTarget(new FileInfo(input).Length,options.TargetBytes,options.ReductionPercent);using var image=(MagickImage)source.Clone();Transform(image,options);
        var originalDimensions=image.Width==source.Width&&image.Height==source.Height;var quality=Math.Clamp(options.Quality,1,100);
        for(var n=0;n<24;n++)
        {
            token.ThrowIfCancellationRequested();if(File.Exists(output))File.Delete(output);progress?.Report(new(null,$"Fitting image · attempt {n+1}…"));Save(image,output,format,quality,options.EncoderPath,token);
            if(target is null||new FileInfo(output).Length<=target){if(originalDimensions&&FeatureCatalog.Extension(input)==format&&new FileInfo(input).Length<new FileInfo(output).Length)File.Copy(input,output,true);return;}
            if(quality>15&&format!="png")quality=Math.Max(1,(int)(quality*.72));else{image.Resize(Math.Max(1,(uint)(image.Width*.8)),Math.Max(1,(uint)(image.Height*.8)));originalDimensions=false;}
        }
        throw new IOException("The requested image size could not be reached. Choose a larger target.");
    }
    public static long? CompressionTarget(long original,long? bytes,double? percent)
    {
        if(bytes is <=0||percent is {} n&&(!double.IsFinite(n)||n<=0||n>=100))throw new ArgumentException("Choose a positive target size or a reduction between 0 and 100%.");
        var target=bytes??(percent is {} p?(long)(original*(1-p/100)):null);if(target is <=0)throw new ArgumentException("Choose a larger target size.");return target;
    }
    private static MagickColor Color(string value){try{return new MagickColor(value);}catch(MagickException){throw new ArgumentException("Choose a valid color such as #FFFFFF.");}}
    private static void Round(MagickImage image,double radius)
    {
        if(radius<=0)return;using var mask=new MagickImage(MagickColors.Transparent,image.Width,image.Height);
        new Drawables().FillColor(MagickColors.White).RoundRectangle(0,0,image.Width-1,image.Height-1,radius,radius).Draw(mask);
        image.Alpha(AlphaAction.Set);image.Composite(mask,CompositeOperator.DstIn);
    }
    public static MagickImage Frame(MagickImage image,FrameOptions o)
    {
        if(o.Padding<0||o.Padding>4000||!double.IsFinite(o.CornerRadius)||!double.IsFinite(o.Shadow)||!double.IsFinite(o.ShadowOpacity))throw new ArgumentException("Choose valid background and image dimensions.");
        var w=image.Width+2u*(uint)o.Padding;var h=image.Height+2u*(uint)o.Padding;
        var canvas=new MagickImage(Color(o.Color),w,h);
        if(o.Backdrop=="gradient"){canvas.Dispose();canvas=new MagickImage("gradient:"+o.Color+"-"+o.GradientEnd,w,h);canvas.Format=MagickFormat.Png;}
        using var photo=(MagickImage)image.Clone();photo.Alpha(AlphaAction.Set);Round(photo,o.CornerRadius);
        if(o.Shadow>0&&o.ShadowOpacity>0)
        {
            using var shadow=new MagickImage(MagickColors.Transparent,w,h);var alpha=(byte)Math.Clamp(o.ShadowOpacity*255,0,255);
            new Drawables().FillColor(new MagickColor(0,0,0,alpha)).RoundRectangle(o.Padding,o.Padding+o.Shadow/3,o.Padding+image.Width,o.Padding+image.Height+o.Shadow/3,o.CornerRadius,o.CornerRadius).Draw(shadow);
            shadow.Blur(0,Math.Min(100,o.Shadow)/2);canvas.Composite(shadow,CompositeOperator.Over);
        }
        canvas.Composite(photo,o.Padding,o.Padding,CompositeOperator.Over);return canvas;
    }
    public static MagickImage Collage(IReadOnlyList<string> inputs,CollageOptions o,CancellationToken token)
    {
        if(inputs.Count<2||inputs.Count>100)throw new ArgumentException("Choose 2–100 images for a collage.");
        if(o.Columns<1||o.Columns>100||o.Padding<0||o.Spacing<0||o.Width<16||o.Height<16||o.Width>16000||o.Height>16000)throw new ArgumentException("Choose valid collage spacing and columns.");
        var columns=o.Layout=="row"?inputs.Count:o.Layout=="column"?1:Math.Min(o.Columns,inputs.Count);var rows=(int)Math.Ceiling(inputs.Count/(double)columns);
        var cw=(o.Width-2d*o.Padding-(columns-1)*o.Spacing)/columns;var ch=(o.Height-2d*o.Padding-(rows-1)*o.Spacing)/rows;if(cw<1||ch<1)throw new ArgumentException("Reduce padding or spacing to fit the images.");
        var canvas=new MagickImage(Color(o.Color),(uint)o.Width,(uint)o.Height);
        for(var n=0;n<inputs.Count;n++)
        {
            token.ThrowIfCancellationRequested();using var tile=Load(inputs[n]);var cell=new PixelRect((int)Math.Round(o.Padding+n%columns*(cw+o.Spacing)),(int)Math.Round(o.Padding+n/columns*(ch+o.Spacing)),(int)cw,(int)ch);
            if(o.Fill){tile.Resize(new MagickGeometry((uint)cell.Width,(uint)cell.Height){FillArea=true});tile.Crop((uint)cell.Width,(uint)cell.Height,Gravity.Center);tile.ResetPage();}
            else tile.Resize((uint)cell.Width,(uint)cell.Height);
            canvas.Composite(tile,cell.X+(cell.Width-(int)tile.Width)/2,cell.Y+(cell.Height-(int)tile.Height)/2,CompositeOperator.Over);
        }
        return canvas;
    }
    public static void Redact(MagickImage image,PixelRect[] regions,string style)
    {
        if(regions.Length==0)throw new ArgumentException("Draw at least one area to redact.");
        foreach(var r in regions)
        {
            if(r.Width<1||r.Height<1||r.X<0||r.Y<0||r.Right>image.Width||r.Bottom>image.Height)throw new ArgumentException("Keep redaction areas inside the image.");
            if(style=="solid"){new Drawables().FillColor(MagickColors.Black).Rectangle(r.X,r.Y,r.Right,r.Bottom).Draw(image);continue;}
            using var area=(MagickImage)image.Clone();area.Crop(new MagickGeometry(r.X,r.Y,(uint)r.Width,(uint)r.Height));area.ResetPage();
            if(style=="pixelate"){area.FilterType=FilterType.Point;area.Resize(Math.Max(1,(uint)r.Width/16),Math.Max(1,(uint)r.Height/16));area.Resize(new MagickGeometry((uint)r.Width,(uint)r.Height){IgnoreAspectRatio=true});}
            else area.GaussianBlur(24);
            image.Composite(area,r.X,r.Y,CompositeOperator.Over);
        }
    }
    private static void Annotate(MagickImage image,ImageOptions options)
    {
        if(options.Regions.Length==0&&string.IsNullOrWhiteSpace(options.AnnotationText))throw new ArgumentException("Draw a box or add a caption to annotate.");
        var color=Color(options.AnnotationColor);var stroke=Math.Max(3,Math.Min(image.Width,image.Height)/200d);
        foreach(var r in options.Regions)
        {
            if(r.Width<1||r.Height<1||r.X<0||r.Y<0||r.Right>image.Width||r.Bottom>image.Height)throw new ArgumentException("Keep annotations inside the image.");
            new Drawables().StrokeColor(color).StrokeWidth(stroke).FillColor(MagickColors.Transparent).RoundRectangle(r.X,r.Y,r.Right,r.Bottom,stroke*2,stroke*2).Draw(image);
        }
        if(!string.IsNullOrWhiteSpace(options.AnnotationText))
        {
            var size=Math.Max(14,image.Height/18d);var band=(uint)Math.Ceiling(size*1.8);
            using var caption=new MagickImage(new MagickColor(0,0,0,160),image.Width,band);
            new Drawables().FontPointSize(size).FillColor(MagickColors.White).Gravity(Gravity.Center).Text(0,0,options.AnnotationText.Trim()).Draw(caption);
            image.Composite(caption,0,(int)(image.Height-band),CompositeOperator.Over);
        }
    }
}
