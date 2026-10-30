using System.Windows.Media.Imaging;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace Nashi.Windows;
internal static class PdfCompression
{
    internal static async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> inputs,string destination,ImageOptions options,string password,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();foreach(var input in inputs)
        {
            var original=new FileInfo(input).Length;var target=ImageEngine.CompressionTarget(original,options.TargetBytes,options.ReductionPercent);using var source=PdfReader.Open(input,password,PdfDocumentOpenMode.Import);
            var featureful=source.Internals.Catalog.Elements.ContainsKey("/AcroForm")||source.Internals.Catalog.Elements.ContainsKey("/Names")||source.Internals.Catalog.Elements.ContainsKey("/Outlines")||source.Internals.Catalog.Elements.ContainsKey("/AF")||source.Internals.Catalog.Elements.ContainsKey("/Collection")||source.Pages.Cast<PdfPage>().Any(p=>p.Elements.ContainsKey("/Annots"));var text=OfficeDocuments.PdfText(input,password);
            if(featureful||!string.IsNullOrWhiteSpace(text))
            {
                var file=featureful?SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Compressed.pdf"):DocumentEngine.Pdf("compress",[input],tx.StagingDirectory,token:token,password:password)[0];if(featureful)File.Copy(input,file);
                // A rewrite can be larger than the source; keep the source before checking the target it may already meet.
                if(new FileInfo(file).Length>original)File.Copy(input,file,true);if(target is {} limit&&new FileInfo(file).Length>limit)throw new IOException("That PDF size cannot be reached while keeping selectable text, links and annotations. Choose a larger target.");outputs.Add(file);continue;
            }
            var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Compressed.pdf");var dpi=144;var quality=options.Quality;var success=false;
            for(var attempt=0;attempt<14;attempt++)
            {
                token.ThrowIfCancellationRequested();if(File.Exists(output))File.Delete(output);var images=await NativePdf.RenderAsync(input,dpi,token,password);using var pdf=new PdfDocument();for(var n=0;n<images.Length;n++)
                {
                    token.ThrowIfCancellationRequested();var bitmap=ImageEngine.FromPng(images[n]);var encoder=new JpegBitmapEncoder{QualityLevel=Math.Clamp(quality,1,100)};encoder.Frames.Add(BitmapFrame.Create(bitmap));using var jpeg=new MemoryStream();encoder.Save(jpeg);jpeg.Position=0;using var image=XImage.FromStream(jpeg);var page=pdf.AddPage();var originalPage=source.Pages[n];var rotated=originalPage.Rotate%180!=0;page.Width=rotated?originalPage.Height:originalPage.Width;page.Height=rotated?originalPage.Width:originalPage.Height;using var drawing=XGraphics.FromPdfPage(page);drawing.DrawImage(image,0,0,page.Width.Point,page.Height.Point);progress?.Report(new(null,$"Compressing PDF · attempt {attempt+1} · page {n+1}/{images.Length}…"));
                }
                pdf.Save(output);if(target is null||new FileInfo(output).Length<=target){success=true;break;}dpi=Math.Max(36,(int)(dpi*.8));quality=Math.Max(5,(int)(quality*.75));
            }
            if(!success&&!(target is {} fits&&original<=fits))throw new IOException("The requested PDF size could not be reached. Choose a larger target.");if(!success||new FileInfo(output).Length>original)File.Copy(input,output,true);outputs.Add(output);
        }
        return tx.Publish(outputs,token);
    }
}
