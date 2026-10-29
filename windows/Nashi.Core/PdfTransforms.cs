using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
namespace Nashi.Core;
public static partial class DocumentEngine
{
    public static int[] PageRange(string text,int count)
    {
        if(string.IsNullOrWhiteSpace(text))return Enumerable.Range(1,count).ToArray();var pages=new List<int>();foreach(var part in text.Split(','))
        {
            var p=part.Trim().Split('-',StringSplitOptions.TrimEntries);if(p.Length is <1 or >2||!int.TryParse(p[0],out var a)||a<1||a>count)throw new ArgumentException("Choose page ranges such as 1-3, 5 within the PDF page count.");var b=a;if(p.Length==2&&(!int.TryParse(p[1],out b)||b<1||b>count))throw new ArgumentException("A page range is outside the PDF.");for(var n=a;;n+=a<=b?1:-1){if(pages.Count>=10000)throw new ArgumentException("Choose up to 10,000 output pages.");pages.Add(n);if(n==b)break;}
        }return pages.ToArray();
    }
    private static void UniformWidths(string file,CancellationToken token)
    {
        var bytes=File.ReadAllBytes(file);using var memory=new MemoryStream(bytes);using var form=XPdfForm.FromStream(memory);var width=double.PositiveInfinity;for(var n=1;n<=form.PageCount;n++){form.PageNumber=n;width=Math.Min(width,form.PointWidth);}using var result=new PdfDocument();for(var n=1;n<=form.PageCount;n++){token.ThrowIfCancellationRequested();form.PageNumber=n;var page=result.AddPage();page.Width=XUnit.FromPoint(width);page.Height=XUnit.FromPoint(form.PointHeight*width/form.PointWidth);using var graphics=XGraphics.FromPdfPage(page);graphics.DrawImage(form,0,0,page.Width.Point,page.Height.Point);}using var output=new MemoryStream();result.Save(output);File.WriteAllBytes(file,output.ToArray());
    }
}
