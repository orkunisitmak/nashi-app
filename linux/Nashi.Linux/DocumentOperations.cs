using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ImageMagick;
using Nashi.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Nashi.Linux;

/// PDF, Office, text, EPUB and Pages conversions: the Linux port of Windows' DocumentConversion.
/// LibreOffice (the system soffice) does Office work through Core's OfficeDocuments; Deno runs the Pages bridge.
// ponytail: the Linux wheel has no options dialog, so DocumentOptions stay at their defaults (144 DPI, editable layout,
// reflowable EPUB, no password); Windows' fixed-EPUB and preserved-layout branches are left out until Linux grows options.
public sealed class DocumentOperations(string? office, string? deno)
{
    static readonly DocumentOptions Options = new();

    /// Inputs this class converts: every catalog document type.
    public static bool Handles(string path) => FeatureCatalog.Extension(path) is "pdf" or "ppt" or "pptx" or "docx" or "pages" or "md" or "rtf" or "rtfd" or "textclipping" or "html" or "epub" or "txt" or "xlsx" or "xls" or "ods" or "csv" or "tsv";

    /// Only PDF to images and to plain text run without LibreOffice.
    public static bool NeedsOffice(string input, string format) => !(FeatureCatalog.Extension(input) == "pdf" && format is "png" or "jpg" or "txt" or "md");

    public static bool NeedsDeno(string input, string format) => format == "pages" || FeatureCatalog.Extension(input) == "pages";

    public async Task<IReadOnlyList<string>> ConvertAsync(IReadOnlyList<string> inputs, string format, string destination, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        // Missing tools fail before any output work, so setup opens with nothing half-written.
        if (office is null && inputs.Any(i => NeedsOffice(i, format))) throw new MissingToolsException(MissingToolsException.Office);
        if (deno is null && inputs.Any(i => NeedsDeno(i, format))) throw new MissingToolsException(MissingToolsException.Pages);
        var engine = office is null ? null : new OfficeDocuments(office, deno ?? "", AppContext.BaseDirectory);
        using var tx = new ExportTransaction(destination);
        var outputs = new List<string>();
        for (var i = 0; i < inputs.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var input = inputs[i];
            var ext = FeatureCatalog.Extension(input);
            var name = Path.GetFileNameWithoutExtension(input);
            var work = Directory.CreateDirectory(Path.Combine(tx.StagingDirectory, "work-" + i)).FullName;
            var source = input;
            if (!FeatureCatalog.Formats([input]).Contains(format)) throw new ArgumentException($"This conversion is not available for {Path.GetFileName(input)}.");
            progress?.Report(new(null, $"Preparing {Path.GetFileName(input)}…"));
            if (ext == "pages") source = await engine!.PagesReadAsync(input, work, token);
            if (ext == "rtfd") source = await RichTextAttachments(input, work, token);
            if (ext == "textclipping") source = await MaterializeClipping(input, work, token);
            if (ext is "txt" or "md" or "csv" or "tsv")
            {
                var normalized = Path.Combine(work, Path.GetFileName(input));
                await LegacyTextCodec.ToUtf8Async(input, normalized, token);
                source = normalized;
            }
            if (ext == "epub") source = EpubToHtml(input, work, token);
            if (ext == "pdf" && format is "txt" or "md")
            {
                var output = SafePaths.Unique(tx.StagingDirectory, name + "." + format);
                await File.WriteAllTextAsync(output, PdfText(input, Options.Password, token), new UTF8Encoding(false), token);
                outputs.Add(output);
                continue;
            }
            if (ext == "pdf" && format == "pptx") source = await Slides(input, work, Options.Dpi, Options.Password, token);
            else if (ext == "pdf" && format is not ("png" or "jpg"))
            {
                source = Path.Combine(work, format == "xlsx" ? "Tables.html" : "PDF document.html");
                await File.WriteAllTextAsync(source, format == "xlsx" ? PdfTables(input, Options.Password) : await EditablePdfHtml(input, token), token);
            }
            if (format is "png" or "jpg")
            {
                var pdf = ext == "pdf" ? input : await engine!.OfficeAsync(source, "pdf", work, token);
                var pages = await PdfRaster.RenderAsync(pdf, Options.Dpi, token, ext == "pdf" ? Options.Password : "");
                for (var page = 0; page < pages.Length; page++)
                {
                    token.ThrowIfCancellationRequested();
                    var output = SafePaths.Unique(tx.StagingDirectory, name + $" Page {page + 1}." + format);
                    if (format == "png") await File.WriteAllBytesAsync(output, pages[page], token);
                    else PhotoCodec.EncodePng(pages[page], output, "jpg", 90);
                    outputs.Add(output);
                    progress?.Report(new((i + (page + 1d) / pages.Length) / inputs.Count * .98, $"Rendering page {page + 1}"));
                }
                continue;
            }
            if (format == "pages")
            {
                var docx = ext == "docx" ? input : await engine!.OfficeAsync(source, "docx", work, token);
                outputs.Add(await engine!.PagesWriteAsync(docx, SafePaths.Unique(tx.StagingDirectory, name + ".pages"), token));
                continue;
            }
            if (FeatureCatalog.Extension(source) == "html")
            {
                var safe = Path.Combine(work, "Safe.html");
                await File.WriteAllTextAsync(safe, OfficeDocuments.SanitizedHtml(await File.ReadAllTextAsync(source, token)), token);
                source = safe;
            }
            var converted = format == "md" ? await Markdown(engine!, source, work, token) : await engine!.OfficeAsync(source, format, work, token);
            var target = SafePaths.Unique(tx.StagingDirectory, name + "." + format);
            File.Move(converted, target);
            outputs.Add(target);
            progress?.Report(new((i + 1d) / inputs.Count * .98, "Saving document…"));
        }
        return tx.Publish(outputs, token);
    }

    // LibreOffice gained a Markdown export filter only recently (distribution builds such as 7.x have none).
    // There, plain text stands in: it is valid Markdown, just without headings and emphasis.
    // Any other LibreOffice failure is an error, never a quiet fallback.
    async Task<string> Markdown(OfficeDocuments engine, string source, string work, CancellationToken token)
    {
        if (ExportsMarkdown(office!)) return await engine.OfficeAsync(source, "md", work, token);
        var text = await engine.OfficeAsync(source, "txt", work, token);
        var markdown = Path.ChangeExtension(text, ".md");
        File.Move(text, markdown);
        Note = PlainMarkdown;
        return markdown;
    }

    public const string PlainMarkdown = "Saved as plain-text Markdown";

    /// What the success card adds to its detail, such as the plain-text Markdown fallback; null when nothing.
    public string? Note { get; private set; }

    static readonly ConcurrentDictionary<string, bool> markdownExport = new();

    /// Whether this LibreOffice has a Markdown export filter: its share/registry declares a "Markdown" filter flagged EXPORT.
    /// An install laid out differently is assumed to have one, so a real failure still shows as an error.
    public static bool ExportsMarkdown(string office) => markdownExport.GetOrAdd(office, path =>
    {
        var program = Path.GetDirectoryName(new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? path)!;
        var registry = Path.GetFullPath(Path.Combine(program, "..", "share", "registry"));
        if (!Directory.Exists(registry)) return true;
        var filter = new Regex("<node oor:name=\"Markdown\"[^>]*>\\s*<prop oor:name=\"Flags\">\\s*<value>([^<]*)</value>", RegexOptions.None, TimeSpan.FromSeconds(5));
        return Directory.EnumerateFiles(registry, "*.xcd").Any(file => filter.Matches(File.ReadAllText(file)).Any(m => m.Groups[1].Value.Split(' ').Contains("EXPORT")));
    });

    // Linux has no OCR engine, so a scanned PDF with no text layer is an error rather than an empty file.
    static string PdfText(string input, string password, CancellationToken token)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(input, new() { Password = password });
        var text = new List<string>();
        foreach (var page in pdf.GetPages())
        {
            token.ThrowIfCancellationRequested();
            text.Add(ContentOrderTextExtractor.GetText(page));
        }
        var output = string.Join("\n\n\f\n\n", text);
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidDataException("No text could be extracted or recognized in this PDF.");
        return output;
    }

    // Scanned pages and images PdfPig cannot decode come from a PDFium render of just those pages:
    // the whole page when nothing else was found, otherwise only the image's area.
    static async Task<string> EditablePdfHtml(string input, CancellationToken token)
    {
        var html = OfficeDocuments.PdfHtml(input, Options.Password, null, out var needed);
        if (needed.Length == 0) return html;
        var pages = await PdfRaster.RenderPagesAsync(input, needed, Options.Dpi, token, Options.Password);
        return OfficeDocuments.PdfHtml(input, Options.Password, (n, area) => pages.TryGetValue(n, out var png) ? area is { } a ? CropRendered(png, a, Options.Dpi) : png : null, out _);
    }

    static byte[]? CropRendered(byte[] png, OfficeDocuments.PdfArea area, int dpi)
    {
        using var page = new MagickImage(png);
        var scale = dpi / 72d;
        var x = Math.Clamp((int)Math.Floor(area.Left * scale), 0, (int)page.Width);
        var y = Math.Clamp((int)Math.Floor(area.Top * scale), 0, (int)page.Height);
        var width = Math.Min((int)page.Width - x, (int)Math.Ceiling(area.Width * scale));
        var height = Math.Min((int)page.Height - y, (int)Math.Ceiling(area.Height * scale));
        if (width < 1 || height < 1) return null;
        page.Crop(new MagickGeometry(x, y, (uint)width, (uint)height));
        page.ResetPage();
        return page.ToByteArray(MagickFormat.Png);
    }

    static string PdfTables(string input, string password)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(input, new() { Password = password });
        var html = new StringBuilder("<html><head><meta charset=\"utf-8\"/></head><body>");
        foreach (var page in pdf.GetPages())
        {
            html.Append("<table>");
            foreach (var line in page.GetWords().OrderByDescending(w => w.BoundingBox.Bottom).GroupBy(w => Math.Round(w.BoundingBox.Bottom / 4)).OrderByDescending(g => g.Key))
            {
                html.Append("<tr>");
                var text = new StringBuilder();
                var right = double.NaN;
                foreach (var word in line.OrderBy(w => w.BoundingBox.Left))
                {
                    if (double.IsFinite(right) && word.BoundingBox.Left - right > 12) { html.Append("<td>").Append(WebUtility.HtmlEncode(text.ToString())).Append("</td>"); text.Clear(); }
                    if (text.Length > 0) text.Append(' ');
                    text.Append(word.Text);
                    right = word.BoundingBox.Right;
                }
                html.Append("<td>").Append(WebUtility.HtmlEncode(text.ToString())).Append("</td></tr>");
            }
            html.Append("</table>");
        }
        return html.Append("</body></html>").ToString();
    }

    /// Each PDF page as a full-slide picture in a flat ODF presentation that LibreOffice saves as PPTX.
    static async Task<string> Slides(string input, string work, int dpi, string password, CancellationToken token)
    {
        var pages = await PdfRaster.RenderAsync(input, dpi, token, password);
        if (pages.Length == 0) throw new InvalidDataException("This PDF contains no pages.");
        static (double W, double H) Size(byte[] png) { var info = new MagickImageInfo(png); return (info.Width, info.Height); }
        var first = Size(pages[0]);
        var width = first.W * 72d / dpi;
        var height = first.H * 72d / dpi;
        static string N(double n) => n.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var slides = new StringBuilder();
        for (var n = 0; n < pages.Length; n++)
        {
            token.ThrowIfCancellationRequested();
            var image = Size(pages[n]);
            var scale = Math.Min(width / image.W, height / image.H);
            var w = image.W * scale;
            var h = image.H * scale;
            slides.Append($"<draw:page draw:name=\"Page {n + 1}\" draw:master-page-name=\"PDFMaster\"><draw:frame svg:x=\"{N((width - w) / 2)}pt\" svg:y=\"{N((height - h) / 2)}pt\" svg:width=\"{N(w)}pt\" svg:height=\"{N(h)}pt\"><draw:image><office:binary-data>{Convert.ToBase64String(pages[n])}</office:binary-data></draw:image></draw:frame></draw:page>");
        }
        var xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.presentation\"><office:styles/><office:automatic-styles><style:page-layout style:name=\"PDFCanvas\"><style:page-layout-properties fo:page-width=\"{N(width)}pt\" fo:page-height=\"{N(height)}pt\" fo:margin=\"0pt\"/></style:page-layout></office:automatic-styles><office:master-styles><style:master-page style:name=\"PDFMaster\" style:page-layout-name=\"PDFCanvas\"/></office:master-styles><office:body><office:presentation>{slides}</office:presentation></office:body></office:document>";
        var file = Path.Combine(work, "PDF slides.fodp");
        await File.WriteAllTextAsync(file, xml, token);
        return file;
    }

    internal static string EpubToHtml(string input, string work, CancellationToken token)
    {
        static string Required(XElement element, string name) => element.Attribute(name)?.Value ?? throw new InvalidDataException("This EPUB could not be read.");
        var extracted = ArchiveEngine.Extract(input, work, null, null, token)[0];
        var container = ReadXml(Path.Combine(extracted, "META-INF", "container.xml"));
        var rootfile = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile") ?? throw new InvalidDataException("This EPUB could not be read.");
        var file = Required(rootfile, "full-path");
        var packagePath = SafePaths.ArchiveEntry(extracted, file);
        var package = ReadXml(packagePath);
        var baseFolder = Path.GetDirectoryName(packagePath)!;
        var items = new Dictionary<string, string>();
        foreach (var item in package.Descendants().Where(e => e.Name.LocalName == "item")) items.TryAdd(Required(item, "id"), Required(item, "href"));
        var html = new StringBuilder("<html><head><meta charset=\"utf-8\"/></head><body>");
        foreach (var reference in package.Descendants().Where(e => e.Name.LocalName == "itemref"))
        {
            token.ThrowIfCancellationRequested();
            if (!items.TryGetValue(Required(reference, "idref"), out var href)) throw new InvalidDataException("This EPUB could not be read.");
            var path = Path.GetFullPath(Path.Combine(baseFolder, Uri.UnescapeDataString(href.Split('#')[0])));
            if (!path.StartsWith(extracted + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Invalid EPUB chapter path.");
            var chapter = ReadXml(path);
            foreach (var image in chapter.Descendants().Where(e => e.Name.LocalName == "img"))
            {
                var src = image.Attribute("src");
                if (src is null || Uri.TryCreate(src.Value, UriKind.Absolute, out _)) continue;
                var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, Uri.UnescapeDataString(src.Value)));
                if (!full.StartsWith(extracted + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full) || new FileInfo(full).Length > 20_000_000) throw new InvalidDataException("Invalid EPUB image path.");
                var kind = FeatureCatalog.Extension(full);
                src.Value = "data:image/" + (kind switch { "jpg" => "jpeg", "svg" => "svg+xml", _ => kind }) + ";base64," + Convert.ToBase64String(File.ReadAllBytes(full));
            }
            var body = chapter.Descendants().FirstOrDefault(e => e.Name.LocalName == "body");
            if (body is not null) foreach (var node in body.Nodes()) html.Append(node.ToString());
        }
        var output = Path.Combine(work, "Book.html");
        File.WriteAllText(output, OfficeDocuments.SanitizedHtml(html.Append("</body></html>").ToString()));
        return output;
    }

    // EPUB 2 chapters use XHTML named entities (&nbsp; &mdash;) that only the DTD declares,
    // and the DTD stays unread. Turn them into character references before parsing.
    static XDocument ReadXml(string path)
    {
        try { return ParseXml(path); }
        catch (Exception error) when (error is System.Xml.XmlException or FileNotFoundException or DirectoryNotFoundException) { throw new InvalidDataException("This EPUB could not be read.", error); }
    }

    static XDocument ParseXml(string path)
    {
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 20_000_000 };
        if (new FileInfo(path).Length > 80_000_000) { using var large = System.Xml.XmlReader.Create(path, settings); return XDocument.Load(large); }
        using var reader = System.Xml.XmlReader.Create(new StringReader(OfficeDocuments.NumericEntities(OfficeDocuments.XmlText(File.ReadAllBytes(path)))), settings);
        return XDocument.Load(reader);
    }

    static async Task<string> MaterializeClipping(string input, string work, CancellationToken token)
    {
        var bytes = await File.ReadAllBytesAsync(input, token);
        byte[]? resource = null;
        var sidecar = Path.Combine(Path.GetDirectoryName(input)!, "._" + Path.GetFileName(input));
        if (File.Exists(sidecar) && new FileInfo(sidecar).Length < 200_000_000) resource = LegacyTextCodec.AppleDoubleResource(await File.ReadAllBytesAsync(sidecar, token));
        var content = LegacyTextCodec.Clipping(bytes, resource);
        var output = Path.Combine(work, "Clipping." + content.Extension);
        await File.WriteAllBytesAsync(output, content.Bytes, token);
        return output;
    }

    /// An RTFD folder's TXT.rtf with each attachment embedded as a PNG picture, so LibreOffice keeps the images.
    static async Task<string> RichTextAttachments(string folder, string work, CancellationToken token)
    {
        var file = Path.Combine(folder, "TXT.rtf");
        if (!File.Exists(file) || new FileInfo(file).Length > 20_000_000) throw new InvalidDataException("Choose a complete RTFD folder containing TXT.rtf.");
        var rtf = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(file, token));
        var result = Regex.Replace(rtf, @"\{\\NeXTGraphic\s+([^{}]+)\}", match =>
        {
            token.ThrowIfCancellationRequested();
            var name = Regex.Split(match.Groups[1].Value, @"\s+\\(?:width|height|baseline)", RegexOptions.None, TimeSpan.FromSeconds(1))[0].Trim().Replace("\\ ", " ");
            var image = SafePaths.ArchiveEntry(folder, name);
            if (!File.Exists(image) || new FileInfo(image).Length > 20_000_000 || (File.GetAttributes(image) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException($"An RTFD attachment is missing or too large: {name}");
            var png = PhotoCodec.Png(image);
            using var dimensions = PhotoCodec.Read(image);
            return "{\\pict\\pngblip\\picw" + dimensions.Width + "\\pich" + dimensions.Height + "\\picwgoal" + (dimensions.Width * 15) + "\\pichgoal" + (dimensions.Height * 15) + "\n" + Convert.ToHexString(png) + "}";
        }, RegexOptions.None, TimeSpan.FromSeconds(10));
        if (result.Contains("\\NeXTGraphic", StringComparison.Ordinal)) throw new InvalidDataException("This RTFD attachment syntax could not be preserved.");
        var output = Path.Combine(work, "Rich document.rtf");
        await File.WriteAllBytesAsync(output, Encoding.Latin1.GetBytes(result), token);
        return output;
    }
}
