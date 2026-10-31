using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nashi.Core;
using MagickImage = ImageMagick.MagickImage;

namespace Nashi.Linux.Editors;

/// Create Collage (macOS collageControls): grid, row, column or featured; columns, canvas ratio and width, fill or
/// fit, spacing, padding, corners, a background colour or image, and the order, by dragging the list or its arrows.
/// One PNG beside the first image.
public sealed class CollageEditor(IReadOnlyList<string> inputs) : EditorPanel("createCollage", inputs)
{
    private static class Copy
    {
        public const string Hint = "Better together.";
        public const string Primary = "Create Collage";
        public const string Style = "Style";
        public const string Grid = "Grid";
        public const string Row = "Row";
        public const string Column = "Column";
        public const string Featured = "Featured";
        public const string Columns = "Columns";
        public const string Canvas = "Canvas";
        public const string Square = "Square · 1:1";
        public const string Landscape = "Landscape · 16:9";
        public const string Portrait = "Portrait · 9:16";
        public const string Tall = "Portrait · 4:5";
        public const string Width = "Canvas width";
        public const string Fit = "Image fit";
        public const string Fill = "Fill cells (crop edges)";
        public const string Whole = "Fit whole image";
        public const string Spacing = "Spacing";
        public const string Padding = "Outer padding";
        public const string Corners = "Corners";
        public const string Background = "Background";
        public const string BackgroundImage = "Background Image…";
        public const string RemoveImage = "Remove Image";
        public const string Order = "Order";
        public const string Drag = "Drag to reorder.";
    }

    public static readonly (double Ratio, string Label)[] Canvases = [(1, Copy.Square), (16 / 9.0, Copy.Landscape), (9 / 16.0, Copy.Portrait), (4 / 5.0, Copy.Tall)];
    public static readonly string[] Colors = ["#FFFFFF", "#000000", "#F4EBDD", "#E3B859", "#FF6B79", "#4B79A1", "#00000000"];
    const double RowHeight = 34;

    private CollageOptions options = new();
    private readonly List<int> order = [.. Enumerable.Range(0, inputs.Count)];
    private readonly Dictionary<string, MagickImage> previews = [];
    private readonly IReadOnlyList<string> sources = inputs;
    private StackPanel? list;
    private int dragging = -1;

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public override bool ComparesResult => false;
    /// The options; the order is Inputs' own, so Order stays empty.
    public CollageOptions Settings { get => options; set { options = value with { Order = [] }; Change(); } }
    /// The preview renders at most 1200 px wide.
    double Scale => Math.Min(1, 1200.0 / Math.Max(1, options.Width));
    public override double PreviewScale => 1 / Scale;

    public override Task LoadAsync(CancellationToken token) => Task.Run(() =>
    {
        foreach (var path in sources.Distinct())
        {
            token.ThrowIfCancellationRequested();
            var small = ImageTools.PreviewCopy(path, 900).Image;
            lock (previews) previews[path] = small;
        }
    }, token);

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 14 };
        var layout = new Segments([("grid", Copy.Grid), ("row", Copy.Row), ("column", Copy.Column), ("featured", Copy.Featured)], options.Layout);
        layout.Chosen += v => Settings = options with { Layout = v };
        stack.Children.Add(Labeled(Copy.Style, layout));
        stack.Children.Add(Ui.Labeled(Copy.Columns, Ui.Number(options.Columns, v => { if (v is >= 1 and <= 12) Settings = options with { Columns = v }; }, 64)));
        var canvas = new ComboBox { ItemsSource = Canvases.Select(c => L10n.T(c.Label)).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(Canvases, c => Math.Abs(c.Ratio - options.AspectRatio) < .001)), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        canvas.SelectionChanged += (_, _) => { if (canvas.SelectedIndex >= 0) Settings = options with { AspectRatio = Canvases[canvas.SelectedIndex].Ratio }; };
        stack.Children.Add(Labeled(Copy.Canvas, canvas));
        stack.Children.Add(Ui.Labeled(Copy.Width, Ui.Number(options.Width, v => { if (v is >= 1 and <= 32000) Settings = options with { Width = v }; }, 90)));
        var fit = new Segments([("fill", Copy.Fill), ("fit", Copy.Whole)], options.Fill ? "fill" : "fit");
        fit.Chosen += v => Settings = options with { Fill = v == "fill" };
        stack.Children.Add(Labeled(Copy.Fit, fit));
        stack.Children.Add(Ui.Row(Ui.Labeled(Copy.Spacing, Ui.Number(options.Spacing, v => { if (v >= 0) Settings = options with { Spacing = v }; }, 64)), Ui.Labeled(Copy.Padding, Ui.Number(options.Padding, v => { if (v >= 0) Settings = options with { Padding = v }; }, 64))));
        stack.Children.Add(Ui.Slider(Copy.Corners, 0, 100, options.CornerRadius, 0, v => Settings = options with { CornerRadius = v }, "px", v => Math.Round(v).ToString(System.Globalization.CultureInfo.CurrentCulture)));
        stack.Children.Add(Labeled(Copy.Background, Ui.Swatches(Colors, options.Color, hex => Settings = options with { Color = hex })));
        var remove = Ui.Button(Copy.RemoveImage, () => Settings = options with { BackgroundImage = null });
        remove.IsVisible = options.BackgroundImage is not null;
        stack.Children.Add(Ui.Row(Ui.Button(Copy.BackgroundImage, async () =>
        {
            if (PickImage is null || await PickImage() is not { } path) return;
            Settings = options with { BackgroundImage = path };
            remove.IsVisible = true;
        }), remove));
        Changed += () => remove.IsVisible = options.BackgroundImage is not null;
        stack.Children.Add(Ui.Heading(Copy.Order));
        stack.Children.Add(Ui.Caption(Copy.Drag));
        list = new StackPanel { Background = Brushes.Transparent };
        stack.Children.Add(list);
        list.PointerMoved += (_, e) => { if (dragging >= 0) Highlight(Index(e.GetPosition(list).Y)); };
        list.PointerReleased += (_, e) =>
        {
            if (dragging < 0) return;
            var to = Index(e.GetPosition(list).Y);
            var from = dragging;
            dragging = -1;
            Move(from, to);
        };
        Fill();
        return stack;
    }

    static Control Labeled(string label, Control control)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = L10n.T(label), FontSize = 12 });
        panel.Children.Add(control);
        return panel;
    }

    private int Index(double y) => Math.Clamp((int)(y / RowHeight), 0, order.Count - 1);

    private void Highlight(int target)
    {
        if (list is null) return;
        for (var n = 0; n < list.Children.Count; n++) ((Border)list.Children[n]).BorderBrush = n == target ? Ui.Gold : Brushes.Transparent;
    }

    // One row per image: its number, name, and up/down arrows; press and drag a row onto another to move it there.
    private void Fill()
    {
        if (list is null) return;
        list.Children.Clear();
        for (var n = 0; n < order.Count; n++)
        {
            var at = n;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto,Auto"), ColumnSpacing = 6 };
            grid.Children.Add(new TextBlock { Text = (n + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), FontSize = 12, Classes = { "secondary" }, VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBlock { Text = System.IO.Path.GetFileName(sources[order[n]]), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var up = new Button { Content = "↑", Classes = { "quiet", "compact" }, IsEnabled = n > 0, Padding = new Thickness(7, 2) };
            up.Click += (_, _) => Move(at, at - 1);
            Grid.SetColumn(up, 2);
            grid.Children.Add(up);
            var down = new Button { Content = "↓", Classes = { "quiet", "compact" }, IsEnabled = n < order.Count - 1, Padding = new Thickness(7, 2) };
            down.Click += (_, _) => Move(at, at + 1);
            Grid.SetColumn(down, 3);
            grid.Children.Add(down);
            var row = new Border { Height = RowHeight, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Child = grid, Cursor = new Cursor(StandardCursorType.SizeNorthSouth) };
            row.PointerPressed += (_, e) => { if (e.Source is not Button && !(e.Source is Visual v && v.FindAncestorOfType<Button>() is not null)) dragging = at; };
            list.Children.Add(row);
        }
    }

    /// Moves the image at `from` to `to` in the collage order.
    public void Move(int from, int to)
    {
        if (from < 0 || from >= order.Count || to < 0 || to >= order.Count || from == to) { Highlight(-1); return; }
        var item = order[from];
        order.RemoveAt(from);
        order.Insert(to, item);
        Inputs = [.. order.Select(i => sources[i])];
        Fill();
        Change();
    }

    private MagickImage Preview(string path)
    {
        lock (previews)
        {
            if (!previews.TryGetValue(path, out var small)) previews[path] = small = ImageTools.PreviewCopy(path, 900).Image;
            return small;
        }
    }

    public override Func<MagickImage>? Preview()
    {
        var o = options;
        var files = Inputs.ToList();
        var scale = Scale;
        return () =>
        {
            using var background = o.BackgroundImage is { } b ? (MagickImage)Preview(b).Clone() : null;
            return ImageTools.Collage([.. files.Select(f => (Func<MagickImage>)(() => (MagickImage)Preview(f).Clone()))], o, scale, background);
        };
    }

    public override void Dispose()
    {
        lock (previews) { foreach (var image in previews.Values) image.Dispose(); previews.Clear(); }
    }

    public override void Reset()
    {
        options = new();
        order.Clear();
        order.AddRange(Enumerable.Range(0, sources.Count));
        Inputs = sources;
    }

    /// The collage of `files` in their order (the editor's), saved as "first - Collage.png".
    public override EditorJob Job()
    {
        var o = options;
        return (files, destination, progress, token) => Task.Run(() =>
        {
            using var tx = new ExportTransaction(destination);
            using var background = o.BackgroundImage is { } b ? PhotoCodec.Read(b) : null;
            // One full-size image at a time: each loads, draws and goes before the next.
            using var collage = ImageTools.Collage([.. files.Select(f => (Func<MagickImage>)(() => { token.ThrowIfCancellationRequested(); return PhotoCodec.Read(f); }))], o, 1, background);
            progress.Report(new(.9, "Saving collage…"));
            var output = SafePaths.Unique(tx.StagingDirectory, System.IO.Path.GetFileNameWithoutExtension(files[0]) + " - Collage.png");
            ImageOperations.Write(collage, output, "png", 92, strip: true);
            return tx.Publish([output], token);
        }, token);
    }
}
