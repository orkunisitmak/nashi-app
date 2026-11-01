using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

var passed = 0;
var failures = new List<string>();
void Check(bool condition, string name)
{
    if (condition) passed++;
    else failures.Add(name);
}

L10n.Language = "en"; // the English checks below must not depend on the machine's locale
// Nothing launches from the checks: programs are recorded, and only the listed ones "start".
var launched = new List<(string Program, string[] Args, string? Folder)>();
var startable = new HashSet<string>();
var exitCodes = new Dictionary<string, int>(); // a listed program "exits" at once with this code; others keep running
var running = new List<Action<int>>(); // the exit callbacks of the ones still running
Desktop.Spawn = (program, args, folder, exited) =>
{
    launched.Add((program, [.. args], folder));
    if (!startable.Contains(program)) return false;
    if (exitCodes.TryGetValue(program, out var code)) exited?.Invoke(code);
    else if (exited is not null) running.Add(exited);
    return true;
};
FileActions.TerminalSettle = TimeSpan.FromMilliseconds(50);
var ran = new List<(string Program, string[] Args)>();
Func<string, IReadOnlyList<string>, string?> runResult = (_, _) => null;
Desktop.Run = (program, args) => { ran.Add((program, [.. args])); return runResult(program, args); };
WheelFeedback.Player = _ => { }; // nothing reaches the speakers
// The editors' preview player: a sink that takes the sound and drops it, and a clock that stands still unless a check moves it.
Nashi.Linux.Editors.Media.MediaPlayer.OpenSink = (_, _) => new((_, _) => true, () => { });
Nashi.Linux.Editors.Media.MediaPlayer.Clock = () => 0;
var stoppedPlayers = new List<string>(); // fallback players are recorded through Desktop.Spawn, never started
WheelFeedback.StartPlayer = (program, args, exited) => Desktop.Spawn(program, args, null, exited) ? () => { lock (stoppedPlayers) stoppedPlayers.Add(program); } : null;
WheelFeedback.SystemReducesMotion = false;
WheelFeedback.ReduceMotion = true; // renders show the wheel at rest; MotionChecks turns it on
var temp = Directory.CreateTempSubdirectory("nashi-linux-checks-");
// No ExifTool unless a check provides one: never the user's tools folder.
XdndTarget.ReadDrag = _ => null; // never asks a real display for the drag
AppPaths.DataOverride = Directory.CreateDirectory(Path.Combine(temp.FullName, "data")).FullName; // never the user's settings
var noMetadata = Directory.CreateDirectory(Path.Combine(temp.FullName, "no-metadata")).FullName;
AppPaths.MetadataRoot = noMetadata;
try
{
    LaunchChecks();
    await InstanceChecks(temp.FullName);
    ClipboardChecks();
    await DragChecks.BrowserAsync(temp.FullName, Check);
    PlacementChecks();
    IntegrationChecks(temp.FullName); // before Avalonia starts, like the real --install-integration
    SelectionChecks(temp.FullName);
    CapabilityChecks();
    ImageChecks(temp.FullName);
    await RunnerChecks(temp.FullName);
    await DocumentChecks.RunAsync(temp.FullName, Check);
    HomeChecks();
    WheelChecks(temp.FullName);
    await ToolSetupChecks(temp.FullName);
    LocalizationChecks(temp.FullName);
    AppearanceChecks(temp.FullName);
    await BackgroundChecks(temp.FullName);
    IconChecks();
    LabelChecks();
    MotionChecks();
    await ActionChecks(temp.FullName);
    DragChecks.Gestures(Check);
    DragChecks.DragWheel(temp.FullName, Check, Smoke);
    WatchChecks.Run(temp.FullName, Check);
    FloatChecks.Run(temp.FullName, Check, Smoke);
    await EditorChecks.RunAsync(temp.FullName, Check, Smoke);
    WheelRenders(temp.FullName);
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
    // The image tools gate: every image input, alone, in pairs and mixed with PNG, offers exactly the catalog's tools.
    string[] images = ["jpg", "png", "webp", "heic", "tiff", "svg", "avif", "bmp"];
    var differ = images.SelectMany(e => new[] { new[] { "x." + e }, ["x." + e, "y." + e], ["x." + e, "y.png"] })
        .Where(s => !Capabilities.Tools(s).SequenceEqual(FeatureCatalog.Tools(s))).Select(s => string.Join("+", s)).ToList();
    Check(differ.Count == 0, "Image tools match the catalog for every image type, one or two, and mixed with PNG; differ: " + string.Join(" ", differ));
    Check(Capabilities.Tools(png).SequenceEqual(FeatureCatalog.Tools(png)) && Capabilities.Tools(["a.jpg", "b.jpg"]).Contains("createCollage"), "A PNG offers every catalog image tool; two JPGs add Create Collage");
    Check(Capabilities.Tools(["x.jpg", "y.pdf"]).SequenceEqual(["compress", "removeMetadata", "readQRCode"]), "Images with a PDF offer Compress, Metadata and Read QR Codes");
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
    Check(Capabilities.Formats(pdf2).SequenceEqual(FeatureCatalog.Formats(pdf2)) && Capabilities.Formats(["x.pdf"]).Contains("docx"), "Formats for PDFs are the catalog's, docx included");
    Check(Capabilities.Formats(docx).SequenceEqual(FeatureCatalog.Formats(docx)), "Formats for a DOCX are the catalog's");
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
    var level = window.ActualTransparencyLevel;
    Check(canvas is { } c && c.A == 0xCC && c.R == 0xFF && c.G == 0xFF && c.B == 0xFF && level != WindowTransparencyLevel.Transparent || canvas is { A: 0xE6, R: 0xFF } && level == WindowTransparencyLevel.Transparent, "Home background is translucent white: 80 % over blur, 90 % when nothing blurs it (" + level + ")");
    Color Light(string key) => window.TryFindResource(key, ThemeVariant.Light, out var v) && v is ISolidColorBrush brush ? brush.Color : default;
    bool Black(Color c, double from, double to) => c.R == 0 && c.G == 0 && c.B == 0 && c.A >= from * 255 - 1 && c.A <= to * 255 + 1;
    Check(Black(Light("NashiSurface"), 0.04, 0.05) && Black(Light("NashiBorder"), 0.08, 0.08) && Light("NashiText") == Colors.Black && Black(Light("NashiTextSecondary"), 0.55, 0.55) && Black(Light("NashiChip"), 0.05, 0.05),
        "Light cards are black at 4–5 %, borders black at 8 %, text black, secondary text black at 55 %, chips black at 5 %");
    Check(Light("NashiWheelPetal") is { R: 255, G: 255, B: 255, A: >= 0xB3 and <= 0xCC } && Light("NashiWheelDisc") == Light("NashiWheelPetal") && Light("NashiWheelLabel") == Colors.Black, "Light wheel petals and disc are white glass at 70–80 % with black labels");
    Check(window.TransparencyLevelHint.SequenceEqual([WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent]), "Home asks for acrylic blur, then blur, then plain transparency");
    Check(window.TransparencyBackgroundFallback is ISolidColorBrush { Color.A: 255 }, "Without any transparency Home falls back to an opaque canvas");
    Check(frame is not null, "Home renders a frame");
    // The frame is see-through where only the canvas is (a corner of the window): the desktop would show there.
    var corner = Pixel(frame!, 4, 770);
    Check(corner.A is > 0xC0 and < 0xF0 && corner.R > 0xF0, "Home's rendered canvas is translucent white (alpha " + corner.A + ")");
    window.Classes.Set("clear", true);
    Dispatcher.UIThread.RunJobs();
    Check(window.Background is ISolidColorBrush { Color: { A: 0xE6, R: 0xFF } }, "The unblurred canvas is denser white (90 %)");
    window.Classes.Set("clear", level == WindowTransparencyLevel.Transparent);

    // Nothing green is left in the Linux UI: no forest or sage resources, no green colour in any view or the palette.
    var ui = Path.Combine(RepoRoot(), "linux", "Nashi.Linux");
    var sources = Directory.EnumerateFiles(ui, "*.axaml", SearchOption.AllDirectories).Concat(Directory.EnumerateFiles(ui, "*.cs", SearchOption.AllDirectories))
        .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/")).ToList();
    var named = sources.Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), "Nashi(Forest|Sage)")).Select(Path.GetFileName).ToList();
    var greens = sources.SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "#(?:[0-9A-Fa-f]{2})?([0-9A-Fa-f]{6})\\b").Select(m => (File: Path.GetFileName(f), Hex: m.Value, Rgb: Convert.ToInt32(m.Groups[1].Value, 16))))
        .Where(x => ((x.Rgb >> 8) & 0xFF) > ((x.Rgb >> 16) & 0xFF) + 10 && ((x.Rgb >> 8) & 0xFF) > (x.Rgb & 0xFF) + 10).Select(x => x.File + " " + x.Hex).ToList();
    Check(named.Count == 0 && greens.Count == 0 && sources.Count > 10, "No forest, sage or other green in the Linux UI: " + string.Join(", ", named.Concat(greens)));

    Smoke(frame, "home-light");

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
    var data = AppPaths.DataOverride;
    AppPaths.DataOverride = null; // these look at the real rule, without the checks' own folder
    try
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/tmp/x");
        Check(AppPaths.Tools == "/tmp/x/Nashi/tools", "XDG_DATA_HOME sets the tools folder");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
        Check(AppPaths.Tools.EndsWith(Path.Combine(".local", "share", "Nashi", "tools")), "Without XDG_DATA_HOME tools live under ~/.local/share");
    }
    finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", saved); AppPaths.DataOverride = data; }

    var missing = new Toolchain(Directory.CreateDirectory(Path.Combine(root, "tools-missing")).FullName, "");
    var presentDir = Directory.CreateDirectory(Path.Combine(root, "tools-present")).FullName;
    foreach (var name in new[] { "ffmpeg", "ffprobe" }) File.WriteAllText(Path.Combine(presentDir, name), "x");
    var present = new Toolchain(presentDir, "");
    Check(!AppPaths.MediaReady(missing) && AppPaths.MediaReady(present), "Media readiness is ffmpeg plus ffprobe");
    Check(!AppPaths.MediaAndDenoReady(present), "The setup download also covers Deno, for Pages");
    File.WriteAllText(Path.Combine(presentDir, "deno"), "x");
    Check(AppPaths.MediaAndDenoReady(present) && AppPaths.Deno(present) == Path.Combine(presentDir, "deno"), "Deno in the tools folder makes the media and Deno downloads ready");
    // LibreOffice comes from PATH only: a fake executable soffice stands in for it.
    var savedOffice = AppPaths.OfficeSearchPath;
    var officeDir = Directory.CreateDirectory(Path.Combine(root, "office-bin")).FullName;
    var soffice = Path.Combine(officeDir, "soffice");
    File.WriteAllText(soffice, "#!/bin/sh\n");
    AppPaths.OfficeSearchPath = officeDir;
    Check(AppPaths.Office() is null, "A soffice that is not executable is not LibreOffice");
    File.SetUnixFileMode(soffice, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    Check(AppPaths.Office() == soffice, "LibreOffice is the executable soffice on PATH");
    var snap = Directory.CreateDirectory(Path.Combine(root, "snap-bin")).FullName;
    File.CreateSymbolicLink(Path.Combine(snap, "libreoffice"), "/snap/bin/libreoffice");
    AppPaths.OfficeSearchPath = snap;
    Check(AppPaths.Office() is null, "A Snap LibreOffice is skipped (its sandbox has a private /tmp)");

    // ExifTool is MetadataService's pinned folder under the tools root.
    var metadataPresent = Directory.CreateDirectory(Path.Combine(root, "metadata-present", "metadata-13.59")).Parent!.FullName;
    File.WriteAllText(Path.Combine(metadataPresent, "metadata-13.59", "exiftool"), "x");
    Check(AppPaths.Metadata().Executable is null && !AppPaths.DownloadsReady(present), "Without ExifTool the downloads are not ready");
    AppPaths.MetadataRoot = metadataPresent;
    Check(AppPaths.Metadata().Executable == Path.Combine(metadataPresent, "metadata-13.59", "exiftool") && AppPaths.DownloadsReady(present), "ExifTool in its pinned folder completes the downloads");
    List<string> Texts(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
    foreach (var (toolchain, label) in new[] { (missing, "missing"), (present, "ready") })
    {
        AppPaths.OfficeSearchPath = label == "ready" ? officeDir : "";
        AppPaths.MetadataRoot = label == "ready" ? metadataPresent : noMetadata;
        var window = new ToolSetupWindow(toolchain);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(window);
        var action = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "Download missing tools");
        var hint = texts.Any(t => t.Contains("sudo apt install libreoffice"));
        if (label == "missing") Check(texts.Count(t => t == "Missing") == 3 && texts.Contains("Office tools") && texts.Contains("Metadata tools") && hint && texts.Contains("Set up tools") && action is not null, "Setup window offers a download for media and metadata tools and says how to install LibreOffice");
        else Check(texts.Count(t => t == "Ready") == 2 && texts.Contains("Installed") && !hint && texts.Contains("Tools ready") && action is null, "Setup window shows media and metadata Ready, LibreOffice Installed and no download when tools are present");
        var frame = window.CaptureRenderedFrame();
        Check(frame is not null, "Setup window renders (" + label + ")");
        Smoke(frame, $"tool-setup-{label}-light");
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

    AppPaths.OfficeSearchPath = officeDir;
    AppPaths.MetadataRoot = metadataPresent;
    var home = new MainWindow(present) { Width = 820, Height = 780 };
    home.Show();
    Dispatcher.UIThread.RunJobs();
    Check(home.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Tools ready"), "Header says Tools ready when media tools, Deno and LibreOffice are present");
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
    AppPaths.OfficeSearchPath = savedOffice;
    AppPaths.MetadataRoot = noMetadata;
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
    Smoke(frame, "wheel-light");
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
    Check(!home.GetVisualDescendants().OfType<Button>().First(x => x.Name == "SettingsButton").IsEnabled, "The Settings button is disabled until it does something");
    var png = Path.Combine(root, "wheel.png");
    File.WriteAllText(png, "x");
    var picks = 0;
    home.PickFiles = () => { picks++; return Task.FromResult<IReadOnlyList<string>>([]); };
    Chip("Tools").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    Check(picks == 1 && home.OwnedWindows.Count == 0, "Tools with nothing selected opens the file picker; cancelling it opens no wheel");
    home.PickFiles = () => { picks++; return Task.FromResult<IReadOnlyList<string>>([png]); };
    home.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
    Dispatcher.UIThread.RunJobs();
    var pickedWheel = home.OwnedWindows.OfType<WheelWindow>().SingleOrDefault();
    Check(picks == 2 && home.Selection.Files.SequenceEqual([png]) && pickedWheel is not null, "Ctrl+Space with nothing selected picks files, then shows the wheel");
    pickedWheel?.Close();
    home.Selection.Clear();
    var doc = Path.Combine(root, "notes.xyz");
    File.WriteAllText(doc, "x");
    home.Selection.Add([doc]);
    Chip("Convert").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    Check(SuccessToast.Current?.FindControl<TextBlock>("TitleText")?.Text == "No conversions for XYZ files yet." && home.OwnedWindows.Count == 0, "An empty format wheel shows a toast card naming the file type");
    SuccessToast.Current?.Close();
    var pdfOnly = Path.Combine(root, "only.pdf");
    File.WriteAllText(pdfOnly, "x");
    home.Selection.Clear();
    home.Selection.Add([pdfOnly]);
    Chip("Convert").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    var pdfWheel = home.OwnedWindows.OfType<WheelWindow>().SingleOrDefault();
    Check(pdfWheel is not null && pdfWheel.Wheel.Items.Select(i => i.Id).SequenceEqual(FeatureCatalog.Formats([pdfOnly]).Select(f => "convert:" + f)), "A single PDF's format wheel offers every catalog route (the empty PDF wheel is gone)");
    pdfWheel?.Close();
    home.Selection.Add([png]);
    Chip("Convert").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    Check(SuccessToast.Current?.FindControl<TextBlock>("TitleText")?.Text == "No conversions for this selection yet.", "Mixed types say No conversions for this selection yet.");
    SuccessToast.Current?.Close();
    home.Selection.Clear();
    home.Selection.Add([png]);
    Chip("Convert").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    var opened = home.OwnedWindows.OfType<WheelWindow>().SingleOrDefault();
    Check(opened is not null && opened.Wheel.Items.Any(i => i.Id == "convert:jpg" && i.Title == "JPG"), "Convert chip on a PNG opens a format wheel with JPG");
    opened?.Close();
    home.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
    Dispatcher.UIThread.RunJobs();
    var tools = home.OwnedWindows.OfType<WheelWindow>().SingleOrDefault();
    Check(tools is not null && tools.Wheel.Items.Select(i => i.Id).SequenceEqual(["convert", .. Capabilities.Tools([png])]), "Ctrl+Space opens the tools wheel with Convert first");
    Check(tools is { Width: Wheel.Size + 2 * WheelWindow.CardColumn } && tools.FindControl<ContentControl>("LeftCard")?.Content is not null && tools.FindControl<ContentControl>("RightCard")?.Content is null, "Home's wheel shows the one selected file as a card beside it");
    tools?.Close();
    Chip("Actions").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    var actions = home.OwnedWindows.OfType<WheelWindow>().SingleOrDefault();
    Check(actions is not null && actions.Wheel.Items.Select(i => i.Id).SequenceEqual(FileActions.Ids.Select(id => "action:" + id)) && actions.Wheel.Items.All(i => i.Enabled), "The Actions chip opens the actions wheel: Share, Float, Copy, Move, Copy Path, Terminal, all enabled");
    Check(actions?.Wheel.Items[0].Title == "Share", "Share is called Share on Linux (no AirDrop)");
    actions?.Close();

    var toast = new ProgressToast("Convert to JPG", null);
    toast.Show(home);
    toast.Update(new OperationProgress(0.5, "Saving images…"));
    Dispatcher.UIThread.RunJobs();
    Check(toast.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Saving images…") && toast.Topmost, "Progress toast shows the step and stays on top");
    var toastFrame = toast.CaptureRenderedFrame();
    Smoke(toastFrame, "toast-progress-light");
    toast.Close();

    // Ctrl+V reads files through the shared clipboard reader, including the uri-list text form.
    var pasted = Path.Combine(root, "pasted ğ.png");
    File.WriteAllText(pasted, "x");
    home.Clipboard!.SetTextAsync("copy\n" + new Uri(pasted).AbsoluteUri).GetAwaiter().GetResult();
    home.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
    Dispatcher.UIThread.RunJobs();
    for (var i = 0; i < 50 && !home.Selection.Files.Contains(pasted); i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
    Check(home.Selection.Files.Contains(pasted), "Ctrl+V adds files copied as file:// text (the GNOME clipboard form)");
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
    Check(await Fails<NotSupportedException>(() => runner.RunAsync("organizePDF", [png], Path.Combine(work, "o-x"), none, default)), "Runner rejects a tool outside Capabilities");
    Check(await Fails<NotSupportedException>(() => runner.RunAsync("splitPDF", [pdfs[0]], Path.Combine(work, "o-x"), none, default)), "Runner rejects an editor's tool chosen without its editor");

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
        foreach (var file in Directory.EnumerateFiles(linux, "*", SearchOption.AllDirectories).Where(f => !f.Contains("/bin/") && !f.Contains("/obj/")))
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
        Smoke(frame, "home-tr");

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
        void Save(Avalonia.Media.Imaging.WriteableBitmap? frame, string name) => Smoke(frame, Path.GetFileNameWithoutExtension(name));
        Color Resource(Window w, string key) => w.TryFindResource(key, w.ActualThemeVariant, out var v) && v is ISolidColorBrush b ? b.Color : default;
        // White at an opacity: dark mode's surfaces, borders, chips and text.
        bool White(Color c, double from, double to) => c.R == 255 && c.G == 255 && c.B == 255 && c.A >= from * 255 - 1 && c.A <= to * 255 + 1;

        var home = new MainWindow { Width = 820, Height = 780 };
        home.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = home.CaptureRenderedFrame();
        var canvas = (home.Background as ISolidColorBrush)?.Color;
        Check(canvas is { R: 0, G: 0, B: 0 } c && (c.A == 0xC7 || c.A == 0xE0), "Dark Home background is translucent black (78 %; 88 % when nothing blurs it)");
        Check(home.Foreground is ISolidColorBrush { Color: { R: 255, G: 255, B: 255, A: 255 } }, "Dark Home text is pure white");
        Check(White(Resource(home, "NashiSurface"), 0.06, 0.08) && White(Resource(home, "NashiBorder"), 0.1, 0.1) && White(Resource(home, "NashiTextSecondary"), 0.6, 0.6) && White(Resource(home, "NashiChip"), 0.08, 0.08),
            "Dark cards are white at 6–8 %, borders white at 10 %, secondary text white at 60 %, chips white at 8 %");
        var corner = Pixel(frame!, 4, 770);
        Check(corner.A is > 0xB0 and < 0xE8 && corner.R < 0x10, "Dark Home's rendered canvas is translucent black (alpha " + corner.A + ")");
        Save(frame, "home-dark.png");
        home.Close();

        var setup = new ToolSetupWindow(new Toolchain(Directory.CreateDirectory(Path.Combine(root, "dark-tools")).FullName, ""));
        setup.Show();
        Dispatcher.UIThread.RunJobs();
        Check(Resource(setup, "NashiCanvas") is { R: 0, A: 0xC7 } && White(Resource(setup, "NashiSurface"), 0.06, 0.08) && setup.TransparencyLevelHint.Count == 3, "Dark setup window is translucent black glass with white-tint cards");
        Save(setup.CaptureRenderedFrame(), "tool-setup-dark.png");
        setup.Close();

        var toast = new ProgressToast("Convert to JPG", null);
        toast.Show();
        Dispatcher.UIThread.RunJobs();
        Check(toast.FindControl<Border>("Card")?.Background is ISolidColorBrush { Color: { R: 0, G: 0, B: 0, A: > 0xC0 and < 0xFF } }, "Dark toast card is translucent black");
        Save(toast.CaptureRenderedFrame(), "toast-dark.png");
        toast.Close();

        var wheel = new WheelWindow("Convert", [new("convert:jpg", "JPG"), new("convert:png", "PNG")]);
        wheel.Show();
        Dispatcher.UIThread.RunJobs();
        Check(Resource(wheel, "NashiWheelPetal") is { R: 0, A: >= 0xB3 and <= 0xCC } && Resource(wheel, "NashiWheelDisc") == Resource(wheel, "NashiWheelPetal") && Resource(wheel, "NashiWheelLabel") == Colors.White, "Dark wheel petals and disc are black glass at 70–80 % with white labels");
        Save(wheel.CaptureRenderedFrame(), "wheel-dark.png");
        wheel.Close();
    }
    finally { Application.Current!.RequestedThemeVariant = ThemeVariant.Light; }
}

void LaunchChecks()
{
    Check(Launch.Parse([]) == new Launch(LaunchMode.Home, []) || Launch.Parse([]).Mode == LaunchMode.Home, "No arguments open Home");
    Check(Launch.Parse([]).Message == new InstanceMessage("show"), "Home forwards show to a running instance");
    Check(Launch.Parse(["--background"]).Mode == LaunchMode.Background && Launch.Parse(["--background"]).Message is null, "--background runs in the background and forwards nothing");
    var wheel = Launch.Parse(["--wheel", "/a b.png", "-x.png", "--version-not"]);
    Check(wheel.Mode == LaunchMode.Wheel && wheel.Files.SequenceEqual(["/a b.png", "/-x.png".Replace("/-", Path.GetFullPath("-x.png")[..^6] + "-"), "/--version-not".Replace("/--", Path.GetFullPath("--version-not")[..^13] + "--")]), "--wheel takes every later argument as a file, even with a leading dash");
    var bare = Launch.Parse(["/x/a.png", "rel.png"]);
    Check(bare.Mode == LaunchMode.Wheel && bare.Files.SequenceEqual(["/x/a.png", Path.GetFullPath("rel.png")]) && bare.Message!.Cmd == "wheel" && bare.Message.Files!.SequenceEqual(bare.Files), "Bare files (the desktop entry's %F) open the wheel with absolute paths");
    var acts = Launch.Parse(["--actions", "/a b.png", "rel.png"]);
    Check(acts.Mode == LaunchMode.Actions && acts.Files.SequenceEqual(["/a b.png", Path.GetFullPath("rel.png")]) && acts.Message!.Cmd == "actions" && acts.Message.Files!.SequenceEqual(acts.Files), "--actions forwards actions with absolute paths");
    Check(Launch.Parse(["--wheel-clipboard"]).Mode == LaunchMode.Wheel, "--wheel-clipboard is gone (only Ctrl-drag remains): it is just another argument");
    Check(Launch.Parse(["--install-integration"]).Mode == LaunchMode.InstallIntegration && Launch.Parse(["--uninstall-integration"]).Mode == LaunchMode.UninstallIntegration, "Integration flags parse");
    Check(Desktop.StartupTime("nautilus-1234-ece-laptop-_TIME98765") == 98765 && Desktop.StartupTime("gnome-shell/Nashi/5-0-host_TIME4294967295") == uint.MaxValue, "DESKTOP_STARTUP_ID _TIME<n> parses to the timestamp");
    Check(Desktop.StartupTime(null) == 0 && Desktop.StartupTime("no-time-here") == 0 && Desktop.StartupTime("x_TIMEabc") == 0 && Desktop.StartupTime("x_TIME99999999999") == 0, "Missing or bad startup times are 0 (CurrentTime)");
    var savedStartup = Environment.GetEnvironmentVariable("DESKTOP_STARTUP_ID");
    try
    {
        Environment.SetEnvironmentVariable("DESKTOP_STARTUP_ID", "nautilus-1-h-_TIME42");
        Check(Launch.Parse(["--wheel", "/a.png"]).Message!.Startup == "nautilus-1-h-_TIME42" && Launch.Parse(["--actions", "/a.png"]).Message!.Startup == "nautilus-1-h-_TIME42", "The caller's DESKTOP_STARTUP_ID is forwarded");
        Environment.SetEnvironmentVariable("DESKTOP_STARTUP_ID", null);
        Check(Launch.Parse(["--wheel", "/a.png"]).Message!.Startup is null, "No DESKTOP_STARTUP_ID forwards none");
    }
    finally { Environment.SetEnvironmentVariable("DESKTOP_STARTUP_ID", savedStartup); }
    Check(Launch.Parse(["--version"]).Mode == LaunchMode.Version && Launch.Parse(["--background", "--version"]).Mode == LaunchMode.Version, "--version wins anywhere");
}

async Task InstanceChecks(string root)
{
    var run = Directory.CreateDirectory(Path.Combine(root, "run")).FullName;
    var saved = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
    Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", run);
    var path = Instance.SocketPath;
    Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", null);
    Check(Instance.SocketPath == $"/tmp/nashi-{Instance.Uid}.sock", "Without XDG_RUNTIME_DIR the socket lives in /tmp");
    Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", saved);
    Check(path == Path.Combine(run, $"nashi-{Instance.Uid}.sock"), "The socket is $XDG_RUNTIME_DIR/nashi-<uid>.sock");

    // A stale socket: bound once, its owner gone, the file left behind.
    // (.NET unlinks on Dispose, so another process leaves it.)
    System.Diagnostics.Process.Start("python3", ["-c", "import socket,sys; socket.socket(socket.AF_UNIX).bind(sys.argv[1])", path])!.WaitForExit();
    Check(File.Exists(path) && !Instance.Send(path, new("show")), "A stale socket file answers nobody");

    using (var instance = Instance.Claim(path))
    {
        Check(instance is not null, "A stale socket file is replaced");
        Check(File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "The socket is mode 0600");
        Check(Instance.Claim(path) is null && Instance.IsRunning(path), "A second claim fails while the instance lives");
        var received = new System.Collections.Concurrent.BlockingCollection<InstanceMessage>();
        instance!.Start(received.Add);
        string[] names = ["/tmp/ğüş şarkı.mp3", "/tmp/a b/-leading dash.png", "/tmp/$(rm -rf ~)'\".png"];
        Check(Instance.Send(path, new("wheel", names, "x-_TIME7")), "Send reports an accepted message");
        Check(received.TryTake(out var got, 5000) && got.Cmd == "wheel" && got.Files!.SequenceEqual(names) && got.Startup == "x-_TIME7", "wheel arrives with Turkish, space, dash and quote names and the startup id intact");

        var oversized = System.Text.Encoding.UTF8.GetBytes("{\"cmd\":\"wheel\",\"files\":[\"" + new string('a', Instance.MaxMessage) + "\"]}\n");
        var accepted = true;
        try
        {
            using var raw = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
            raw.Connect(new System.Net.Sockets.UnixDomainSocketEndPoint(path));
            raw.Send(oversized);
            raw.ReceiveTimeout = 5000;
            accepted = raw.Receive(new byte[1]) == 1;
        }
        catch (System.Net.Sockets.SocketException) { accepted = false; }
        Check(!accepted && !received.TryTake(out _, 300), "A message over 1 MB is rejected");
        Check(Instance.Send(path, new("quit")) && received.TryTake(out var quit, 5000) && quit.Cmd == "quit", "The instance still answers after a rejected message");
    }
    Check(!File.Exists(path) && !Instance.IsRunning(path), "Disposing the instance removes its socket");
    await Task.CompletedTask;
}

void ClipboardChecks()
{
    var gnome = Desktop.ClipboardPaths("copy\nfile:///home/ece/My%20Files/%C4%9Fne.png\nfile:///tmp/%C5%9F%20x.mp4\n");
    Check(gnome.SequenceEqual(["/home/ece/My Files/ğne.png", "/tmp/ş x.mp4"]), "x-special/gnome-copied-files decodes to paths without the copy header");
    var uris = Desktop.ClipboardPaths("# comment\r\nfile:///a%20b/ğ.png\r\nhttps://example.com/x.png\r\nsmb://server/share/x.png\r\nfile://localhost/c.png\r\n");
    Check(uris.SequenceEqual(["/a b/ğ.png", "/c.png"]), "uri-list decodes %20 and ğ and ignores non-file URIs");
    Check(Desktop.ClipboardPaths("/plain/path one.png\nnot a path").SequenceEqual(["/plain/path one.png"]), "Plain absolute paths count, other text does not");
}

void PlacementChecks()
{
    var area = new PixelRect(0, 32, 1920, 1048);
    var size = new PixelSize(320, 320);
    bool Inside(PixelPoint p) => p.X >= area.X && p.Y >= area.Y && p.X + size.Width <= area.Right && p.Y + size.Height <= area.Bottom;
    foreach (var corner in new[] { new PixelPoint(0, 0), new PixelPoint(1919, 0), new PixelPoint(0, 1079), new PixelPoint(1919, 1079) })
        Check(Inside(Desktop.Place(corner, size, area)), $"The wheel stays on screen with the pointer at {corner}");
    Check(Desktop.Place(new PixelPoint(960, 540), size, area) == new PixelPoint(800, 380), "Away from edges the wheel is centered on the pointer");
    var second = new PixelRect(1920, 0, 1280, 1024);
    Check(Desktop.Place(new PixelPoint(3199, 1023), size, second) == new PixelPoint(2880, 704), "Clamping uses the pointer's own screen");
}

void IntegrationChecks(string root)
{
    var home = Directory.CreateDirectory(Path.Combine(root, "home")).FullName;
    var apps = Directory.CreateDirectory(Path.Combine(root, "My Apps")).FullName;
    var exe = Path.Combine(apps, "Nashi");
    // A stand-in executable that prints its arguments, one per line.
    File.WriteAllText(exe, "#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\"; done\n");
    File.SetUnixFileMode(exe, (UnixFileMode)0b111_101_101);
    const string schema = "org.gnome.settings-daemon.plugins.media-keys";
    const string other = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/";
    var settings = new Dictionary<string, string> { ["list"] = $"['{other}']" };
    string? Fake(string program, IReadOnlyList<string> args)
    {
        if (program != "gsettings") return null;
        switch (args[0])
        {
            case "list-keys": return "volume-up\ncustom-keybindings\n";
            case "get": return settings["list"] + "\n";
            case "set" when args[1] == schema: settings["list"] = args[3]; return "";
            case "set": settings[args[1] + " " + args[2]] = args[3]; return "";
            case "reset-recursively": foreach (var key in settings.Keys.Where(k => k.StartsWith(args[1] + " ")).ToList()) settings.Remove(key); return "";
            default: return null;
        }
    }
    var keyed = schema + ".custom-keybinding:" + Integration.KeybindingPath;
    var scripts = Path.GetDirectoryName(Integration.Script(home))!;
    Directory.CreateDirectory(scripts);
    File.WriteAllText(Path.Combine(scripts, "Someone else's script"), "x");

    var log = Integration.Install(home, exe, Fake);
    Integration.Install(home, exe, Fake);
    var autostart = File.ReadAllText(Integration.Autostart(home));
    var entry = File.ReadAllText(Integration.OpenWith(home));
    Check(autostart.Contains($"\nExec=\"{exe}\" --background\n") && autostart.Contains("\nX-GNOME-Autostart-enabled=true\n") && autostart.Contains("\nNoDisplay=true\n"), "Autostart runs the quoted executable with --background");
    Check(entry.Contains($"\nExec=\"{exe}\" %F\n") && entry.Contains("\nIcon=nashi\n"), "The Open With entry passes %F to the quoted executable");
    Check(entry.Contains("image/png;") && entry.Contains("video/mp4;") && entry.Contains("audio/mpeg;") && entry.Contains("application/pdf;") && entry.Contains("application/zip;") && !entry.Contains("officedocument"), "MimeType lists image, audio, video, PDF and ZIP inputs only");
    Check(Integration.InputExtensions().Count > 15, "Every catalog input the entry claims has a MIME type (" + Integration.InputExtensions().Count + ")");
    using (var icon = new MagickImage(Integration.Icon(home))) Check(icon.Width == 256 && icon.Height == 256, "The icon is installed as a 256×256 PNG");
    var script = Integration.Script(home);
    Check(Path.GetFileName(script) == "Convert with Nashi" && (File.GetUnixFileMode(script) & UnixFileMode.UserExecute) != 0, "The Nautilus script is named in English and executable");
    var shell = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sh", [script, "a b.png", "-x.png", "$HOME `x`.png", "ğ\"q'.png"]) { RedirectStandardOutput = true })!;
    var printed = shell.StandardOutput.ReadToEnd();
    shell.WaitForExit();
    Check(printed == "--wheel\na b.png\n-x.png\n$HOME `x`.png\nğ\"q'.png\n", "The script passes the selected files intact after --wheel: " + printed.Replace("\n", "|"));
    var actionsScript = Integration.ActionsScript(home);
    Check(Path.GetFileName(actionsScript) == "Nashi actions" && (File.GetUnixFileMode(actionsScript) & UnixFileMode.UserExecute) != 0, "The Nautilus actions script is named Nashi actions and executable");
    var actionShell = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sh", [actionsScript, "a b.png", "ğ\"q'.png"]) { RedirectStandardOutput = true })!;
    var actionPrinted = actionShell.StandardOutput.ReadToEnd();
    actionShell.WaitForExit();
    Check(actionPrinted == "--actions\na b.png\nğ\"q'.png\n", "The actions script passes the files intact after --actions: " + actionPrinted.Replace("\n", "|"));
    Check(AppLocalization.Text("Nashi actions", "tr") == "Nashi işlemleri", "The Turkish actions script is Nashi işlemleri");
    Check(settings["list"] == $"['{other}']" && !settings.Keys.Any(k => k.StartsWith(keyed)), "Install adds no keyboard shortcut (only Ctrl-drag)");
    Check(log.Any(l => l.StartsWith("Wrote ")) && !log.Any(l => l.Contains("Ctrl+Alt+N")), "Install prints what it wrote");

    var removed = Integration.Uninstall(home, Fake);
    Check(!File.Exists(Integration.Autostart(home)) && !File.Exists(Integration.OpenWith(home)) && !File.Exists(Integration.Icon(home)) && !File.Exists(script) && !File.Exists(actionsScript), "Uninstall removes Nashi's files");
    Check(File.Exists(Path.Combine(scripts, "Someone else's script")), "Uninstall keeps other Nautilus scripts");

    // The Ctrl+Alt+N entry an earlier version added goes on install and uninstall; everyone else's stay.
    foreach (var uninstall in new[] { false, true })
    {
        settings["list"] = $"['{other}', '{Integration.KeybindingPath}']";
        settings[keyed + " binding"] = "'<Primary><Alt>n'";
        var said = uninstall ? Integration.Uninstall(home, Fake) : Integration.Install(home, exe, Fake);
        Check(settings["list"] == $"['{other}']" && !settings.Keys.Any(k => k.StartsWith(keyed)) && said[^1] == "Removed the old Ctrl+Alt+N shortcut.",
            (uninstall ? "Uninstall" : "Install") + " removes the old Ctrl+Alt+N entry, keeping the others, and says so");
    }
    Integration.Uninstall(home, Fake);

    // A list that can't be read is never overwritten.
    foreach (var (output, label) in new[] { ((string?)null, "fails"), ("garbage output", "is unparsable") })
    {
        settings["list"] = $"['{other}', '{Integration.KeybindingPath}']";
        var writes = 0;
        string? Broken(string program, IReadOnlyList<string> args)
        {
            if (program == "gsettings" && args[0] == "get") return output;
            if (program == "gsettings" && args[0] is "set" or "reset-recursively") writes++;
            return Fake(program, args);
        }
        var aborted = Integration.Install(Directory.CreateDirectory(Path.Combine(root, "home-broken")).FullName, exe, Broken);
        var abortedRemove = Integration.Uninstall(Path.Combine(root, "home-broken"), Broken);
        Check(writes == 0 && !aborted.Concat(abortedRemove).Any(l => l.Contains("Ctrl+Alt+N")), $"When gsettings get {label}, the shortcuts are left exactly as they are");
    }
    var bare = Directory.CreateDirectory(Path.Combine(root, "home-bare")).FullName;
    Integration.Install(bare, exe, (_, _) => null);
    Check(File.Exists(Integration.Script(bare)), "Without gsettings everything else installs");
    // The background instance starts detached: stdio on /dev/null (nothing for a terminal or the AppImage runtime to
    // wait on), in its own session, and the call returns at once.
    var marker = Path.Combine(root, "started-in-background");
    var slow = Path.Combine(apps, "Slow Nashi");
    File.WriteAllText(slow, $"#!/bin/sh\nps -o sid= -p $$ | tr -d ' ' > {Integration.Quote(marker + ".sid")}\necho $$ > {Integration.Quote(marker + ".pid")}\nprintf '%s' \"$*\" > {Integration.Quote(marker)}\nsleep 1\n");
    File.SetUnixFileMode(slow, (UnixFileMode)0b111_101_101);
    var clock = System.Diagnostics.Stopwatch.StartNew();
    var started = Integration.StartBackground(slow);
    var returned = clock.Elapsed;
    for (var i = 0; i < 100 && !File.Exists(marker); i++) Thread.Sleep(20);
    Check(started && File.Exists(marker) && File.ReadAllText(marker) == "--background" && returned < TimeSpan.FromSeconds(0.9), "StartBackground runs exe --background and returns without waiting for it");
    // Read while it sleeps: its own descriptors, as the kernel sees them.
    var fds = File.Exists(marker + ".pid") ? Enumerable.Range(0, 3).Select(fd => File.ResolveLinkTarget($"/proc/{File.ReadAllText(marker + ".pid").Trim()}/fd/{fd}", false)?.FullName).ToArray() : [];
    Check(fds.Length == 3 && fds.All(f => f == "/dev/null"), "The background instance's stdin, stdout and stderr are /dev/null: " + string.Join(", ", fds));
    Check(File.Exists(marker + ".sid") && File.ReadAllText(marker + ".sid").Trim() == File.ReadAllText(marker + ".pid").Trim(), "The background instance leads its own session (setsid)");
    var source = File.ReadAllText(Path.Combine(RepoRoot(), "linux", "Nashi.Linux", "Integration.cs"));
    Check(source.Contains("StartBackground(exe)") && System.Text.RegularExpressions.Regex.Matches(source, @"Process\.Start\(").Count == 2 && source.Contains("-f \"$0\" --background </dev/null >/dev/null 2>&1") && source.Contains("\"/usr/bin/setsid\""), "The installer starts the background instance only through StartBackground (setsid -f, stdio on /dev/null)");
    // Without setsid: nohup (SIGHUP ignored) in its own process group, stdio still on /dev/null.
    foreach (var f in Directory.GetFiles(root, "started-in-background*")) File.Delete(f);
    Check(Integration.StartBackground(slow, "/nonexistent/setsid"), "StartBackground without setsid still starts");
    for (var i = 0; i < 100 && !File.Exists(marker); i++) Thread.Sleep(20);
    var pid = File.Exists(marker + ".pid") ? File.ReadAllText(marker + ".pid").Trim() : "0";
    var stat = File.Exists($"/proc/{pid}/stat") ? File.ReadAllText($"/proc/{pid}/stat") : "";
    var group = stat.Length > 0 ? stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[2] : "";
    var ignored = File.Exists($"/proc/{pid}/status") ? File.ReadAllLines($"/proc/{pid}/status").FirstOrDefault(l => l.StartsWith("SigIgn:"))?.Split('\t')[1] : null;
    var fallbackFds = Enumerable.Range(0, 3).Select(fd => File.Exists($"/proc/{pid}/fd/{fd}") || Directory.Exists($"/proc/{pid}/fd") ? File.ResolveLinkTarget($"/proc/{pid}/fd/{fd}", false)?.FullName : null).ToArray();
    Check(group == pid && ignored is not null && (Convert.ToUInt64(ignored, 16) & 1) == 1 && fallbackFds.All(f => f == "/dev/null"), $"The fallback runs in its own process group with SIGHUP ignored and stdio on /dev/null (pgrp {group}, pid {pid}, SigIgn {ignored})");
    Check(Integration.Quote("/a \"b\" $c `d` \\e") == "\"/a \\\"b\\\" \\$c \\`d\\` \\\\e\"", "Quote escapes \\ \" $ and `");
}

async Task BackgroundChecks(string root)
{
    var app = (App)Application.Current!;
    string? Title() => SuccessToast.Current?.FindControl<TextBlock>("TitleText")?.Text;

    // The pointer-mode wheel: missing and folder paths are filtered; nothing left says so in a card.
    app.ShowWheel([Path.Combine(root, "missing.png"), root]);
    Dispatcher.UIThread.RunJobs();
    Check(Title() == "Nothing to convert" && app.Flow.LastWheel is null, "--wheel with nothing usable shows Nothing to convert");
    var png = Path.Combine(root, "background.png");
    using (var g = new MagickImage("gradient:red-blue", 64, 48)) g.Write(png);
    app.ShowWheel([png, Path.Combine(root, "missing.png")]);
    Dispatcher.UIThread.RunJobs();
    var wheel = app.Flow.LastWheel;
    Check(wheel is { IsVisible: true, Owner: null, Topmost: true } && wheel.Wheel.Items.Select(i => i.Id).SequenceEqual(["convert", .. Capabilities.Tools([png])]), "--wheel shows the topmost tools wheel, Convert first, with no main window");
    wheel!.Wheel.HighlightedIndex = 0;
    wheel.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
    Dispatcher.UIThread.RunJobs();
    var formats = app.Flow.LastWheel;
    Check(formats != wheel && formats is { IsVisible: true } && formats.Wheel.Items.Any(i => i.Id == "convert:jpg"), "Convert opens the format wheel in the same mode");
    formats!.Wheel.HighlightedIndex = formats.Wheel.Items.ToList().FindIndex(i => i.Id == "convert:jpg");
    formats.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
    for (var i = 0; i < 200 && Title()?.StartsWith("Nicely") != true; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
    Check(Title() == "Nicely done. 1 file is ready." && File.Exists(Path.Combine(root, "background.jpg")), "Choosing JPG writes next to the file and shows the success card");
    SuccessToast.Current?.Close();
    app.ShowWheel([Path.Combine(root, "notes.xyz")]);
    Dispatcher.UIThread.RunJobs();
    Check(Title() == "No tools for XYZ files yet.", "An empty background wheel says there are no tools for XYZ files yet");
    SuccessToast.Current?.Close();

    // A pointer wheel nobody touches closes after its idle timeout (30 s by default).
    var now = new DateTime(2026, 1, 1);
    var idle = new WheelWindow("Tools", [new("compress", "Compress"), new("convert", "Convert")]) { Clock = () => now };
    idle.ShowAt(null);
    Dispatcher.UIThread.RunJobs();
    now += TimeSpan.FromSeconds(29);
    idle.CloseIfIdle();
    var openAt29 = idle.IsVisible;
    idle.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
    now += TimeSpan.FromSeconds(29);
    idle.CloseIfIdle();
    var openAfterKey = idle.IsVisible;
    now += TimeSpan.FromSeconds(1);
    idle.CloseIfIdle();
    Dispatcher.UIThread.RunJobs();
    Check(WheelWindow.IdleTimeout == TimeSpan.FromSeconds(30) && openAt29 && openAfterKey && !idle.IsVisible, "A pointer wheel closes after 30 s without a key or pointer move; a key resets the clock");
    var home = new WheelWindow("Tools", [new("compress", "Compress")]) { Clock = () => now };
    home.Show();
    now += TimeSpan.FromMinutes(5);
    home.CloseIfIdle();
    Check(home.IsVisible, "Home's wheel (not at the pointer) has no idle close");
    home.Close();

    // Toasts stay bottom-center when SizeToContent changes their height.
    var growing = new ProgressToast("Convert to JPG", null);
    growing.Show();
    Dispatcher.UIThread.RunJobs();
    var screen = growing.Screens.Primary;
    if (screen is not null)
    {
        int Bottom() => growing.Position.Y + (int)(growing.Bounds.Height * screen.Scaling);
        var before = Bottom();
        var height = growing.Bounds.Height;
        growing.FindControl<TextBlock>("TitleText")!.Text = string.Join(" ", Enumerable.Repeat("A much longer title that wraps onto more lines", 6));
        Dispatcher.UIThread.RunJobs();
        Check(growing.Bounds.Height > height && Math.Abs(Bottom() - before) <= 1 && Bottom() == screen.WorkingArea.Bottom - 28, "A toast that grows is placed again, still 28 px above the bottom");
    }
    else Check(false, "Headless has a screen for placement checks");
    growing.Close();
    Check(Desktop.BottomCenter(new PixelRect(0, 0, 1920, 1080), new PixelSize(560, 88), 1) == new PixelPoint(680, 964) && Desktop.BottomCenter(new PixelRect(1920, 0, 2560, 1440), new PixelSize(1120, 176), 2) == new PixelPoint(2640, 1208), "Bottom-center math with scaling and a second screen");

    // The success card: copy for 0, 1 and 3 files; light, dark and Turkish renders.
    Check(SuccessToast.Heading(0) == "Nicely done." && SuccessToast.Heading(1) == "Nicely done. 1 file is ready." && SuccessToast.Heading(3) == "Nicely done. 3 files are ready.", "Success titles for 0, 1 and 3 files");
    void Render(string name, IReadOnlyList<string> outputs)
    {
        var card = SuccessToast.Done(outputs, root);
        card.Present();
        Dispatcher.UIThread.RunJobs();
        var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Check(texts.Contains(SuccessToast.Heading(outputs.Count)) && texts.Contains(Path.GetFileName(outputs[0])) && texts.Contains(L10n.T("Show in folder")) && card.Topmost && !card.ShowActivated, $"Success card ({name}) shows title, first file and Show in folder, on top without focus");
        Check(card.Width == 560 && card.Height == 88, "Success card is 560×88");
        var frame = card.CaptureRenderedFrame();
        Check(frame is not null, $"Success card renders ({name})");
        Smoke(frame, $"toast-{name}");
        card.Close();
    }
    string[] three = [Path.Combine(root, "a.jpg"), Path.Combine(root, "b.jpg"), Path.Combine(root, "c.jpg")];
    Render("success-light", [Path.Combine(root, "background.jpg")]);
    Render("success-three-light", three);
    var noted = SuccessToast.Done([Path.Combine(root, "notes.md")], root, DocumentOperations.PlainMarkdown);
    Check(noted.FindControl<TextBlock>("DetailText")?.Text == "notes.md · Saved as plain-text Markdown", "The success card's detail adds the runner's note (plain-text Markdown)");
    noted.Close();
    Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
    try { Render("success-dark", [Path.Combine(root, "background.jpg")]); }
    finally { Application.Current!.RequestedThemeVariant = ThemeVariant.Light; }
    L10n.Language = "tr";
    try
    {
        Check(SuccessToast.Heading(3) == "Hazır! 3 dosyan hazır.", "Turkish success title for 3 files");
        Render("success-tr", three);
        var notice = SuccessToast.Notice(L10n.F("No conversions for {0} files yet.", "PDF"));
        Check(notice.FindControl<TextBlock>("TitleText")?.Text == "PDF dosyaları için henüz dönüştürme yok.", "Turkish empty-wheel card names the type");
        Check(L10n.T("Nothing was created.") == "Hiçbir şey oluşturulmadı.", "Turkish card for a run that created nothing");
    }
    finally { L10n.Language = "en"; }

    var error = SuccessToast.Notice("Couldn’t finish: disk full");
    error.Present();
    Dispatcher.UIThread.RunJobs();
    Check(!error.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible) && SuccessToast.Current == error, "An error card has no action and replaces the current card");
    var errorFrame = error.CaptureRenderedFrame();
    Smoke(errorFrame, "toast-error-light");
    error.Close();
    Check(SuccessToast.Visible == TimeSpan.FromSeconds(3) && SuccessToast.ErrorVisible == TimeSpan.FromSeconds(6) && SuccessToast.Fade == TimeSpan.FromSeconds(0.28), "Cards stay 3 s (errors 6 s) and fade over 0.28 s");
}

void IconChecks()
{
    // Every id the Linux wheel can show: its tools, Convert, the actions and the paging petals; formats have none.
    var ids = new[] { "convert", "compress", "muteVideo", "normalizeAudio", "extractArchive", "createPDF", "mergePDF", "page:previous", "page:next" }
        .Concat(FileActions.Ids.Select(id => "action:" + id)).Concat(FeatureCatalog.ToolNames.Keys).ToList();
    foreach (var probe in new[] { "x.png", "x.mp4", "x.mp3", "x.zip" }) ids.AddRange(Capabilities.Tools([probe]));
    var missing = ids.Distinct().Where(id => WheelIcons.Key(id) is not { } key || !WheelIcons.Paths.ContainsKey(key)).ToList();
    Check(missing.Count == 0, "Every tool, action and paging id maps to a playground icon; missing: " + string.Join(", ", missing));
    Check(WheelIcons.Paths.Values.All(d => StreamGeometry.Parse(d).Bounds.Width > 0), "Every copied icon path parses");
    Check(Capabilities.Formats(["x.png"]).Concat(Capabilities.Formats(["x.mp4"])).All(f => WheelIcons.ForId("convert:" + f) is null), "Format petals have no icon, as in the playground");
    var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Resources", "FeatureCatalog.json"))).RootElement.GetProperty("fileActions").EnumerateArray().Select(e => e.GetString()).ToArray();
    Check(catalog.SequenceEqual(FileActions.Ids), "FileActions.Ids match the catalog's fileActions");
}

void LabelChecks()
{
    var title = AppLocalization.Uppercase("Ses kanallarını dönüştür", "tr");
    Check(title == "SES KANALLARINI DÖNÜŞTÜR", "Turkish upper case: SES KANALLARINI DÖNÜŞTÜR");
    for (var i = 0; i < 8; i++)
    {
        var label = Wheel.Layout(8, i, title, hasIcon: true);
        Check(label.Fits && label.Lines.Count <= 2 && label.FontSize >= 6.5 && Wheel.Rects(label).All(r => Wheel.Contains(Wheel.Petal(8, i), r)), $"A long Turkish label fits petal {i} of 8 in at most two lines at ≥ 6.5 pt ({label.FontSize} pt, {label.Lines.Count} lines)");
    }
    var english = Wheel.Layout(8, 0, "COMPRESS", hasIcon: true);
    Check(english.Fits && english.Lines.Count == 1 && english.FontSize == 9.5, "A short label keeps one line at 9.5 pt");
    var format = Wheel.Layout(8, 3, "WEBP", hasIcon: false);
    Check(format.Fits && format.FontSize == 13 && !format.HasIcon, "A format label is 13 pt with no icon");
    foreach (var language in new[] { "en", "tr" })
        foreach (var id in FeatureCatalog.ToolNames.Keys.Concat(FileActions.Ids.Select(a => "action:" + a)))
        {
            var name = id.StartsWith("action:") ? FileActions.Title(id[7..]) : FeatureCatalog.ToolNames[id];
            var upper = AppLocalization.Uppercase(AppLocalization.Text(name, language), language);
            var label = Wheel.Layout(8, 5, upper, true);
            Check(label.Fits && label.Lines.Count <= 2, $"{language} {upper} fits an eight-petal wheel");
        }
}

void MotionChecks()
{
    WheelFeedback.ReduceMotion = false;
    try
    {
        var moving = new WheelWindow("Tools", [new("compress", "Compress")]);
        var root = moving.FindControl<Grid>("Root")!;
        Check(root.Opacity == 0 && root.Transitions?.Count == 2 && root.Transitions.All(t => t is Avalonia.Animation.ITransition) && WheelFeedback.Opening == TimeSpan.FromSeconds(0.2), "With motion on, the wheel opens with a 0.2 s scale and fade");
        moving.Show();
        Dispatcher.UIThread.RunJobs();
        Check(root.Opacity == 1 || root.Transitions is { Count: 2 }, "Opening sets the target opacity");
        moving.Close();
        WheelFeedback.SystemReducesMotion = true;
        var still = new WheelWindow("Tools", [new("compress", "Compress")]);
        Check(still.FindControl<Grid>("Root") is { Opacity: 1, Transitions: null }, "The system's reduce-motion setting skips the animation");
        still.Close();
    }
    finally { WheelFeedback.ReduceMotion = true; WheelFeedback.SystemReducesMotion = false; }
    WheelFeedback.ReduceMotion = true;
    var reduced = new WheelWindow("Tools", [new("compress", "Compress")]);
    Check(reduced.FindControl<Grid>("Root") is { Opacity: 1, Transitions: null }, "The app's reduce-motion setting skips the animation");
    reduced.Close();
}

async Task ActionChecks(string root)
{
    var work = Directory.CreateDirectory(Path.Combine(root, "actions")).FullName;
    var a = Path.Combine(work, "a ğ, #1.png");
    var b = Path.Combine(work, "b.txt");
    File.WriteAllText(a, "a");
    File.WriteAllText(b, "b");
    var into = Directory.CreateDirectory(Path.Combine(work, "into")).FullName;
    File.WriteAllText(Path.Combine(into, "b.txt"), "older");

    var home0 = new Window(); // never shown: it only lends the headless clipboard
    var copies = FileActions.CopyTo([a, b], into);
    Check(copies.SequenceEqual([Path.Combine(into, "a ğ, #1.png"), Path.Combine(into, "b 2.txt")]) && File.Exists(a) && File.Exists(b) && File.ReadAllText(Path.Combine(into, "b.txt")) == "older", "Copy keeps originals and gives the copies unique names");

    var to = Directory.CreateDirectory(Path.Combine(work, "to")).FullName;
    var moved = FileActions.MoveTo([a, b], to);
    Check(moved.Files.Count == 2 && moved.AlreadyThere == 0 && !File.Exists(a) && !File.Exists(b) && File.ReadAllText(Path.Combine(to, "b.txt")) == "b", "Move moves every file");
    var first = moved.Files[0];
    var gone = Path.Combine(to, "vanished.txt");
    try { FileActions.MoveTo([first, gone], into); Check(false, "Move with a missing second file fails"); }
    catch (FileNotFoundException) { Check(File.Exists(first) && !File.Exists(Path.Combine(into, "a ğ, #1 2.png")), "A failed second move rolls the first file back into place"); }
    var stay = FileActions.MoveTo([first], to);
    Check(stay.Files.Count == 0 && stay.AlreadyThere == 1 && File.Exists(first), "A file already in the folder is left alone and counted as already there, not moved");

    // Linux allows ':' in names (Windows does not): copy and convert "a:b.png".
    var colon = Path.Combine(work, "a:b.png");
    using (var g = new MagickImage("gradient:red-blue", 16, 12)) g.Write("png:" + colon);
    var colonCopy = FileActions.CopyTo([colon], into);
    var colonJpg = Pumped(new OperationRunner(new Toolchain(work, "")).RunAsync("convert:jpg", [colon], Directory.CreateDirectory(Path.Combine(work, "colon-out")).FullName, new Progress<OperationProgress>(), default));
    Check(colonCopy.Single() == Path.Combine(into, "a:b.png") && colonJpg.Single().EndsWith("/a:b.jpg") && File.Exists(colonJpg[0]), "A file named a:b.png copies and converts on Linux");
    Check(!OperatingSystem.IsWindows() && SafePaths.Unique(work, "12:30 x\\y.png").EndsWith("/12:30 x\\y.png"), "SafePaths.Unique keeps ':' and '\\' in Linux names");
    try { SafePaths.Unique(work, ".."); Check(false, "Unique rejects .."); } catch (ArgumentException) { Check(true, "Unique rejects .. on Linux"); }

    var text = Pumped(FileActions.CopyPathsAsync(home0.Clipboard, [first, moved.Files[1]]));
    Check(text == first + "\n" + moved.Files[1], "Copy Path joins full paths with newlines");
    try { await FileActions.CopyPathsAsync(null, [first]); Check(false, "Copy Path without a clipboard fails"); }
    catch (InvalidOperationException error) { Check(error.Message == "Couldn’t reach the clipboard." && AppLocalization.Text(error.Message, "tr") != error.Message, "Copy Path without a clipboard is an error, never Path copied."); }

    launched.Clear(); startable.Clear(); exitCodes.Clear(); running.Clear();
    startable.UnionWith(["konsole"]);
    exitCodes["konsole"] = 0;
    Check(FileActions.OpenTerminal(first) && launched.Select(l => l.Program).SequenceEqual(["x-terminal-emulator", "gnome-terminal", "konsole"]) && launched[2].Args.SequenceEqual(["--workdir", to]) && launched[1].Args.SequenceEqual(["--working-directory=" + to]) && launched.All(l => l.Folder == to), "Terminal tries x-terminal-emulator, gnome-terminal, konsole in order, in the file's folder");
    launched.Clear();
    startable.UnionWith(["x-terminal-emulator", "gnome-terminal"]);
    exitCodes["x-terminal-emulator"] = 1; // a broken alternative exits at once
    Check(FileActions.OpenTerminal(first) && launched.Select(l => l.Program).SequenceEqual(["x-terminal-emulator", "gnome-terminal"]), "A terminal that exits non-zero at once counts as failed; one still running after the settle time counts as open");
    launched.Clear(); startable.Clear(); exitCodes.Clear(); running.Clear();
    Check(!FileActions.OpenTerminal(first) && launched.Count == 4 && launched[3].Program == "xfce4-terminal", "Without any terminal, all four are tried and the action reports failure");

    ran.Clear(); launched.Clear();
    runResult = (program, _) => program == "dbus-send" ? "method return" : null;
    Check(FileActions.Share([first]) && ran.Single().Program == "dbus-send" && ran[0].Args.Contains("org.freedesktop.FileManager1.ShowItems") && ran[0].Args.Contains("array:string:file://" + to.Replace(" ", "%20") + "/a%20%C4%9F%2C%20%231.png") && launched.Count == 0, "Share asks the file manager to show the file, with commas and # escaped in its URI");
    runResult = (_, _) => null;
    startable.Add("xdg-open");
    Check(FileActions.Share([first]) && launched.Single().Program == "xdg-open" && launched[0].Args.SequenceEqual([to]), "Without FileManager1, Share opens the folder with xdg-open");
    startable.Clear(); launched.Clear();

    // A disabled petal (here the second): no gold, skipped by the keys, never chosen.
    var wheel = new Wheel { Items = FileActions.Ids.Select(id => new WheelItem("action:" + id, FileActions.Title(id), id != "float")).ToList() };
    WheelItem? chosen = null;
    wheel.Chosen += item => chosen = item;
    void Key(Key key) => wheel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    wheel.HighlightedIndex = 1;
    Check(wheel.HighlightedIndex == -1, "A disabled petal never takes the highlight");
    wheel.HighlightedIndex = 0;
    Key(Avalonia.Input.Key.Right);
    Check(wheel.HighlightedIndex == 2, "Right skips a disabled petal");
    Key(Avalonia.Input.Key.Left);
    Check(wheel.HighlightedIndex == 0, "Left skips a disabled petal back");

    // Sound: a choice plays the wav at full volume; every highlight change the user makes (arrows, Tab, the pointer
    // into another petal) plays it at 0.65, none dropped; opening and setting the highlight in code are silent.
    Check(File.Exists(WheelFeedback.Sound), "WheelSelection.wav ships beside the executable");
    var plays = new List<double>();
    WheelFeedback.Player = plays.Add;
    Key(Avalonia.Input.Key.Enter);
    Check(chosen?.Id == "action:share" && plays.SequenceEqual([1.0]), "Choosing plays the sound at full volume");
    plays.Clear();
    wheel.Items = [.. wheel.Items];
    wheel.HighlightedIndex = 3;
    Check(plays.Count == 0, "Opening a wheel and setting its highlight in code play no tick");
    var eight = new Wheel { Items = [.. Enumerable.Range(0, 8).Select(i => new WheelItem("p" + i, "P" + i))] };
    void Press(Key key, KeyModifiers modifiers = KeyModifiers.None) => eight.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
    for (var i = 0; i < 8; i++) Press(Avalonia.Input.Key.Right);
    Check(plays.Count == 8 && plays.All(v => v == 0.65) && eight.HighlightedIndex == 0, "Eight quick Right presses round 8 petals: 8 ticks at 0.65, none dropped");
    plays.Clear();
    for (var i = 0; i < 8; i++) Press(Avalonia.Input.Key.Tab);
    for (var i = 0; i < 8; i++) Press(Avalonia.Input.Key.Tab, KeyModifiers.Shift);
    Check(plays.Count == 16 && eight.HighlightedIndex == 0, "Tab and Shift+Tab move the highlight and tick on every move");
    plays.Clear();
    var eightWindow = new Window { Width = Wheel.Size, Height = Wheel.Size, Content = eight };
    eightWindow.Show();
    Dispatcher.UIThread.RunJobs();
    Point PetalCenter(int index)
    {
        var angle = (90 - index * 45) * Math.PI / 180;
        var r = (Wheel.Inner + Wheel.Radius) / 2;
        return new(Wheel.Size / 2 + r * Math.Cos(angle), Wheel.Size / 2 - r * Math.Sin(angle));
    }
    foreach (var index in new[] { 1, 2, 3, 4, 5, 6, 7, 0 }) eightWindow.MouseMove(PetalCenter(index), RawInputModifiers.None);
    Check(plays.Count == 8 && eight.HighlightedIndex == 0, "The pointer sweeping across 8 petals quickly ticks 8 times, none dropped");
    eightWindow.MouseMove(PetalCenter(0) + new Vector(1, 1), RawInputModifiers.None);
    Check(plays.Count == 8, "Moving within the same petal does not tick");
    eightWindow.Close();
    WheelFeedback.SoundEnabled = false;
    plays.Clear();
    Key(Avalonia.Input.Key.Right);
    Key(Avalonia.Input.Key.Enter);
    Check(plays.Count == 0, "With the sound setting off nothing plays");
    WheelFeedback.SoundEnabled = true;

    // The wav decodes once to 16-bit PCM (its LIST chunk skipped); the tick volume scales and clamps the samples.
    var wav = WheelFeedback.DecodeWav(File.ReadAllBytes(WheelFeedback.Sound));
    Check(wav is { Rate: 48000, Channels: 2 } && wav.Samples.Length == 192000 / 2 && wav.Samples.Any(v => v != 0), "WheelSelection.wav decodes as 48 kHz stereo 16-bit PCM");
    byte[] Riff(short format, short bits, short[] data)
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);
        w.Write("RIFF"u8); w.Write(0); w.Write("WAVE"u8);
        w.Write("LIST"u8); w.Write(3); w.Write("abc\0"u8); // odd size, padded
        w.Write("fmt "u8); w.Write(16); w.Write(format); w.Write((short)1); w.Write(8000); w.Write(16000); w.Write((short)2); w.Write(bits);
        w.Write("data"u8); w.Write(data.Length * 2); foreach (var v in data) w.Write(v);
        return buffer.ToArray();
    }
    var made = WheelFeedback.DecodeWav(Riff(1, 16, [1, -2, short.MaxValue, short.MinValue]));
    Check(made is { Rate: 8000, Channels: 1 } && made.Samples.SequenceEqual(new short[] { 1, -2, short.MaxValue, short.MinValue }), "A PCM wav after an odd-sized chunk decodes sample for sample");
    Check(WheelFeedback.DecodeWav(Riff(3, 32, [0, 0])) is null && WheelFeedback.DecodeWav(Riff(1, 8, [0])) is null && WheelFeedback.DecodeWav("RIFF"u8.ToArray()) is null, "Float, 8-bit and truncated wavs are refused");
    var scaled = new short[4];
    WheelFeedback.Scale(new short[] { 1000, -1000, short.MaxValue, short.MinValue }, scaled, 0.65);
    var louder = new short[2];
    WheelFeedback.Scale(new short[] { 30000, -30000 }, louder, 2);
    Check(scaled.SequenceEqual(new short[] { 650, -650, 21299, -21299 }) && louder.SequenceEqual(new short[] { short.MaxValue, short.MinValue }), "The tick volume scales the samples and clamps them");

    // PulseAudio is opened on the writer thread, never the caller's: Play only hands over. Without a server that
    // tick goes to the players and PulseAudio backs off (1 s, doubling, at most 60 s), then is tried again.
    Check(Pulse.Backoff(1) == TimeSpan.FromSeconds(1) && Pulse.Backoff(2) == TimeSpan.FromSeconds(2) && Pulse.Backoff(4) == TimeSpan.FromSeconds(8) && Pulse.Backoff(7) == TimeSpan.FromSeconds(60) && Pulse.Backoff(40) == TimeSpan.FromSeconds(60), "PulseAudio retries back off 1, 2, 4… s, at most a minute");
    var pulseNow = new DateTime(2026, 1, 1);
    Pulse.Clock = () => pulseNow;
    Pulse.Server = "unix:/nonexistent/nashi-checks";
    WheelFeedback.Missing.Clear();
    launched.Clear();
    var handOver = System.Diagnostics.Stopwatch.StartNew();
    var accepted = Pulse.Play(0.65);
    handOver.Stop();
    for (var i = 0; i < 500 && !Pulse.Down; i++) Thread.Sleep(10);
    for (var i = 0; i < 200 && WheelFeedback.Missing.Count < 3; i++) Thread.Sleep(10);
    Check(accepted && handOver.ElapsedMilliseconds < 100 && Pulse.Down && !Pulse.Play(0.65), "Play only hands the sound to the writer thread; without a server PulseAudio backs off and declines");
    Check(launched.FirstOrDefault().Program == "paplay" && launched[0].Args[0] == "--volume=42598", "The tick that found no server still plays, through the players");
    pulseNow += Pulse.Backoff(1) + TimeSpan.FromMilliseconds(1);
    Check(!Pulse.Down && Pulse.Play(0.65), "After the backoff the next tick tries PulseAudio again");
    for (var i = 0; i < 500 && !Pulse.Down; i++) Thread.Sleep(10);
    pulseNow += TimeSpan.FromSeconds(1.5);
    Check(Pulse.Down, "A second failure in a row waits longer (2 s)");
    WheelFeedback.Player = WheelFeedback.PlayThroughPlayers;
    WheelFeedback.Missing.Clear();
    launched.Clear();
    Key(Avalonia.Input.Key.Enter);
    Check(launched.Select(l => l.Program).SequenceEqual(["paplay", "pw-play", "aplay"]) && launched[0].Args.SequenceEqual(["--volume=65536", WheelFeedback.Sound]), "The fallback tries paplay, then pw-play, then aplay");
    launched.Clear();
    Key(Avalonia.Input.Key.Enter);
    Check(launched.Count == 0 && WheelFeedback.Missing.SetEquals(["paplay", "pw-play", "aplay"]), "Missing players are remembered and not tried again");
    WheelFeedback.Missing.Clear();
    startable.UnionWith(["paplay", "pw-play"]);
    exitCodes["paplay"] = 1; // no sound server
    Key(Avalonia.Input.Key.Enter);
    Check(launched.Select(l => l.Program).SequenceEqual(["paplay", "pw-play"]) && launched[1].Args.SequenceEqual(["--volume=1", WheelFeedback.Sound]), "A player that exits non-zero hands over to the next one");
    exitCodes.Clear(); running.Clear(); launched.Clear();
    startable.Add("paplay");
    Key(Avalonia.Input.Key.Right); // stops pw-play, still running from the hand-over above
    stoppedPlayers.Clear();
    Key(Avalonia.Input.Key.Right);
    Check(launched.Count == 2 && launched.All(l => l.Program == "paplay" && l.Args[0] == "--volume=42598"), "Through the fallback too, every highlight change ticks at 0.65");
    Check(stoppedPlayers.SequenceEqual(["paplay"]), "At most one fallback player runs: a newer tick stops the playing one and restarts");
    running[0](1); // the stopped one exits non-zero: no hand-over to pw-play
    Check(launched.Count == 2, "A player stopped for a newer sound does not hand over to the next player");
    startable.Clear(); running.Clear(); launched.Clear();
    WheelFeedback.Player = _ => { };

    // Through Home: the Actions chip → Copy → the picked folder → the success card; Copy Path → the clipboard.
    var home = new MainWindow { Width = 820, Height = 780 };
    home.Show();
    Dispatcher.UIThread.RunJobs();
    var src = Path.Combine(work, "home.png");
    File.WriteAllText(src, "x");
    home.Selection.Add([src]);
    var picked = Directory.CreateDirectory(Path.Combine(work, "picked")).FullName;
    home.Flow.PickFolder = (_, title) => Task.FromResult<string?>(title == "Copy to…" || title == "Move to…" ? picked : null);
    string? Title() => SuccessToast.Current?.FindControl<TextBlock>("TitleText")?.Text;
    void Choose(string id)
    {
        SuccessToast.Current?.Close();
        home.Flow.ShowActions(home.Selection.Files);
        var w = home.Flow.LastWheel!;
        w.Wheel.HighlightedIndex = w.Wheel.Items.ToList().FindIndex(i => i.Id == id);
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        // Pumped by hand: an await here would wait on this same dispatcher.
        for (var i = 0; i < 100; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); if (!home.Flow.Busy && SuccessToast.Current is not null) break; }
    }
    Choose("action:copy");
    Check(Title() == "Copied." && File.Exists(Path.Combine(picked, "home.png")) && File.Exists(src), "Copy from the Actions wheel copies into the picked folder and says Copied.");
    Choose("action:copyPath");
    var clip = home.Clipboard!.TryGetTextAsync();
    for (var i = 0; i < 200 && !clip.IsCompleted; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
    Check(Title() == "Path copied." && clip.IsCompleted && clip.Result == src, "Copy Path puts the full path on the clipboard and says Path copied.");
    Choose("action:move");
    Check(Title() == "Moved." && File.Exists(Path.Combine(picked, "home 2.png")) && !File.Exists(src), "Move from the Actions wheel moves into the picked folder and says Moved.");
    home.Selection.Clear();
    home.Selection.Add([Path.Combine(picked, "home 2.png")]);
    Choose("action:move");
    Check(Title() == "Already there" && File.Exists(Path.Combine(picked, "home 2.png")), "Moving a file into its own folder says Already there");
    home.Flow.PickFolder = (_, _) => Task.FromResult<string?>(null);
    Choose("action:copy");
    Check(SuccessToast.Current is null && !home.Flow.Busy, "A cancelled folder picker returns quietly");
    SuccessToast.Current?.Close();
    home.Close();

    // Background: --actions opens a bare actions wheel at the pointer; the tools wheel there has no Actions petal.
    var app = (App)Application.Current!;
    var png = Path.Combine(work, "bg.png");
    File.WriteAllText(png, "x");
    app.Handle(new InstanceMessage("actions", [png]));
    Dispatcher.UIThread.RunJobs();
    var bare = app.Flow.LastWheel;
    Check(bare is { Owner: null, Width: Wheel.Size } && bare.Wheel.Items.Count == 6 && bare.Wheel.Items[0].Id == "action:share", "--actions shows the bare actions wheel at the pointer");
    // Background Copy: the picker gets a real, shown 1×1 topmost owner at the pointer, closed afterwards;
    // a picker that can't open or a non-local folder is an error card, never silence.
    Window? owner = null;
    var ownerWasShown = false;
    app.Flow.PickFolder = (host, _) =>
    {
        owner = host as Window;
        ownerWasShown = owner is { IsVisible: true, Topmost: true, Width: 1, Height: 1, SystemDecorations: SystemDecorations.None };
        throw new InvalidOperationException("Choose a folder on this computer.");
    };
    string? Card() => SuccessToast.Current?.FindControl<TextBlock>("TitleText")?.Text;
    void Pick()
    {
        SuccessToast.Current?.Close();
        bare!.Wheel.HighlightedIndex = bare.Wheel.Items.ToList().FindIndex(i => i.Id == "action:copy");
        bare.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        for (var i = 0; i < 100 && SuccessToast.Current is null; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
    }
    Pick();
    Check(ownerWasShown && owner is { IsVisible: false }, "The background folder picker is owned by a shown 1×1 topmost window that closes afterwards");
    Check(Card() == "Choose a folder on this computer.", "A non-local folder shows an error card");
    app.Flow.PickFolder = WheelFlow.PickLocalFolderAsync; // the real one, over the headless platform
    app.Handle(new InstanceMessage("actions", [png]));
    Dispatcher.UIThread.RunJobs();
    bare = app.Flow.LastWheel;
    Pick();
    Check(Card() == "Couldn’t open the folder picker.", "Without a folder picker an error card says so: " + Card());
    SuccessToast.Current?.Close();
    bare?.Close();
    app.ShowWheel([png]);
    Dispatcher.UIThread.RunJobs();
    Check(app.Flow.LastWheel?.Wheel.Items.All(i => !i.Id.StartsWith("action:")) == true && app.Flow.LastWheel.Width == Wheel.Size, "The pointer tools wheel has no actions and no cards, as on macOS");
    app.Flow.LastWheel?.Close();
}

void WheelRenders(string root)
{
    var work = Directory.CreateDirectory(Path.Combine(root, "renders")).FullName;
    var jpg = Path.Combine(work, "orchard.jpg");
    var png = Path.Combine(work, "woodboard.png");
    using (var image = new MagickImage("gradient:#cfe0c0-#2f6a3a", 400, 300)) image.Write(jpg);
    using (var image = new MagickImage("gradient:#d8b27a-#7a4f2a", 400, 300)) image.Write(png);
    var pdf = Path.Combine(work, "notes.pdf");
    File.WriteAllText(pdf, "x");
    foreach (var (theme, name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
    {
        Application.Current!.RequestedThemeVariant = theme;
        try
        {
            var home = new MainWindow { Width = 820, Height = 780 };
            home.Show();
            Dispatcher.UIThread.RunJobs();
            void Render(Action<IReadOnlyList<string>> show, IReadOnlyList<string> files, string file, int highlight)
            {
                show(files);
                var w = home.Flow.LastWheel!;
                Dispatcher.UIThread.RunJobs();
                for (var i = 0; i < 400 && !w.Thumbnails.IsCompleted; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
                Dispatcher.UIThread.RunJobs();
                w.Wheel.HighlightedIndex = highlight;
                Dispatcher.UIThread.RunJobs();
                w.CaptureRenderedFrame(); // the compositor shows a change one frame later
                var frame = w.CaptureRenderedFrame();
                Check(frame is not null && w.Width == Wheel.Size + 2 * WheelWindow.CardColumn, $"{file} renders with file cards");
                Smoke(frame, Path.GetFileNameWithoutExtension(file));
                w.Close();
            }
            Render(home.Flow.ShowTools, [jpg, png], $"wheel-tools-jpg-{name}.png", 1);
            Render(home.Flow.ShowActions, [jpg, png], $"wheel-actions-{name}.png", -1);
            Render(home.Flow.ShowFormats, [jpg, png], $"wheel-formats-{name}.png", 2);
            Render(home.Flow.ShowActions, [pdf, jpg], $"wheel-actions-sheet-{name}.png", 3);
            // The macOS image tools wheel of the reference (2.png), eight petals, and the same in Turkish.
            foreach (var language in new[] { "en", "tr" })
            {
                L10n.Language = language;
                string[] eight = ["compress", "removeMetadata", "editImage", "annotateImage", "frameImage", "cropImage", "redactImage", "audioChannels"];
                var full = new WheelWindow(L10n.T("Tools"), eight.Select(id => new WheelItem(id, L10n.T(FeatureCatalog.ToolNames[id]))).ToList(), [jpg, png]);
                full.Show(home);
                for (var i = 0; i < 400 && !full.Thumbnails.IsCompleted; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
                full.Wheel.HighlightedIndex = 4;
                Dispatcher.UIThread.RunJobs();
                full.CaptureRenderedFrame();
                var frame = full.CaptureRenderedFrame();
                Check(frame is not null, $"The eight-petal {language} tools wheel renders");
                Smoke(frame, $"wheel-tools-eight-{language}-{name}");
                full.Close();
                if (language == "tr") Render(home.Flow.ShowActions, [jpg, png], $"wheel-actions-tr-{name}.png", 2);
            }
            L10n.Language = "en";
            home.Close();
        }
        finally { Application.Current!.RequestedThemeVariant = ThemeVariant.Light; }
    }
}

// One pixel of a rendered frame (premultiplied BGRA from Skia, returned unpremultiplied).
static Color Pixel(Avalonia.Media.Imaging.WriteableBitmap frame, int x, int y)
{
    using var buffer = frame.Lock();
    var bytes = new byte[4];
    System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, bytes, 0, 4);
    var a = bytes[3];
    byte Un(byte v) => a == 0 ? (byte)0 : (byte)Math.Min(255, Math.Round(v * 255.0 / a));
    return buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? Color.FromArgb(a, Un(bytes[0]), Un(bytes[1]), Un(bytes[2])) : Color.FromArgb(a, Un(bytes[2]), Un(bytes[1]), Un(bytes[0]));
}

// A headless render saved to NASHI_SMOKE_DIRECTORY as is (transparent where the window is) and over a sample photo,
// which shows the translucency a real desktop would see through the window.
static void Smoke(Avalonia.Media.Imaging.WriteableBitmap? frame, string name)
{
    var smoke = Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY");
    if (string.IsNullOrEmpty(smoke) || frame is null) return;
    Directory.CreateDirectory(smoke);
    // WriteableBitmap.Save drops the alpha channel, so the PNG is written from the unpremultiplied pixels.
    var size = frame.PixelSize;
    var rgba = new byte[size.Width * size.Height * 4];
    for (var y = 0; y < size.Height; y++)
        for (var x = 0; x < size.Width; x++)
        {
            var c = Pixel(frame, x, y);
            var at = (y * size.Width + x) * 4;
            (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) = (c.R, c.G, c.B, c.A);
        }
    using var window = new MagickImage();
    window.ReadPixels(rgba, new PixelReadSettings((uint)size.Width, (uint)size.Height, StorageType.Char, PixelMapping.RGBA));
    window.Write(Path.Combine(smoke, name + ".png"), MagickFormat.Png32);
    using var photo = SamplePhoto(size.Width, size.Height);
    photo.Composite(window, CompositeOperator.Over);
    photo.Write(Path.Combine(smoke, name + "-over-photo.png"));
}

// A busy, colourful "desktop photo": a sky-to-field gradient with sun, hills and stripes of text-like bars.
static MagickImage SamplePhoto(int width, int height)
{
    var photo = new MagickImage("gradient:#3d7fd6-#f2a65a", (uint)width, (uint)height);
    new ImageMagick.Drawing.Drawables()
        .FillColor(new MagickColor("#ffe58a")).Circle(width * 0.78, height * 0.22, width * 0.78 + height * 0.12, height * 0.22)
        .FillColor(new MagickColor("#2e5e3a")).Ellipse(width * 0.25, height * 1.05, width * 0.45, height * 0.35, 0, 360)
        .FillColor(new MagickColor("#6b3fa0")).Ellipse(width * 0.8, height * 1.1, width * 0.5, height * 0.4, 0, 360)
        .FillColor(new MagickColor("#ffffff")).Rectangle(width * 0.05, height * 0.1, width * 0.4, height * 0.13)
        .FillColor(new MagickColor("#101010")).Rectangle(width * 0.05, height * 0.17, width * 0.33, height * 0.2)
        .Draw(photo);
    return photo;
}

// Waits for a task whose continuations need the UI dispatcher (an await there would wait on itself).
static T Pumped<T>(Task<T> task)
{
    for (var i = 0; i < 1000 && !task.IsCompleted; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
    return task.GetAwaiter().GetResult();
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
