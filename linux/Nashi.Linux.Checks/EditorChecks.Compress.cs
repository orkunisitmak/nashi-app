using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;
using Nashi.Linux.Editors;

static partial class EditorChecks
{
    // ---- Compress

    static void Compress(string work, string photo)
    {
        var noisy = Path.Combine(work, Name + " noise.jpg");
        var bytes = new byte[600 * 400 * 3];
        new Random(3).NextBytes(bytes);
        using (var n = new MagickImage()) { n.ReadPixels(bytes, new PixelReadSettings(600, 400, StorageType.Char, PixelMapping.RGB)); n.Quality = 100; using var file = File.Create(noisy); n.Write(file, MagickFormat.Jpeg); }
        var original = new FileInfo(noisy).Length;
        string Out(string name) => Directory.CreateDirectory(Path.Combine(work, "compress-" + name)).FullName;
        long Size(IReadOnlyList<string> files) => new FileInfo(files[0]).Length;

        var balanced = ImageOperations.Compress([noisy], Out("balanced"), null, default);
        var strong = ImageOperations.Compress([noisy], Out("strong"), null, default, new CompressOptions { Quality = 45 });
        check(Path.GetFileName(balanced[0]) == Name + " noise - Compressed.jpg" && Size(balanced) < original && Size(strong) < Size(balanced), "Compress names “name - Compressed.jpg”, and Strong comes out smaller than Balanced");
        var target = ImageOperations.Compress([noisy], Out("target"), null, default, new CompressOptions { TargetBytes = 40_000 });
        check(Size(target) <= 40_000, "A 40 KB target is met (" + Size(target) + " bytes)");
        var half = ImageOperations.Compress([noisy], Out("percent"), null, default, new CompressOptions { ReductionPercent = 50 });
        check(Size(half) <= original / 2, "A 50 % reduction halves the file at least");
        var scaled = ImageOperations.Compress([noisy], Out("scaled"), null, default, new CompressOptions { Scale = .5 });
        var edge = ImageOperations.Compress([noisy], Out("edge"), null, default, new CompressOptions { LongestEdge = 300 });
        using (var s = PhotoCodec.Read(scaled[0])) using (var e = PhotoCodec.Read(edge[0])) check(s.Width == 300 && s.Height == 200 && e.Width == 300 && e.Height == 200, "The 50 % and longest-edge presets size the copy");
        var png = Path.Combine(work, Name + " big.png");
        using (var n = new MagickImage()) { n.ReadPixels(bytes, new PixelReadSettings(600, 400, StorageType.Char, PixelMapping.RGB)); using var file = File.Create(png); n.Write(file, MagickFormat.Png); }
        var pngTarget = ImageOperations.Compress([png], Out("png"), null, default, new CompressOptions { TargetBytes = 60_000 });
        using (var p = PhotoCodec.Read(pngTarget[0])) check(new FileInfo(pngTarget[0]).Length <= 60_000 && pngTarget[0].EndsWith(" - Compressed.png") && p.Width < 600, "A PNG target is met by shrinking it, still a PNG");
        check(Throws(() => ImageOperations.Compress([noisy], Out("bad"), null, default, new CompressOptions { ReductionPercent = 100 })), "A 100 % reduction is refused");

        // The editor: options only, presets, a target, Save.
        var window = Open(new CompressEditor([noisy]));
        var editor = (CompressEditor)window.Panel;
        check(window.Canvas.GetVisualRoot() is null && Texts(window).Contains("Compression preset") && Texts(window).Any(t => t.StartsWith("Original size: ")) && Texts(window).Contains("Compress & save"), "Compress opens with its options only: the original size, presets and Compress & save");
        var strongButton = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Strong");
        strongButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        check(editor.Settings.Quality == 45 && Texts(window).Contains("Smaller files with stronger compression."), "Strong sets quality 45 and says so");
        editor.Target(120, "percent");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        check(window.IsVisible && Texts(window).Contains("Enter a reduction greater than 0% and less than 100%."), "A reduction of 120 % is refused before saving");
        editor.Target(30, "KB");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var outputs = Run(window.Job!, [noisy], Out("editor"));
        check(outputs.Count == 1 && new FileInfo(outputs[0]).Length <= 30_000, "The editor's 30 KB target is met");
        var two = Run(new CompressEditor([noisy, png]).Job(), [noisy, png], Out("two"));
        check(two.Count == 2 && two.All(f => f.Contains(" - Compressed.")), "Compressing two images makes two copies");

        Renders("compress", () => new CompressEditor([noisy]));
    }
}
