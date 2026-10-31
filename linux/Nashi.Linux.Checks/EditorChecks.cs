using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;
using Nashi.Linux.Editors;

/// The image tools: each engine on real fixtures (exact sizes, pixels moved or changed the expected way, originals
/// untouched, outputs beside the source under awkward names), the editors driven headlessly (drag, Shift, Enter, Esc),
/// and renders of every editor in light, dark and Turkish.
static partial class EditorChecks
{
    // A leading dash, Turkish letters, a colon and spaces: every fixture path has them.
    const string Name = "-ğüş: photo 1";

    static Action<bool, string> check = (_, _) => { };
    static Action<WriteableBitmap?, string> smoke = (_, _) => { };

    public static Task RunAsync(string root, Action<bool, string> check, Action<WriteableBitmap?, string> smoke)
    {
        EditorChecks.check = check;
        EditorChecks.smoke = smoke;
        var work = Directory.CreateDirectory(Path.Combine(root, "editors")).FullName;
        var photo = Quadrants(Path.Combine(work, Name + ".png"), 400, 300);
        var hash = Hash(photo);
        Crop(work, photo);
        Photo(work, photo);
        Frame(work, photo);
        Redact(work, photo);
        Compress(work, photo);
        Metadata(work, photo);
        Qr(work, photo);
        Annotate(work, photo);
        check(Hash(photo).SequenceEqual(hash), "Editors never modify the original");
        return Task.CompletedTask;
    }

    // ---- fixtures and helpers

    /// Red, green, blue and white quadrants: where a pixel ends up says how the image moved.
    public static string Quadrants(string path, int width, int height)
    {
        using var image = new MagickImage(MagickColors.White, (uint)width, (uint)height);
        new ImageMagick.Drawing.Drawables()
            .FillColor(MagickColors.Red).Rectangle(0, 0, width / 2 - 1, height / 2 - 1)
            .FillColor(new MagickColor(0, 255, 0)).Rectangle(width / 2, 0, width - 1, height / 2 - 1)
            .FillColor(MagickColors.Blue).Rectangle(0, height / 2, width / 2 - 1, height - 1)
            .Draw(image);
        using (var file = File.Create(path)) image.Write(file, path.EndsWith(".jpg") ? MagickFormat.Jpeg : MagickFormat.Png);
        return path;
    }

    static byte[] Hash(string path) => System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));

    /// The colour of one pixel of an image file.
    public static (byte R, byte G, byte B) At(string path, int x, int y)
    {
        using var image = PhotoCodec.Read(path);
        return At(image, x, y);
    }

    public static (byte R, byte G, byte B) At(MagickImage image, int x, int y)
    {
        var p = image.GetPixelsUnsafe().GetPixel(x, y);
        return ((byte)p.GetChannel(0), (byte)p.GetChannel(1), (byte)p.GetChannel(2));
    }

    static bool Near((byte R, byte G, byte B) c, int r, int g, int b, int slack = 12) => Math.Abs(c.R - r) <= slack && Math.Abs(c.G - g) <= slack && Math.Abs(c.B - b) <= slack;

    static void Wait(Task task, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        for (var i = 0; i < 4000 && !task.IsCompleted; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
        if (!task.IsCompleted) throw new TimeoutException("A headless editor task did not finish (line " + line + ").");
        task.GetAwaiter().GetResult();
    }

    static T Wait<T>(Task<T> task, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        Wait((Task)task, line);
        return task.Result;
    }

    /// An editor window shown headlessly and loaded.
    static ToolEditorWindow Open(EditorPanel panel)
    {
        var window = new ToolEditorWindow(panel) { Width = 1000, Height = 760 };
        window.Show();
        Wait(window.Loading);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// Window coordinates of a source pixel on the preview.
    static Point On(ToolEditorWindow window, Point image) =>
        window.Canvas.TranslatePoint(window.Canvas.View.Transform(image), window) ?? default;

    static void DragOn(ToolEditorWindow window, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseDown(On(window, from), MouseButton.Left, modifiers);
        window.MouseMove(On(window, new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2)), modifiers | RawInputModifiers.LeftMouseButton);
        window.MouseMove(On(window, to), modifiers | RawInputModifiers.LeftMouseButton);
        window.MouseUp(On(window, to), MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    /// Chooses a petal with the keyboard, turning pages until it shows.
    public static void Choose(WheelWindow wheel, string id)
    {
        for (var i = 0; i < wheel.Wheel.PageCount && !wheel.Wheel.Segments.Any(s => s.Id == id); i++) wheel.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
        wheel.Wheel.HighlightedIndex = wheel.Wheel.Segments.ToList().FindIndex(s => s.Id == id);
        wheel.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    static IReadOnlyList<string> Texts(Window window) => window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();

    static TextBox Field(Window window, int index) => window.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible).ElementAt(index);

    static IReadOnlyList<string> Run(EditorJob job, IReadOnlyList<string> files, string destination) =>
        Wait(job(files, destination, new Progress<OperationProgress>(), CancellationToken.None));

    /// The editor in light, dark and Turkish, saved to NASHI_SMOKE_DIRECTORY.
    static void Renders(string name, Func<EditorPanel> panel, Action<ToolEditorWindow>? prepare = null)
    {
        foreach (var (theme, language, suffix) in new[] { (ThemeVariant.Light, "en", "light"), (ThemeVariant.Dark, "en", "dark"), (ThemeVariant.Light, "tr", "tr") })
        {
            Application.Current!.RequestedThemeVariant = theme;
            L10n.Language = language;
            try
            {
                var window = Open(panel());
                prepare?.Invoke(window);
                Wait(window.RenderNow());
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
                var frame = window.CaptureRenderedFrame();
                check(frame is not null, $"{name} editor renders ({suffix})");
                if (language == "tr") check(!Texts(window).Contains(window.Panel.Primary) && Texts(window).Contains(L10n.T(window.Panel.Primary)), $"Turkish {name} editor translates its Save button");
                smoke(frame, $"editor-{name}-{suffix}");
                window.Close();
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                L10n.Language = "en";
            }
        }
    }

    // ---- Crop

    static void Crop(string work, string photo)
    {
        using (var image = PhotoCodec.Read(photo))
        using (var cropped = ImageTools.Crop(image, new Rect(190, 140, 20.7, 30.2)))
        {
            check(cropped.Width == 20 && cropped.Height == 30, "Crop keeps whole pixels: 20.7 × 30.2 at (190,140) is 20 × 30");
            check(Near(At(cropped, 0, 0), 255, 0, 0) && Near(At(cropped, 19, 0), 0, 255, 0) && Near(At(cropped, 0, 29), 0, 0, 255) && Near(At(cropped, 19, 29), 255, 255, 255), "Crop takes the pixels at its rectangle (the four quadrants meet in it)");
            check(Throws(() => ImageTools.Crop(image, new Rect(350, 0, 100, 10))) && Throws(() => ImageTools.Crop(image, new Rect(-1, 0, 10, 10))), "Crop refuses a rectangle outside the image");
        }

        // The editor: drag to draw, fields follow, Shift squares it, handles resize, inside moves.
        var window = Open(new CropEditor([photo]));
        var crop = (CropEditor)window.Panel;
        check(!window.SaveButton.IsEnabled && Texts(window).Contains("Crop Image") && Texts(window).Contains("Keep the good part.") && Texts(window).Contains("Save cropped copy"), "Crop editor opens with its title, hint and a disabled Save until there is a selection");
        DragOn(window, new Point(40, 30), new Point(240, 130));
        check(crop.Selection is { } s && Math.Abs(s.Width - 200) <= 2 && Math.Abs(s.Height - 100) <= 2, "Dragging on the preview draws the crop (" + crop.Selection + ")");
        check(Field(window, 0).Text == ((int)crop.Selection!.Value.Width).ToString() && Field(window, 1).Text == ((int)crop.Selection.Value.Height).ToString(), "W and H follow the dragged selection");
        check(window.SaveButton.IsEnabled, "A selection enables Save");
        Field(window, 0).Text = "120";
        Dispatcher.UIThread.RunJobs();
        check(crop.Selection!.Value.Width == 120, "Typing W resizes the selection");
        DragOn(window, new Point(20, 20), new Point(220, 90), RawInputModifiers.Shift);
        check(crop.Selection is { } sq && sq.Width == sq.Height && sq.Width >= 190, "Shift locks a new selection to a square (" + crop.Selection + ")");
        var before = crop.Selection!.Value;
        DragOn(window, new Point(before.Right, before.Bottom), new Point(before.Right + 40, before.Bottom + 10), RawInputModifiers.Shift);
        check(crop.Selection is { } kept && Math.Abs(kept.Width - kept.Height) <= 1 && kept.Width > before.Width, "Shift on a corner handle keeps the selection's ratio (" + crop.Selection + ")");
        before = crop.Selection!.Value;
        DragOn(window, before.Center, before.Center + new Point(30, 5));
        check(crop.Selection is { } moved && moved.Width == before.Width && Math.Abs(moved.X - before.X - 30) <= 1, "Dragging inside moves the selection");
        crop.SetAspect("16:9");
        check(crop.Selection is { } wide && Math.Abs(wide.Width / wide.Height - 16 / 9.0) < .02 && wide.Width == 400, "The 16:9 preset is the widest centred 16:9 selection");
        crop.SetAspect("Original");
        check(crop.Selection is { } full && full.Width == 400 && full.Height == 300, "Original is the image's own ratio");
        crop.SetAspect("Free");
        DragOn(window, new Point(10, 10), new Point(110, 60));
        var expected = crop.Selection!.Value;
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        check(!window.IsVisible && window.Job is not null, "Enter saves and closes the crop editor");
        var outputs = Run(window.Job!, [photo], work);
        check(outputs.Count == 1 && Path.GetFileName(outputs[0]) == Name + " - Cropped.png" && Path.GetDirectoryName(outputs[0]) == work, "The cropped copy lands beside the source as “name - Cropped.png”");
        using (var saved = PhotoCodec.Read(outputs[0])) check(saved.Width == (uint)expected.Width && saved.Height == (uint)expected.Height, $"The saved crop is exactly {expected.Width} × {expected.Height}");

        var esc = Open(new CropEditor([photo]));
        DragOn(esc, new Point(10, 10), new Point(100, 100));
        var count = Directory.GetFiles(work).Length;
        esc.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        check(!esc.IsVisible && esc.Job is null && Directory.GetFiles(work).Length == count, "Esc closes the crop editor with nothing saved");

        // One crop for several images must fit inside each one.
        var small = Quadrants(Path.Combine(work, "small.png"), 120, 90);
        var multi = Open(new CropEditor([photo, small]));
        check(Texts(multi).Contains("This crop applies to every selected image and must fit inside each one.") && Texts(multi).Contains("2 files"), "A multi-image crop says it applies to every image");
        DragOn(multi, new Point(0, 0), new Point(200, 150));
        multi.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        check(multi.IsVisible && multi.Job is null && Texts(multi).Any(t => t == "small.png: The crop rectangle must fit completely inside the 120 × 90 pixel image."), "A crop too big for one of the images is refused with the macOS message");
        ((CropEditor)multi.Panel).Set(new Rect(0, 0, 60, 40));
        multi.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var both = Run(multi.Job!, [photo, small], Directory.CreateDirectory(Path.Combine(work, "multi")).FullName);
        check(both.Count == 2 && both.All(o => { using var i = PhotoCodec.Read(o); return i.Width == 60 && i.Height == 40; }), "The crop applies to each image");

        // From the wheel: Crop Image opens the editor; Save runs through the runner and the success card.
        var app = (App)Application.Current!;
        var flowFolder = Directory.CreateDirectory(Path.Combine(work, "flow")).FullName;
        var flowPhoto = Quadrants(Path.Combine(flowFolder, Name + ".jpg"), 400, 300);
        app.Flow.ShowTools([flowPhoto]);
        Dispatcher.UIThread.RunJobs();
        var wheel = app.Flow.LastWheel!;
        Choose(wheel, "cropImage");
        Dispatcher.UIThread.RunJobs();
        var editor = app.Flow.LastEditor;
        check(editor is { IsVisible: true, Owner: null } && editor.Panel is CropEditor, "Crop Image from the pointer wheel opens the crop editor as its own window");
        Wait(editor!.Loading);
        DragOn(editor, new Point(100, 50), new Point(300, 250));
        editor.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        string? Title() => SuccessToast.Current?.FindControl<TextBlock>("TitleText")?.Text;
        // A synchronous pump: an await here could resume on a context nobody pumps.
        for (var i = 0; i < 300 && Title()?.StartsWith("Nicely") != true; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
        var flowOut = Path.Combine(flowFolder, Name + " - Cropped.jpg");
        check(Title() == "Nicely done. 1 file is ready." && File.Exists(flowOut) && Near(At(flowOut, 5, 5), 255, 0, 0, 40) && Near(At(flowOut, 195, 195), 255, 255, 255, 40), "Saving from the wheel's editor writes the JPG crop beside the source and shows the success card");
        SuccessToast.Current?.Close();

        Renders("crop", () => new CropEditor([photo]), w => { DragOn(w, new Point(60, 40), new Point(300, 220)); });
    }

    static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }
}
