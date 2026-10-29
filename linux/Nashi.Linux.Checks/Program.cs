using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;

var passed = 0;
var failures = new List<string>();
void Check(bool condition, string name)
{
    if (condition) passed++;
    else failures.Add(name);
}

var temp = Directory.CreateTempSubdirectory("nashi-linux-checks-");
try
{
    SelectionChecks(temp.FullName);
    CapabilityChecks();
    ImageChecks(temp.FullName);
    HomeChecks();
}
finally
{
    temp.Delete(recursive: true);
}

if (failures.Count > 0)
{
    foreach (var failure in failures) Console.Error.WriteLine($"FAIL: {failure}");
    return 1;
}
Console.WriteLine($"{{\"passed\":{passed},\"scope\":\"Linux app\"}}");
return 0;

void SelectionChecks(string root)
{
    var a = Path.Combine(root, "a.png");
    var unicode = Path.Combine(root, "-ğüş photo.png");
    var dir = Path.Combine(root, "dir");
    File.WriteAllText(a, "a");
    File.WriteAllText(unicode, "ş");
    Directory.CreateDirectory(dir);

    var selection = new Selection();
    var changes = 0;
    selection.Changed += () => changes++;

    var result = selection.Add([a, a, dir, Path.Combine(root, "missing.png"), unicode]);
    Check(result == new AddResult(2, 3), "Add counts added and skipped paths");
    Check(selection.Files.SequenceEqual([Path.GetFullPath(a), Path.GetFullPath(unicode)]), "Add keeps existing files in order without duplicates");
    Check(changes == 1, "Add fires Changed once");

    selection.Add([Path.Combine(root, ".", "a.png")]);
    Check(selection.Files.Count == 2, "Add normalizes paths before de-duplicating");

    selection.Remove(a);
    Check(selection.Files.Count == 1 && selection.Files[0] == Path.GetFullPath(unicode), "Remove drops one file");
    Check(changes == 2, "Remove fires Changed once");

    selection.Clear();
    Check(selection.Files.Count == 0 && changes == 3, "Clear empties and fires Changed once");
}

void CapabilityChecks()
{
    string[] png = ["x.png"], mp4 = ["x.mp4"], pdf2 = ["a.pdf", "b.pdf"], docx = ["x.docx"], zip = ["x.zip"];
    string[] mixed = ["x.png", "y.mp4"];

    var pngFormats = Capabilities.Formats(png);
    Check(pngFormats.Contains("jpg") && pngFormats.Contains("webp") && pngFormats.Contains("pdf"), "Formats for a PNG include jpg, webp and pdf");
    Check(!pngFormats.Contains("png"), "Formats for a PNG exclude png");
    Check(Capabilities.Tools(png).SequenceEqual(["compress"]), "Tools for a PNG is compress only");
    var mp4Tools = Capabilities.Tools(mp4);
    Check(mp4Tools.Contains("compress") && mp4Tools.Contains("muteVideo"), "Tools for an MP4 include compress and muteVideo");
    Check(Capabilities.Formats(mp4).SequenceEqual(FeatureCatalog.Formats(mp4)), "Formats for an MP4 keep catalog order");
    Check(Capabilities.Formats(zip).SequenceEqual(["tar", "gz"]), "Formats for a ZIP are tar and gz");
    Check(Capabilities.Formats(mixed).Length == 0, "Formats for PNG plus MP4 is empty");
    Check(Capabilities.Tools(mixed).All(t => FeatureCatalog.Tools(mixed).Contains(t)), "Tools for PNG plus MP4 stay inside the catalog intersection");
    Check(Capabilities.Tools(pdf2).Contains("mergePDF"), "Tools for two PDFs include mergePDF");
    Check(Capabilities.Tools([]).Length == 0, "Tools for an empty selection is empty");
    Check(Capabilities.Formats(pdf2).Length == 0 && Capabilities.Formats(["x.pdf"]).Length == 0, "Formats for PDFs are empty");
    Check(Capabilities.Formats(docx).Length == 0, "Formats for a DOCX are empty");
}

void ImageChecks(string root)
{
    var work = Directory.CreateDirectory(Path.Combine(root, "images")).FullName;
    var source = Path.Combine(work, "-ğüş photo.png");
    using (var gradient = new MagickImage("gradient:red-blue", 64, 48)) gradient.Write(source);
    var second = Path.Combine(work, "second.png");
    File.Copy(source, second);
    var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source));

    foreach (var format in new[] { "png", "jpg", "webp", "heic", "avif", "bmp", "gif", "tiff", "pdf" })
    {
        var dest = Path.Combine(work, "out-" + format);
        if (format == "heic")
        {
            // Magick.NET on Linux has no HEIC encoder, so the route is not offered.
            Check(!Capabilities.Formats(["x.png"]).Contains("heic"), "heic is not offered for images");
            try { ImageOperations.Convert([source], "heic", dest, null, default); Check(false, "Convert to heic throws"); }
            catch (NotSupportedException) { Check(true, "Convert to heic throws NotSupportedException"); }
            continue;
        }
        try
        {
            var files = ImageOperations.Convert([source], format, dest, null, default);
            Check(files.Count == 1 && Directory.GetFileSystemEntries(dest).Length == 1 && Path.GetExtension(files[0]) == "." + format, $"Convert to {format} produces one file with its extension");
            if (format == "pdf")
                using (var doc = UglyToad.PdfPig.PdfDocument.Open(files[0])) Check(doc.NumberOfPages == 1, "PDF has one page");
            else
            {
                using var back = PhotoCodec.Read(files[0]);
                Check(back.Width == 64 && back.Height == 48, $"{format} output reopens at 64x48");
            }
        }
        catch (Exception error)
        {
            Check(false, $"Convert to {format} threw {error.GetType().Name}: {error.Message}");
        }
    }

    var pdf2 = ImageOperations.Convert([source, second], "pdf", Path.Combine(work, "out-pdf2"), null, default);
    using (var doc = UglyToad.PdfPig.PdfDocument.Open(pdf2[0])) Check(pdf2.Count == 1 && doc.NumberOfPages == 2, "Two images make one two-page PDF");

    var noisy = Path.Combine(work, "noisy.jpg");
    var pixels = new byte[1000 * 1000 * 3];
    new Random(1).NextBytes(pixels);
    using (var noise = new MagickImage()) { noise.ReadPixels(pixels, new PixelReadSettings(1000, 1000, StorageType.Char, PixelMapping.RGB)); noise.Quality = 100; noise.Write(noisy); }
    var small = ImageOperations.Compress([noisy], Path.Combine(work, "out-compress"), null, default);
    Check(small.Count == 1 && new FileInfo(small[0]).Length < new FileInfo(noisy).Length && small[0].EndsWith(" Compressed.jpg"), "Compress makes a smaller JPG");

    var cancelled = Path.Combine(work, "out-cancel");
    var token = new CancellationToken(true);
    try { ImageOperations.Convert([source], "jpg", cancelled, null, token); Check(false, "Cancelled convert throws"); }
    catch (OperationCanceledException) { Check(Directory.GetFileSystemEntries(cancelled).Length == 0, "Cancelled convert leaves the destination empty"); }

    Check(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(hash), "Source file is unchanged");
}

void HomeChecks()
{
    AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont()
        .SetupWithoutStarting();
    Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

    var window = new MainWindow { Width = 820, Height = 780 };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    var frame = window.CaptureRenderedFrame();

    var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
    Check(texts.Contains("Your files. Their next form."), "Home shows the hero title");
    Check(window.GetVisualDescendants().OfType<Button>().Any(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Choose files")), "Home shows Choose files");
    var canvas = (window.Background as ISolidColorBrush)?.Color;
    Check(canvas is { } c && Near(c.R, 0xF9) && Near(c.G, 0xF6) && Near(c.B, 0xED), "Home background is light canvas #F9F6ED");
    Check(frame is not null, "Home renders a frame");

    var smoke = Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY");
    if (!string.IsNullOrEmpty(smoke) && frame is not null)
    {
        Directory.CreateDirectory(smoke);
        frame.Save(Path.Combine(smoke, "home-light.png"));
    }

    var picked = Path.Combine(temp.FullName, "chip.png");
    File.WriteAllText(picked, "x");
    window.Selection.Add([picked]);
    Dispatcher.UIThread.RunJobs();
    window.CaptureRenderedFrame();
    var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
    Check(shown.Count(t => t == "chip.png") >= 2 && shown.Contains("1 file ready."), "Selected files show as chips with a status line");
    window.Close();
}

static bool Near(byte actual, byte expected) => Math.Abs(actual - expected) <= 1;
