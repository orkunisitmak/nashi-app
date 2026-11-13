// Ported from windows/Nashi.Windows for Linux: WPF imaging -> Magick.NET, PDFium-Win32 -> LinuxPdf.
using System.Text;
using System.Net;
using Nashi.Core;
namespace Nashi.Linux.Engine;
internal static class PdfDocumentMedia
{
    // Windows uses Windows.Media.Ocr; Linux uses Tesseract when it is installed
    // (for example: sudo apt install tesseract-ocr tesseract-ocr-tur).
    internal static string? Tesseract=>LinuxShell.FindInPath("tesseract");
    internal static bool OcrAvailable=>Tesseract is not null;
    internal static async Task<string> Recognize(byte[] image,CancellationToken token)
    {
        var tesseract=Tesseract??throw new InvalidOperationException("Install Tesseract OCR (tesseract-ocr) to recognize scanned PDF text.");token.ThrowIfCancellationRequested();
        var work=Path.Combine(Path.GetTempPath(),"NashiOcr-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        try
        {
            var input=Path.Combine(work,"page.png");
            using(var source=Magick.FromPng(image)){var factor=Math.Min(1,2400d/Math.Max(source.Width,source.Height));if(factor<1)source.Resize((uint)Math.Max(1,source.Width*factor),(uint)Math.Max(1,source.Height*factor));await source.WriteAsync(input,ImageMagick.MagickFormat.Png,token);}
            var result=await ProcessRunner.RunAsync(tesseract,[input,"stdout","-l",await OcrLanguagesAsync(tesseract,token)],token);token.ThrowIfCancellationRequested();
            return string.Join("\n",result.Output.Replace("\f","").Split('\n').Select(l=>l.TrimEnd()).Where(l=>l.Length>0));
        }
        finally{try{Directory.Delete(work,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private static string? languages;
    /// English plus Turkish when that language pack is installed; whatever is installed otherwise.
    private static async Task<string> OcrLanguagesAsync(string tesseract,CancellationToken token)
    {
        if(languages is not null)return languages;
        var listed=(await ProcessRunner.RunAsync(tesseract,["--list-langs"],token)).Output.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Where(l=>!l.Contains(' ')&&l!="osd").ToHashSet();
        var wanted=new[]{"eng","tur"}.Where(listed.Contains).ToArray();
        return languages=wanted.Length>0?string.Join('+',wanted):listed.FirstOrDefault()??"eng";
    }
    internal static async Task<string> Text(string input,string password,CancellationToken token)
    {
        using var pdf=UglyToad.PdfPig.PdfDocument.Open(input,new(){Password=password});byte[][]? images=null;var text=new List<string>();foreach(var page in pdf.GetPages())
        {token.ThrowIfCancellationRequested();var content=UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);if(string.IsNullOrWhiteSpace(content)){images??=await LinuxPdf.RenderAsync(input,180,token,password);content=await Recognize(images[page.Number-1],token);}text.Add(content);}var output=string.Join("\n\n\f\n\n",text);if(string.IsNullOrWhiteSpace(output))throw new InvalidDataException("No text could be extracted or recognized in this PDF.");return output;
    }
    internal static async Task<string> Slides(string input,string work,int dpi,string password,CancellationToken token)
    {
        var pages=await LinuxPdf.RenderAsync(input,dpi,token,password);if(pages.Length==0)throw new InvalidDataException("This PDF contains no pages.");var first=Magick.Size(pages[0]);var width=first.Width*72d/dpi;var height=first.Height*72d/dpi;string N(double n)=>n.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);var slides=new StringBuilder();
        for(var n=0;n<pages.Length;n++){token.ThrowIfCancellationRequested();var image=Magick.Size(pages[n]);var scale=Math.Min(width/image.Width,height/image.Height);var w=image.Width*scale;var h=image.Height*scale;slides.Append($"<draw:page draw:name=\"Page {n+1}\" draw:master-page-name=\"PDFMaster\"><draw:frame svg:x=\"{N((width-w)/2)}pt\" svg:y=\"{N((height-h)/2)}pt\" svg:width=\"{N(w)}pt\" svg:height=\"{N(h)}pt\"><draw:image><office:binary-data>{Convert.ToBase64String(pages[n])}</office:binary-data></draw:image></draw:frame></draw:page>");}
        var xml=$"<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.presentation\"><office:styles/><office:automatic-styles><style:page-layout style:name=\"PDFCanvas\"><style:page-layout-properties fo:page-width=\"{N(width)}pt\" fo:page-height=\"{N(height)}pt\" fo:margin=\"0pt\"/></style:page-layout></office:automatic-styles><office:master-styles><style:master-page style:name=\"PDFMaster\" style:page-layout-name=\"PDFCanvas\"/></office:master-styles><office:body><office:presentation>{slides}</office:presentation></office:body></office:document>";var file=Path.Combine(work,"PDF slides.fodp");await File.WriteAllTextAsync(file,xml,token);return file;
    }
}
