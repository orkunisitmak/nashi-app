using System.Runtime.InteropServices;
using ImageMagick;
using Nashi.Core;
namespace Nashi.Linux.Engine;

/// Linux counterpart of the Windows NativePdf/PdfRasterDocument pair: the same
/// PDFium build rasterizes on the CPU, Magick.NET encodes PNG, ZXing reads QR
/// codes and the distribution's Tesseract replaces Windows.Media.Ocr.
public static class LinuxPdf
{
    // PDFium's public API is not thread-safe; every call is serialized.
    private static readonly object gate=new();private static bool initialized;
    public static Task<byte[][]> RenderAsync(string path,int dpi=144,CancellationToken token=default,string password="")=>Task.Run(()=>
    {
        if(dpi<36||dpi>600)throw new ArgumentOutOfRangeException(nameof(dpi));
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);if(input.Length<1||input.Length>200_000_000)throw new InvalidDataException("Choose a PDF smaller than 200 MB.");
        var data=new byte[checked((int)input.Length)];input.ReadExactly(data);var images=new List<byte[]>();long total=0;
        lock(gate)
        {
            if(!initialized){Native.FPDF_InitLibrary();initialized=true;}
            var source=GCHandle.Alloc(data,GCHandleType.Pinned);nint document=0;
            try
            {
                document=Native.FPDF_LoadMemDocument64(source.AddrOfPinnedObject(),(nuint)data.LongLength,password);
                if(document==0)throw new InvalidDataException(Native.FPDF_GetLastError()==4?"The PDF password is missing or incorrect.":"This PDF could not be opened.");
                var count=Native.FPDF_GetPageCount(document);if(count<1||count>1000)throw new InvalidDataException("Choose a PDF with 1 to 1,000 pages.");
                for(var index=0;index<count;index++)
                {
                    token.ThrowIfCancellationRequested();var image=RenderPage(document,index,dpi);total+=image.LongLength;
                    if(total>512_000_000)throw new InvalidDataException("Rendered pages exceed 512 MB. Choose a lower DPI or split the PDF first.");images.Add(image);
                }
            }
            finally{if(document!=0)Native.FPDF_CloseDocument(document);source.Free();}
        }
        return images.ToArray();
    },token);
    private static byte[] RenderPage(nint document,int index,int dpi)
    {
        var page=Native.FPDF_LoadPage(document,index);if(page==0)throw new InvalidDataException("This PDF page could not be read.");
        try
        {
            var w=Native.FPDF_GetPageWidth(page)*dpi/72;var h=Native.FPDF_GetPageHeight(page)*dpi/72;
            if(!double.IsFinite(w)||!double.IsFinite(h)||w<1||h<1||w*h>100_000_000)throw new InvalidDataException("PDF page exceeds 100 MP.");
            var width=checked((int)Math.Ceiling(w));var height=checked((int)Math.Ceiling(h));var stride=checked(width*4);var pixels=new byte[checked(stride*height)];
            var pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);nint bitmap=0;
            try
            {
                bitmap=Native.FPDFBitmap_CreateEx(width,height,4,pinned.AddrOfPinnedObject(),stride);if(bitmap==0)throw new InvalidDataException("PDF preview bitmap could not be allocated.");
                if(Native.FPDFBitmap_FillRect(bitmap,0,0,width,height,0xffffffff)==0)throw new InvalidDataException("PDF preview background could not be prepared.");
                // FPDF_ANNOT renders form appearances and annotations like the Windows preview.
                Native.FPDF_RenderPageBitmap(bitmap,page,0,0,width,height,0,1);
            }
            finally{if(bitmap!=0)Native.FPDFBitmap_Destroy(bitmap);pinned.Free();}
            return LinuxImages.PngFromBgra(pixels,width,height);
        }
        finally{Native.FPDF_ClosePage(page);}
    }
    public static string[] DecodeQr(byte[] png)
    {
        using var image=LinuxImages.FromPng(png);var width=(int)image.Width;var height=(int)image.Height;
        using var pixels=image.GetPixels();var bytes=pixels.ToByteArray(PixelMapping.BGRA)??throw new InvalidDataException("Image pixels could not be read.");
        var reader=new ZXing.BarcodeReaderGeneric{AutoRotate=true,Options=new ZXing.Common.DecodingOptions{TryHarder=true,PossibleFormats=[ZXing.BarcodeFormat.QR_CODE]}};
        return reader.DecodeMultiple(bytes,width,height,ZXing.RGBLuminanceSource.BitmapFormat.BGRA32)?.Select(r=>r.Text).Distinct().ToArray()??[];
    }
    public static async Task<string[]> ReadQrAsync(IReadOnlyList<string> inputs,CancellationToken token)
    {
        var results=new List<string>();
        foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();
            var pages=FeatureCatalog.Extension(input)=="pdf"?await RenderAsync(input,200,token):[PhotoCodec.Png(input)];
            foreach(var page in pages)results.AddRange(DecodeQr(page));
        }
        return results.Distinct().ToArray();
    }

    public static string? Tesseract=>SystemTools.Find("tesseract");
    /// Recognizes a rendered page with the distribution's Tesseract. Languages are
    /// chosen from the installed traineddata files, preferring Turkish and English.
    public static async Task<string> RecognizeAsync(byte[] png,CancellationToken token)
    {
        var tesseract=Tesseract??throw new InvalidOperationException("Install Tesseract OCR to recognize scanned PDF text: "+InstallTesseract);
        var languages=(await ProcessRunner.RunAsync(tesseract,["--list-langs"],token)).Output.Split('\n').Select(l=>l.Trim()).Where(l=>l.Length>0&&!l.Contains(' ')&&l!="osd").ToArray();
        var chosen=new[]{"tur","eng"}.Where(languages.Contains).ToArray();if(chosen.Length==0)chosen=languages.Take(1).ToArray();
        if(chosen.Length==0)throw new InvalidOperationException("Install a Tesseract language pack to recognize scanned PDF text.");
        var work=Path.Combine(Path.GetTempPath(),"Nashi-ocr-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        try
        {
            var page=Path.Combine(work,"page.png");await File.WriteAllBytesAsync(page,png,token);
            return (await ProcessRunner.RunAsync(tesseract,[page,"stdout","-l",string.Join('+',chosen)],token)).Output.Trim();
        }
        finally{Directory.Delete(work,true);}
    }
    public static string InstallTesseract=>SystemTools.Distribution() switch
    {
        SystemTools.Family.Arch=>"sudo pacman -S --needed tesseract tesseract-data-eng tesseract-data-tur",
        SystemTools.Family.Debian=>"sudo apt install tesseract-ocr tesseract-ocr-eng tesseract-ocr-tur",
        SystemTools.Family.Fedora=>"sudo dnf install tesseract tesseract-langpack-eng tesseract-langpack-tur",
        SystemTools.Family.Suse=>"sudo zypper install tesseract-ocr tesseract-ocr-traineddata-english tesseract-ocr-traineddata-turkish",
        _=>"Install tesseract and its English/Turkish language data with your package manager."
    };

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
