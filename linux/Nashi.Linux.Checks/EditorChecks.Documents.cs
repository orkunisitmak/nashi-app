using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Nashi.Core;
using Nashi.Linux;
using Nashi.Linux.Editors;

static partial class EditorChecks
{
    // ---- PDF and archive editors: organize, split, merge, compress, passwords, extract.

    /// A PDF whose page n is (200 + 10n) × (300 + 10n) points, so a page's size says which one it was.
    static string Pages(string path, int count, string? password = null)
    {
        using var pdf = new PdfSharp.Pdf.PdfDocument();
        for (var n = 1; n <= count; n++)
        {
            var page = pdf.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(200 + 10 * n);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(300 + 10 * n);
        }
        if (password is not null) { pdf.SecuritySettings.UserPassword = password; pdf.SecuritySettings.OwnerPassword = password + "-owner"; }
        pdf.Save(path);
        return path;
    }

    /// Each page's original number (from its size) and rotation.
    static (int Number, int Rotation)[] Read(string path, string password = "")
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(path, new UglyToad.PdfPig.ParsingOptions { Password = password });
        return [.. pdf.GetPages().Select(p => ((int)Math.Round((Math.Min(p.MediaBox.Bounds.Width, p.MediaBox.Bounds.Height) - 200) / 10), p.Rotation.Value))];
    }

    static void Documents(string work)
    {
        var folder = Directory.CreateDirectory(Path.Combine(work, "pdf")).FullName;
        var pdf = Pages(Path.Combine(folder, Name + ".pdf"), 5);
        var other = Pages(Path.Combine(folder, "--other.pdf"), 2);
        var locked = Pages(Path.Combine(folder, Name + " locked.pdf"), 3, "secret");
        var hashes = new[] { pdf, other, locked }.ToDictionary(f => f, Hash);
        IReadOnlyList<string> Save(ToolEditorWindow window, string? into = null)
        {
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            return window.Job is null ? [] : Run(window.Job, window.Panel.Inputs, into ?? folder);
        }

        // Organize: select, rotate, duplicate, move, delete; the saved order and turns match.
        var organize = Open(new PdfOrganizeEditor([pdf]));
        var pages = (PdfOrganizeEditor)organize.Panel;
        check(pages.Pages.Count == 5 && !organize.Canvas.IsVisible && Texts(organize).Contains("5 pages"), "Organize PDF shows every page as a thumbnail");
        pages.Select(1);
        pages.Rotate();
        pages.Duplicate();
        pages.Select(4);
        pages.Select(0, KeyModifiers.Control);
        organize.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        pages.Move(3, 0);
        check(pages.Pages.Select(p => p.Number).SequenceEqual([5, 2, 2, 3]), "Rotate, Duplicate, Delete and a drag reorder the pages (" + string.Join(",", pages.Pages.Select(p => p.Number)) + ")");
        var outputs = Save(organize);
        var saved = outputs.Count == 1 ? Read(outputs[0]) : [];
        check(saved.Select(p => p.Number).SequenceEqual([5, 2, 2, 3]) && saved.Select(p => p.Rotation).SequenceEqual([0, 90, 90, 0]) && Path.GetFileName(outputs[0]) == Name + " Organized.pdf", "The organized PDF has that order, the duplicate and the rotation, beside the source");
        var uniform = Open(new PdfOrganizeEditor([pdf]));
        ((PdfOrganizeEditor)uniform.Panel).Uniform(true);
        outputs = Save(uniform, Directory.CreateDirectory(Path.Combine(folder, "uniform")).FullName);
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(outputs[0]))
            check(doc.GetPages().Select(p => Math.Round(p.CropBox.Bounds.Width)).Distinct().Count() == 1, "Make all pages the same width gives every page one width");

        // Split: every page, every 2 pages, ranges (one file each), refused ranges.
        var split = Open(new PdfSplitEditor([pdf]));
        var splitter = (PdfSplitEditor)split.Panel;
        check(Texts(split).Contains(Name + ".pdf: 5 pages"), "Split PDF shows the page count");
        outputs = Save(split, Directory.CreateDirectory(Path.Combine(folder, "every")).FullName);
        check(outputs.Count == 5, "Split every page makes 5 files");
        split = Open(new PdfSplitEditor([pdf]));
        ((PdfSplitEditor)split.Panel).Mode("group", 2);
        outputs = Save(split, Directory.CreateDirectory(Path.Combine(folder, "group")).FullName);
        check(outputs.Count == 3 && Read(outputs[2]).Length == 1, "Split every 2 pages makes 2 + 2 + 1");
        split = Open(new PdfSplitEditor([pdf]));
        splitter = (PdfSplitEditor)split.Panel;
        splitter.Mode("ranges", text: "1-3, 5");
        outputs = Save(split);
        check(outputs.Select(Path.GetFileName).SequenceEqual([Name + " Pages 1-3.pdf", Name + " Pages 5.pdf"]) && Read(outputs[0]).Select(p => p.Number).SequenceEqual([1, 2, 3]) && Read(outputs[1]).Single().Number == 5, "Split by ranges makes one file per range, named by it");
        split = Open(new PdfSplitEditor([pdf]));
        splitter = (PdfSplitEditor)split.Panel;
        splitter.Mode("ranges", text: "2-9");
        split.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        var refused = Texts(split).Any(t => t.Contains("A page range is outside the PDF."));
        splitter.Mode("ranges", text: "1-2; x");
        split.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        check(refused && split.IsVisible && split.Job is null && Texts(split).Contains("Choose page ranges such as 1-3, 5 within the PDF page count."), "Ranges past the end or not like 1-3, 5 are refused before Save");
        split.Close();

        // Merge in the chosen order.
        var merge = Open(new OrderEditor("mergePDF", [pdf, other], null));
        check(Texts(merge).Contains("5 pages") && Texts(merge).Contains("2 pages"), "Merge PDFs lists each PDF's page count");
        ((OrderEditor)merge.Panel).Move(1, 0);
        outputs = Save(merge);
        check(outputs.Count == 1 && Read(outputs[0]).Select(p => p.Number).SequenceEqual([1, 2, 1, 2, 3, 4, 5]), "Merge PDFs follows the order (the 2-page PDF first)");

        // A password-protected PDF: refused without its password, opened with it.
        check(!Locks.PdfOpens(locked) && Locks.PdfOpens(locked, "secret"), "A locked PDF opens only with its password");
        var locks = Open(new PdfOrganizeEditor([locked]));
        var unlocking = (PdfOrganizeEditor)locks.Panel;
        check(unlocking.Locked && !locks.SaveButton.IsEnabled && Texts(locks).Contains(Name + " locked.pdf is password-protected. Enter its password."), "Organize asks for a locked PDF's password");
        check(!Wait(unlocking.UnlockAsync("wrong")) && Texts(locks).Contains("The password is missing or incorrect."), "A wrong password says so");
        check(Wait(unlocking.UnlockAsync("secret")) && unlocking.Pages.Count == 3 && locks.SaveButton.IsEnabled, "The right password shows the pages");
        unlocking.Select(0);
        unlocking.Rotate();
        outputs = Save(locks);
        check(outputs.Count == 1 && Read(outputs[0]).Select(p => p.Rotation).SequenceEqual([90, 0, 0]), "The unlocked PDF saves organized");
        var noPassword = false;
        try { DocumentEngine.Pdf("splitPDF", [locked], Directory.CreateDirectory(Path.Combine(folder, "nopass")).FullName); }
        catch (Exception) { noPassword = true; }
        var lockedSplit = Open(new PdfSplitEditor([locked]));
        lockedSplit.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        var lockedRefused = lockedSplit.Job is null && Texts(lockedSplit).Contains("The password is missing or incorrect.");
        ((PdfSplitEditor)lockedSplit.Panel).Password("secret");
        outputs = Save(lockedSplit, Directory.CreateDirectory(Path.Combine(folder, "locked-split")).FullName);
        check(noPassword && lockedRefused && outputs.Count == 3, "Split refuses a locked PDF without its password and splits it with it");

        // Compress and metadata for PDFs.
        var compress = Open(ToolEditorWindow.For("compress", [pdf])!);
        check(compress.Panel is FileCompressEditor && Texts(compress).Contains("Nashi rewrites the PDF with compressed content streams. Text stays selectable and pages stay sharp."), "Compress for a PDF explains what it does");
        outputs = Save(compress);
        check(outputs.Count == 1 && Path.GetFileName(outputs[0]) == Name + " Compressed.pdf" && Read(outputs[0]).Length == 5, "Compress writes the PDF beside it with every page");
        check(MetadataEditor.EditableFor([pdf]).Contains("PDF:Title") && !MetadataEditor.EditableFor([pdf]).Any(k => k.StartsWith("EXIF:")) && MetadataEditor.EditableFor(["a.jpg", pdf]).All(k => k.StartsWith("XMP:")), "PDF metadata edits the PDF and XMP tags (XMP only with images)");
        check(hashes.All(p => Hash(p.Key).SequenceEqual(p.Value)), "The PDF editors never modify the originals");

        // Archives: a protected ZIP asks for its password; a plain one extracts straight away.
        var archives = Directory.CreateDirectory(Path.Combine(work, "archives")).FullName;
        var plain = Path.Combine(archives, Name + " plain.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(plain, System.IO.Compression.ZipArchiveMode.Create))
            using (var entry = new StreamWriter(zip.CreateEntry("hello.txt").Open())) entry.Write("hello");
        check(!Locks.ArchiveLocked(plain) && ToolEditorWindow.For("extractArchive", [plain]) is null && !ToolEditorWindow.Opens("extractArchive", [plain]), "A plain archive extracts without an editor");
        // A damaged ZIP whose comment talks about passwords: it is not "locked", so it never opens the password editor;
        // its extraction reports the real error.
        var damaged = Path.Combine(archives, Name + " damaged.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(damaged, System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.Comment = "password required: this archive is encrypted";
            using var entry = new StreamWriter(zip.CreateEntry("hello.txt").Open());
            entry.Write("hello");
        }
        var bytes = File.ReadAllBytes(damaged);
        var central = bytes.AsSpan().IndexOf("PK\u0001\u0002"u8);
        bytes[central + 2] = 9; // neither the central directory nor the entry's own header reads any more
        bytes[2] = 9;
        File.WriteAllBytes(damaged, bytes);
        var extractError = "";
        try { ArchiveEngine.Extract(damaged, Directory.CreateDirectory(Path.Combine(archives, "damaged-out")).FullName, null, null, CancellationToken.None); }
        catch (Exception error) { extractError = error.Message; }
        check(central > 0 && !Locks.ArchiveLocked(damaged) && ToolEditorWindow.For("extractArchive", [damaged]) is null && Locks.ArchiveOpens(damaged, "x"), "A damaged ZIP that mentions a password is not taken for a locked one");
        check(extractError.Length > 0, "Extracting a damaged ZIP reports its own error: " + extractError);
        var brokenPdf = Path.Combine(archives, Name + " broken.pdf");
        File.WriteAllText(brokenPdf, "%PDF-1.7\nthis is not a PDF body, and no password helps\n");
        var brokenSplit = Open(new PdfSplitEditor([brokenPdf]));
        brokenSplit.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        check(!Locks.PdfLocked(brokenPdf) && Locks.PdfRefusal(brokenPdf) == "This PDF could not be opened." && brokenSplit.Job is null && Texts(brokenSplit).Contains("This PDF could not be opened."), "A damaged PDF says it could not be opened instead of asking for a password");
        brokenSplit.Close();
        var zipTool = new[] { "/usr/bin/zip", "/bin/zip" }.FirstOrDefault(File.Exists);
        if (zipTool is null) Console.WriteLine("Archive checks: zip absent; the password round trip skipped");
        else
        {
            var source = Directory.CreateDirectory(Path.Combine(archives, "src")).FullName;
            File.WriteAllText(Path.Combine(source, "secret.txt"), "the plans");
            var protectedZip = Path.Combine(archives, Name + " locked.zip");
            Wait(ProcessRunner.RunAsync(zipTool, ["-q", "-j", "-P", "hunter2", protectedZip, Path.Combine(source, "secret.txt")]));
            var archiveHash = Hash(protectedZip);
            check(Locks.ArchiveLocked(protectedZip) && ToolEditorWindow.Opens("extractArchive", [protectedZip]), "A password-protected archive opens the password editor");
            var archive = Open(ToolEditorWindow.For("extractArchive", [protectedZip])!);
            check(Texts(archive).Contains(Name + " locked.zip is password-protected. Enter its password.") && Texts(archive).Contains("Archive password (if needed)"), "The archive editor names the protected archive");
            ((ArchiveEditor)archive.Panel).Password("wrong");
            archive.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            check(archive.IsVisible && archive.Job is null && Texts(archive).Contains("The password is missing or incorrect."), "A wrong archive password is refused before extracting");
            ((ArchiveEditor)archive.Panel).Password("hunter2");
            outputs = Save(archive, archives);
            check(outputs.Count == 1 && outputs[0] == Path.Combine(archives, Name + " locked Extracted") && File.ReadAllText(Path.Combine(outputs[0], "secret.txt")) == "the plans" && Hash(protectedZip).SequenceEqual(archiveHash), "The right password extracts into one folder beside the archive, the archive untouched");
            Renders("archive", () => new ArchiveEditor([protectedZip]));
        }

        Renders("organize-pdf", () => new PdfOrganizeEditor([pdf]), w => ((PdfOrganizeEditor)w.Panel).Select(1));
        Renders("split-pdf", () => new PdfSplitEditor([pdf]), w => { ((PdfSplitEditor)w.Panel).Mode("ranges", text: "1-3, 5"); w.Rebuild(); });
        Renders("merge-pdf", () => new OrderEditor("mergePDF", [pdf, other], null));
        Renders("compress-pdf", () => new FileCompressEditor([pdf], null));
        Renders("organize-locked", () => new PdfOrganizeEditor([locked]));
    }
}
