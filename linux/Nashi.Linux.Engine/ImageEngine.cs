using ImageMagick.Drawing;
using ImageMagick;
using Nashi.Core;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
namespace Nashi.Windows;

public sealed record ImageDimensions(int PixelWidth,int PixelHeight);
public static class ImageEngine
{
    public static ImageDimensions FromPng(byte[] bytes)
    { using var image=new MagickImage(bytes);return new((int)image.Width,(int)image.Height); }
    public static async Task<IReadOnlyList<string>> Apply(string tool,IReadOnlyList<string> inputs,string destination,LinuxOptions o,string? format,CancellationToken token,IProgress<OperationProgress>? progress)
    {
        if(inputs.Count==0)throw new ArgumentException("Choose images first.");
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();
        if(tool=="createPDF"||format=="pdf")
        {
            using var pdf=new PdfDocument();
            foreach(var input in inputs)
            {
                token.ThrowIfCancellationRequested();using var source=PhotoCodec.Read(input);using var stream=new MemoryStream(source.ToByteArray(MagickFormat.Png));using var image=XImage.FromStream(stream);
                var page=pdf.AddPage();page.Width=XUnit.FromPoint(source.Width*72d/96);page.Height=XUnit.FromPoint(source.Height*72d/96);
                using var g=XGraphics.FromPdfPage(page);g.DrawImage(image,0,0,page.Width.Point,page.Height.Point);
            }
            var output=Path.Combine(tx.StagingDirectory,"Images.pdf");pdf.Save(output);return tx.Publish([output],token);
        }
        if(tool=="createCollage")
        {
            using var canvas=Compositions.Collage(inputs,o.Collage,token);
            var output=Path.Combine(tx.StagingDirectory,"Collage.png");canvas.Write(output,MagickFormat.Png);return tx.Publish([output],token);
        }
        for(var i=0;i<inputs.Count;i++)
        {
            token.ThrowIfCancellationRequested();using var image=PhotoCodec.Read(inputs[i]);
            if(tool=="editImage") { image.Read(PhotoCodec.Adjust(image.ToByteArray(MagickFormat.Png),o.Adjustments));if(o.Width is {} w||o.Height is not null){var width=o.Width??(int)image.Width;var height=o.Height??(int)image.Height;ValidateSize(width,height);image.Resize(new MagickGeometry((uint)width,(uint)height){IgnoreAspectRatio=true});}if(o.Rotation!=0)image.Rotate(o.Rotation);if(o.FlipHorizontal)image.Flop();if(o.FlipVertical)image.Flip(); }
            if(tool=="cropImage") { ValidateRegion(o.X,o.Y,o.Width,o.Height,image);image.Crop(new MagickGeometry(o.X,o.Y,(uint)o.Width!.Value,(uint)o.Height!.Value));image.ResetPage(); }
            if(tool=="redactImage")
            {
                var regions=o.Regions.Length>0?o.Regions:[new MediaRegion(o.X,o.Y,o.Width??0,o.Height??0)];
                foreach(var region in regions){ValidateRegion((int)region.X,(int)region.Y,(int)region.Width,(int)region.Height,image);
                    if(o.RedactionStyle=="solid")new Drawables().FillColor(MagickColors.Black).Rectangle(region.X,region.Y,region.X+region.Width-1,region.Y+region.Height-1).Draw(image);
                    else{using var part=image.Clone();part.Crop(new MagickGeometry((int)region.X,(int)region.Y,(uint)region.Width,(uint)region.Height));part.ResetPage();
                        if(o.RedactionStyle=="pixelate"){part.Resize(Math.Max(1,(uint)region.Width/16),Math.Max(1,(uint)region.Height/16));part.FilterType=FilterType.Point;part.Resize((uint)region.Width,(uint)region.Height);}else part.GaussianBlur(24);
                        image.Composite(part,(int)region.X,(int)region.Y,CompositeOperator.Over);
                    }
                }
            }
            if(tool=="annotateImage")
            {
                if(o.AnnotatedPng is not null){image.Read(o.AnnotatedPng);}else {
                if(string.IsNullOrWhiteSpace(o.Text))throw new ArgumentException("Enter annotation text.");
                new Drawables().Font(o.Font).FontPointSize(o.FontSize).FillColor(new MagickColor(o.Ink)).Text(o.X,o.Y+o.FontSize,o.Text).Draw(image);
                }
            }
            if(tool=="frameImage"){using var framed=Compositions.Frame(image,o.Frame);image.Read(framed.ToByteArray(MagickFormat.Png));}
            var ext=tool is "frameImage" or "redactImage"?"png":format??FeatureCatalog.Extension(inputs[i]);if(ext=="svg")ext="png";
            var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(inputs[i])+" "+tool+"."+ext);
            image.Strip();
            var target=tool=="compress"?Compression.Target(new FileInfo(inputs[i]).Length,o.TargetBytes,o.ReductionPercent):null;
            var quality=Math.Clamp(o.Quality,1,100);var fitted=false;
            for(var attempt=0;attempt<24;attempt++)
            {
                token.ThrowIfCancellationRequested();if(File.Exists(output))File.Delete(output);
                PhotoCodec.EncodePng(image.ToByteArray(MagickFormat.Png),output,ext,quality,SystemTools.Find("ffmpeg"),token);
                if(target is null||new FileInfo(output).Length<=target){fitted=true;break;}
                if(quality>15&&ext!="png")quality=Math.Max(1,(int)(quality*.72));
                else image.Resize((uint)Math.Max(1,(int)(image.Width*.8)),(uint)Math.Max(1,(int)(image.Height*.8)));
            }
            if(!fitted)throw new IOException("The requested image size could not be reached. Choose a larger target.");
            if(tool=="compress"&&ext==FeatureCatalog.Extension(inputs[i])&&new FileInfo(inputs[i]).Length<new FileInfo(output).Length)File.Copy(inputs[i],output,true);
            if(tool=="convert"&&o.PreserveImageMetadata&&ext is not ("pdf" or "bmp" or "gif"))await new MetadataService("").CopyImageTagsAsync(inputs[i],output,token);
            outputs.Add(output);
            progress?.Report(new((i+1d)/inputs.Count,"Saving images…"));
        }
        await Task.CompletedTask;return tx.Publish(outputs,token);
    }
    private static void ValidateSize(int w,int h)
    {if(w<1||h<1||w>30000||h>30000||(long)w*h>100_000_000)throw new ArgumentException("Choose positive dimensions up to 30,000 pixels and 100 MP.");}
    private static void ValidateRegion(int x,int y,int? w,int? h,MagickImage image)
    {if(x<0||y<0||w is null or <1||h is null or <1||(long)x+w>image.Width||(long)y+h>image.Height)throw new ArgumentException("Choose a region inside the image.");}
}
