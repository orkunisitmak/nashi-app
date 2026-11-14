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
    // ---- Edit Photo

    static double Mean(MagickImage image, int channel)
    {
        var pixels = image.GetPixelsUnsafe().ToByteArray(PixelMapping.RGB)!;
        double sum = 0;
        for (var i = channel; i < pixels.Length; i += 3) sum += pixels[i];
        return sum / (pixels.Length / 3);
    }

    static void Photo(string work, string photo)
    {
        var gray = Path.Combine(work, "gray.png");
        using (var g = new MagickImage(new MagickColor(110, 120, 130), 60, 40)) using (var file = File.Create(gray)) g.Write(file, MagickFormat.Png);
        using var plain = PhotoCodec.Read(gray);
        MagickImage Edit(PhotoAdjustments a) => ImageTools.Edit(plain, new PhotoOptions { Adjustments = a });
        using (var brighter = Edit(new() { Exposure = 1 })) check(Mean(brighter, 0) > Mean(plain, 0) + 40 && Mean(brighter, 2) > Mean(plain, 2) + 40, "Exposure +1 EV brightens every channel");
        using (var warmer = Edit(new() { Temperature = 1 })) check(Mean(warmer, 0) > Mean(plain, 0) + 10 && Mean(warmer, 2) < Mean(plain, 2) - 10, "Temperature +1 warms: more red, less blue");
        using (var muted = Edit(new() { Saturation = 0 })) check(Math.Abs(Mean(muted, 0) - Mean(muted, 2)) < 3, "Saturation 0 turns the colour grey");
        using (var darker = Edit(new() { Brightness = -.5 })) check(Mean(darker, 1) < Mean(plain, 1) - 20, "Brightness −0.5 darkens");
        using (var hue = Edit(new() { Hue = 150 })) check(Mean(hue, 0) > Mean(plain, 0) + 5 || Mean(hue, 1) < Mean(plain, 1) - 5, "Hue beyond ±100 (macOS ±180°) turns the colour instead of being refused");

        using var quads = PhotoCodec.Read(photo);
        using (var turned = ImageTools.Edit(quads, new PhotoOptions { Rotation = 90 }))
            check(turned.Width == 300 && turned.Height == 400 && Near(At(turned, 5, 5), 0, 0, 255) && Near(At(turned, 295, 5), 255, 0, 0), "Rotate Right turns clockwise: blue goes top left, red top right");
        using (var left = ImageTools.Edit(quads, new PhotoOptions { Rotation = -90 })) check(Near(At(left, 5, 5), 0, 255, 0), "Rotate Left turns counter-clockwise: green goes top left");
        using (var mirrored = ImageTools.Edit(quads, new PhotoOptions { FlipHorizontal = true })) check(Near(At(mirrored, 5, 5), 0, 255, 0) && Near(At(mirrored, 395, 295), 0, 0, 255), "Flip horizontally mirrors left and right");
        using (var flipped = ImageTools.Edit(quads, new PhotoOptions { FlipVertical = true })) check(Near(At(flipped, 5, 5), 0, 0, 255), "Flip vertically mirrors top and bottom");
        using (var half = ImageTools.Edit(quads, new PhotoOptions { Scale = .5 })) check(half.Width == 200 && half.Height == 150, "The 50% preset halves each side");
        using (var edge = ImageTools.Edit(quads, new PhotoOptions { LongestEdge = 100 })) check(edge.Width == 100 && edge.Height == 75, "A longest edge of 100 makes 100 × 75");
        using (var sized = ImageTools.Edit(quads, new PhotoOptions { Rotation = 90, Width = 30, Height = 40 })) check(sized.Width == 30 && sized.Height == 40, "Width and height describe the finished, turned image");

        // The editor: sliders, rotate and flip drive the preview; Save writes " - Edited".
        var window = Open(new PhotoEditor([photo]));
        var editor = (PhotoEditor)window.Panel;
        check(Texts(window).Contains("Lighting") && Texts(window).Contains("Color") && Texts(window).Contains("Detail") && Texts(window).Contains("Exposure") && Texts(window).Contains("Noise reduction"), "Edit Photo shows the macOS light, colour and detail sliders");
        check(window.GetVisualDescendants().OfType<Slider>().Count() == 22, "Edit Photo has the 22 macOS adjustments");
        editor.Rotate(90);
        editor.Flip(horizontal: true);
        editor.Adjust("Exposure", 1);
        Wait(window.RenderNow());
        Dispatcher.UIThread.RunJobs();
        check(Texts(window).Contains("300 × 400"), "The preview shows the turned size");
        check(Texts(window).Contains("Result") && Texts(window).Contains("Original"), "Edit Photo offers Original / Result");
        editor.Size("50");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var outputs = Run(window.Job!, [photo], Directory.CreateDirectory(Path.Combine(work, "edited")).FullName);
        check(outputs.Count == 1 && Path.GetFileName(outputs[0]) == Name + " - Edited.png", "Edit Photo saves “name - Edited.png”");
        using (var saved = PhotoCodec.Read(outputs[0]))
            check(saved.Width == 150 && saved.Height == 200 && At(saved, 145, 5) is { G: > 200, R: < 60, B: < 60 } && At(saved, 5, 5) is { R: > 240, G: > 240, B: > 240 }, "The edited copy is mirrored, then turned, and halved: green top right, white top left (" + At(saved, 145, 5) + ")");

        Renders("edit", () => new PhotoEditor([photo]), w => ((PhotoEditor)w.Panel).Adjust("Saturation", -.6));
    }
}
