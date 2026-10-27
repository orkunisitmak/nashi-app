using System.Text;
using System.Net;
using Nashi.Core;
// Ported from windows/Nashi.Windows for Linux: PDFium (Linux build), Magick.NET and
// Tesseract replace WPF imaging and Windows.Media.Ocr; the conversion logic is unchanged.
namespace Nashi.Linux.Engine;
internal static class PdfDocumentMedia
{
    internal static bool OcrAvailable=>LinuxPdf.Tesseract is not null;
    internal static Task<string> Recognize(byte[] image,CancellationToken token)=>LinuxPdf.RecognizeAsync(image,token);
    internal static async Task<string> Text(string input,string password,CancellationToken token)
    {
        using var pdf=UglyToad.PdfPig.PdfDocument.Open(input,new(){Password=password});byte[][]? images=null;var text=new List<string>();foreach(var page in pdf.GetPages())
        {token.ThrowIfCancellationRequested();var content=UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);if(string.IsNullOrWhiteSpace(content)){images??=await LinuxPdf.RenderAsync(input,180,token,password);content=await Recognize(images[page.Number-1],token);}text.Add(content);}var output=string.Join("\n\n\f\n\n",text);if(string.IsNullOrWhiteSpace(output))throw new InvalidDataException("No text could be extracted or recognized in this PDF.");return output;
    }
    internal static async Task<string> Slides(string input,string work,int dpi,string password,CancellationToken token)
    {
        var pages=await LinuxPdf.RenderAsync(input,dpi,token,password);if(pages.Length==0)throw new InvalidDataException("This PDF contains no pages.");var first=LinuxImages.Size(pages[0]);var width=first.PixelWidth*72d/dpi;var height=first.PixelHeight*72d/dpi;string N(double n)=>n.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);var slides=new StringBuilder();
        for(var n=0;n<pages.Length;n++){token.ThrowIfCancellationRequested();var image=LinuxImages.Size(pages[n]);var scale=Math.Min(width/image.PixelWidth,height/image.PixelHeight);var w=image.PixelWidth*scale;var h=image.PixelHeight*scale;slides.Append($"<draw:page draw:name=\"Page {n+1}\" draw:master-page-name=\"PDFMaster\"><draw:frame svg:x=\"{N((width-w)/2)}pt\" svg:y=\"{N((height-h)/2)}pt\" svg:width=\"{N(w)}pt\" svg:height=\"{N(h)}pt\"><draw:image><office:binary-data>{Convert.ToBase64String(pages[n])}</office:binary-data></draw:image></draw:frame></draw:page>");}
        var xml=$"<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.presentation\"><office:styles/><office:automatic-styles><style:page-layout style:name=\"PDFCanvas\"><style:page-layout-properties fo:page-width=\"{N(width)}pt\" fo:page-height=\"{N(height)}pt\" fo:margin=\"0pt\"/></style:page-layout></office:automatic-styles><office:master-styles><style:master-page style:name=\"PDFMaster\" style:page-layout-name=\"PDFCanvas\"/></office:master-styles><office:body><office:presentation>{slides}</office:presentation></office:body></office:document>";var file=Path.Combine(work,"PDF slides.fodp");await File.WriteAllTextAsync(file,xml,token);return file;
    }
}
