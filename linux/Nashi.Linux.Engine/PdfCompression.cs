using ImageMagick;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace Nashi.Windows;
// Linux adapter of Nashi.Windows/PdfCompression.cs; uses Poppler and Magick in place of WPF.
internal static class PdfCompression
{
    internal static async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> inputs,string destination,LinuxOptions options,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();
        foreach(var input in inputs)
        {
            var original=new FileInfo(input).Length;var target=Compression.Target(original,options.TargetBytes,options.ReductionPercent);
            using var source=PdfReader.Open(input,options.Password,PdfDocumentOpenMode.Import);
            var featureful=source.Internals.Catalog.Elements.ContainsKey("/AcroForm")||source.Internals.Catalog.Elements.ContainsKey("/Names")||source.Internals.Catalog.Elements.ContainsKey("/Outlines")||source.Pages.Cast<PdfPage>().Any(p=>p.Elements.ContainsKey("/Annots"));
            var text=OfficeDocuments.PdfText(input,options.Password);
            if(featureful||!string.IsNullOrWhiteSpace(text))
            {
                var file=featureful?SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Compressed.pdf"):DocumentEngine.Pdf("compress",[input],tx.StagingDirectory,token:token,password:options.Password)[0];
                if(featureful)File.Copy(input,file);
                if(target is {} limit&&new FileInfo(file).Length>limit)throw new IOException("That PDF size cannot be reached while keeping selectable text, links and annotations. Choose a larger target.");
                if(new FileInfo(file).Length>original)File.Copy(input,file,true);outputs.Add(file);continue;
            }
            var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Compressed.pdf");var dpi=144;var quality=options.Quality;var success=false;
            for(var attempt=0;attempt<14;attempt++)
            {
                token.ThrowIfCancellationRequested();if(File.Exists(output))File.Delete(output);var images=await NativePdf.RenderAsync(input,dpi,token,options.Password);using var pdf=new PdfDocument();
                for(var n=0;n<images.Length;n++)
                {
                    token.ThrowIfCancellationRequested();using var raster=new MagickImage(images[n]);raster.Quality=(uint)Math.Clamp(quality,1,100);
                    using var jpeg=new MemoryStream(raster.ToByteArray(MagickFormat.Jpeg));using var image=XImage.FromStream(jpeg);var page=pdf.AddPage();var originalPage=source.Pages[n];var rotated=originalPage.Rotate%180!=0;
                    page.Width=rotated?originalPage.Height:originalPage.Width;page.Height=rotated?originalPage.Width:originalPage.Height;using var drawing=XGraphics.FromPdfPage(page);drawing.DrawImage(image,0,0,page.Width.Point,page.Height.Point);
                    progress?.Report(new(null,$"Compressing PDF · attempt {attempt+1} · page {n+1}/{images.Length}…"));
                }
                pdf.Save(output);if(target is null||new FileInfo(output).Length<=target){success=true;break;}dpi=Math.Max(36,(int)(dpi*.8));quality=Math.Max(5,(int)(quality*.75));
            }
            if(!success)throw new IOException("The requested PDF size could not be reached. Choose a larger target.");
            if(new FileInfo(output).Length>original)File.Copy(input,output,true);outputs.Add(output);
        }
        return tx.Publish(outputs,token);
    }
}
internal static class Compression
{
    internal static long? Target(long original,long? bytes,double? percent)
    {
        if(bytes is <=0||percent is {} n&&(!double.IsFinite(n)||n<=0||n>=100))throw new ArgumentException("Choose a positive target size or a reduction between 0 and 100%.");
        var target=bytes??(percent is {} p?(long)(original*(1-p/100)):null);if(target is <=0)throw new ArgumentException("Choose a larger target size.");return target;
    }
}
