using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;

/// Parity with the catalog, then every offered route run once on a small real fixture, with its output opened and checked.
/// Routes that need a tool this machine lacks (ffmpeg, LibreOffice, Deno) are skipped and counted.
static class DocumentChecks
{
    const string Name = "-ğüş doc"; // leading dash, Turkish letters and a space, in every document path
    const string Marker = "Nashi";

    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        // The acceptance gate: every catalog input offers its catalog routes, minus HEIC output.
        var mismatched = FeatureCatalog.Conversions.Keys.Where(ext =>
            !FeatureCatalog.Formats(["x." + ext]).Where(f => f != "heic").All(Capabilities.Formats(["x." + ext]).Contains)).ToList();
        check(FeatureCatalog.Conversions.Count == 46 && mismatched.Count == 0, "Capabilities retain every catalog route for 46 inputs, minus HEIC output; differ: " + string.Join(" ", mismatched));
        var saved = AppPaths.OfficeSearchPath;
        AppPaths.OfficeSearchPath = "";
        check(AppPaths.Office() is null && Capabilities.Formats(["x.docx"]).SequenceEqual(FeatureCatalog.Formats(["x.docx"])), "Without LibreOffice the wheel still offers Office routes (choosing one opens setup)");
        AppPaths.OfficeSearchPath = saved;
        check(Capabilities.Formats(["a.docx", "b.md"]).SequenceEqual(FeatureCatalog.Formats(["a.docx", "b.md"])) && Capabilities.Formats(["a.pdf", "b.png"]).Length == 0, "Mixed documents keep the catalog intersection; documents plus images offer nothing");

        var ffmpeg = Environment.GetEnvironmentVariable("NASHI_VERIFY_FFMPEG");
        var ffprobe = Environment.GetEnvironmentVariable("NASHI_VERIFY_FFPROBE");
        var media = !string.IsNullOrEmpty(ffmpeg) && !string.IsNullOrEmpty(ffprobe) ? new MediaEngine(ffmpeg, ffprobe) : null;
        var denoPath = Environment.GetEnvironmentVariable("NASHI_VERIFY_DENO");
        var search = string.Join(':', new[] { ffmpeg, denoPath }.Where(p => !string.IsNullOrEmpty(p)).Select(p => Path.GetDirectoryName(p)!));
        var tools = new Toolchain(Directory.CreateDirectory(Path.Combine(root, "doc-tools")).FullName, search);
        var office = AppPaths.Office();
        var deno = AppPaths.Deno(tools);
        Console.WriteLine($"Route checks: ffmpeg {(media is null ? "absent" : "present")}, LibreOffice {office ?? "absent"}, Deno {deno ?? "absent"}");
        var runner = new OperationRunner(tools);
        var none = new Progress<OperationProgress>();
        var fixtures = Directory.CreateDirectory(Path.Combine(root, "fixtures")).FullName;
        var outputs = Directory.CreateDirectory(Path.Combine(root, "routes")).FullName;

        var made = await Fixtures(fixtures, office, deno, ffmpeg);
        var results = new ConcurrentBag<(string Route, string? Error)>();
        var skipped = new ConcurrentDictionary<string, int>();
        using var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount, 2, 6));
        var jobs = new List<Task>();
        foreach (var ext in Capabilities.InputExtensions)
            foreach (var format in Capabilities.Formats(["x." + ext]))
            {
                var route = ext + "→" + format;
                var why = Missing(ext, format, media is not null, office is not null, deno is not null) ?? (made.ContainsKey(ext) ? null : "fixture");
                if (why is not null) { skipped.AddOrUpdate(why, 1, (_, n) => n + 1); continue; }
                jobs.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        var produced = await runner.RunAsync("convert:" + format, [made[ext]], Path.Combine(outputs, ext + "-" + format), none, default);
                        results.Add((route, await Valid(ext, format, produced, media)));
                    }
                    catch (Exception error) { results.Add((route, error.GetType().Name + ": " + error.Message)); }
                    finally { gate.Release(); }
                }));
            }
        await Task.WhenAll(jobs);
        var total = FeatureCatalog.Conversions.Sum(c => c.Value.Length);
        var heic = FeatureCatalog.Conversions.Sum(c => c.Value.Count(f => f == "heic"));
        var offered = Capabilities.InputExtensions.Sum(e => Capabilities.Formats(["x." + e]).Length);
        Console.WriteLine($"Route checks: {results.Count} of {offered} offered routes run ({total} catalog routes, {heic} HEIC outputs not offered); skipped {skipped.Values.Sum()}" + string.Concat(skipped.OrderBy(s => s.Key).Select(s => $", {s.Value} need {s.Key}")));
        foreach (var (route, error) in results.OrderBy(r => r.Route, StringComparer.Ordinal)) check(error is null, "Route " + route + " makes a valid file: " + error);
        check(results.Count >= 4, "Routes ran without any optional tool (pdf→png, jpg, txt, md at least)");
        // A skip is only ever for a tool this machine lacks; with every tool present (CI) nothing may be skipped.
        check(!skipped.ContainsKey("fixture"), "No route is skipped for a missing fixture");
        if (media is not null && office is not null && deno is not null) check(skipped.IsEmpty && results.Count == offered, "With ffmpeg, LibreOffice and Deno every offered route runs (" + results.Count + " of " + offered + ")");

        // PDF to images: one per page, named like Windows.
        var pages = await runner.RunAsync("convert:png", [made["pdf"]], Path.Combine(outputs, "pdf-pages"), none, default);
        check(pages.Select(Path.GetFileName).SequenceEqual([Name + " Page 1.png", Name + " Page 2.png"]) && pages.All(p => new MagickImageInfo(p).Width == 612 * 2), "pdf→png makes one 144 DPI PNG per page, named '<name> Page N'");
        check(PdfRasterDocument.OpenDocuments == 0, "Every PDFium document is closed after rendering");
        check(Throws<InvalidDataException>(() => PdfRasterDocument.Open(made["txt"])), "PDFium rejects a file that is not a PDF");

        // Missing tools fail before output work and open setup (MissingToolsException).
        AppPaths.OfficeSearchPath = "";
        try
        {
            var dest = Path.Combine(outputs, "no-office");
            var error = await Catch(() => runner.RunAsync("convert:docx", [made["rtf"]], dest, none, default));
            check(error is MissingToolsException { Message: MissingToolsException.Office } && !Directory.Exists(dest), "Without LibreOffice an Office route asks for setup before writing anything");
            check((await runner.RunAsync("convert:txt", [made["pdf"]], Path.Combine(outputs, "no-office-txt"), none, default)).Count == 1, "pdf→txt needs no LibreOffice");
        }
        finally { AppPaths.OfficeSearchPath = saved; }
        var bare = new OperationRunner(new Toolchain(Directory.CreateDirectory(Path.Combine(root, "no-deno")).FullName, ""));
        var noDeno = Path.Combine(outputs, "no-deno");
        check(await Catch(() => bare.RunAsync("convert:pages", [made["pdf"]], noDeno, none, default)) is MissingToolsException { Message: MissingToolsException.Pages or MissingToolsException.Office } && !Directory.Exists(noDeno), "Without Deno a Pages route asks for setup before writing anything");

        await BesideChecks(root, runner, made, office is not null, media is not null, check);
        await EpubChecks(root, check);
        MarkdownFilterChecks(root, check);
        // A LibreOffice that has the Markdown filter but fails the export (while plain text would work) reports the error.
        var failing = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "failing-office")).FullName, "soffice");
        File.WriteAllText(failing, "#!/bin/sh\nout=; for a; do case \"$prev\" in --outdir) out=$a;; --convert-to) to=$a;; esac; prev=$a; done\n" +
            "case \"$to\" in md*) echo 'md export failed' >&2; exit 1;; esac\nn=$(basename \"$prev\"); echo text > \"$out/${n%.*}.txt\"\n");
        File.SetUnixFileMode(failing, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var failed = Path.Combine(outputs, "failing-md");
        var mdError = await Catch(() => new DocumentOperations(failing, null).ConvertAsync([made["rtf"]], "md", failed, null, default));
        check(mdError is IOException && (!Directory.Exists(failed) || Directory.GetFileSystemEntries(failed).Length == 0), "A real LibreOffice failure on the Markdown route is an error, not a plain-text fallback: " + mdError?.Message);
        if (office is not null)
        {
            var markdown = await runner.RunAsync("convert:md", [made["docx"]], Path.Combine(outputs, "note-md"), none, default);
            var plain = !DocumentOperations.ExportsMarkdown(office);
            check(runner.Note == (plain ? DocumentOperations.PlainMarkdown : null), "Markdown says it was saved as plain text exactly when LibreOffice has no Markdown export (" + (plain ? "fallback" : "native") + ")");
            await CancelChecks(runner, made["docx"], outputs, check);
        }
        else Console.WriteLine("Route checks: LibreOffice cancellation check skipped (no soffice)");
    }

    /// Outputs land directly beside their source, for every route class: no new folder (except the one an archive
    /// extracts into), no staging left behind, no LibreOffice job folder left in temp. Two folders keep their own outputs.
    static async Task BesideChecks(string root, OperationRunner runner, Dictionary<string, string> made, bool office, bool media, Action<bool, string> check)
    {
        var none = new Progress<OperationProgress>();
        Func<string, Task<string?>> same = folder => Task.FromResult<string?>(folder);
        var routes = new List<(string Class, string Choice, string Ext)>
        {
            ("image", "convert:jpg", "png"), ("image compress", "compress", "png"), ("pdf to images", "convert:png", "pdf"),
            ("pdf to text", "convert:txt", "pdf"), ("archive extract", "extractArchive", "zip"), ("archive convert", "convert:tar", "zip"),
            ("subtitles", "convert:vtt", "srt"),
        };
        if (office) routes.AddRange([("office", "convert:pdf", "docx"), ("office to images", "convert:jpg", "docx")]);
        if (media) routes.AddRange([("audio", "convert:mp3", "wav"), ("video", "convert:webm", "mp4"), ("video tool", "muteVideo", "mp4")]);
        string[] OfficeJobFolders() => Directory.GetDirectories(Path.GetTempPath(), "NashiOffice-*");
        var jobsBefore = OfficeJobFolders();
        var n = 0;
        foreach (var (kind, choice, ext) in routes)
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, "beside", (n++).ToString())).FullName;
            var source = Path.Combine(folder, Path.GetFileName(made[ext]));
            File.Copy(made[ext], source);
            var before = Directory.GetFileSystemEntries(folder);
            IReadOnlyList<string>? outputs;
            try { outputs = await runner.RunBesideSourcesAsync(choice, [source], same, none, default); }
            catch (Exception error) { check(false, $"Beside: {kind} ({ext} {choice}) runs: {error.Message}"); continue; }
            var added = Directory.GetFileSystemEntries(folder).Except(before).ToArray();
            var folders = added.Where(Directory.Exists).ToArray();
            var extracts = choice == "extractArchive";
            check(outputs is { Count: > 0 } && outputs.All(o => Path.GetDirectoryName(o) == folder) && added.Order().SequenceEqual(outputs.Order()),
                $"Beside: {kind} ({ext} {choice}) writes its outputs directly beside the source: " + string.Join(", ", added.Select(Path.GetFileName)));
            check(extracts ? folders.Select(Path.GetFileName).SequenceEqual([Path.GetFileNameWithoutExtension(source) + " Extracted"]) : folders.Length == 0,
                $"Beside: {kind} makes no new folder" + (extracts ? " except the one named after the archive" : ""));
            check(!Directory.EnumerateFileSystemEntries(folder, ".nashi-*").Any(), $"Beside: {kind} leaves no .nashi- staging behind");
        }
        check(OfficeJobFolders().Except(jobsBefore).ToArray() is [], "No LibreOffice job folder is left in temp");

        // Two folders: each keeps its own outputs. A combining choice (merge) writes one file beside the first source.
        var a = Directory.CreateDirectory(Path.Combine(root, "beside", "a")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(root, "beside", "b")).FullName;
        File.Copy(made["png"], Path.Combine(a, "one.png"));
        File.Copy(made["png"], Path.Combine(b, "two.png"));
        File.Copy(made["pdf"], Path.Combine(a, "one.pdf"));
        File.Copy(made["pdf"], Path.Combine(b, "two.pdf"));
        var asked = new List<string>();
        var two = await runner.RunBesideSourcesAsync("convert:jpg", [Path.Combine(a, "one.png"), Path.Combine(b, "two.png")], f => { asked.Add(f); return Task.FromResult<string?>(f); }, none, default);
        check(two is [var ja, var jb] && ja == Path.Combine(a, "one.jpg") && jb == Path.Combine(b, "two.jpg") && File.Exists(ja) && File.Exists(jb) && asked.SequenceEqual([a, b]), "Two sources in two folders get their outputs in their own folders");
        var merged = await runner.RunBesideSourcesAsync("mergePDF", [Path.Combine(b, "two.pdf"), Path.Combine(a, "one.pdf")], same, none, default);
        check(merged is [var m] && Path.GetDirectoryName(m) == b, "Merging PDFs from two folders writes one file beside the first");
        var picked = Directory.CreateDirectory(Path.Combine(root, "beside", "picked")).FullName;
        var elsewhere = await runner.RunBesideSourcesAsync("convert:webp", [Path.Combine(a, "one.png"), Path.Combine(b, "two.png")], f => Task.FromResult<string?>(f == b ? picked : f), none, default);
        check(elsewhere is [var wa, var wb] && Path.GetDirectoryName(wa) == a && Path.GetDirectoryName(wb) == picked, "A read-only folder's outputs go to the picked folder; the others stay beside their source");
        var cancelled = await runner.RunBesideSourcesAsync("convert:bmp", [Path.Combine(a, "one.png"), Path.Combine(b, "two.png")], f => Task.FromResult<string?>(f == b ? null : f), none, default);
        check(cancelled is null && !File.Exists(Path.Combine(a, "one.bmp")) && !File.Exists(Path.Combine(b, "two.bmp")), "Cancelling the folder picker for one folder writes nothing anywhere");
        File.WriteAllText(Path.Combine(b, "broken.png"), "not a picture");
        var failure = await Catch(() => runner.RunBesideSourcesAsync("convert:gif", [Path.Combine(a, "one.png"), Path.Combine(b, "broken.png")], same, none, default));
        check(failure is not null && !File.Exists(Path.Combine(a, "one.gif")) && !Directory.EnumerateFileSystemEntries(a, ".nashi-*").Any() && !Directory.EnumerateFileSystemEntries(b, ".nashi-*").Any(), "A failure in the second folder removes the first folder's outputs and leaves no staging");
    }

    /// Cancel while soffice runs: the destination stays empty and every soffice process of the job is gone.
    static async Task CancelChecks(OperationRunner runner, string docx, string outputs, Action<bool, string> check)
    {
        var before = OfficeJobs().ToHashSet();
        var dest = Path.Combine(outputs, "cancel-office");
        using var cts = new CancellationTokenSource();
        var run = runner.RunAsync("convert:pdf", [docx], dest, new Progress<OperationProgress>(), cts.Token);
        int[] mine = [];
        for (var i = 0; i < 300 && mine.Length == 0 && !run.IsCompleted; i++) { await Task.Delay(20); mine = OfficeJobs().Except(before).ToArray(); }
        cts.Cancel();
        var error = await Catch(() => run);
        var alive = mine;
        for (var i = 0; i < 100 && alive.Length > 0; i++) { await Task.Delay(50); alive = mine.Where(Alive).ToArray(); }
        check(mine.Length > 0, "The cancel check saw its soffice start");
        check(error is OperationCanceledException && (!Directory.Exists(dest) || Directory.GetFileSystemEntries(dest).Length == 0), "Cancelling an Office conversion leaves no partial output");
        check(alive.Length == 0, "Cancelling an Office conversion kills soffice");
        foreach (var pid in alive) try { System.Diagnostics.Process.GetProcessById(pid).Kill(); } catch (ArgumentException) { } // only this job's own processes
    }

    /// Malformed EPUB packages give one readable error; SVG pictures keep their MIME type.
    static async Task EpubChecks(string root, Action<bool, string> check)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, "epub-cases")).FullName;
        const string Container = "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>";
        string Book(string name, string opf, string chapter = "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><p>x</p></body></html>")
        {
            var path = Path.Combine(dir, name + ".epub");
            Zip(path, ("mimetype", "application/epub+zip"), ("META-INF/container.xml", Container), ("content.opf", opf), ("c.xhtml", chapter), ("pic.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4\" height=\"4\"/>"));
            return path;
        }
        const string Manifest = "<manifest><item id=\"c\" href=\"c.xhtml\" media-type=\"application/xhtml+xml\"/></manifest>";
        var cases = new[]
        {
            Book("missing-item", "<package xmlns=\"http://www.idpf.org/2007/opf\">" + Manifest + "<spine><itemref idref=\"nope\"/></spine></package>"),
            Book("no-href", "<package xmlns=\"http://www.idpf.org/2007/opf\"><manifest><item id=\"c\"/></manifest><spine><itemref idref=\"c\"/></spine></package>"),
            Book("bad-xml", "<package><manifest>"),
        };
        foreach (var book in cases)
        {
            var work = Directory.CreateDirectory(Path.Combine(dir, "work-" + Path.GetFileNameWithoutExtension(book))).FullName;
            Exception? error = null;
            try { DocumentOperations.EpubToHtml(book, work, default); } catch (Exception e) { error = e; }
            check(error is InvalidDataException { Message: "This EPUB could not be read." }, "A malformed EPUB (" + Path.GetFileNameWithoutExtension(book) + ") says it could not be read: " + error?.Message);
        }
        var svg = Book("svg", "<package xmlns=\"http://www.idpf.org/2007/opf\">" + Manifest + "<spine><itemref idref=\"c\"/></spine></package>", "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><img src=\"pic.svg\"/></body></html>");
        var html = File.ReadAllText(DocumentOperations.EpubToHtml(svg, Directory.CreateDirectory(Path.Combine(dir, "work-svg")).FullName, default));
        check(html.Contains("data:image/svg+xml;base64,"), "An EPUB's SVG picture is embedded as image/svg+xml");
        await Task.CompletedTask;
    }

    /// The Markdown export probe reads LibreOffice's filter registry next to the resolved soffice.
    static void MarkdownFilterChecks(string root, Action<bool, string> check)
    {
        bool Probe(string name, string? xcd)
        {
            var program = Directory.CreateDirectory(Path.Combine(root, "lo-" + name, "program")).FullName;
            var soffice = Path.Combine(program, "soffice");
            File.WriteAllText(soffice, "#!/bin/sh\n");
            if (xcd is not null) File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(root, "lo-" + name, "share", "registry")).FullName, "writer.xcd"), xcd);
            var link = Path.Combine(root, "lo-" + name + "-bin");
            File.CreateSymbolicLink(link, soffice);
            return DocumentOperations.ExportsMarkdown(link);
        }
        const string Node = "<node oor:name=\"Markdown\" oor:op=\"replace\">\n  <prop oor:name=\"Flags\">\n    <value>{0}</value>";
        check(Probe("export", string.Format(Node, "IMPORT EXPORT ALIEN")), "A LibreOffice whose registry has a Markdown EXPORT filter exports Markdown");
        check(!Probe("import", string.Format(Node, "IMPORT ALIEN")), "An import-only Markdown filter does not count");
        check(!Probe("none", "<node oor:name=\"MS Word 2007 XML\"/>"), "A LibreOffice without a Markdown filter falls back to plain text");
        check(Probe("unknown", null), "An unfamiliar install layout is assumed to export Markdown, so its failures stay errors");
    }

    // soffice processes running with a Nashi job profile (OfficeDocuments names them NashiOffice-<guid>).
    static IEnumerable<int> OfficeJobs()
    {
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            string command;
            try { command = File.ReadAllText(Path.Combine(dir, "cmdline")); } catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
            if (command.Contains("NashiOffice-", StringComparison.Ordinal)) yield return pid;
        }
    }

    static bool Alive(int pid)
    {
        try { return File.ReadAllText($"/proc/{pid}/stat").Split(' ')[2] != "Z"; }
        catch (IOException) { return false; }
    }

    static string? Missing(string ext, string format, bool media, bool office, bool deno)
    {
        var input = "x." + ext;
        if ((FeatureCatalog.IsAudio(input) || FeatureCatalog.IsVideo(input) || ext == "heic") && !media) return "ffmpeg";
        if (!DocumentOperations.Handles(input)) return null;
        if ((DocumentOperations.NeedsOffice(input, format) || ext is "ppt" or "pptx" or "xls" or "xlsx" or "ods") && !office) return "LibreOffice";
        if ((DocumentOperations.NeedsDeno(input, format)) && !deno) return "Deno";
        return null;
    }

    /// One small fixture per input type. Office-made fixtures (slides, spreadsheets, Pages) need the tools that make them.
    static async Task<Dictionary<string, string>> Fixtures(string dir, string? office, string? deno, string? ffmpeg)
    {
        var made = new Dictionary<string, string>();
        string File(string ext) => Path.Combine(dir, Name + "." + ext);
        string Text(string ext, string text) { System.IO.File.WriteAllText(File(ext), text, new UTF8Encoding(false)); return made[ext] = File(ext); }

        made["pdf"] = File("pdf");
        System.IO.File.WriteAllBytes(made["pdf"], Pdf(Marker + " PDF page one", Marker + " PDF page two"));
        Text("txt", Marker + " ğüş text\nSecond line\n");
        Text("md", "# " + Marker + " ğüş\n\nSome *markdown* text.\n");
        Text("csv", "Name,Count\n" + Marker + " ğüş,3\nPear,5\n");
        Text("tsv", "Name\tCount\n" + Marker + " ğüş\t3\nPear\t5\n");
        Text("html", "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/></head><body><h1>" + Marker + " ğüş</h1><p>HTML text.</p></body></html>");
        Text("rtf", @"{\rtf1\ansi\deff0{\fonttbl{\f0 Helvetica;}}\f0 " + Marker + @" rich text\par Second line\par}");
        Text("textclipping", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><plist version=\"1.0\"><dict><key>UTI-Data</key><dict><key>public.utf8-plain-text</key><string>" + Marker + " ğüş clipping</string></dict></dict></plist>");
        var rtfd = Directory.CreateDirectory(File("rtfd")).FullName;
        using (var picture = new MagickImage(MagickColors.SteelBlue, 40, 30)) picture.Write(Path.Combine(rtfd, "picture.png"));
        System.IO.File.WriteAllText(Path.Combine(rtfd, "TXT.rtf"), @"{\rtf1\ansi\deff0{\fonttbl{\f0 Helvetica;}}\f0 " + Marker + @" RTFD text\par {{\NeXTGraphic picture.png \width800 \height600 \appleattachmentpadding0 \appleembedtype0 \appleaqc}" + "¬" + @"}\par}", Encoding.Latin1);
        made["rtfd"] = rtfd;
        made["docx"] = File("docx");
        Zip(made["docx"], ("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>"),
            ("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>"),
            ("word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>" + Marker + " ğüş docx</w:t></w:r></w:p><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Bold line</w:t></w:r></w:p></w:body></w:document>"));
        made["epub"] = File("epub");
        Zip(made["epub"], ("mimetype", "application/epub+zip"),
            ("META-INF/container.xml", "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>"),
            ("OEBPS/content.opf", "<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"id\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"id\">urn:uuid:1</dc:identifier><dc:title>Fixture</dc:title><dc:language>tr</dc:language></metadata><manifest><item id=\"c1\" href=\"chapter.xhtml\" media-type=\"application/xhtml+xml\"/></manifest><spine><itemref idref=\"c1\"/></spine></package>"),
            ("OEBPS/chapter.xhtml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>One</title></head><body><h1>" + Marker + "&nbsp;ğüş</h1><p>EPUB chapter text&mdash;one.</p></body></html>"));

        foreach (var (ext, format) in new[] { ("jpg", MagickFormat.Jpeg), ("png", MagickFormat.Png), ("webp", MagickFormat.WebP), ("tiff", MagickFormat.Tiff), ("avif", MagickFormat.Avif), ("bmp", MagickFormat.Bmp) })
        {
            using var image = new MagickImage("gradient:red-blue", 64, 48);
            image.Write(made[ext] = File(ext), format);
        }
        Text("svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"48\"><rect width=\"64\" height=\"48\" fill=\"#3a6\"/></svg>");
        var zipEntry = Path.Combine(dir, "inner ğ.txt");
        System.IO.File.WriteAllText(zipEntry, "Nashi archive fixture");
        made["zip"] = File("zip");
        using (var zip = ZipFile.Open(made["zip"], ZipArchiveMode.Create)) zip.CreateEntryFromFile(zipEntry, "inner ğ.txt");
        made["tar"] = File("tar");
        using (var tar = System.IO.File.Create(made["tar"])) using (var writer = new TarWriter(tar)) writer.WriteEntry(zipEntry, "inner ğ.txt");
        made["gz"] = File("gz");
        using (var gz = new GZipStream(System.IO.File.Create(made["gz"]), CompressionLevel.Fastest)) using (var tar = System.IO.File.OpenRead(made["tar"])) tar.CopyTo(gz);
        made["rar"] = File("rar");
        StoredRar(made["rar"]);
        Text("srt", "1\n00:00:01,000 --> 00:00:02,000\n" + Marker + " ş\n\n");
        Text("vtt", "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n" + Marker + " ş\n\n");

        if (!string.IsNullOrEmpty(ffmpeg))
        {
            using (var image = new MagickImage("gradient:red-blue", 64, 48)) PhotoCodec.EncodePng(image.ToByteArray(MagickFormat.Png), made["heic"] = File("heic"), "heic", encoder: ffmpeg);
            foreach (var ext in new[] { "mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma", "aac" })
                await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=duration=1", made[ext] = File(ext)]);
            foreach (var ext in new[] { "mp4", "mov", "mkv", "webm", "avi", "wmv" })
                await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=160x120:rate=10", "-f", "lavfi", "-i", "sine=duration=1", "-shortest", "-pix_fmt", "yuv420p", made[ext] = File(ext)]);
            System.IO.File.Copy(made["mp4"], made["m4v"] = File("m4v"));
            await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=160x120:rate=10", made["gif"] = File("gif")]);
        }
        if (office is not null)
        {
            var engine = new OfficeDocuments(office, "", AppContext.BaseDirectory);
            var slides = Path.Combine(dir, "slides.fodp");
            System.IO.File.WriteAllText(slides, "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.presentation\"><office:body><office:presentation><draw:page draw:name=\"One\"><draw:frame svg:x=\"2cm\" svg:y=\"2cm\" svg:width=\"20cm\" svg:height=\"3cm\"><draw:text-box><text:p>" + Marker + " ğüş slide</text:p></draw:text-box></draw:frame></draw:page></office:presentation></office:body></office:document>");
            var sheets = Path.Combine(dir, "sheet.csv");
            System.IO.File.Copy(made["csv"], sheets);
            var built = await Task.WhenAll(new[] { (slides, "pptx"), (slides, "ppt"), (sheets, "xlsx"), (sheets, "xls"), (sheets, "ods") }.Select(async s =>
            {
                var output = await engine.OfficeAsync(s.Item1, s.Item2, Path.Combine(dir, "made-" + s.Item2), default);
                var named = File(s.Item2);
                System.IO.File.Move(output, named);
                return (Ext: s.Item2, Path: named);
            }));
            foreach (var (ext, path) in built) made[ext] = path;
            if (deno is not null)
            {
                var pages = await new DocumentOperations(office, deno).ConvertAsync([made["docx"]], "pages", Path.Combine(dir, "made-pages"), null, default);
                System.IO.File.Move(pages[0], made["pages"] = File("pages"));
            }
        }
        return made;
    }

    /// null when the outputs are real files of the format; otherwise what is wrong.
    static async Task<string?> Valid(string input, string format, IReadOnlyList<string> outputs, MediaEngine? media)
    {
        if (outputs.Count == 0) return "no output";
        foreach (var output in outputs)
        {
            if (!File.Exists(output) || new FileInfo(output).Length == 0) return "empty or missing: " + output;
            if (FeatureCatalog.Extension(output) != format) return "wrong extension: " + Path.GetFileName(output);
            var document = DocumentOperations.Handles("x." + input);
            if (document && !Path.GetFileName(output).StartsWith(Name, StringComparison.Ordinal)) return "lost the file name: " + Path.GetFileName(output);
            string? Entry(string name) { using var zip = ZipFile.OpenRead(output); return zip.GetEntry(name) is { Length: > 0 } ? null : "no " + name; }
            string Read() => File.ReadAllText(output);
            var problem = format switch
            {
                // A document's text survives into the PDF; an image's PDF is a picture.
                "pdf" => Try(() => { using var pdf = UglyToad.PdfPig.PdfDocument.Open(output); return pdf.NumberOfPages < 1 ? "no pages" : document && !pdf.GetPages().Any(p => p.Text.Contains(Marker)) ? "text lost" : null; }),
                "png" or "jpg" or "webp" or "tiff" or "avif" or "bmp" => Try(() => { var info = new MagickImageInfo(output); return info.Format == PhotoCodec.Format(format) && info.Width > 0 ? null : "decodes as " + info.Format; }),
                "gif" => Try(() => new MagickImageInfo(output).Format == MagickFormat.Gif ? null : "not a GIF"),
                "docx" => Entry("word/document.xml") ?? Try(() => { using var zip = ZipFile.OpenRead(output); using var r = new StreamReader(zip.GetEntry("word/document.xml")!.Open()); return r.ReadToEnd().Contains(Marker) ? null : "text lost"; }),
                "pptx" => Entry("ppt/presentation.xml"),
                "xlsx" => Entry("xl/workbook.xml"),
                "ods" => Try(() => { using var zip = ZipFile.OpenRead(output); using var r = new StreamReader(zip.GetEntry("mimetype")!.Open()); return r.ReadToEnd() == "application/vnd.oasis.opendocument.spreadsheet" ? null : "bad mimetype"; }),
                "epub" => Try(() => { using var zip = ZipFile.OpenRead(output); using var r = new StreamReader(zip.Entries[0].Open()); return zip.Entries[0].FullName == "mimetype" && r.ReadToEnd() == "application/epub+zip" ? null : "bad mimetype"; }),
                "pages" => Entry("Index/Document.iwa"),
                "txt" or "md" when DocumentOperations.Handles("x." + input) => Read().Contains(Marker, StringComparison.Ordinal) ? null : "text lost: " + Read()[..Math.Min(80, Read().Length)],
                "html" => Read().Contains(Marker, StringComparison.Ordinal) && Read().Contains("<html", StringComparison.OrdinalIgnoreCase) ? null : "not HTML with the text",
                "csv" => Read().Contains(Marker + " ğüş,3", StringComparison.Ordinal) ? null : "bad CSV: " + Read(),
                "tsv" => Read().Contains(Marker + " ğüş\t3", StringComparison.Ordinal) ? null : "bad TSV: " + Read(),
                "srt" => Read().Contains("-->") ? null : "bad SRT",
                "vtt" => Read().StartsWith("WEBVTT") ? null : "bad VTT",
                "txt" => Read().Contains(Marker) ? null : "text lost",
                "zip" => Try(() => { using var zip = ZipFile.OpenRead(output); return zip.Entries.Count > 0 ? null : "empty zip"; }),
                "tar" => Try(() => { using var s = File.OpenRead(output); using var r = new TarReader(s); return r.GetNextEntry() is not null ? null : "empty tar"; }),
                "gz" => Try(() => { using var s = new GZipStream(File.OpenRead(output), CompressionMode.Decompress); return s.ReadByte() >= 0 ? null : "empty gz"; }),
                _ when media is not null => await Probe(media, output, format),
                _ => "unchecked format " + format,
            };
            if (problem is not null) return problem;
        }
        return null;
    }

    static async Task<string?> Probe(MediaEngine media, string output, string format)
    {
        try
        {
            var info = await media.InspectAsync(output);
            var video = MediaEngine.VideoFormats.Contains(format);
            return info.Duration > 0 && (video ? info.Streams.Any(s => s.Kind == "video") : info.Streams.Any(s => s.Kind == "audio")) ? null : "no " + (video ? "video" : "audio") + " stream";
        }
        catch (Exception error) { return "ffprobe: " + error.Message; }
    }

    static string? Try(Func<string?> f)
    {
        try { return f(); }
        catch (Exception error) { return error.GetType().Name + ": " + error.Message; }
    }

    static async Task<Exception?> Catch(Func<Task> f)
    {
        try { await f(); return null; }
        catch (Exception error) { return error; }
    }

    static bool Throws<T>(Action a) where T : Exception { try { a(); return false; } catch (T) { return true; } }

    static void Zip(string path, params (string Name, string Text)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name, name == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
    }

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

    // The stored (uncompressed) RAR 4 fixture Nashi.Verify uses.
    static void StoredRar(string output)
    {
        static uint Crc(byte[] data) { var crc = uint.MaxValue; foreach (var b in data) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1; } return ~crc; }
        using var file = new BinaryWriter(File.Create(output));
        file.Write(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00 });
        void Header(byte type, ushort flags, byte[] payload)
        {
            using var memory = new MemoryStream();
            using (var header = new BinaryWriter(memory, Encoding.UTF8, true)) { header.Write(type); header.Write(flags); header.Write((ushort)(7 + payload.Length)); header.Write(payload); }
            var bytes = memory.ToArray();
            file.Write((ushort)Crc(bytes));
            file.Write(bytes);
        }
        Header(0x73, 0, new byte[6]);
        var data = Encoding.UTF8.GetBytes("Nashi pear fixture");
        var name = Encoding.ASCII.GetBytes("Fruit.txt");
        using var content = new MemoryStream();
        using (var header = new BinaryWriter(content, Encoding.UTF8, true)) { header.Write((uint)data.Length); header.Write((uint)data.Length); header.Write((byte)2); header.Write(Crc(data)); header.Write(0u); header.Write((byte)20); header.Write((byte)0x30); header.Write((ushort)name.Length); header.Write(0x20u); header.Write(name); }
        Header(0x74, 0x8000, content.ToArray());
        file.Write(data);
        Header(0x7b, 0, []);
    }
}
