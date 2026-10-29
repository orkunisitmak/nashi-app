using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
namespace Nashi.Core;

public sealed record RasterOptions(int? Width=null,int? Height=null,int X=0,int Y=0,
    int Rotation=90,int Quality=85,bool PreserveMetadata=true,PhotoAdjustments? Adjustments=null);

/// Pixel operations without WPF types, using the existing bounded image decoder
/// and export transaction. A batch publishes only after every image succeeds.
public static class RasterEngine
{
    public static readonly string[] Formats=["png","jpg","webp","avif","tiff","bmp","gif","pdf"];
    public static IReadOnlyList<string> Export(string action,IReadOnlyList<string> inputs,string format,
        string destination,RasterOptions options,IProgress<OperationProgress>? progress=null,CancellationToken token=default)
    {
        if(inputs.Count==0||!Formats.Contains(format)||options.Quality is <1 or >100||
            options.Width is <1 or >30000||options.Height is <1 or >30000)
            throw new ArgumentException("Choose images, an output format and valid dimensions/quality.");
        if(action is not ("convert" or "resize" or "rotate" or "flip" or "crop" or "editImage" or "removeMetadata" or "createPDF"))
            throw new ArgumentException("Unknown image operation.");
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();
        using var pdf=format=="pdf"||action=="createPDF"?new PdfDocument():null;
        for(var index=0;index<inputs.Count;index++)
        {
            token.ThrowIfCancellationRequested();using var image=PhotoCodec.Read(inputs[index]);
            switch(action)
            {
                case "resize":
                    if(options.Width is null&&options.Height is null)throw new ArgumentException("Enter a width or height.");
                    var width=options.Width??checked((int)Math.Round(image.Width*(double)options.Height!.Value/image.Height));
                    var height=options.Height??checked((int)Math.Round(image.Height*(double)width/image.Width));
                    if(width<1||height<1||(long)width*height>100_000_000)throw new ArgumentException("Choose dimensions within 100 MP.");
                    image.Resize(new MagickGeometry((uint)width,(uint)height){IgnoreAspectRatio=true});break;
                case "crop":
                    if(options.Width is not {} cw||options.Height is not {} ch||options.X<0||options.Y<0||
                        (long)options.X+cw>image.Width||(long)options.Y+ch>image.Height)
                        throw new ArgumentException("Enter a crop rectangle inside the oriented image.");
                    image.Crop(new MagickGeometry(options.X,options.Y,(uint)cw,(uint)ch));image.ResetPage();break;
                case "rotate":
                    if(options.Rotation is not (90 or 180 or 270))throw new ArgumentException("Choose 90, 180 or 270 degrees.");
                    image.Rotate(options.Rotation);break;
                case "flip":image.Flop();break;
                case "editImage":image.Read(PhotoCodec.Adjust(image.ToByteArray(MagickFormat.Png),options.Adjustments??new()));break;
            }
            // AutoOrient materializes orientation. Never carry a stale rotation tag.
            image.Orientation=Orientation.TopLeft;
            if(!options.PreserveMetadata||action=="removeMetadata")image.Strip();
            if(pdf is not null)
            {
                using var stream=new MemoryStream(image.ToByteArray(MagickFormat.Png));using var picture=XImage.FromStream(stream);
                var page=pdf.AddPage();page.Width=XUnit.FromPoint(image.Width*72d/96);page.Height=XUnit.FromPoint(image.Height*72d/96);
                using var graphics=XGraphics.FromPdfPage(page);graphics.DrawImage(picture,0,0,page.Width.Point,page.Height.Point);
            }
            else
            {
                var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(inputs[index])+" Export."+format);
                image.Quality=(uint)options.Quality;image.Write(output,PhotoCodec.Format(format));outputs.Add(output);
            }
            progress?.Report(new((index+1d)/inputs.Count*.98,"Processing images…"));
        }
        if(pdf is not null){var output=Path.Combine(tx.StagingDirectory,"Images.pdf");pdf.Save(output);outputs.Add(output);}
        return tx.Publish(outputs,token);
    }
}
