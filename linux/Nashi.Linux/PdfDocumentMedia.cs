using System.Text;
using System.Net;
using Nashi.Core;
namespace Nashi.Linux;
internal static class PdfDocumentMedia
{
    private static string[]? installed;
    /// English and the interface language first (tur for Turkish), then others, at most six: Tesseract lists
    /// languages alphabetically, so the first six of a full install would be afr, amh, ara…
    private static async Task<string[]> Languages(string tesseract,CancellationToken token)
    {
        if(installed is null)
        {
            var listed=await ProcessRunner.RunAsync(tesseract,["--list-langs"],token);
            installed=(listed.Output+"\n"+listed.Error).Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Where(l=>l.Length is >= 3 and <= 12&&l.All(c=>char.IsAsciiLetterLower(c)||c=='_')&&l is not "osd").ToArray();
        }
        string[] preferred=Localizer.Language=="tr"?["tur","eng"]:["eng","tur"];
        return preferred.Where(installed.Contains).Concat(installed.Except(preferred)).Take(6).ToArray();
    }
    /// Scanned pages are read by Tesseract when it is installed. The page image goes to a private temporary
    /// folder (0700), never to a world-readable file in /tmp.
    internal static bool OcrAvailable=>Operations.Executable("tesseract") is not null;
    internal static async Task<string> Recognize(byte[] image,CancellationToken token)
    {
        var tesseract=Operations.Executable("tesseract")??throw new InvalidOperationException("Install tesseract (with your language, e.g. tesseract-data-tur) to recognize scanned PDF text.");
        var languages=await Languages(tesseract,token);
        if(languages.Length==0)throw new InvalidOperationException("Install a Tesseract language (e.g. tesseract-data-eng) to recognize scanned PDF text.");
        var folder=Directory.CreateTempSubdirectory("nashi-ocr-").FullName;
        try
        {
            var file=Path.Combine(folder,"page.png");
            // Same 2400 px ceiling as Windows keeps recognition time predictable.
            using(var page=new ImageMagick.MagickImage(image)){if(Math.Max(page.Width,page.Height)>2400)page.Resize(new ImageMagick.MagickGeometry(2400,2400));page.Write(file,ImageMagick.MagickFormat.Png);}
            var result=await ProcessRunner.RunAsync(tesseract,[file,"stdout","-l",string.Join('+',languages)],token);return result.Output.Trim();
        }
        finally{try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    internal static Task<string[]> LanguagesFor(string tesseract,CancellationToken token)=>Languages(tesseract,token);
    internal static async Task<string> Text(string input,string password,CancellationToken token)
    {
        using var pdf=UglyToad.PdfPig.PdfDocument.Open(input,new(){Password=password});var text=new List<string>();foreach(var page in pdf.GetPages())
        {token.ThrowIfCancellationRequested();var content=UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);
            // A blank page needs OCR only when Tesseract is there, and then only that page is rendered.
            if(string.IsNullOrWhiteSpace(content)&&OcrAvailable){var rendered=await NativePdf.RenderPagesAsync(input,[page.Number],180,token,password);if(rendered.TryGetValue(page.Number,out var png))content=await Recognize(png,token);}text.Add(content);}var output=string.Join("\n\n\f\n\n",text);if(string.IsNullOrWhiteSpace(output))throw new InvalidDataException("No text could be extracted or recognized in this PDF.");return output;
    }
    internal static async Task<string> Slides(string input,string work,int dpi,string password,CancellationToken token)
    {
        var pages=await NativePdf.RenderAsync(input,dpi,token,password);if(pages.Length==0)throw new InvalidDataException("This PDF contains no pages.");var first=ImageEngine.FromPng(pages[0]);var width=first.PixelWidth*72d/dpi;var height=first.PixelHeight*72d/dpi;string N(double n)=>n.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);var slides=new StringBuilder();
        for(var n=0;n<pages.Length;n++){token.ThrowIfCancellationRequested();var image=ImageEngine.FromPng(pages[n]);var scale=Math.Min(width/image.PixelWidth,height/image.PixelHeight);var w=image.PixelWidth*scale;var h=image.PixelHeight*scale;slides.Append($"<draw:page draw:name=\"Page {n+1}\" draw:master-page-name=\"PDFMaster\"><draw:frame svg:x=\"{N((width-w)/2)}pt\" svg:y=\"{N((height-h)/2)}pt\" svg:width=\"{N(w)}pt\" svg:height=\"{N(h)}pt\"><draw:image><office:binary-data>{Convert.ToBase64String(pages[n])}</office:binary-data></draw:image></draw:frame></draw:page>");}
        var xml=$"<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.presentation\"><office:styles/><office:automatic-styles><style:page-layout style:name=\"PDFCanvas\"><style:page-layout-properties fo:page-width=\"{N(width)}pt\" fo:page-height=\"{N(height)}pt\" fo:margin=\"0pt\"/></style:page-layout></office:automatic-styles><office:master-styles><style:master-page style:name=\"PDFMaster\" style:page-layout-name=\"PDFCanvas\"/></office:master-styles><office:body><office:presentation>{slides}</office:presentation></office:body></office:document>";var file=Path.Combine(work,"PDF slides.fodp");await File.WriteAllTextAsync(file,xml,token);return file;
    }
}
