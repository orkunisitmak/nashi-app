using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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

L10n.Language = "en"; // the English checks below must not depend on the machine's locale
var temp = Directory.CreateTempSubdirectory("nashi-linux-checks-");
try
{
    SelectionChecks(temp.FullName);
    CapabilityChecks();
    ImageChecks(temp.FullName);
    await RunnerChecks(temp.FullName);
    HomeChecks();
    WheelChecks(temp.FullName);
    await ToolSetupChecks(temp.FullName);
    LocalizationChecks(temp.FullName);
    AppearanceChecks(temp.FullName);
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
    var mp3Formats = Capabilities.Formats(["x.mp3"]);
    Check(mp3Formats.Contains("wav") && !mp3Formats.Contains("mp3") && !mp3Formats.Contains("mp4"), "Formats for an MP3 are audio formats without mp3 or video");
    Check(Capabilities.Formats(["x.srt"]).SequenceEqual(["vtt", "txt"]) && Capabilities.Formats(["x.vtt"]).SequenceEqual(["srt", "txt"]), "Formats for srt and vtt are the other subtitle formats plus txt");
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

    var tiny = Path.Combine(work, "tiny.jpeg");
    using (var t = new MagickImage("gradient:red-blue", 8, 8)) { t.Quality = 5; t.Write(tiny); }
    var tinyOut = ImageOperations.Compress([tiny], Path.Combine(work, "out-tiny"), null, default);
    Check(new FileInfo(tinyOut[0]).Length <= new FileInfo(tiny).Length, "Compress never returns a larger .jpeg");

    var icc = Path.Combine(work, "icc.png");
    using (var p = new MagickImage("gradient:red-blue", 64, 48)) { p.SetProfile(ColorProfiles.AppleRGB); p.Write(icc); }
    var iccOut = ImageOperations.Convert([icc], "jpg", Path.Combine(work, "out-icc"), null, default);
    using (var pre = new MagickImage(icc)) Check(pre.GetColorProfile() != null, "ICC fixture carries a profile");
    using (var back = new MagickImage(iccOut[0])) Check(back.GetColorProfile() != null, "Convert keeps the ICC profile");

    var gps = Path.Combine(work, "gps.jpg");
    using (var g = new MagickImage("gradient:red-blue", 64, 48))
    {
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(41, 1), new Rational(0, 1), new Rational(0, 1) });
        g.SetProfile(exif); g.SetProfile(ColorProfiles.AppleRGB); g.Write(gps);
    }
    using (var pre = new MagickImage(gps)) Check(pre.GetExifProfile()?.GetValue(ExifTag.GPSLatitude) is not null, "GPS fixture carries GPS");
    foreach (var fmt in new[] { "jpg", "png" })
    {
        var o = ImageOperations.Convert([gps], fmt, Path.Combine(work, "out-gps-" + fmt), null, default);
        using var back = new MagickImage(o[0]);
        Check(back.GetExifProfile()?.GetValue(ExifTag.GPSLatitude) is null && (fmt == "png" || back.GetColorProfile() != null), $"Convert to {fmt} drops GPS and keeps the colour profile");
        // ponytail: PNG writes the sRGB profile as an sRGB chunk, so only jpg can show the ICC survives.
    }

    Check(Throws<ArgumentException>(() => ImageOperations.Convert([], "pdf", Path.Combine(work, "out-empty"), null, default)) && Throws<ArgumentException>(() => ImageOperations.Compress([], Path.Combine(work, "out-empty"), null, default)), "Empty inputs throw ArgumentException");

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

async Task ToolSetupChecks(string root)
{
    var saved = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    try
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/tmp/x");
        Check(AppPaths.Tools == "/tmp/x/Nashi/tools", "XDG_DATA_HOME sets the tools folder");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
        Check(AppPaths.Tools.EndsWith(Path.Combine(".local", "share", "Nashi", "tools")), "Without XDG_DATA_HOME tools live under ~/.local/share");
    }
    finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", saved); }

    var missing = new Toolchain(Directory.CreateDirectory(Path.Combine(root, "tools-missing")).FullName, "");
    var presentDir = Directory.CreateDirectory(Path.Combine(root, "tools-present")).FullName;
    foreach (var name in new[] { "ffmpeg", "ffprobe" }) File.WriteAllText(Path.Combine(presentDir, name), "x");
    var present = new Toolchain(presentDir, "");
    Check(!AppPaths.MediaReady(missing) && AppPaths.MediaReady(present), "Media readiness is ffmpeg plus ffprobe");

    var smoke = Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY");
    List<string> Texts(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
    foreach (var (toolchain, label) in new[] { (missing, "missing"), (present, "ready") })
    {
        var window = new ToolSetupWindow(toolchain);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(window);
        var action = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "Download missing tools");
        if (label == "missing") Check(texts.Contains("Missing") && texts.Contains("Set up tools") && action is not null, "Setup window offers a download when media tools are missing");
        else Check(texts.Contains("Ready") && texts.Contains("Tools ready") && action is null, "Setup window shows Ready and no download when tools are present");
        var frame = window.CaptureRenderedFrame();
        Check(frame is not null, "Setup window renders (" + label + ")");
        if (!string.IsNullOrEmpty(smoke) && frame is not null)
        {
            Directory.CreateDirectory(smoke);
            frame.Save(Path.Combine(smoke, $"tool-setup-{label}-light.png"));
        }
        window.Close();
    }

    // Injected installs: cancel then retry, and a failure.
    var attempt = 0;
    var fake = new ToolSetupWindow(missing, async (_, token) =>
    {
        if (++attempt == 3) throw new IOException("disk full");
        await Task.Delay(Timeout.Infinite, token);
    });
    fake.Show();
    Dispatcher.UIThread.RunJobs();
    Button Find(string text) => fake.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == text);
    bool Visible(string text) => fake.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && b.Content as string == text);
    void Click(string text) { Find(text).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    async Task Settle() { for (var i = 0; i < 50 && Visible("Cancel"); i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); } }
    Click("Download missing tools");
    Check(Visible("Cancel") && Find("Cancel").IsEnabled, "Installing shows an enabled Cancel");
    Click("Cancel");
    Check(!Find("Cancel").IsEnabled, "Cancel disables itself while cancelling");
    await Settle();
    Check(Visible("Retry") && Texts(fake).Any(t => t.StartsWith("Cancelled")), "A cancelled install offers Retry");
    Click("Retry");
    Check(Visible("Cancel") && Find("Cancel").IsEnabled, "Cancel works again after Retry");
    Click("Cancel");
    await Settle();
    Click("Retry");
    await Settle();
    Check(Visible("Retry") && Texts(fake).Any(t => t.Contains("disk full")), "A failed install shows the error and Retry");
    fake.Close();

    var home = new MainWindow(present) { Width = 820, Height = 780 };
    home.Show();
    Dispatcher.UIThread.RunJobs();
    Check(home.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Tools ready"), "Header says Tools ready when media tools are present");
    home.Close();
    var needs = new MainWindow(missing) { Width = 820, Height = 780 };
    needs.Show();
    Dispatcher.UIThread.RunJobs();
    var header = needs.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Set up tools");
    header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    Check(needs.OwnedWindows.OfType<ToolSetupWindow>().Count() == 1, "Header Set up tools opens the setup window");
    foreach (var w in needs.OwnedWindows.ToArray()) w.Close();
    needs.Close();
}

void WheelChecks(string root)
{
    // Pure geometry at 320: radius 140.8, inner 57.3.
    var size = new Size(320, 320);
    Check(Wheel.IndexAt(new Point(160, 60), size, 8) == 0, "IndexAt top of the ring is item 0");
    Check(Wheel.IndexAt(new Point(260, 160), size, 8) == 2, "IndexAt right-middle of the ring is item 2");
    Check(Wheel.IndexAt(new Point(160, 160), size, 8) == -1, "IndexAt the center is -1");
    Check(Wheel.IndexAt(new Point(5, 5), size, 8) == -1 && Wheel.IndexAt(new Point(160, 3), size, 8) == -1, "IndexAt outside the ring is -1");

    string[] formats = ["JPG", "WEBP", "HEIC", "TIFF", "AVIF", "BMP", "PDF", "GIF"];
    var items = formats.Select(f => new WheelItem("convert:" + f.ToLowerInvariant(), f)).ToArray();
    var window = new WheelWindow("Convert", items);
    WheelItem? chosen = null;
    window.Wheel.Chosen += item => chosen = item;
    window.Show();
    Dispatcher.UIThread.RunJobs();
    window.Wheel.Focus();
    Check(window.Wheel.HighlightedIndex == 0, "Wheel opens with the first item highlighted");
    window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
    Check(window.Wheel.HighlightedIndex == 1, "Right moves the highlight forward");
    window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
    window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
    Check(window.Wheel.HighlightedIndex == 7, "Left wraps around to the last item");
    window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
    Check(window.Wheel.HighlightedIndex == 0, "Right wraps around to the first item");
    var frame = window.CaptureRenderedFrame();
    Check(frame is not null, "Wheel renders a frame");
    var smoke = Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY");
    if (!string.IsNullOrEmpty(smoke) && frame is not null)
    {
        Directory.CreateDirectory(smoke);
        frame.Save(Path.Combine(smoke, "wheel-light.png"));
    }
    window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
    Dispatcher.UIThread.RunJobs();
    Check(chosen == items[0] && !window.IsVisible, "Enter chooses the highlighted item and closes the wheel");

    var escape = new WheelWindow("Convert", items);
    escape.Show();
    Dispatcher.UIThread.RunJobs();
    escape.Wheel.Focus();
    escape.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
    Dispatcher.UIThread.RunJobs();
    Check(!escape.IsVisible, "Esc closes the wheel");

    var many = Enumerable.Range(1, 10).Select(n => new WheelItem("t" + n, "Item " + n)).ToArray();
    var paged = new Wheel { Items = many };
    Check(paged.Segments.Count == 8 && paged.Segments[^1].Id == "page:next" && paged.PageCount == 2, "Ten items page as six plus Previous and Next");
    paged.HighlightedIndex = 7;
    paged.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
    Check(paged.Segments[0].Id == "t7" && paged.Segments.Count == 6, "Next shows the remaining items with Previous and Next");

    // Home: empty wheels say why; a PNG opens a format wheel with JPG; the success toast copy.
    var home = new MainWindow { Width = 820, Height = 780 };
    home.Show();
    Dispatcher.UIThread.RunJobs();
    Button Chip(string text) => home.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));
    string Status() => home.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "StatusLine").Text ?? "";
    Check(!Chip("Actions").IsEnabled, "The Actions chip is disabled until it does something");
    Check(!home.GetVisualDescendants().OfType<Button>().First(x => x.Name == "SettingsButton").IsEnabled, "The Settings button is disabled until it does something");
    Chip("Tools").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(Status() == "Choose files first." && home.OwnedWindows.Count == 0, "Tools with nothing selected shows a status message");
    var doc = Path.Combine(root, "notes.docx");
    File.WriteAllText(doc, "x");
    home.Selection.Add([doc]);
    Chip("Convert").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(Status().Contains("No conversions") && home.OwnedWindows.Count == 0, "An empty format wheel shows a status message instead");
    home.Selection.Clear();
    var png = Path.Combine(root, "wheel.png");
    File.WriteAllText(png, "x");
    home.Selection.Add([png]);
    Chip("Convert").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    var opened = home.OwnedWindows.OfType<WheelWindow>().SingleOrDefault();
    Check(opened is not null && opened.Wheel.Items.Any(i => i.Id == "convert:jpg" && i.Title == "JPG"), "Convert chip on a PNG opens a format wheel with JPG");
    opened?.Close();
    home.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
    Dispatcher.UIThread.RunJobs();
    var tools = home.OwnedWindows.OfType<WheelWindow>().SingleOrDefault();
    Check(tools is not null && tools.Wheel.Items.Select(i => i.Id).SequenceEqual(["convert", "compress"]), "Ctrl+Space opens the tools wheel with Convert first");
    tools?.Close();

    var toast = new ProgressToast("Convert to JPG", null);
    toast.Show(home);
    toast.Update(new OperationProgress(0.5, "Saving images…"));
    toast.Complete([Path.Combine(root, "wheel.jpg")], root);
    Dispatcher.UIThread.RunJobs();
    var toastTexts = toast.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
    Check(toastTexts.Contains("Saved 1 new file. Originals kept.") && toastTexts.Contains("Show in folder"), "Success toast says what was saved and offers Show in folder");
    var toastFrame = toast.CaptureRenderedFrame();
    if (!string.IsNullOrEmpty(smoke) && toastFrame is not null) toastFrame.Save(Path.Combine(smoke, "toast-success-light.png"));
    toast.Close();
    home.Close();
}

async Task RunnerChecks(string root)
{
    var work = Directory.CreateDirectory(Path.Combine(root, "runner")).FullName;
    var png = Path.Combine(work, "-ğüş photo.png");
    using (var g = new MagickImage("gradient:red-blue", 64, 48)) g.Write(png);
    var png2 = Path.Combine(work, "two.png");
    File.Copy(png, png2);
    var empty = Directory.CreateDirectory(Path.Combine(work, "no-tools")).FullName;
    var runner = new OperationRunner(new Toolchain(empty, ""));
    var none = new Progress<OperationProgress>();

    var jpg = await runner.RunAsync("convert:jpg", [png], Path.Combine(work, "o-jpg"), none, default);
    Check(jpg.Count == 1 && jpg[0].EndsWith(".jpg") && Path.GetFileName(jpg[0]).StartsWith("-ğüş"), "Runner convert:jpg on a PNG returns one .jpg");

    var pdfs = await runner.RunAsync("convert:pdf", [png], Path.Combine(work, "o-pdf-a"), none, default);
    var pdfs2 = await runner.RunAsync("convert:pdf", [png2], Path.Combine(work, "o-pdf-b"), none, default);
    var merged = await runner.RunAsync("mergePDF", [pdfs[0], pdfs2[0]], Path.Combine(work, "o-merge"), none, default);
    using (var doc = UglyToad.PdfPig.PdfDocument.Open(merged[0])) Check(merged.Count == 1 && doc.NumberOfPages == 2, "Runner mergePDF makes one 2-page PDF");

    var two = await runner.RunAsync("createPDF", [png, png2], Path.Combine(work, "o-pdf-two"), none, default);
    using (var doc = UglyToad.PdfPig.PdfDocument.Open(two[0])) Check(two.Count == 1 && doc.NumberOfPages == 2, "Runner createPDF makes one 2-page PDF from two images");

    var zip = Path.Combine(work, "a.zip");
    using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
    using (var w = new StreamWriter(z.CreateEntry("ğ.txt").Open())) w.Write("hi");
    var ex = await runner.RunAsync("extractArchive", [zip], Path.Combine(work, "o-zip"), none, default);
    Check(ex.Count == 1 && File.Exists(Path.Combine(ex[0], "ğ.txt")), "Runner extractArchive extracts ğ.txt");
    var tar = await runner.RunAsync("convert:tar", [zip], Path.Combine(work, "o-tar"), none, default);
    Check(tar.Count == 1 && tar[0].EndsWith(".tar"), "Runner converts a ZIP to TAR");

    var srt = Path.Combine(work, "s.srt");
    File.WriteAllText(srt, "1\n00:00:01,000 --> 00:00:02,000\nMerhaba ş\n\n");
    var vtt = await runner.RunAsync("convert:vtt", [srt], Path.Combine(work, "o-vtt"), none, default);
    Check(vtt.Count == 1 && vtt[0].EndsWith(".vtt") && File.ReadAllText(vtt[0]).StartsWith("WEBVTT"), "Runner convert:vtt on an SRT starts with WEBVTT");

    var wav = Path.Combine(work, "tone.wav");
    var mp4 = Path.Combine(work, "clip.mp4");
    async Task<bool> Fails<E>(Func<Task> f) where E : Exception { try { await f(); return false; } catch (E) { return true; } }
    // Missing tools fail before any output work: the destination is not even created.
    var missingDest = Path.Combine(work, "o-missing");
    Check(await Fails<MissingToolsException>(() => runner.RunAsync("convert:mp3", [wav], missingDest, none, default)) && !Directory.Exists(missingDest), "Runner convert:mp3 without tools throws MissingToolsException before output work");
    Check(await Fails<MissingToolsException>(() => runner.RunAsync("compress", [png, mp4], missingDest, none, default)) && !Directory.Exists(missingDest), "Mixed compress without tools fails before compressing the image");

    Check(await Fails<NotSupportedException>(() => runner.RunAsync("convert:heic", [png], Path.Combine(work, "o-x"), none, default)), "Runner rejects convert:heic");
    Check(await Fails<NotSupportedException>(() => runner.RunAsync("convert:mp3", [png], Path.Combine(work, "o-x"), none, default)), "Runner rejects a format outside Capabilities");
    Check(await Fails<NotSupportedException>(() => runner.RunAsync("splitPDF", [pdfs[0]], Path.Combine(work, "o-x"), none, default)), "Runner rejects a tool outside Capabilities");

    var pdfIn = Path.Combine(work, "doc.pdf");
    File.Copy(pdfs[0], pdfIn);
    var mixed = await runner.RunAsync("compress", [png, pdfIn], Path.Combine(work, "o-mixed"), none, default);
    Check(mixed.Count == 2 && mixed.Any(f => f.EndsWith(" Compressed.png") || f.EndsWith(" Compressed.jpg")) && mixed.Any(f => f.EndsWith(" Compressed.pdf")), "Compress routes a PNG and a PDF each to its own engine");

    // Cancel after the first image is written: nothing may remain.
    var cancelDest = Path.Combine(work, "o-cancel");
    using var cts = new CancellationTokenSource();
    var cancelling = new Cancel(cts);
    var cancelled = await Fails<OperationCanceledException>(() => runner.RunAsync("convert:webp", [png, png2, png], cancelDest, cancelling, cts.Token));
    Check(cancelled && (!Directory.Exists(cancelDest) || Directory.GetFileSystemEntries(cancelDest).Length == 0), "Cancelling mid-run leaves no file in the destination");

    var ffmpeg = Environment.GetEnvironmentVariable("NASHI_VERIFY_FFMPEG");
    var ffprobe = Environment.GetEnvironmentVariable("NASHI_VERIFY_FFPROBE");
    if (string.IsNullOrEmpty(ffmpeg) || string.IsNullOrEmpty(ffprobe)) return;
    async Task Ffmpeg(params string[] args)
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ffmpeg, ["-v", "error", "-y", .. args]) { RedirectStandardError = true })!;
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new Exception(await p.StandardError.ReadToEndAsync());
    }
    await Ffmpeg("-f", "lavfi", "-i", "sine=duration=1", wav);
    await Ffmpeg("-f", "lavfi", "-i", "testsrc=duration=1:size=160x120:rate=10", "-f", "lavfi", "-i", "sine=duration=1", "-shortest", "-pix_fmt", "yuv420p", mp4);
    var media = new OperationRunner(new Toolchain(empty, Path.GetDirectoryName(ffmpeg)!));
    var engine = new MediaEngine(ffmpeg, ffprobe);

    var mp3 = await media.RunAsync("convert:mp3", [wav], Path.Combine(work, "o-mp3"), none, default);
    Check(mp3.Count == 1 && mp3[0].EndsWith(".mp3") && new FileInfo(mp3[0]).Length > 0, "Runner convert:mp3 on a WAV returns an .mp3");
    var muted = await media.RunAsync("muteVideo", [mp4], Path.Combine(work, "o-mute"), none, default);
    var info = await engine.InspectAsync(muted[0]);
    Check(muted.Count == 1 && info.Video is not null && !info.HasAudio, "Runner muteVideo returns a video with no audio stream");
    var mixedMedia = await media.RunAsync("compress", [png, pdfIn, mp4], Path.Combine(work, "o-mixed-media"), none, default);
    Check(mixedMedia.Count == 3 && mixedMedia.All(File.Exists) && mixedMedia.Any(f => f.EndsWith(".mp4")), "Compress handles PNG, PDF and MP4 together");
    var cancelMedia = Path.Combine(work, "o-cancel-media");
    using var preCancelled = new CancellationTokenSource(); preCancelled.Cancel();
    var mediaCancelled = await Fails<OperationCanceledException>(() => media.RunAsync("muteVideo", [mp4], cancelMedia, none, preCancelled.Token));
    Check(mediaCancelled && (!Directory.Exists(cancelMedia) || Directory.GetFileSystemEntries(cancelMedia).Length == 0), "Cancelled media run leaves the destination empty");
}

static bool Throws<T>(Action a) where T : Exception { try { a(); return false; } catch (T) { return true; } }

static bool Near(byte actual, byte expected) => Math.Abs(actual - expected) <= 1;

void LocalizationChecks(string root)
{
    Check(L10n.Language == "en", "Checks run in English");
    L10n.Language = "tr";
    try
    {
        Check(L10n.T("Choose files") == "Dosya seç", "T translates Choose files to Turkish");
        Check(L10n.F("Saved {0} new files. Originals kept.", 3).Contains('3') && !L10n.F("Saved {0} new files. Originals kept.", 3).Contains("Saved"), "F translates a template and fills the count");
        Check(L10n.T("Converting to webp is not supported on Linux.") == "webp biçimine dönüştürme Linux'ta desteklenmiyor.", "Interpolated exception messages translate at display time");
        Check(L10n.T("Some text nobody translated") == "Some text nobody translated", "Unknown text stays as written");
        Check(L10n.Upper("Ready when you are") == AppLocalization.Uppercase(L10n.T("Ready when you are"), "tr"), "Upper follows the Turkish casing rules");

        // Every literal the Linux UI can show has a Turkish entry.
        var linux = Path.Combine(RepoRoot(), "linux", "Nashi.Linux");
        var literals = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(linux, "*", SearchOption.TopDirectoryOnly))
        {
            var source = File.ReadAllText(file);
            if (file.EndsWith(".axaml"))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source, @"\{l:T '((?:[^'\\]|\\.)*)'\}")) literals.Add(m.Groups[1].Value);
            else if (file.EndsWith(".cs") && !file.EndsWith("L10n.cs"))
            {
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source, "(?:public const string \\w+ = |L10n\\.(?:T|F|Upper)\\()\"((?:[^\"\\\\]|\\\\.)*)\"")) literals.Add(m.Groups[1].Value);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source, "Exception\\((\\$?)\"((?:[^\"\\\\]|\\\\.)*)\"\\)"))
                    literals.Add(m.Groups[1].Value == "$" ? System.Text.RegularExpressions.Regex.Replace(m.Groups[2].Value, @"\{[^}]+\}", "{0}") : m.Groups[2].Value);
            }
        }
        literals.RemoveWhere(l => !l.Any(char.IsLetter));
        // Copy constants that are Windows-style product words rather than UI copy.
        var missing = literals.Where(l => !AppLocalization.Turkish.ContainsKey(l) && AppLocalization.Text(l, "tr") == l).ToList();
        Check(literals.Count > 40, "Literal scan finds the Linux UI strings (" + literals.Count + ")");
        Check(missing.Count == 0, "Every Linux UI literal has a Turkish entry; missing: " + string.Join(" | ", missing));
        Check(AppLocalization.Text("JPG", "tr") == "JPG", "Format names stay as they are");

        // Headless Home in Turkish has no English hero title.
        L10n.Language = "tr";
        var window = new MainWindow { Width = 820, Height = 780 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Check(!texts.Contains("Your files. Their next form.") && texts.Contains(AppLocalization.Text("Your files. Their next form.", "tr")), "Turkish Home has no English hero title");
        Check(texts.Contains("Dosya seç") && texts.Contains(AppLocalization.Text("START HERE", "tr")), "Turkish Home shows Dosya seç and the Turkish eyebrow");
        var smoke = Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY");
        if (!string.IsNullOrEmpty(smoke) && frame is not null) { Directory.CreateDirectory(smoke); frame.Save(Path.Combine(smoke, "home-tr.png")); }

        // Turkish wheel: tool names translated, petals upper-cased with İ.
        var wheel = new WheelWindow(L10n.T("Tools"), [new("compress", L10n.T(FeatureCatalog.ToolNames["compress"])), new("convert", L10n.T("Convert"))]);
        wheel.Show();
        Dispatcher.UIThread.RunJobs();
        Check(wheel.Wheel.Items[0].Title == "Sıkıştır" && wheel.CaptureRenderedFrame() is not null, "Turkish wheel translates tool names and renders");
        wheel.Close();
        window.Close();
    }
    finally { L10n.Language = "en"; }
}

void AppearanceChecks(string root)
{
    Check(Application.Current!.RequestedThemeVariant == ThemeVariant.Light, "Checks pinned Light earlier");
    var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "linux", "Nashi.Linux", "App.axaml"));
    Check(xaml.Contains("RequestedThemeVariant=\"Default\""), "App follows the system theme by default");

    Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
    try
    {
        var smoke = Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY");
        void Save(Avalonia.Media.Imaging.WriteableBitmap? frame, string name)
        {
            if (string.IsNullOrEmpty(smoke) || frame is null) return;
            Directory.CreateDirectory(smoke);
            frame.Save(Path.Combine(smoke, name));
        }
        Color Resource(Window w, string key) => w.TryFindResource(key, w.ActualThemeVariant, out var v) && v is ISolidColorBrush b ? b.Color : default;
        bool IsDark(Color c) => c.A == 255 && c.R < 60 && c.G < 60 && c.B < 60;

        var home = new MainWindow { Width = 820, Height = 780 };
        home.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = home.CaptureRenderedFrame();
        var canvas = (home.Background as ISolidColorBrush)?.Color;
        Check(canvas is { } c && Near(c.R, 0x11) && Near(c.G, 0x1A) && Near(c.B, 0x14), "Dark Home background is the dark canvas #111A14");
        Check(home.Foreground is ISolidColorBrush { Color: { R: > 200 } }, "Dark Home text is light");
        Save(frame, "home-dark.png");
        home.Close();

        var setup = new ToolSetupWindow(new Toolchain(Directory.CreateDirectory(Path.Combine(root, "dark-tools")).FullName, ""));
        setup.Show();
        Dispatcher.UIThread.RunJobs();
        Check(IsDark(Resource(setup, "NashiCanvas")) && IsDark(Resource(setup, "NashiSurface")), "Dark setup window resolves dark canvas and surface");
        Save(setup.CaptureRenderedFrame(), "tool-setup-dark.png");
        setup.Close();

        var toast = new ProgressToast("Convert to JPG", null);
        toast.Show();
        Dispatcher.UIThread.RunJobs();
        Check(IsDark(Resource(toast, "NashiSurface")) && toast.FindControl<Border>("Card")?.Background is ISolidColorBrush { Color: { R: < 60 } }, "Dark toast card uses the dark surface");
        Save(toast.CaptureRenderedFrame(), "toast-dark.png");
        toast.Close();

        var wheel = new WheelWindow("Convert", [new("convert:jpg", "JPG"), new("convert:png", "PNG")]);
        wheel.Show();
        Dispatcher.UIThread.RunJobs();
        Check(IsDark(Resource(wheel, "NashiSurface")) && Resource(wheel, "NashiText").R > 200, "Dark wheel resolves dark surface and light text");
        Save(wheel.CaptureRenderedFrame(), "wheel-dark.png");
        wheel.Close();
    }
    finally { Application.Current!.RequestedThemeVariant = ThemeVariant.Light; }
}

static string RepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (Directory.Exists(Path.Combine(dir.FullName, "Sources", "NashiCore"))) return dir.FullName;
    throw new DirectoryNotFoundException("Repository root not found.");
}

sealed class Cancel(CancellationTokenSource cts) : IProgress<OperationProgress>
{
    public void Report(OperationProgress value) => cts.Cancel();
}
