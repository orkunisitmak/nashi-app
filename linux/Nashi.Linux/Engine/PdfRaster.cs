using System.Runtime.InteropServices;
using ImageMagick;
using Nashi.Core;
namespace Nashi.Linux.Engine;

/// CPU-only PDF rasterization through the same PDFium build the Windows app uses
/// (bblanchon.PDFium.Linux ships libpdfium.so). PDFium is not thread-safe, so every
/// call is serialized, and the source bytes stay pinned while a document is open.
internal sealed class PdfRaster:IDisposable
{
    private static readonly object gate=new();private static bool initialized;
    private nint document;private GCHandle source;
    internal int PageCount{get;}
    private PdfRaster(string path,string password)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);var length=input.Length;
        if(length<1||length>200_000_000)throw new InvalidDataException("Choose a PDF smaller than 200 MB.");
        var data=new byte[checked((int)length)];input.ReadExactly(data);
        lock(gate)
        {
            if(!initialized){Native.FPDF_InitLibrary();initialized=true;}
            source=GCHandle.Alloc(data,GCHandleType.Pinned);
            try
            {
                document=Native.FPDF_LoadMemDocument64(source.AddrOfPinnedObject(),(nuint)data.LongLength,password);
                if(document==0)throw new InvalidDataException(Native.FPDF_GetLastError()==4?"The PDF password is missing or incorrect.":"This PDF could not be opened.");
                PageCount=Native.FPDF_GetPageCount(document);
                if(PageCount<1||PageCount>1000)throw new InvalidDataException("Choose a PDF with 1 to 1,000 pages.");
            }
            catch{if(document!=0){Native.FPDF_CloseDocument(document);document=0;}if(source.IsAllocated)source.Free();throw;}
        }
    }
    internal static PdfRaster Open(string path,string password="")=>new(path,password);
    /// Returns the page as PNG bytes on a white background.
    internal byte[] RenderPage(int index,int dpi,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(dpi<36||dpi>600)throw new ArgumentOutOfRangeException(nameof(dpi));
        byte[] pixels;int width,height;
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(document==0,this);
            if(index<0||index>=PageCount)throw new ArgumentOutOfRangeException(nameof(index));
            var page=Native.FPDF_LoadPage(document,index);if(page==0)throw new InvalidDataException("This PDF page could not be read.");
            try
            {
                var w=Native.FPDF_GetPageWidth(page)*dpi/72;var h=Native.FPDF_GetPageHeight(page)*dpi/72;
                if(!double.IsFinite(w)||!double.IsFinite(h)||w<1||h<1||w*h>100_000_000)throw new InvalidDataException("PDF page exceeds 100 MP.");
                width=checked((int)Math.Ceiling(w));height=checked((int)Math.Ceiling(h));var stride=checked(width*4);pixels=new byte[checked(stride*height)];
                var pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);nint bitmap=0;
                try
                {
                    bitmap=Native.FPDFBitmap_CreateEx(width,height,4,pinned.AddrOfPinnedObject(),stride);
                    if(bitmap==0)throw new InvalidDataException("PDF preview bitmap could not be allocated.");
                    if(Native.FPDFBitmap_FillRect(bitmap,0,0,width,height,0xffffffff)==0)throw new InvalidDataException("PDF preview background could not be prepared.");
                    // FPDF_ANNOT: draw annotations and form appearances like a viewer does.
                    Native.FPDF_RenderPageBitmap(bitmap,page,0,0,width,height,0,1);
                }
                finally{if(bitmap!=0)Native.FPDFBitmap_Destroy(bitmap);pinned.Free();}
            }
            finally{Native.FPDF_ClosePage(page);}
        }
        token.ThrowIfCancellationRequested();
        return Magick.PngFromBgra(pixels,width,height);
    }
    public void Dispose(){lock(gate){if(document==0)return;Native.FPDF_CloseDocument(document);document=0;if(source.IsAllocated)source.Free();}GC.SuppressFinalize(this);}
    ~PdfRaster(){Dispose();}
    private static class Native
    {
        [DllImport("pdfium")]internal static extern void FPDF_InitLibrary();
        [DllImport("pdfium")]internal static extern nint FPDF_LoadMemDocument64(nint data,nuint size,[MarshalAs(UnmanagedType.LPUTF8Str)]string password);
        [DllImport("pdfium")]internal static extern uint FPDF_GetLastError();
        [DllImport("pdfium")]internal static extern int FPDF_GetPageCount(nint document);
        [DllImport("pdfium")]internal static extern nint FPDF_LoadPage(nint document,int index);
        [DllImport("pdfium")]internal static extern double FPDF_GetPageWidth(nint page);
        [DllImport("pdfium")]internal static extern double FPDF_GetPageHeight(nint page);
        [DllImport("pdfium")]internal static extern nint FPDFBitmap_CreateEx(int width,int height,int format,nint data,int stride);
        [DllImport("pdfium")]internal static extern int FPDFBitmap_FillRect(nint bitmap,int left,int top,int width,int height,uint color);
        [DllImport("pdfium")]internal static extern void FPDF_RenderPageBitmap(nint bitmap,nint page,int left,int top,int width,int height,int rotate,int flags);
        [DllImport("pdfium")]internal static extern void FPDFBitmap_Destroy(nint bitmap);
        [DllImport("pdfium")]internal static extern void FPDF_ClosePage(nint page);
        [DllImport("pdfium")]internal static extern void FPDF_CloseDocument(nint document);
    }
}

public static class LinuxPdf
{
    public static Task<byte[][]> RenderAsync(string path,int dpi=144,CancellationToken token=default,string password="")=>Task.Run(()=>
    {
        using var pdf=PdfRaster.Open(path,password);var images=new List<byte[]>();long total=0;
        for(var i=0;i<pdf.PageCount;i++){token.ThrowIfCancellationRequested();var image=pdf.RenderPage(i,dpi,token);total+=image.LongLength;if(total>512_000_000)throw new InvalidDataException("Rendered pages exceed 512 MB. Choose a lower DPI or split the PDF first.");images.Add(image);}
        return images.ToArray();
    },token);
    /// Only the listed 1-based pages.
    public static Task<Dictionary<int,byte[]>> RenderPagesAsync(string path,IReadOnlyCollection<int> pages,int dpi=144,CancellationToken token=default,string password="")=>Task.Run(()=>
    {
        using var pdf=PdfRaster.Open(path,password);var images=new Dictionary<int,byte[]>();long total=0;
        foreach(var page in pages.Distinct()){token.ThrowIfCancellationRequested();if(page<1||page>pdf.PageCount)continue;var image=pdf.RenderPage(page-1,dpi,token);total+=image.LongLength;if(total>512_000_000)throw new InvalidDataException("Rendered pages exceed 512 MB. Choose a lower DPI or split the PDF first.");images[page]=image;}
        return images;
    },token);
    public static Task<int> PageCountAsync(string path,string password="")=>Task.Run(()=>{using var pdf=PdfRaster.Open(path,password);return pdf.PageCount;});
    public static Task<byte[]> RenderFirstAsync(string path,int dpi=72,string password="",CancellationToken token=default)=>Task.Run(()=>{using var pdf=PdfRaster.Open(path,password);return pdf.RenderPage(0,dpi,token);},token);
}

/// Magick.NET helpers. Every use goes through PhotoCodec's static constructor first, so the
/// core's restrictive policy (no delegates, no URL/PS/PDF coders) is always in force.
public static class Magick
{
    public static void Ensure()=>_=PhotoCodec.Format("png");
    public static byte[] PngFromBgra(byte[] bgra,int width,int height)
    {
        Ensure();using var image=new MagickImage();image.ReadPixels(bgra,new PixelReadSettings((uint)width,(uint)height,StorageType.Char,PixelMapping.BGRA));image.ColorSpace=ColorSpace.sRGB;return image.ToByteArray(MagickFormat.Png);
    }
    public static MagickImage FromPng(byte[] png){Ensure();return new MagickImage(png,new MagickReadSettings{Format=MagickFormat.Png});}
    public static (int Width,int Height) Size(byte[] png){using var image=FromPng(png);return((int)image.Width,(int)image.Height);}
    public static byte[] Crop(byte[] png,int x,int y,int width,int height)
    {
        using var image=FromPng(png);image.Crop(new MagickGeometry(x,y,(uint)width,(uint)height));image.ResetPage();return image.ToByteArray(MagickFormat.Png);
    }
}
