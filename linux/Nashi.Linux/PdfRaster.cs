using System.Runtime.InteropServices;
using ImageMagick;

namespace Nashi.Linux;

/// Whole-document and selected-page PDF renders, as PNG bytes. The Linux port of Windows' NativePdf render half.
public static class PdfRaster
{
    const long RenderLimit = 512_000_000;

    public static Task<byte[][]> RenderAsync(string path, int dpi = 144, CancellationToken token = default, string password = "") => Task.Run(() =>
    {
        using var pdf = PdfRasterDocument.Open(path, password);
        var images = new List<byte[]>();
        long total = 0;
        for (var i = 0; i < pdf.PageCount; i++)
        {
            token.ThrowIfCancellationRequested();
            var image = pdf.RenderPage(i, dpi, token);
            total += image.LongLength;
            if (total > RenderLimit) throw new InvalidDataException("Rendered pages exceed 512 MB. Choose a lower DPI or split the PDF first.");
            images.Add(image);
        }
        return images.ToArray();
    }, token);

    /// Only the listed 1-based pages, so a fallback for a few pages does not render a whole document.
    public static Task<Dictionary<int, byte[]>> RenderPagesAsync(string path, IReadOnlyCollection<int> pages, int dpi = 144, CancellationToken token = default, string password = "") => Task.Run(() =>
    {
        using var pdf = PdfRasterDocument.Open(path, password);
        var images = new Dictionary<int, byte[]>();
        long total = 0;
        foreach (var page in pages.Distinct())
        {
            token.ThrowIfCancellationRequested();
            if (page < 1 || page > pdf.PageCount) continue;
            var image = pdf.RenderPage(page - 1, dpi, token);
            total += image.LongLength;
            if (total > RenderLimit) throw new InvalidDataException("Rendered pages exceed 512 MB. Choose a lower DPI or split the PDF first.");
            images[page] = image;
        }
        return images;
    }, token);
}

/// CPU-only PDF rasterization with PDFium. Native PDF objects and their pinned source bytes stay together,
/// and every PDFium call goes through one gate: its public API is not thread-safe.
internal sealed class PdfRasterDocument : IDisposable
{
    static readonly object gate = new();
    static bool initialized;
    static int documents;
    nint document;
    GCHandle source;
    PdfFormPreview? form;

    internal int PageCount { get; }
    internal static int OpenDocuments { get { lock (gate) return documents; } }

    PdfRasterDocument(string path, string password)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = input.Length;
        if (length < 1 || length > 200_000_000) throw new InvalidDataException("Choose a PDF smaller than 200 MB.");
        var data = new byte[checked((int)length)];
        input.ReadExactly(data);
        lock (gate)
        {
            if (!initialized) { Native.FPDF_InitLibrary(); initialized = true; }
            source = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                document = Native.FPDF_LoadMemDocument64(source.AddrOfPinnedObject(), (nuint)data.LongLength, password);
                if (document == 0) throw new InvalidDataException(Native.FPDF_GetLastError() == 4 ? "The PDF password is missing or incorrect." : "This PDF could not be opened.");
                PageCount = Native.FPDF_GetPageCount(document);
                if (PageCount < 1 || PageCount > 1000) throw new InvalidDataException("Choose a PDF with 1 to 1,000 pages.");
                if (Native.FPDF_GetFormType(document) != 0) form = new PdfFormPreview(document);
                documents++;
            }
            catch
            {
                form?.Dispose();
                if (document != 0) { Native.FPDF_CloseDocument(document); document = 0; }
                if (source.IsAllocated) source.Free();
                throw;
            }
        }
    }

    internal static PdfRasterDocument Open(string path, string password = "") => new(path, password);

    internal byte[] RenderPage(int index, int dpi, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (dpi < 36 || dpi > 600) throw new ArgumentOutOfRangeException(nameof(dpi));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(document == 0, this);
            if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
            var page = Native.FPDF_LoadPage(document, index);
            if (page == 0) throw new InvalidDataException("This PDF page could not be read.");
            try
            {
                form?.Load(page, index);
                var w = Native.FPDF_GetPageWidth(page) * dpi / 72;
                var h = Native.FPDF_GetPageHeight(page) * dpi / 72;
                if (!double.IsFinite(w) || !double.IsFinite(h) || w < 1 || h < 1 || w * h > 100_000_000) throw new InvalidDataException("PDF page exceeds 100 MP.");
                var width = checked((int)Math.Ceiling(w));
                var height = checked((int)Math.Ceiling(h));
                if ((long)width * height > 100_000_000) throw new InvalidDataException("PDF page exceeds 100 MP.");
                var stride = checked(width * 4);
                var pixels = new byte[checked(stride * height)];
                var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                nint bitmap = 0;
                try
                {
                    bitmap = Native.FPDFBitmap_CreateEx(width, height, 4, pinned.AddrOfPinnedObject(), stride); // FPDFBitmap_BGRA
                    if (bitmap == 0) throw new InvalidDataException("PDF preview bitmap could not be allocated.");
                    if (Native.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xffffffff) == 0) throw new InvalidDataException("PDF preview background could not be prepared.");
                    Native.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, 1); // FPDF_ANNOT
                    form?.Draw(bitmap, width, height);
                    token.ThrowIfCancellationRequested();
                    using var image = new MagickImage();
                    image.ReadPixels(pixels, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.BGRA));
                    image.ColorSpace = ColorSpace.sRGB;
                    image.Density = new Density(dpi, dpi, DensityUnit.PixelsPerInch);
                    return image.ToByteArray(MagickFormat.Png);
                }
                finally
                {
                    if (bitmap != 0) Native.FPDFBitmap_Destroy(bitmap);
                    pinned.Free();
                }
            }
            finally
            {
                form?.ClosePage();
                Native.FPDF_ClosePage(page);
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (document == 0) return;
            form?.Dispose();
            form = null;
            Native.FPDF_CloseDocument(document);
            document = 0;
            documents--;
            if (source.IsAllocated) source.Free();
        }
        GC.SuppressFinalize(this);
    }

    ~PdfRasterDocument() { Dispose(); }

    internal static void Shutdown()
    {
        lock (gate)
        {
            if (initialized && documents == 0) { Native.FPDF_DestroyLibrary(); initialized = false; }
        }
    }

    static class Native
    {
        [DllImport("pdfium")] internal static extern void FPDF_InitLibrary();
        [DllImport("pdfium")] internal static extern void FPDF_DestroyLibrary();
        [DllImport("pdfium")] internal static extern nint FPDF_LoadMemDocument64(nint data, nuint size, [MarshalAs(UnmanagedType.LPUTF8Str)] string password);
        [DllImport("pdfium")] internal static extern uint FPDF_GetLastError();
        [DllImport("pdfium")] internal static extern int FPDF_GetPageCount(nint document);
        [DllImport("pdfium")] internal static extern int FPDF_GetFormType(nint document);
        [DllImport("pdfium")] internal static extern nint FPDF_LoadPage(nint document, int index);
        [DllImport("pdfium")] internal static extern double FPDF_GetPageWidth(nint page);
        [DllImport("pdfium")] internal static extern double FPDF_GetPageHeight(nint page);
        [DllImport("pdfium")] internal static extern nint FPDFBitmap_CreateEx(int width, int height, int format, nint data, int stride);
        [DllImport("pdfium")] internal static extern int FPDFBitmap_FillRect(nint bitmap, int left, int top, int width, int height, uint color);
        [DllImport("pdfium")] internal static extern void FPDF_RenderPageBitmap(nint bitmap, nint page, int left, int top, int width, int height, int rotate, int flags);
        [DllImport("pdfium")] internal static extern void FPDFBitmap_Destroy(nint bitmap);
        [DllImport("pdfium")] internal static extern void FPDF_ClosePage(nint page);
        [DllImport("pdfium")] internal static extern void FPDF_CloseDocument(nint document);
    }
}

/// Read-only AcroForm appearance rendering. No JavaScript platform, URI actions,
/// focus events or timers are supplied to a preview document.
internal sealed class PdfFormPreview : IDisposable
{
    nint info, page;
    int pageIndex;
    readonly nint document;
    readonly Delegate[] callbacks;
    internal nint Handle { get; private set; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Invalidate(nint self, nint page, double left, double top, double right, double bottom);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Cursor(nint self, int cursor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Timer(nint self, int milliseconds, nint callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void KillTimer(nint self, int id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate SystemTime LocalTime(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Change(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate nint GetPage(nint self, nint document, int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate nint CurrentPage(nint self, nint document);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Rotation(nint self, nint page);
    [StructLayout(LayoutKind.Sequential)] struct SystemTime { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }

    // Exact version-1 prefix from the pinned PDFium fpdf_formfill.h. All
    // callbacks remain rooted until ExitFormFillEnvironment returns.
    [StructLayout(LayoutKind.Sequential)]
    struct FormInfo
    {
        public int Version;
        public nint Release, Invalidate, OutputSelectedRect, SetCursor, SetTimer, KillTimer, GetLocalTime, OnChange, GetPage, GetCurrentPage, GetRotation, ExecuteNamedAction, SetTextFieldFocus, DoUriAction, DoGoToAction, JsPlatform;
    }

    internal PdfFormPreview(nint document)
    {
        this.document = document;
        callbacks = [new Invalidate((_, _, _, _, _, _) => { }), new Cursor((_, _) => { }), new Timer((_, _, _) => -1), new KillTimer((_, _) => { }), new LocalTime(_ => default), new Change(_ => { }),
            new GetPage((_, doc, index) => doc == this.document && index == pageIndex ? page : 0), new CurrentPage((_, doc) => doc == this.document ? page : 0), new Rotation((_, _) => 0)];
        nint Pointer(int index) => Marshal.GetFunctionPointerForDelegate(callbacks[index]);
        var value = new FormInfo { Version = 1, Invalidate = Pointer(0), SetCursor = Pointer(1), SetTimer = Pointer(2), KillTimer = Pointer(3), GetLocalTime = Pointer(4), OnChange = Pointer(5), GetPage = Pointer(6), GetCurrentPage = Pointer(7), GetRotation = Pointer(8) };
        info = Marshal.AllocHGlobal(Marshal.SizeOf<FormInfo>());
        Marshal.StructureToPtr(value, info, false);
        Handle = Native.FPDFDOC_InitFormFillEnvironment(document, info);
        if (Handle == 0)
        {
            Marshal.FreeHGlobal(info);
            info = 0;
            throw new InvalidDataException("PDF form appearances could not be prepared.");
        }
    }

    internal void Load(nint value, int index) { page = value; pageIndex = index; Native.FORM_OnAfterLoadPage(page, Handle); }
    internal void Draw(nint bitmap, int width, int height) => Native.FPDF_FFLDraw(Handle, bitmap, page, 0, 0, width, height, 0, 1);
    internal void ClosePage() { if (page != 0) Native.FORM_OnBeforeClosePage(page, Handle); page = 0; }

    public void Dispose()
    {
        if (Handle == 0) return;
        ClosePage();
        Native.FPDFDOC_ExitFormFillEnvironment(Handle);
        Handle = 0;
        Marshal.FreeHGlobal(info);
        info = 0;
        GC.KeepAlive(callbacks);
    }

    static class Native
    {
        [DllImport("pdfium")] internal static extern nint FPDFDOC_InitFormFillEnvironment(nint document, nint info);
        [DllImport("pdfium")] internal static extern void FPDFDOC_ExitFormFillEnvironment(nint handle);
        [DllImport("pdfium")] internal static extern void FORM_OnAfterLoadPage(nint page, nint handle);
        [DllImport("pdfium")] internal static extern void FORM_OnBeforeClosePage(nint page, nint handle);
        [DllImport("pdfium")] internal static extern void FPDF_FFLDraw(nint handle, nint bitmap, nint page, int x, int y, int width, int height, int rotate, int flags);
    }
}
