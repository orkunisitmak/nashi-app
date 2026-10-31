using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;
using Nashi.Linux.Editors;

static partial class EditorChecks
{
    // ---- Annotate Photo

    static void Annotate(string work, string photo)
    {
        using var gray = new MagickImage(new MagickColor(128, 128, 128), 300, 200);
        MagickImage Draw(params Mark[] marks) => ImageTools.Annotate(gray, marks);
        using (var boxed = Draw(new Mark("rectangle", new Rect(50, 50, 100, 80)) { Color = "#FF0000", Stroke = 4 }))
            check(Near(At(boxed, 50, 90), 255, 0, 0, 10) && Near(At(boxed, 150, 90), 255, 0, 0, 10) && Near(At(boxed, 100, 90), 128, 128, 128, 0) && Near(At(boxed, 10, 10), 128, 128, 128, 0), "A drawn rectangle's border pixels are its colour; inside and outside stay as they were");
        using (var ring = Draw(new Mark("ellipse", new Rect(50, 50, 100, 80)) { Color = "#0000FF", Stroke = 4 }))
            check(Near(At(ring, 50, 90), 0, 0, 255, 30) && Near(At(ring, 51, 51), 128, 128, 128, 0), "An ellipse touches its box at the sides, not the corners");
        using (var arrow = Draw(new Mark("arrow", new Rect(20, 100, 200, 0)) { Points = [new(20, 100), new(220, 100)], Color = "#FFFFFF", Stroke = 4 }))
            check(Near(At(arrow, 120, 100), 255, 255, 255, 10) && Near(At(arrow, 212, 95), 255, 255, 255, 60), "An arrow draws its line and its head");
        using (var number = Draw(new Mark("number", new Rect(100, 50, 60, 60)) { Color = "#FF0000" }))
            check(Near(At(number, 105, 80), 255, 0, 0, 10) && At(number, 130, 80) is { R: > 200, G: > 200 }, "A number is a filled dot with its digit in white");
        using (var marked = Draw(new Mark("highlight", default) { Points = [new(10, 20), new(290, 20)], Color = "#FFCC00", Stroke = 20 }))
            check(At(marked, 150, 20) is { B: < 120 and > 40, R: >= 120 } && Near(At(marked, 150, 60), 128, 128, 128, 0), "The marker multiplies a translucent stroke over the photo");
        using (var written = Draw(new Mark("text", new Rect(20, 20, 260, 60)) { Text = "Nashi ğüş", Color = "#000000", FontSize = 30 }))
        {
            var dark = 0;
            for (var y = 20; y < 80; y += 2) for (var x = 20; x < 280; x += 2) if (At(written, x, y).R < 60) dark++;
            check(dark > 30, "Text is drawn in its box (" + dark + " dark samples)");
        }
        var stamp = Quadrants(Path.Combine(work, "stamp.png"), 40, 40);
        using (var stamped = Draw(new Mark("image", new Rect(200, 100, 80, 80)) { ImagePath = stamp }))
            check(Near(At(stamped, 205, 105), 255, 0, 0, 10) && Near(At(stamped, 275, 175), 255, 255, 255, 10), "An image stamp fits its box");
        using var quads = PhotoCodec.Read(photo);
        using (var magnified = ImageTools.Annotate(quads, [new Mark("magnifier", new Rect(150, 100, 100, 100)) { Magnification = 2, Color = "#000000" }]))
            check(Near(At(magnified, 180, 130), 255, 0, 0, 20) && Near(At(magnified, 220, 170), 255, 255, 255, 20), "The magnifier shows the middle of its circle twice as big");
        check(Mark.Draft(new Mark("rectangle", default), [new(10, 10), new(10.5, 40)]) is null && Mark.Draft(new Mark("line", default), [new(10, 10), new(10, 40)]) is { Points.Length: 2 } && Mark.Draft(new Mark("freehand", default), [new(1, 1), new(5, 9), new(20, 3)]) is { Points.Length: 3 }, "Drafts: a sliver is no rectangle, a line keeps its ends, a pen keeps its trail");

        var window = Open(new AnnotateEditor([photo]) { PickImage = () => Task.FromResult<string?>(stamp) });
        var editor = (AnnotateEditor)window.Panel;
        check(!window.SaveButton.IsEnabled && Texts(window).Contains("Make your point.") && Texts(window).Contains("Select / Move") && Texts(window).Contains("Stroke width"), "Annotate Photo opens with its tools and Save off");
        editor.SetKind("rectangle");
        editor.SetColor("#000000");
        DragOn(window, new Point(20, 20), new Point(120, 90));
        check(editor.Marks.Count == 1 && editor.Marks[0].Kind == "rectangle" && window.SaveButton.IsEnabled, "Dragging draws one mark with the chosen tool");
        editor.SetKind("image");
        DragOn(window, new Point(250, 180), new Point(330, 260));
        Wait(Task.Delay(50));
        check(editor.Marks.Count == 2 && editor.Marks[1].ImagePath == stamp, "An image stamp asks for its image");
        editor.SetSelecting(true);
        var first = editor.Marks[0].Rect;
        DragOn(window, first.TopLeft, first.TopLeft + new Point(10, 15));
        check(editor.Selected == 0 && editor.Marks[0].Rect.X == first.X + 10 && editor.Marks[0].Rect.Y == first.Y + 15, "Select / Move drags a mark");
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        check(editor.Marks.Count == 1 && editor.Marks[0].Kind == "image", "Delete removes the selected mark");
        editor.SetSelecting(false);
        editor.SetKind("rectangle");
        DragOn(window, new Point(60, 60), new Point(160, 120));
        var box = editor.Marks[^1].Rect;
        Wait(window.RenderNow());
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var outputs = Run(window.Job!, [photo], Directory.CreateDirectory(Path.Combine(work, "annotated")).FullName);
        check(Path.GetFileName(outputs[0]) == Name + " - Annotated.png" && Near(At(outputs[0], (int)box.X, (int)box.Center.Y), 0, 0, 0, 40) && Near(At(outputs[0], 5, 5), 255, 0, 0, 0), "The annotated copy is a PNG with the rectangle drawn at full size");

        Renders("annotate", () => new AnnotateEditor([photo]), w =>
        {
            var a = (AnnotateEditor)w.Panel;
            DragOn(w, new Point(40, 200), new Point(170, 120));
            a.SetKind("ellipse");
            DragOn(w, new Point(220, 40), new Point(360, 130));
            a.SetKind("number");
            DragOn(w, new Point(30, 30), new Point(70, 70));
        });
    }
}
