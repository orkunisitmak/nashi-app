using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;

/// The one wheel's faces: group fan-out (geometry and timing), the flip order, and every quick tool on a fixture
/// (cases after Filee's DonutGroupTests, DonutFlipTests and BackgroundRemovalTests).
static class FaceChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Avalonia.Media.Imaging.WriteableBitmap?, string> smoke)
    {
        // Geometry: a fan of 5 around the top petal of 8 spans its petal and a quarter of each neighbour, at least 44 points a petal.
        var span = Wheel.SubSpan(45, 5);
        check(Math.Abs(span - Math.Max(45 * 1.5 / 5, 44 / ((Wheel.SubInner + Wheel.SubOuter) / 2) * 180 / Math.PI)) < 1e-9 && Wheel.SubSpan(45, 40) == 9, "A group's ring: each petal shares the fan, at least 44 points wide, never more than once round");
        var top = 90.0;
        bool AllFound()
        {
            for (var k = 0; k < 5; k++)
            {
                var mid = Wheel.SubMid(top, k, 5, span) * Math.PI / 180;
                var r = (Wheel.SubInner + Wheel.SubOuter) / 2;
                if (Wheel.SubIndexAt(new Point(160 + r * Math.Cos(mid), 160 - r * Math.Sin(mid)), top, 5, span) != k) return false;
            }
            return true;
        }
        check(AllFound() && Wheel.SubMid(top, 0, 5, span) > Wheel.SubMid(top, 4, 5, span), "Each sub petal's middle hits that petal, in clockwise order");
        check(Wheel.SubIndexAt(new Point(160, 160 - Wheel.Radius + 10), top, 5, span) == -1 && Wheel.SubIndexAt(new Point(160, 160 - Wheel.SubOuter - 5), top, 5, span) == -1, "Inside the main ring or past the outer ring is no sub petal");

        // Timing on an injected clock: resting 250 ms on a group fans it out; passing over does not.
        var now = new DateTime(2026, 1, 1);
        var ticks = 0;
        WheelFeedback.Player = _ => ticks++;
        WheelItem? chosen = null;
        try
        {
            var formats = new[] { "mp4", "mov", "mkv", "webm" }.Select(f => new WheelItem("convert:" + f, f.ToUpperInvariant())).ToList();
            var window = new WheelWindow("", [new("group:video", "Video", Group: formats), new("convert:mp3", "MP3"), new("convert:wav", "WAV"), new("side:1", "Quick tools ›")]);
            var wheel = window.Wheel;
            wheel.Clock = () => now;
            wheel.Chosen += item => chosen = item;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var b = wheel.Bounds;
            Point Ring(double r, double degrees) => new(b.Width / 2 + r * Math.Cos(degrees * Math.PI / 180), b.Height / 2 - r * Math.Sin(degrees * Math.PI / 180));
            var onGroup = Ring(100, 90);
            ticks = 0;
            wheel.HighlightAt(onGroup);
            now += TimeSpan.FromMilliseconds(100);
            wheel.Rest();
            check(wheel.OpenGroupIndex == -1 && wheel.HighlightedIndex == 0, "100 ms on a group petal: highlighted, not open yet");
            wheel.HighlightAt(Ring(100, 0)); // passing on to MP3
            now += TimeSpan.FromMilliseconds(300);
            wheel.Rest();
            check(wheel.OpenGroupIndex == -1 && wheel.HighlightedIndex == 1, "Passing over a group on the way elsewhere doesn't open it");
            wheel.HighlightAt(onGroup);
            var before = ticks;
            now += TimeSpan.FromMilliseconds(260);
            wheel.Rest();
            check(Wheel.GroupDelay == TimeSpan.FromMilliseconds(250) && wheel.OpenGroupIndex == 0 && wheel.SubItems.Count == 4 && ticks == before + 1, "Resting 250 ms fans out the group's second ring, with a tick");
            window.CaptureRenderedFrame();
            smoke(window.CaptureRenderedFrame(), "wheel-group-open");
            var subSpan = Wheel.SubSpan(90, 4);
            Point Sub(int k) => Ring((Wheel.SubInner + Wheel.SubOuter) / 2, Wheel.SubMid(90, k, 4, subSpan));
            before = ticks;
            wheel.HighlightAt(Sub(0));
            wheel.HighlightAt(Sub(2));
            wheel.HighlightAt(Ring(Wheel.Radius + 3, 90)); // the gap between the rings keeps it
            check(wheel.SubHighlightedIndex == -1 && wheel.OpenGroupIndex == 0 && ticks == before + 2, "Each outer petal the pointer reaches ticks; the gap keeps the ring open");
            wheel.HighlightAt(Sub(1));
            check(window.Wheel.SubAt(Sub(1)) == 1 && wheel.ChooseAt(Sub(1)) && chosen?.Id == "convert:mov", "A drop or click on an outer petal runs its format");
            window.Close();
            Dispatcher.UIThread.RunJobs();

            var again = new WheelWindow("", [new("group:video", "Video", Group: formats), new("convert:mp3", "MP3"), new("convert:wav", "WAV"), new("side:1", "Quick tools ›")]);
            again.Wheel.Clock = () => now;
            again.Show();
            Dispatcher.UIThread.RunJobs();
            again.Wheel.OpenGroup(0);
            again.Wheel.HighlightAt(Ring(100, 0));
            check(again.Wheel.OpenGroupIndex == -1 && again.Wheel.HighlightedIndex == 1, "Another petal folds the ring away");
            again.Wheel.OpenGroup(0);
            again.Wheel.HighlightAt(new Point(b.Width / 2, b.Height / 2));
            check(again.Wheel.OpenGroupIndex == -1, "The hole folds it away too");
            again.Wheel.Focus();
            again.Wheel.HighlightedIndex = 0;
            again.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            again.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            again.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            chosen = null;
            again.Wheel.Chosen += item => chosen = item;
            again.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            check(chosen?.Id == "convert:mov", "Keys: Enter opens a group, arrows move along its ring, Enter chooses");
            again.Close();
        }
        finally { WheelFeedback.Player = _ => { }; }

        // The faces of the real wheel: groups when the formats don't fit, and the flip order both ways.
        var video = WheelFlow.Sides(["x.pdf"]);
        check(video[0].Items.Any(i => i.Group is { Count: > 1 }) && video[0].Items.Where(i => !i.Id.StartsWith("side:")).SelectMany(i => i.Group ?? [i]).Count() == Capabilities.Formats(["x.pdf"]).Length
            && WheelFlow.Sides(["x.mp4"])[0].Items.All(i => i.Group is null), "A PDF's many formats are grouped (Images, Documents…) and every one is still there; a video's seven fit as they are");
        check(video[0].Items.Where(i => i.Group is not null).All(i => i.Title is "Video" or "Audio" or "Images" or "Documents" or "Archives"), "Groups are named after catalog categories");
        var sides = WheelFlow.Sides(["a.png", "b.png"]);
        check(sides[1].Items.Select(i => i.Id).SequenceEqual(["cropImage", "quick:rotate", "quick:flip", "quick:grayscale", "quick:removeBackground", "createPDF", "quick:zip", "side:2"])
            && WheelFlow.Sides(["a.pdf", "b.pdf"]).Single(s => s.Key == "quick").Items.Select(i => i.Id).SequenceEqual(["mergePDF", "quick:zip", "side:2"]),
            "The quick face: crop, rotate, flip, grayscale, remove background, one PDF (images or PDFs), one ZIP");
        var flip = new Wheel { Sides = sides };
        var order = new List<string>();
        for (var i = 0; i < 12 && order.Count < 5; i++) { if (order.LastOrDefault() != sides[flip.Side].Key) order.Add(sides[flip.Side].Key); flip.Flip(1); }
        flip.StartAt(0);
        flip.Flip(-1);
        check(order.SequenceEqual(["convert", "quick", "tools", "actions", "convert"]) && sides[flip.Side].Key == "actions", "Tab turns Convert → Quick tools → Tools → Actions → Convert; Shift+Tab turns back");

        // Each quick tool on a fixture, beside the source.
        var work = Directory.CreateDirectory(Path.Combine(root, "quick tools ş")).FullName;
        var halves = Path.Combine(work, "-halves.png");
        using (var image = new MagickImage(MagickColors.Blue, 40, 20))
        {
            image.Draw(new ImageMagick.Drawing.Drawables().FillColor(MagickColors.Red).Rectangle(0, 0, 19, 19));
            image.Write(halves);
        }
        var runner = new OperationRunner(new Toolchain(Path.Combine(work, "none"), ""));
        IReadOnlyList<string> Run(string id, params string[] files) =>
            Wait(runner.RunBesideSourcesAsync(id, files, f => Task.FromResult<string?>(f), new Progress<OperationProgress>(), CancellationToken.None))!;
        IMagickColor<byte> At(string file, int x, int y) { using var image = new MagickImage(file); return image.GetPixels().GetPixel(x, y).ToColor()!; }
        bool Red(IMagickColor<byte> c) => c.R > 200 && c.B < 60;

        var rotated = Run("quick:rotate", halves);
        using (var image = new MagickImage(rotated[0]))
            check(rotated is [var r] && Path.GetDirectoryName(r) == work && Path.GetFileName(r) == "-halves - Edited.png" && image.Width == 20 && image.Height == 40 && Red(At(r, 10, 5)) && !Red(At(r, 10, 35)),
                "Rotate 90° turns it clockwise (the left half on top), saved beside it as - Edited");
        var flipped = Run("quick:flip", halves);
        check(!Red(At(flipped[0], 5, 10)) && Red(At(flipped[0], 35, 10)), "Flip mirrors it left to right");
        var gray = Run("quick:grayscale", halves);
        var g = At(gray[0], 5, 10);
        check(Math.Abs(g.R - g.G) <= 1 && Math.Abs(g.G - g.B) <= 1, "Grayscale leaves no saturation");

        var logo = Path.Combine(work, "logo.jpg");
        using (var image = new MagickImage(MagickColors.White, 80, 80))
        {
            image.Draw(new ImageMagick.Drawing.Drawables().FillColor(MagickColors.Black).Rectangle(25, 25, 55, 55));
            image.Write(logo);
        }
        var cut = Run("quick:removeBackground", logo);
        using (var image = new MagickImage(cut[0]))
        {
            var pixels = image.GetPixels();
            check(Path.GetFileName(cut[0]) == "logo - No Background.png" && image.HasAlpha && pixels.GetPixel(0, 0).ToColor()!.A == 0 && pixels.GetPixel(40, 40).ToColor()!.A == 255,
                "Remove background makes the plain backdrop transparent (alpha 0 at the border, 255 on the ink), as a PNG beside it");
        }
        var zip = Run("quick:zip", halves, logo, rotated[0]);
        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip[0]))
            check(zip is [var z] && Path.GetDirectoryName(z) == work && archive.Entries.Select(e => e.Name).Order().SequenceEqual(new[] { "-halves.png", "logo.jpg", "-halves - Edited.png" }.Order()), "Pack into ZIP puts every file into one archive beside them");
        var pdf = Run("createPDF", halves, logo);
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(pdf[0]))
            check(pdf.Count == 1 && doc.NumberOfPages == 2 && Path.GetDirectoryName(pdf[0]) == work, "Images merged into one PDF: one page each, beside them");
        check(!Capabilities.QuickTools(["a.mp4"]).Contains("quick:rotate") && Capabilities.QuickTools(["a.mp4"]).Contains("quick:zip"), "Image quick tools are only for images; ZIP is for anything");

        // Renders: the quick face and a grouped front face, light, dark and Turkish.
        foreach (var (theme, language, suffix) in new[] { (ThemeVariant.Light, "en", "light"), (ThemeVariant.Dark, "en", "dark"), (ThemeVariant.Light, "tr", "tr") })
        {
            Application.Current!.RequestedThemeVariant = theme;
            L10n.Language = language;
            try
            {
                foreach (var (files, face, name) in new[] { (new[] { halves, logo }, 1, "quick"), (new[] { Path.Combine(work, "x.pdf") }, 0, "grouped") })
                {
                    var w = new WheelWindow("", []);
                    w.Wheel.Sides = WheelFlow.Sides(files);
                    w.Wheel.StartAt(face);
                    w.Show();
                    Dispatcher.UIThread.RunJobs();
                    if (name == "grouped") w.Wheel.OpenGroup(w.Wheel.Items.ToList().FindIndex(i => i.Group is not null));
                    w.CaptureRenderedFrame();
                    var frame = w.CaptureRenderedFrame();
                    check(frame is not null, $"The {name} face renders ({suffix})");
                    smoke(frame, $"wheel-{name}-{suffix}");
                    w.Close();
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                L10n.Language = "en";
            }
        }
    }

    static T Wait<T>(Task<T> task)
    {
        for (var i = 0; i < 3000 && !task.IsCompleted; i++) { Thread.Sleep(2); Dispatcher.UIThread.RunJobs(); }
        return task.GetAwaiter().GetResult();
    }
}
