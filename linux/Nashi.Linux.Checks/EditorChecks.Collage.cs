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
    // ---- Create Collage

    static string Solid(string path, MagickColor color, int width, int height)
    {
        using var image = new MagickImage(color, (uint)width, (uint)height);
        using var file = File.Create(path);
        image.Write(file, MagickFormat.Png);
        return path;
    }

    static void Collage(string work, string photo)
    {
        var folder = Directory.CreateDirectory(Path.Combine(work, "collage")).FullName;
        var red = Solid(Path.Combine(folder, Name + " red.png"), MagickColors.Red, 300, 200);
        var blue = Solid(Path.Combine(folder, "blue.png"), MagickColors.Blue, 200, 300);
        var yellow = Solid(Path.Combine(folder, "yellow.png"), MagickColors.Yellow, 100, 100);
        var hashes = new[] { red, blue, yellow }.Select(Hash).ToList();
        using var r = PhotoCodec.Read(red);
        using var b = PhotoCodec.Read(blue);
        using var y = PhotoCodec.Read(yellow);
        using (var square = ImageTools.Collage([r, b], new CollageOptions { Spacing = 0, Padding = 0 }))
            check(square.Width == 2048 && square.Height == 2048 && Near(At(square, 500, 1000), 255, 0, 0, 0) && Near(At(square, 1500, 1000), 0, 0, 255, 0), "A grid collage is 2048 × 2048 with the images in order, left to right");
        using (var wide = ImageTools.Collage([r, b], new CollageOptions { AspectRatio = 16 / 9.0, Order = [1, 0] }))
            check(wide.Width == 2048 && wide.Height == 1152 && Near(At(wide, 500, 576), 0, 0, 255, 0) && Near(At(wide, 1500, 576), 255, 0, 0, 0), "16:9 makes 2048 × 1152, and the order puts blue first");
        using (var fit = ImageTools.Collage([r, b], new CollageOptions { Fill = false, Spacing = 0, Padding = 0, Color = "#000000" }))
            check(Near(At(fit, 500, 100), 0, 0, 0, 0) && Near(At(fit, 500, 1000), 255, 0, 0, 0), "Fit keeps each whole image, with the background around it");
        using (var column = ImageTools.Collage([r, b, y], new CollageOptions { Layout = "column", Spacing = 0, Padding = 0, Width = 300, AspectRatio = 1 }))
            check(Near(At(column, 150, 50), 255, 0, 0, 0) && Near(At(column, 150, 150), 0, 0, 255, 0) && Near(At(column, 150, 250), 255, 255, 0, 0), "Column stacks the images top to bottom");
        var cells = ImageTools.CollageCells(3, "featured", 2, new Rect(0, 0, 900, 600), 0);
        check(cells[0] == new Rect(0, 0, 600, 600) && cells[1] == new Rect(600, 0, 300, 300) && cells[2] == new Rect(600, 300, 300, 300), "Featured gives the first image the left two thirds");
        check(ImageTools.CollageCells(3, "row", 2, new Rect(0, 0, 300, 100), 0).Select(c => c.X).SequenceEqual([0, 100, 200]), "Row lays the images side by side");
        check(Throws(() => ImageTools.Collage([r], new CollageOptions())) && Throws(() => ImageTools.Collage([r, b], new CollageOptions { Order = [0, 0] })), "A collage needs two images and a real order");

        var window = Open(new CollageEditor([red, blue, yellow]));
        var editor = (CollageEditor)window.Panel;
        Wait(window.RenderNow());
        Dispatcher.UIThread.RunJobs();
        check(Texts(window).Contains("Better together.") && Texts(window).Contains("2048 × 2048") && Texts(window).Contains("3 files") && Texts(window).Contains(Name + " red.png"), "Create Collage shows the canvas size and the images in order");
        // Drag the third row onto the first.
        var list = window.GetVisualDescendants().OfType<Border>().Where(x => x.Child is Grid g && g.Children.OfType<TextBlock>().Any(t => t.Text == "yellow.png")).First();
        var rows = (StackPanel)list.Parent!;
        rows.BringIntoView(); // the list sits below the fold of the options
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Point RowAt(int row) => rows.TranslatePoint(new Point(60, row * 34 + 17), window) ?? default;
        window.MouseDown(RowAt(2), MouseButton.Left);
        window.MouseMove(RowAt(1), RawInputModifiers.LeftMouseButton);
        window.MouseMove(RowAt(0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(RowAt(0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        check(editor.Inputs.SequenceEqual([yellow, red, blue]), "Dragging a row reorders the collage");
        editor.Move(2, 1);
        check(editor.Inputs.SequenceEqual([yellow, blue, red]), "The arrows' move works too");
        editor.Settings = editor.Settings with { Layout = "row", Spacing = 0, Padding = 0, AspectRatio = 16 / 9.0 };
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var outputs = Run(window.Job!, editor.Inputs, folder);
        check(outputs.Count == 1 && Path.GetFileName(outputs[0]) == "yellow - Collage.png" && Path.GetDirectoryName(outputs[0]) == folder, "One “first - Collage.png” beside the first image");
        using (var saved = PhotoCodec.Read(outputs[0]))
            check(saved.Width == 2048 && saved.Height == 1152 && Near(At(saved, 300, 576), 255, 255, 0, 0) && Near(At(saved, 1000, 576), 0, 0, 255, 0) && Near(At(saved, 1700, 576), 255, 0, 0, 0), "The saved collage has the chosen layout, size and order");
        check(new[] { red, blue, yellow }.Select(Hash).Zip(hashes).All(p => p.First.SequenceEqual(p.Second)), "The collage leaves its images untouched");

        Renders("collage", () => new CollageEditor([red, blue, yellow, photo]));
    }
}
