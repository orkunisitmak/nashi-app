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
    // ---- Add BG

    static void Frame(string work, string photo)
    {
        using var quads = PhotoCodec.Read(photo);
        using (var framed = ImageTools.Frame(quads, new FrameOptions { Padding = 50, Color = "#000000", ShadowRadius = 0, CornerRadius = 0 }))
        {
            check(framed.Width == 500 && framed.Height == 400, "Add BG makes the image plus twice the padding (400 × 300 + 2 × 50)");
            check(Near(At(framed, 10, 10), 0, 0, 0) && Near(At(framed, 55, 55), 255, 0, 0) && Near(At(framed, 444, 344), 255, 255, 255), "The photo sits centred on the solid background");
        }
        using (var rounded = ImageTools.Frame(quads, new FrameOptions { Padding = 20, Color = "#000000", ShadowRadius = 0, CornerRadius = 40 }))
            check(Near(At(rounded, 21, 21), 0, 0, 0) && Near(At(rounded, 60, 60), 255, 0, 0), "Rounded corners show the background at the photo's corner");
        using (var shadowed = ImageTools.Frame(quads, new FrameOptions { Padding = 60, Color = "#FFFFFF", ShadowRadius = 40, ShadowOpacity = .5 }))
            check(At(shadowed, 250, 365).R < 235 && Near(At(shadowed, 5, 5), 255, 255, 255, 2), "The shadow darkens the background just under the photo, not the corners");
        using (var gradient = ImageTools.Frame(quads, new FrameOptions { Backdrop = "gradient", Gradient = ["#FF0000", "#0000FF"], Padding = 40, ShadowRadius = 0 }))
            check(At(gradient, 3, 376).R > 200 && At(gradient, 476, 3).B > 200, "A 45° gradient runs from bottom left to top right");
        using (var square = ImageTools.Frame(quads, new FrameOptions { Padding = 0, AspectRatio = 1, ShadowRadius = 0, CornerRadius = 0, X = 0, Y = 0 }))
            check(square.Width == 400 && square.Height == 400 && Near(At(square, 5, 5), 255, 0, 0) && Near(At(square, 5, 395), 255, 255, 255), "A 1:1 canvas grows the short side; position 0,0 puts the photo top left");
        var backdrop = Quadrants(Path.Combine(work, "backdrop.png"), 40, 40);
        using (var imaged = ImageTools.Frame(quads, new FrameOptions { Backdrop = "image", BackgroundImage = backdrop, Padding = 30, ShadowRadius = 0 }))
            check(Near(At(imaged, 5, 5), 255, 0, 0) && Near(At(imaged, 455, 355), 255, 255, 255), "An image background fills the canvas");

        var window = Open(new FrameEditor([photo]) { PickImage = () => Task.FromResult<string?>(backdrop) });
        var editor = (FrameEditor)window.Panel;
        check(Texts(window).Contains("Background") && Texts(window).Contains("Padding") && Texts(window).Contains("Corners") && Texts(window).Contains("Shadow") && Texts(window).Contains("Save with background"), "Add BG shows background, padding, corners and shadow");
        Wait(window.RenderNow());
        Dispatcher.UIThread.RunJobs();
        check(Texts(window).Contains("720 × 620"), "The preview reports the framed size (400 × 300 + 2 × 160)");
        editor.Settings = editor.Settings with { Color = "#FFFFFF00", Padding = 10, ShadowRadius = 0 };
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var outputs = Run(window.Job!, [photo], Directory.CreateDirectory(Path.Combine(work, "framed")).FullName);
        using (var saved = PhotoCodec.Read(outputs[0]))
            check(Path.GetFileName(outputs[0]) == Name + " - Framed.png" && saved.Width == 420 && saved.Height == 320 && saved.GetPixelsUnsafe().GetPixel(0, 0).GetChannel(3) == 0, "A transparent background saves “name - Framed.png” with clear corners");
        var jpg = Quadrants(Path.Combine(work, Name + ".jpg"), 400, 300);
        var jpgOut = Run(new FrameEditor([jpg]).Job(), [jpg], Directory.CreateDirectory(Path.Combine(work, "framed-jpg")).FullName);
        check(Path.GetFileName(jpgOut[0]) == Name + " - Framed.jpg", "An opaque background keeps the source format");

        Renders("frame", () => new FrameEditor([photo]), w => { ((FrameEditor)w.Panel).Settings = ((FrameEditor)w.Panel).Settings with { Backdrop = "gradient", Padding = 90 }; w.Rebuild(); });
    }
}
