using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nashi.Core;
using Nashi.Linux;

/// Float: the references, persistence, the shelf window, drops and renders.
static class FloatChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Avalonia.Media.Imaging.WriteableBitmap?, string> smoke)
    {
        var work = Directory.CreateDirectory(Path.Combine(root, "float")).FullName;
        string Make(string name)
        {
            var path = Path.Combine(work, name);
            using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.SkyBlue, 24, 24);
            image.Write(path);
            return path;
        }
        var a = Make("-a ğ.png");
        var b = Make("b photo.jpg");
        var c = Make("c.webp");
        var folder = Directory.CreateDirectory(Path.Combine(work, "a folder")).FullName;
        Settings.Update(s => s with { Shelf = [], ShelfCollapsed = false, ShelfEdge = "right" });

        var shelf = new Shelf();
        check(shelf.Add([a, b, a, Path.Combine(work, "missing.png"), folder]) == 3 && shelf.Paths.SequenceEqual([a, b, folder]), "Float adds existing files and folders once each, in order");
        shelf.Remove([b]);
        check(shelf.Paths.SequenceEqual([a, folder]) && File.Exists(b), "Removing a reference keeps the file");
        check(new Shelf().Paths.SequenceEqual([a, folder]) && Settings.Load().Shelf.SequenceEqual([a, folder]), "Float's references persist across restarts");
        shelf.Clear();
        check(new Shelf().Paths.Count == 0 && File.Exists(a), "Clear empties Float and deletes nothing");

        // The window: opened by the Actions wheel's Float petal.
        check(FileActions.Available("float"), "The Float petal is enabled");
        var wheels = new List<IReadOnlyList<string>>();
        var flow = new WheelFlow(new Toolchain(Path.Combine(work, "none"), ""), null, _ => { }, () => Task.CompletedTask);
        flow.ShowActions([a, b]);
        var actions = flow.LastWheel!;
        Dispatcher.UIThread.RunJobs();
        var petal = actions.Wheel.Items.ToList().FindIndex(i => i.Id == "action:float");
        actions.Wheel.HighlightedIndex = petal;
        actions.Wheel.Focus();
        actions.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        for (var i = 0; i < 100 && FloatShelf.Current is null; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
        var window = FloatShelf.Current!;
        check(window is { IsVisible: true, Topmost: true } && window.Items.Paths.SequenceEqual([a, b]) && !window.Collapsed, "The Float petal opens the shelf, always on top, with the files");
        window.OpenWheel = wheels.Add;
        FloatShelf.Open([c, a], wheels.Add);
        check(FloatShelf.Current == window && window.Items.Paths.SequenceEqual([a, b, c]), "Float again adds to the same shelf");

        window.Select(0, 2);
        check(window.Selection.SequenceEqual([a, c]), "Several references can be selected");
        window.Focus();
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        check(window.Items.Paths.SequenceEqual([b]) && File.Exists(a) && File.Exists(c), "Delete removes the selected references, never the files");
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        check(window.Collapsed && window.Width == FloatShelf.PillWidth, "Escape folds the shelf into its pill");
        window.SetCollapsed(false);
        check(!window.Collapsed && !Settings.Current.ShelfCollapsed, "The pill opens back into the shelf");

        // Pinned to an edge: centered along it, inside the work area.
        var area = new PixelRect(0, 30, 1920, 1050);
        var size = new PixelSize(232, 400);
        check(FloatShelf.EdgePosition(area, size, "right", 8) == new PixelPoint(1680, 355) && FloatShelf.EdgePosition(area, size, "left", 8) == new PixelPoint(8, 355)
            && FloatShelf.EdgePosition(area, size, "top", 8) == new PixelPoint(844, 38) && FloatShelf.EdgePosition(area, size, "bottom", 8) == new PixelPoint(844, 672), "Float pins to each screen edge, centered along it");
        window.Pin("left");
        check(Settings.Current.ShelfEdge == "left", "The edge it is pinned to is kept");

        // Dropping on it (as the XDND target does while a drag runs) adds the files and opens the wheel for them.
        IDisposable? target = null;
        Action<DropContent, int, int>? dropped = null;
        var saved = XdndTarget.Create;
        XdndTarget.Create = (_, _, _, _, _, d, _) => { dropped = d; return target = new CancellationTokenSource(); };
        try
        {
            window.DragRunning(true);
            check(target is not null, "While a drag runs anywhere, Float is a drop target");
            dropped!(new DropContent([a], []), 0, 0);
            for (var i = 0; i < 50 && wheels.Count == 0; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
            check(window.Items.Paths.SequenceEqual([b, a]) && wheels is [[var w]] && w == a, "A drop on Float adds the files and opens the wheel for them");
            window.DragRunning(false);
        }
        finally { XdndTarget.Create = saved; }
        window.OpenWheel = flow.ShowFormats;
        Wait(window.DroppedAsync(new DropContent([c], [])));
        check(flow.LastWheel is { IsVisible: true } tools && tools.Wheel.Side == 0 && tools.Wheel.Items.Any(i => i.Id == "convert:jpg"), "In the app, a drop opens the wheel at the pointer on its front face");
        flow.LastWheel?.Close();
        window.Close();
        Dispatcher.UIThread.RunJobs();

        // Renders: the shelf and its pill, light, dark and Turkish.
        foreach (var (theme, language, suffix) in new[] { (ThemeVariant.Light, "en", "light"), (ThemeVariant.Dark, "en", "dark"), (ThemeVariant.Light, "tr", "tr") })
        {
            Application.Current!.RequestedThemeVariant = theme;
            L10n.Language = language;
            try
            {
                Settings.Update(s => s with { ShelfCollapsed = false, Shelf = [a, b, c, folder] });
                var shown = FloatShelf.Open([], _ => { });
                shown.Select(1);
                for (var i = 0; i < 100; i++) { Thread.Sleep(3); Dispatcher.UIThread.RunJobs(); } // thumbnails
                shown.CaptureRenderedFrame();
                var frame = shown.CaptureRenderedFrame();
                check(frame is not null, $"Float renders ({suffix})");
                if (language == "tr") check(shown.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == L10n.T("Open wheel")) && L10n.T("Open wheel") != "Open wheel", "Turkish Float translates its buttons");
                smoke(frame, "float-" + suffix);
                shown.SetCollapsed(true);
                Dispatcher.UIThread.RunJobs();
                shown.CaptureRenderedFrame();
                smoke(shown.CaptureRenderedFrame(), "float-pill-" + suffix);
                shown.Close();
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                L10n.Language = "en";
            }
        }
        Settings.Update(s => s with { Shelf = [], ShelfCollapsed = false, ShelfEdge = "right" });
    }

    static void Wait(Task task)
    {
        for (var i = 0; i < 1000 && !task.IsCompleted; i++) { Thread.Sleep(2); Dispatcher.UIThread.RunJobs(); }
    }
}
