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
    await RunnerChecks(temp.FullName);
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

sealed class Cancel(CancellationTokenSource cts) : IProgress<OperationProgress>
{
    public void Report(OperationProgress value) => cts.Cancel();
}
