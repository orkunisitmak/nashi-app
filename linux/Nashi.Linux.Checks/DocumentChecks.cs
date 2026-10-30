using System.Text;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;

/// PDF rendering with PDFium: one image per page, named like Windows, through the runner.
static class DocumentChecks
{
    const string Name = "-ğüş doc"; // leading dash, Turkish letters and a space

    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var runner = new OperationRunner(new Toolchain(Directory.CreateDirectory(Path.Combine(root, "doc-tools")).FullName, ""));
        var none = new Progress<OperationProgress>();
        var dir = Directory.CreateDirectory(Path.Combine(root, "documents")).FullName;
        var pdf = Path.Combine(dir, Name + ".pdf");
        File.WriteAllBytes(pdf, Pdf("Nashi PDF page one", "Nashi PDF page two"));
        check(Capabilities.Formats([pdf]).SequenceEqual(["jpg", "png"]), "A PDF offers JPG and PNG");

        var pages = await runner.RunAsync("convert:png", [pdf], Path.Combine(dir, "png"), none, default);
        check(pages.Select(Path.GetFileName).SequenceEqual([Name + " Page 1.png", Name + " Page 2.png"]) && pages.All(p => new MagickImageInfo(p) is { Format: MagickFormat.Png, Width: 612 * 2 }), "pdf→png makes one 144 DPI PNG per page, named '<name> Page N'");
        var jpgs = await runner.RunAsync("convert:jpg", [pdf], Path.Combine(dir, "jpg"), none, default);
        check(jpgs.Count == 2 && jpgs.All(p => new MagickImageInfo(p).Format == MagickFormat.Jpeg), "pdf→jpg makes one JPEG per page");
        using (var page = new MagickImage(pages[0])) check(page.GetPixels().GetPixel(5, 5).ToColor()!.R == 255 && page.Histogram().Keys.Any(c => c.R < 128), "A rendered page is white paper with dark text");
        check(PdfRasterDocument.OpenDocuments == 0, "Every PDFium document is closed after rendering");
        check(Throws<InvalidDataException>(() => PdfRasterDocument.Open(Path.Combine(dir, "png", Name + " Page 1.png"))), "PDFium rejects a file that is not a PDF");

        var cancelled = Path.Combine(dir, "cancelled");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { await runner.RunAsync("convert:png", [pdf], cancelled, none, cts.Token); check(false, "A cancelled PDF render throws"); }
        catch (OperationCanceledException) { check(!Directory.Exists(cancelled) || Directory.GetFileSystemEntries(cancelled).Length == 0, "A cancelled PDF render leaves no output"); }
    }

    static bool Throws<T>(Action a) where T : Exception { try { a(); return false; } catch (T) { return true; } }

    /// A US Letter PDF, one Helvetica line per page.
    static byte[] Pdf(params string[] lines)
    {
        var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
        var kids = new List<string>();
        foreach (var line in lines)
        {
            var content = $"BT /F1 24 Tf 72 700 Td ({line}) Tj ET";
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {objects.Count} 0 R >>");
            kids.Add($"{objects.Count} 0 R");
        }
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {kids.Count} >>";
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var n = 0; n < objects.Count; n++) { offsets.Add(pdf.Length); pdf.Append($"{n + 1} 0 obj\n{objects[n]}\nendobj\n"); }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

}
