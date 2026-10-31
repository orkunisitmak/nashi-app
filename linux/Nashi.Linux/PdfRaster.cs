using System.Runtime.InteropServices;
using ImageMagick;
namespace Nashi.Linux;

/// CPU-only PDF rasterization with the same PDFium build as Windows. PDFium's API is not
/// thread-safe, so every call is serialized and the pinned source bytes live with the document.
public sealed class PdfRaster:IDisposable
{
    private static readonly object gate=new();private static bool initialized;
    private nint document;private GCHandle source;
    public int PageCount{get;}
    private PdfRaster(string path,string password)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);var length=input.Length;if(length<1||length>200_000_000)throw new InvalidDataException("Choose a PDF smaller than 200 MB.");
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
    public static PdfRaster Open(string path,string password="")=>new(path,password);
    public static int Pages(string path,string password=""){using var pdf=Open(path,password);return pdf.PageCount;}

    /// One page as PNG on white. Annotations with an appearance (stamps, ink, filled forms) are drawn.
    public byte[] RenderPage(int index,int dpi,CancellationToken token=default)=>RenderPage(index,dpi,0,token);
    /// Size-capped render for thumbnails: the longer side is at most `longSide` pixels.
    public byte[] Thumbnail(int index,int longSide,CancellationToken token=default)=>RenderPage(index,0,longSide,token);
    private byte[] RenderPage(int index,int dpi,int longSide,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(longSide==0&&(dpi<36||dpi>600))throw new ArgumentOutOfRangeException(nameof(dpi));
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(document==0,this);
            if(index<0||index>=PageCount)throw new ArgumentOutOfRangeException(nameof(index));
            var page=Native.FPDF_LoadPage(document,index);if(page==0)throw new InvalidDataException("This PDF page could not be read.");
            try
            {
                double pw=Native.FPDF_GetPageWidth(page),ph=Native.FPDF_GetPageHeight(page);
                // PDFium already reports the size after /Rotate, as the page is displayed.
                var scale=longSide>0?longSide/Math.Max(pw,ph):dpi/72d;var w=pw*scale;var h=ph*scale;
                if(!double.IsFinite(w)||!double.IsFinite(h)||w<1||h<1||w*h>100_000_000)throw new InvalidDataException("PDF page exceeds 100 MP.");
                var width=checked((int)Math.Ceiling(w));var height=checked((int)Math.Ceiling(h));var stride=checked(width*4);var pixels=new byte[checked(stride*height)];
                var pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);nint bitmap=0;
                try
                {
                    bitmap=Native.FPDFBitmap_CreateEx(width,height,4,pinned.AddrOfPinnedObject(),stride);
                    if(bitmap==0)throw new InvalidDataException("PDF preview bitmap could not be allocated.");
                    if(Native.FPDFBitmap_FillRect(bitmap,0,0,width,height,0xffffffff)==0)throw new InvalidDataException("PDF preview background could not be prepared.");
                    Native.FPDF_RenderPageBitmap(bitmap,page,0,0,width,height,0,1);token.ThrowIfCancellationRequested();
                }
                finally{if(bitmap!=0)Native.FPDFBitmap_Destroy(bitmap);pinned.Free();}
                using var image=new MagickImage(pixels,new MagickReadSettings{Format=MagickFormat.Bgra,Width=(uint)width,Height=(uint)height,Depth=8});
                image.Alpha(AlphaAction.Off);if(longSide==0)image.Density=new Density(dpi,dpi,DensityUnit.PixelsPerInch);
                return image.ToByteArray(MagickFormat.Png);
            }
            finally{Native.FPDF_ClosePage(page);}
        }
    }
    public void Dispose(){lock(gate){if(document==0)return;Native.FPDF_CloseDocument(document);document=0;if(source.IsAllocated)source.Free();}GC.SuppressFinalize(this);}
    ~PdfRaster(){Dispose();}

    /// Every page, with a 512 MB ceiling on the decoded result.
    public static byte[][] RenderAll(string path,int dpi,CancellationToken token=default,string password="")
    {
        using var pdf=Open(path,password);var pages=new List<byte[]>();long total=0;
        for(var i=0;i<pdf.PageCount;i++){var png=pdf.RenderPage(i,dpi,token);total+=png.LongLength;if(total>512_000_000)throw new InvalidDataException("Rendered pages exceed 512 MB. Choose a lower DPI or split the PDF first.");pages.Add(png);}
        return pages.ToArray();
    }

    private static class Native
    {
        private const string Library="pdfium";
        [DllImport(Library)]internal static extern void FPDF_InitLibrary();
        [DllImport(Library)]internal static extern nint FPDF_LoadMemDocument64(nint data,nuint size,[MarshalAs(UnmanagedType.LPUTF8Str)]string password);
        [DllImport(Library)]internal static extern uint FPDF_GetLastError();
        [DllImport(Library)]internal static extern int FPDF_GetPageCount(nint document);
        [DllImport(Library)]internal static extern nint FPDF_LoadPage(nint document,int index);
        [DllImport(Library)]internal static extern double FPDF_GetPageWidth(nint page);
        [DllImport(Library)]internal static extern double FPDF_GetPageHeight(nint page);
        [DllImport(Library)]internal static extern nint FPDFBitmap_CreateEx(int width,int height,int format,nint data,int stride);
        [DllImport(Library)]internal static extern int FPDFBitmap_FillRect(nint bitmap,int left,int top,int width,int height,uint color);
        [DllImport(Library)]internal static extern void FPDF_RenderPageBitmap(nint bitmap,nint page,int left,int top,int width,int height,int rotate,int flags);
        [DllImport(Library)]internal static extern void FPDFBitmap_Destroy(nint bitmap);
        [DllImport(Library)]internal static extern void FPDF_ClosePage(nint page);
        [DllImport(Library)]internal static extern void FPDF_CloseDocument(nint document);
    }
}
