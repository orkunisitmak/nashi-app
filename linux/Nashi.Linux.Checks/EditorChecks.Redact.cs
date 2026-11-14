using Avalonia;
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
    // ---- Redact Photo

    /// The spread of brightness in an area, sampled every 4 px (0 for flat, about 127 for black text on white).
    static double Deviation(MagickImage image, Rect area)
    {
        var values = new List<double>();
        for (var y = (int)area.Y; y < area.Bottom; y += 4)
            for (var x = (int)area.X; x < area.Right; x += 4) { var c = At(image, x, y); values.Add(.2126 * c.R + .7152 * c.G + .0722 * c.B); }
        var mean = values.Average();
        return Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
    }

    // Text a tenth of a 4000 px image's height must not survive blur or pixelate.
    static void Legibility(string work)
    {
        using var page = new MagickImage(MagickColors.White, 1500, 4000);
        using var text = ImageTools.Annotate(page, [new Mark("text", new Rect(100, 1800, 1300, 480)) { Text = "NASHI", FontSize = 400, Color = "#000000" }]);
        var area = new Rect(80, 1750, 1340, 560);
        var before = Deviation(text, area);
        using var blurred = ImageTools.Redact(text, new RedactOptions { Style = "blur", Areas = [area] });
        using var blocks = ImageTools.Redact(text, new RedactOptions { Style = "pixelate", Areas = [area] });
        var (blur, pixel) = (Deviation(blurred, area), Deviation(blocks, area));
        // Blur leaves a near-flat grey; pixelate leaves three rows of blocks over the letters (the spread that is left is
        // mostly the white margin against the averaged text).
        check(before > 90 && blur < 20 && pixel < 55, $"400 px text in a 4000 px image is unreadable after blur and pixelate: brightness spread {before:0} → {blur:0.0} and {pixel:0.0}");
    }

    static void Redact(string work, string photo)
    {
        // A noisy fixture: blur and pixelate must change it, and only inside the areas.
        var noisy = Path.Combine(work, Name + " noisy.png");
        var bytes = new byte[200 * 100 * 3];
        new Random(7).NextBytes(bytes);
        using (var n = new MagickImage()) { n.ReadPixels(bytes, new PixelReadSettings(200, 100, StorageType.Char, PixelMapping.RGB)); using var file = File.Create(noisy); n.Write(file, MagickFormat.Png); }
        using var source = PhotoCodec.Read(noisy);
        var area = new Rect(20, 10, 60.5, 40.2);
        bool Outside(MagickImage result)
        {
            for (var y = 0; y < 100; y += 3)
                for (var x = 0; x < 200; x += 3)
                    if ((x < 20 || x >= 81 || y < 10 || y >= 51) && At(result, x, y) != At(source, x, y)) return false;
            return true;
        }
        using (var solid = ImageTools.Redact(source, new RedactOptions { Areas = [area], Color = "#102030" }))
        {
            var inside = Enumerable.Range(0, 40).All(i => At(solid, 20 + i, 10 + i) == ((byte)0x10, (byte)0x20, (byte)0x30)) && At(solid, 80, 50) == ((byte)0x10, (byte)0x20, (byte)0x30);
            check(inside && Outside(solid), "Solid covers the whole area (its partial pixels too) in the colour and leaves the rest untouched");
        }
        using (var blurred = ImageTools.Redact(source, new RedactOptions { Style = "blur", Areas = [area] }))
        {
            int Spread(MagickImage image) { var values = Enumerable.Range(0, 30).Select(i => (int)At(image, 35 + i, 30).R).ToList(); return values.Max() - values.Min(); }
            check(Spread(blurred) < Spread(source) / 3 && Outside(blurred), "Blur smooths the area and leaves the rest untouched");
        }
        using (var pixelated = ImageTools.Redact(source, new RedactOptions { Style = "pixelate", PixelSize = 10, Areas = [area] }))
        {
            var block = Enumerable.Range(0, 8).All(i => At(pixelated, 20 + i, 11) == At(pixelated, 20, 11));
            var changed = Enumerable.Range(0, 8).Count(i => At(pixelated, 20 + i, 11) != At(source, 20 + i, 11)) >= 7;
            check(block && changed && Outside(pixelated), "Pixelate fills the area with blocks and leaves the rest untouched");
        }
        Legibility(work);
        check(Throws(() => ImageTools.Redact(source, new RedactOptions { Areas = [new Rect(190, 0, 20, 10)] })) && Throws(() => ImageTools.Redact(source, new RedactOptions())), "Redact refuses an area outside the image and an empty list");

        var window = Open(new RedactEditor([photo]));
        var editor = (RedactEditor)window.Panel;
        check(!window.SaveButton.IsEnabled && Texts(window).Contains("0 areas selected") && Texts(window).Contains("Pixelate"), "Redact Photo starts with no areas and Save off");
        DragOn(window, new Point(10, 10), new Point(110, 60));
        DragOn(window, new Point(250, 200), new Point(350, 280));
        check(editor.Areas.Count == 2 && Texts(window).Contains("2 areas selected") && window.SaveButton.IsEnabled, "Dragging adds areas and enables Save");
        var first = editor.Areas[0];
        DragOn(window, first.Center, first.Center + new Point(20, 10));
        check(editor.Selected == 0 && Math.Abs(editor.Areas[0].X - first.X - 20) <= 1 && Math.Abs(editor.Areas[0].Y - first.Y - 10) <= 1, "Dragging an area selects and moves it");
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        check(editor.Areas.Count == 1 && Texts(window).Contains("1 area selected"), "Delete removes the selected area");
        editor.SetStyle("pixelate");
        Wait(window.RenderNow());
        check(window.Panel.Preview() is not null, "Pixelate renders a preview");
        editor.SetStyle("solid");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var kept = editor.Areas[0];
        var outputs = Run(window.Job!, [photo], Directory.CreateDirectory(Path.Combine(work, "redacted")).FullName);
        check(Path.GetFileName(outputs[0]) == Name + " - Redacted.png" && Near(At(outputs[0], 5, 5), 255, 0, 0, 0) && Near(At(outputs[0], (int)kept.Center.X, (int)kept.Center.Y), 0, 0, 0, 0), "Redact Photo saves “name - Redacted.png”: black in the area, the rest untouched");

        Renders("redact", () => new RedactEditor([photo]), w => { DragOn(w, new Point(30, 30), new Point(180, 120)); DragOn(w, new Point(220, 160), new Point(380, 280)); });
    }
}
