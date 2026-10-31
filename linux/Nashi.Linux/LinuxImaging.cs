namespace Nashi.Linux;

/// The small part of the Windows NativePdf and ImageEngine APIs that the ported document code uses.
internal static class NativePdf
{
    public static Task<byte[][]> RenderAsync(string path,int dpi=144,CancellationToken token=default,string password="")=>Task.Run(()=>PdfRaster.RenderAll(path,dpi,token,password),token);
    public static Task<Dictionary<int,byte[]>> RenderPagesAsync(string path,IReadOnlyCollection<int> pages,int dpi=144,CancellationToken token=default,string password="")=>Task.Run(()=>
    {
        using var pdf=PdfRaster.Open(path,password);var images=new Dictionary<int,byte[]>();long total=0;
        foreach(var page in pages.Distinct()){if(page<1||page>pdf.PageCount)continue;var png=pdf.RenderPage(page-1,dpi,token);total+=png.LongLength;if(total>512_000_000)throw new InvalidDataException("Rendered pages exceed 512 MB. Choose a lower DPI or split the PDF first.");images[page]=png;}
        return images;
    },token);
}
internal sealed record PngSize(int PixelWidth,int PixelHeight);
internal static class ImageEngine
{
    public static PngSize FromPng(byte[] png){var info=new ImageMagick.MagickImageInfo(png);return new((int)info.Width,(int)info.Height);}
}
