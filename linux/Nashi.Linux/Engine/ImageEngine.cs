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
public sealed record FrameOptions(int Padding=80,double CornerRadius=24,string Color="#FFFFFF",string Backdrop="solid",string GradientEnd="#FFD85C",string? BackgroundImage=null,double Blur=0,double Shadow=24,double ShadowOpacity=.25,int? Width=null,int? Height=null,double Scale=1,double X=.5,double Y=.5);
public sealed record CollageOptions(string Layout="grid",int Columns=2,int Width=2048,int Height=2048,int Spacing=12,int Padding=16,double CornerRadius=0,string Color="#FFFFFF",bool Fill=true,int[]? Order=null);
public sealed record ImageOptions(int? Width=null,int? Height=null,int Rotation=0,PixelRect? Crop=null,int Quality=85,bool Horizontal=true,byte[]? Annotated=null)
{
    public PhotoAdjustments Adjustments{get;init;}=new();public FrameOptions Frame{get;init;}=new();public CollageOptions Collage{get;init;}=new();
    public string RedactionStyle{get;init;}="solid";public PixelRect[] Redactions{get;init;}=[];
    public long? TargetBytes{get;init;}public double? ReductionPercent{get;init;}
    public bool FlipHorizontal{get;init;}public bool FlipVertical{get;init;}
    // Size presets apply to each image's own dimensions rather than the first image's.
    public double? Scale{get;init;}public int? LongestEdge{get;init;}
    public string? EncoderPath{get;init;}
}

/// Linux counterpart of the Windows WPF ImageEngine. Every operation keeps the same
/// options, limits, output names and transaction rules; pixels go through Magick.NET.
public static class ImageEngine
{
    public static readonly string[] Formats=["png","jpg","webp","heic","avif","bmp","gif","tiff","pdf"];
    public static MagickImage Load(string file){Magick.Ensure();var image=PhotoCodec.Read(file);image.Alpha(AlphaAction.Set);return image;}
    public static byte[] PreviewPng(string file,int longest=1600)
    {
        using var image=Load(file);if(Math.Max(image.Width,image.Height)>longest)image.Resize(new MagickGeometry((uint)longest,(uint)longest));return image.ToByteArray(MagickFormat.Png);
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
            foreach(var input in inputs)
            {
                token.ThrowIfCancellationRequested();using var source=Load(input);AddPdfPage(pdf,source);
                progress?.Report(new(++completed/(double)inputs.Count*.98,"Adding pages…"));
            }
            pdf.Save(file);return tx.Publish([file],token);
        }
        foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();
            var image=options.Annotated is {} annotated?Magick.FromPng(annotated):Load(input);
            try
            {
                var ext=(format??Path.GetExtension(input).TrimStart('.')).ToLowerInvariant();if(ext=="jpeg")ext="jpg";if(ext=="tif")ext="tiff";
                if(!Formats.Contains(ext))ext="png";
                switch(tool)
                {
                    case "resize":
                        var width=options.Width??(int)Math.Round(image.Width*(double)(options.Height??(int)image.Height)/image.Height);
                        var height=options.Height??(int)Math.Round(image.Height*(double)width/image.Width);
                        if(width<1||height<1||width>30000||height>30000||(long)width*height>100_000_000)throw new ArgumentException("Choose dimensions between 1 and 30,000 pixels (100 MP limit).");
                        Resize(image,width,height);break;
                    case "crop":
                        if(options.Crop is not {} crop||crop.Width<1||crop.Height<1||crop.X<0||crop.Y<0||crop.Right>image.Width||crop.Bottom>image.Height)throw new ArgumentException("Drag a crop inside the image.");
                        image.Crop(new MagickGeometry(crop.X,crop.Y,(uint)crop.Width,(uint)crop.Height));image.ResetPage();break;
                    case "rotate":Rotate(image,options.Rotation);break;
                    case "flip":if(options.Horizontal)image.Flop();else image.Flip();break;
                    case "editImage":
                        {var adjusted=Magick.FromPng(PhotoCodec.Adjust(image.ToByteArray(MagickFormat.Png),options.Adjustments));image.Dispose();image=adjusted;TransformPhoto(image,options);break;}
                    case "frameImage":{var framed=Frame(image,options.Frame);image.Dispose();image=framed;ext="png";break;}
                    // Like macOS, keep the source format. Writing HEIC needs the media tools, so without them
                    // a HEIC source keeps the previous PNG output.
                    case "redactImage":Redact(image,options.Redactions,options.RedactionStyle);if(ext=="heic"&&options.EncoderPath is null)ext="png";break;
                    case "convert":case "compress":case "removeMetadata":case "annotate":break;
                    default:throw new ArgumentException("Unsupported image tool.");
                }
                if(tool=="compress"&&!new[]{"jpg","png","heic","webp","avif"}.Contains(ext))ext="jpg";
                var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+(tool=="convert"?"":" "+tool)+"."+ext);
                if(tool=="compress")CompressImage(image,input,output,ext,options,token,progress);else Save(image,output,ext,options.Quality,options.EncoderPath,token);
                outputs.Add(output);progress?.Report(new(++completed/(double)inputs.Count*.98,"Saving images…"));
            }
            finally{image.Dispose();}
        }
        return tx.Publish(outputs,token);
    }

    private static void Resize(MagickImage image,int width,int height)
    {
        if(width==image.Width&&height==image.Height)return;
        image.Resize(new MagickGeometry((uint)width,(uint)height){IgnoreAspectRatio=true});image.ResetPage();
    }
    /// Flip, turn, then size, as on macOS and Windows: Width/Height describe the finished image.
    private static void TransformPhoto(MagickImage image,ImageOptions options)
    {
        if(options.FlipHorizontal)image.Flop();if(options.FlipVertical)image.Flip();
        Rotate(image,options.Rotation);var (ew,eh)=TargetSize(image,options);
        if(ew!=image.Width||eh!=image.Height){if(ew<1||eh<1||ew>30000||eh>30000||(long)ew*eh>100_000_000)throw new ArgumentException("Choose valid image dimensions.");Resize(image,ew,eh);}
    }
    private static (int Width,int Height) TargetSize(MagickImage image,ImageOptions options)
    {
        int w=(int)image.Width,h=(int)image.Height;
        var factor=options.Scale??(options.LongestEdge is {} edge?(double?)Math.Min(1,(double)edge/Math.Max(w,h)):null);
        if(factor is {} f)return(Math.Max(1,(int)Math.Round(w*f)),Math.Max(1,(int)Math.Round(h*f)));
        return(options.Width,options.Height) switch{({} width,{} height)=>(width,height),({} width,null)=>(width,Math.Max(1,(int)Math.Round(h*(double)width/w))),(null,{} height)=>(Math.Max(1,(int)Math.Round(w*(double)height/h)),height),_=>(w,h)};
    }
    /// Clockwise, on a transparent canvas large enough for the turned corners.
    internal static void Rotate(MagickImage image,int degrees)
    {
        var angle=((degrees%360)+360)%360;if(angle==0)return;
        if(angle%90!=0)
        {
            var radians=angle*Math.PI/180;var cos=Math.Abs(Math.Cos(radians));var sin=Math.Abs(Math.Sin(radians));
            var width=Math.Ceiling(image.Width*cos+image.Height*sin);var height=Math.Ceiling(image.Width*sin+image.Height*cos);
            if(width>30000||height>30000||width*height>100_000_000)throw new ArgumentException("Choose valid image dimensions.");
        }
        image.BackgroundColor=MagickColors.Transparent;image.Rotate(angle);image.ResetPage();
    }

    public static void Save(MagickImage source,string output,string format,int quality=85,string? encoderPath=null,CancellationToken token=default)
    {
        // A fresh copy without profiles or comments: originals stay unchanged and no EXIF,
        // GPS or colour-profile payload reaches the export.
        using var image=(MagickImage)source.Clone();image.Strip();image.Quality=(uint)Math.Clamp(quality,1,100);
        switch(format)
        {
            case "webp" or "heic" or "avif":PhotoCodec.EncodePng(image.ToByteArray(MagickFormat.Png),output,format,quality,encoderPath,token);return;
            case "pdf":{using var pdf=new PdfDocument();AddPdfPage(pdf,image);pdf.Save(output);return;}
            case "jpg":
                // JPEG has no alpha: flatten onto white so transparent areas don't turn black.
                image.BackgroundColor=MagickColors.White;image.Alpha(AlphaAction.Remove);image.Alpha(AlphaAction.Off);
                image.Write(output,MagickFormat.Jpeg);return;
            default:
                if(image.IsOpaque&&format is "bmp" or "tiff" or "png")image.Alpha(AlphaAction.Off);
                image.Write(output,PhotoCodec.Format(format));return;
        }
    }
    private static void AddPdfPage(PdfDocument pdf,MagickImage source)
    {
        // PDFsharp reads 8-bit RGBA PNGs, not the grey, palette or 16-bit ones Magick writes for
        // some sources (a black-and-white QR code, for example), so always hand it PNG32.
        using var png=new MemoryStream(source.ToByteArray(MagickFormat.Png32));using var image=XImage.FromStream(png);
        var page=pdf.AddPage();page.Width=XUnit.FromPoint(source.Width*72/96d);page.Height=XUnit.FromPoint(source.Height*72/96d);
        using var g=XGraphics.FromPdfPage(page);g.DrawImage(image,0,0,page.Width.Point,page.Height.Point);
    }

    private static void CompressImage(MagickImage source,string input,string output,string format,ImageOptions options,CancellationToken token,IProgress<OperationProgress>? progress)
    {
        var target=CompressionTarget(new FileInfo(input).Length,options.TargetBytes,options.ReductionPercent);
        using var image=(MagickImage)source.Clone();TransformPhoto(image,options);
        var originalDimensions=image.Width==source.Width&&image.Height==source.Height;var quality=Math.Clamp(options.Quality,1,100);
        for(var n=0;n<24;n++)
        {
            token.ThrowIfCancellationRequested();if(File.Exists(output))File.Delete(output);progress?.Report(new(null,$"Fitting image · attempt {n+1}…"));Save(image,output,format,quality,options.EncoderPath,token);
            if(target is null||new FileInfo(output).Length<=target){if(originalDimensions&&FeatureCatalog.Extension(input)==format&&new FileInfo(input).Length<new FileInfo(output).Length)File.Copy(input,output,true);return;}
            if(quality>15&&format!="png")quality=Math.Max(1,(int)(quality*.72));
            else{Resize(image,Math.Max(1,(int)(image.Width*.8)),Math.Max(1,(int)(image.Height*.8)));originalDimensions=false;}
        }
        throw new IOException("The requested image size could not be reached. Choose a larger target.");
    }
    internal static long? CompressionTarget(long original,long? bytes,double? percent)
    {
        if(bytes is <=0||percent is {} n&&(!double.IsFinite(n)||n<=0||n>=100))throw new ArgumentException("Choose a positive target size or a reduction between 0 and 100%.");
        var target=bytes??(percent is {} p?(long)(original*(1-p/100)):null);if(target is <=0)throw new ArgumentException("Choose a larger target size.");return target;
    }

    private static void CheckCanvas(int width,int height){if(width<1||height<1||width>30000||height>30000||(long)width*height>100_000_000)throw new ArgumentException("Choose a canvas within 100 MP and 30,000 pixels.");}
    /// WPF reads #AARRGGBB; ImageMagick reads #RRGGBBAA. Accept the documented Windows form.
    internal static MagickColor ParseColor(string value)
    {
        var text=value.Trim();
        if(text.Length==9&&text[0]=='#')text="#"+text[3..]+text[1..3];
        if(!(text.Length is 4 or 7 or 9)||text[0]!='#'||!text[1..].All(Uri.IsHexDigit))throw new ArgumentException("Choose a hex background color, such as #FFFFFF or #00FFFFFF.");
        return new MagickColor(text);
    }
    private static MagickImage RoundedMask(int width,int height,double radius)
    {
        var mask=new MagickImage(MagickColors.Transparent,(uint)width,(uint)height);
        new Drawables().FillColor(MagickColors.White).StrokeColor(MagickColors.Transparent).RoundRectangle(0,0,width-1,height-1,radius,radius).Draw(mask);return mask;
    }
    /// Scales the source to cover (fill) or fit inside the cell, clips it to the cell's rounded
    /// rectangle and composites it at the cell's position, like the WPF Fitted helper.
    private static void Fitted(MagickImage canvas,MagickImage source,double x,double y,double width,double height,bool fill,double radius=0)
    {
        int cw=Math.Max(1,(int)Math.Round(width)),ch=Math.Max(1,(int)Math.Round(height));
        var scale=fill?Math.Max(width/source.Width,height/source.Height):Math.Min(width/source.Width,height/source.Height);
        using var scaled=(MagickImage)source.Clone();scaled.Alpha(AlphaAction.Set);Resize(scaled,Math.Max(1,(int)Math.Round(source.Width*scale)),Math.Max(1,(int)Math.Round(source.Height*scale)));
        using var cell=new MagickImage(MagickColors.Transparent,(uint)cw,(uint)ch);
        cell.Composite(scaled,(cw-(int)scaled.Width)/2,(ch-(int)scaled.Height)/2,CompositeOperator.Over);
        if(radius>0){using var mask=RoundedMask(cw,ch,radius);cell.Composite(mask,CompositeOperator.DstIn);}
        canvas.Composite(cell,(int)Math.Round(x),(int)Math.Round(y),CompositeOperator.Over);
    }
    internal static MagickImage Frame(MagickImage image,FrameOptions o)
    {
        if(o.Padding<0||o.Padding>4000||!new[]{o.CornerRadius,o.Blur,o.Shadow,o.ShadowOpacity,o.Scale,o.X,o.Y}.All(double.IsFinite)||o.Scale<=0||o.Scale>10)throw new ArgumentException("Choose valid background and image dimensions.");
        var w=o.Width??(int)image.Width+2*o.Padding;var h=o.Height??(int)image.Height+2*o.Padding;CheckCanvas(w,h);
        MagickImage canvas;
        if(o.Backdrop=="gradient")
        {
            var start=ParseColor(o.Color);var end=ParseColor(o.GradientEnd);var settings=new MagickReadSettings{Width=(uint)w,Height=(uint)h};
            // 135° runs from the top-left to the bottom-right corner, like WPF's 45° brush.
            settings.SetDefine(MagickFormat.Gradient,"angle","135");canvas=new MagickImage("gradient:"+start.ToHexString()+"-"+end.ToHexString(),settings);
        }
        else canvas=new MagickImage(ParseColor(o.Color),(uint)w,(uint)h);
        try
        {
            canvas.Alpha(AlphaAction.Set);
            if(o.Backdrop=="image")
            {
                if(o.BackgroundImage is null)throw new ArgumentException("Choose a background image.");
                using var back=Load(o.BackgroundImage);if(o.Blur>0)back.Blur(0,Math.Min(50,o.Blur));Fitted(canvas,back,0,0,w,h,true);
            }
            var scale=Math.Min(Math.Max(1,w-2*o.Padding)/(double)image.Width,Math.Max(1,h-2*o.Padding)/(double)image.Height)*o.Scale;
            var iw=image.Width*scale;var ih=image.Height*scale;var x=(w-iw)*Math.Clamp(o.X,0,1);var y=(h-ih)*Math.Clamp(o.Y,0,1);
            if(o.Shadow>0)
            {
                var spread=Math.Min(100,o.Shadow);var sw=(int)Math.Ceiling(iw+4*spread);var sh=(int)Math.Ceiling(ih+4*spread);
                using var shadow=new MagickImage(MagickColors.Transparent,(uint)sw,(uint)sh);
                var alpha=(byte)Math.Round(Math.Clamp(o.ShadowOpacity,0,1)*48);
                new Drawables().FillColor(new MagickColor(0,0,0,alpha)).StrokeColor(MagickColors.Transparent).RoundRectangle(2*spread-spread/2,2*spread-spread/2,2*spread+iw+spread/2,2*spread+ih+spread/2,o.CornerRadius+spread/2,o.CornerRadius+spread/2).Draw(shadow);
                shadow.Blur(0,Math.Max(1,spread/2));canvas.Composite(shadow,(int)Math.Round(x-2*spread),(int)Math.Round(y-2*spread),CompositeOperator.Over);
            }
            Fitted(canvas,image,x,y,iw,ih,true,o.CornerRadius);
            return canvas;
        }
        catch{canvas.Dispose();throw;}
    }
    internal static MagickImage Collage(IReadOnlyList<string> inputs,CollageOptions o,CancellationToken token)
    {
        if(inputs.Count<2||inputs.Count>100)throw new ArgumentException("Choose 2–100 images for a collage.");
        var order=o.Order??Enumerable.Range(0,inputs.Count).ToArray();if(order.Length!=inputs.Count||!order.Order().SequenceEqual(Enumerable.Range(0,inputs.Count)))throw new ArgumentException("The image order must include every image once.");
        if(o.Columns<1||o.Columns>100||o.Padding<0||o.Spacing<0)throw new ArgumentException("Choose valid collage spacing and columns.");
        var columns=o.Layout=="row"?inputs.Count:o.Layout=="column"?1:Math.Min(o.Columns,inputs.Count);var rows=(int)Math.Ceiling(inputs.Count/(double)columns);var width=o.Width;var height=o.Height;
        CheckCanvas(width,height);var canvas=new MagickImage(ParseColor(o.Color),(uint)width,(uint)height);
        try
        {
            canvas.Alpha(AlphaAction.Set);
            var cw=(width-2d*o.Padding-(columns-1)*o.Spacing)/columns;var ch=(height-2d*o.Padding-(rows-1)*o.Spacing)/rows;if(cw<=0||ch<=0)throw new ArgumentException("Reduce padding or spacing to fit the images.");
            for(var n=0;n<order.Length;n++)
            {
                token.ThrowIfCancellationRequested();double x,y,cellWidth,cellHeight;
                if(o.Layout=="featured")
                {
                    var availableWidth=width-2d*o.Padding;var availableHeight=height-2d*o.Padding;var largeWidth=(availableWidth-o.Spacing)*.62;var smallHeight=(availableHeight-(order.Length-2)*o.Spacing)/(order.Length-1);
                    if(largeWidth<=0||smallHeight<=0)throw new ArgumentException("Reduce spacing to fit the featured layout.");
                    (x,y,cellWidth,cellHeight)=n==0?(o.Padding,o.Padding,largeWidth,availableHeight):(o.Padding+largeWidth+o.Spacing,o.Padding+(n-1)*(smallHeight+o.Spacing),availableWidth-largeWidth-o.Spacing,smallHeight);
                }
                else(x,y,cellWidth,cellHeight)=(o.Padding+n%columns*(cw+o.Spacing),o.Padding+n/columns*(ch+o.Spacing),cw,ch);
                using var image=Load(inputs[order[n]]);Fitted(canvas,image,x,y,cellWidth,cellHeight,o.Fill,o.CornerRadius);
            }
            return canvas;
        }
        catch{canvas.Dispose();throw;}
    }
    internal static void Redact(MagickImage image,PixelRect[] regions,string style)
    {
        if(regions.Length==0)throw new ArgumentException("Draw at least one area to redact.");CheckCanvas((int)image.Width,(int)image.Height);
        foreach(var r in regions)
        {
            if(r.Width<1||r.Height<1||r.X<0||r.Y<0||r.Right>image.Width||r.Bottom>image.Height)throw new ArgumentException("Keep redaction areas inside the image.");
            if(style=="solid"){new Drawables().FillColor(MagickColors.Black).StrokeColor(MagickColors.Transparent).Rectangle(r.X,r.Y,r.Right-1,r.Bottom-1).Draw(image);continue;}
            using var area=(MagickImage)image.Clone();area.Crop(new MagickGeometry(r.X,r.Y,(uint)r.Width,(uint)r.Height));area.ResetPage();
            if(style=="pixelate")
            {
                // 16-pixel blocks, scaled back up without smoothing so nothing readable survives.
                area.Sample(new MagickGeometry((uint)Math.Max(1,r.Width/16),(uint)Math.Max(1,r.Height/16)){IgnoreAspectRatio=true});
                area.Sample(new MagickGeometry((uint)r.Width,(uint)r.Height){IgnoreAspectRatio=true});
            }
            else area.Blur(0,24);
            image.Composite(area,r.X,r.Y,CompositeOperator.Over);
        }
    }
    /// QR codes in an image (BGRA through ZXing, as on Windows).
    public static string[] DecodeQr(MagickImage image)
    {
        using var copy=(MagickImage)image.Clone();copy.Alpha(AlphaAction.Set);
        using var pixels=copy.GetPixelsUnsafe();var bgra=pixels.ToByteArray(PixelMapping.BGRA)??[];
        var reader=new ZXing.BarcodeReaderGeneric{AutoRotate=true,Options=new ZXing.Common.DecodingOptions{TryHarder=true,PossibleFormats=[ZXing.BarcodeFormat.QR_CODE]}};
        return reader.DecodeMultiple(bgra,(int)copy.Width,(int)copy.Height,ZXing.RGBLuminanceSource.BitmapFormat.BGRA32)?.Select(r=>r.Text).Distinct().ToArray()??[];
    }
}
