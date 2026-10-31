using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MagickImage = ImageMagick.MagickImage;

namespace Nashi.Linux.Editors;

/// Annotate Photo (macOS annotationControls): arrows, lines, shapes, pen and marker strokes, text, numbers, callouts,
/// a magnifier and image stamps, in a colour, font, stroke and size; Select / Move to drag marks, Delete, Undo and
/// Clear. The copy is a PNG drawn at full size by the same renderer as the preview.
public sealed class AnnotateEditor(IReadOnlyList<string> inputs) : EditorPanel("annotateImage", inputs)
{
    private static class Copy
    {
        public const string Hint = "Make your point.";
        public const string Primary = "Save annotated copy";
        public const string Tool = "Tool";
        public const string Select = "Select / Move";
        public const string Text = "Text or callout";
        public const string Color = "Color";
        public const string Font = "Font";
        public const string Stroke = "Stroke width";
        public const string Size = "Text size";
        public const string Drawing = "Drag to draw with the selected tool. Use Select / Move to edit an existing annotation.";
        public const string Moving = "Drag an annotation to move it; press Delete to remove it. Turn off Select / Move to draw.";
        public const string Undo = "Undo";
        public const string Clear = "Clear";
        public const string Delete = "Delete";
        public const string DefaultText = "Text";
    }

    public static readonly (string Id, string Label)[] Tools = [("arrow", "Arrow"), ("line", "Line"), ("rectangle", "Rectangle"), ("ellipse", "Ellipse"), ("freehand", "Freehand"), ("highlight", "Highlight"), ("text", "Text"), ("number", "Number"), ("callout", "Callout"), ("magnifier", "Magnifier"), ("image", "Image")];
    public static readonly (string Id, string Label)[] Fonts = [("system", "System"), ("rounded", "Rounded"), ("serif", "Serif"), ("monospaced", "Monospaced")];
    public static readonly string[] Colors = ["#FF3B30", "#FF9500", "#FFCC00", "#007AFF", "#AF52DE", "#FFFFFF", "#000000"];

    private readonly List<Mark> marks = [];
    private readonly List<Point> stroke = [];
    private readonly Dictionary<string, MagickImage> stamps = [];
    private string kind = "arrow";
    private string color = "#FF3B30";
    private string font = "system";
    private string text = L10n.T(Copy.DefaultText);
    private double width = 3, size = 24;
    private bool selecting;
    private int selected = -1;
    private Point anchor;
    private Mark? before;
    private StackPanel? list;
    private TextBlock? hint;

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public override bool Interactive => true;
    public override bool CanSave => marks.Count > 0;
    public IReadOnlyList<Mark> Marks => marks;
    public int Selected => selected;

    /// The mark the next drag draws.
    public Mark Style => new(kind, default) { Text = text, Color = color, Stroke = width, FontSize = size, Font = font, Number = marks.Count + 1 };

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 12 };
        var tools = new ComboBox { ItemsSource = Tools.Select(t => L10n.T(t.Label)).ToList(), SelectedIndex = Array.FindIndex(Tools, t => t.Id == kind), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        var select = Ui.Button(Copy.Select, () => SetSelecting(!selecting), "segment");
        tools.SelectionChanged += (_, _) => { if (tools.SelectedIndex >= 0) { kind = Tools[tools.SelectedIndex].Id; SetSelecting(false); } };
        stack.Children.Add(Labeled(Copy.Tool, tools));
        var toggle = new Border { Classes = { "segments" }, Child = select };
        stack.Children.Add(toggle);
        var field = new TextBox { Text = text, FontSize = 12, Watermark = L10n.T(Copy.Text) };
        field.TextChanged += (_, _) => text = field.Text ?? "";
        stack.Children.Add(Labeled(Copy.Text, field));
        stack.Children.Add(Ui.Labeled(Copy.Color, Ui.Swatches(Colors, color, hex => color = hex)));
        var fonts = new ComboBox { ItemsSource = Fonts.Select(f => L10n.T(f.Label)).ToList(), SelectedIndex = Array.FindIndex(Fonts, f => f.Id == font), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        fonts.SelectionChanged += (_, _) => { if (fonts.SelectedIndex >= 0) font = Fonts[fonts.SelectedIndex].Id; };
        stack.Children.Add(Labeled(Copy.Font, fonts));
        stack.Children.Add(Ui.Slider(Copy.Stroke, 1, 40, width, 3, v => width = v, "px", v => Math.Round(v).ToString(System.Globalization.CultureInfo.CurrentCulture)));
        stack.Children.Add(Ui.Slider(Copy.Size, 8, 160, size, 24, v => size = v, "px", v => Math.Round(v).ToString(System.Globalization.CultureInfo.CurrentCulture)));
        hint = Ui.Caption(Copy.Drawing);
        stack.Children.Add(hint);
        stack.Children.Add(Ui.Row(Ui.Button(Copy.Undo, () => { if (marks.Count > 0) { marks.RemoveAt(marks.Count - 1); selected = -1; Change(); } }), Ui.Button(Copy.Clear, () => { marks.Clear(); selected = -1; Change(); })));
        list = new StackPanel { Spacing = 6 };
        stack.Children.Add(list);
        void Toggle() { select.Classes.Set("chosen", selecting); if (hint is not null) hint.Text = L10n.T(selecting ? Copy.Moving : Copy.Drawing); }
        selectionShown = Toggle;
        Changed -= Fill;
        Changed += Fill;
        Fill();
        return stack;
    }

    private Action? selectionShown;

    static Control Labeled(string label, Control control)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = L10n.T(label), FontSize = 12 });
        panel.Children.Add(control);
        return panel;
    }

    public void SetKind(string id) { kind = id; SetSelecting(false); }
    public void SetColor(string hex) => color = hex;

    public void SetSelecting(bool on)
    {
        selecting = on;
        if (!on) selected = -1;
        selectionShown?.Invoke();
        Change();
    }

    // "1. Arrow   Delete" for each mark, as on macOS.
    private void Fill()
    {
        if (list is null) return;
        list.Children.Clear();
        for (var n = 0; n < marks.Count; n++)
        {
            var index = n;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new TextBlock { Text = $"{n + 1}. {L10n.T(Tools.First(t => t.Id == marks[n].Kind).Label)}", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, FontWeight = n == selected ? FontWeight.SemiBold : FontWeight.Normal });
            var delete = Ui.Button(Copy.Delete, () => { marks.RemoveAt(index); selected = -1; Change(); }, "quiet");
            Grid.SetColumn(delete, 1);
            row.Children.Add(delete);
            list.Children.Add(row);
        }
    }

    /// Adds a mark as a drag would (the checks use it); image stamps ask for their image first.
    public async Task AddAsync(IReadOnlyList<Point> points)
    {
        if (Mark.Draft(Style, points) is not { } mark) return;
        if (mark.Kind == "image")
        {
            if (PickImage is null || await PickImage() is not { } path) return;
            mark = mark with { ImagePath = path };
        }
        marks.Add(mark);
        Change();
    }

    private int MarkAt(Point point, double tolerance) => marks.FindLastIndex(m => m.Rect.Inflate(Math.Max(tolerance, m.Stroke)).Contains(point));

    public override void Press(Point point, KeyModifiers modifiers, double tolerance)
    {
        stroke.Clear();
        if (selecting)
        {
            selected = MarkAt(point, tolerance);
            anchor = point;
            before = selected >= 0 ? marks[selected] : null;
            Change();
            return;
        }
        stroke.Add(point);
    }

    public override void Drag(Point point, KeyModifiers modifiers)
    {
        if (!double.IsFinite(point.X)) return;
        if (selecting)
        {
            if (before is null || selected < 0) return;
            var delta = point - anchor;
            marks[selected] = before with { Rect = before.Rect.Translate(delta), Points = [.. before.Points.Select(p => p + delta)] };
            Change();
            return;
        }
        if (stroke.Count > 0) stroke.Add(point);
    }

    public override void Release(Point point, KeyModifiers modifiers)
    {
        Drag(point, modifiers);
        before = null;
        if (!selecting && stroke.Count > 1) _ = AddAsync([.. stroke]);
        stroke.Clear();
    }

    public override StandardCursorType Cursor(Point point, double tolerance) => selecting ? MarkAt(point, tolerance) >= 0 ? StandardCursorType.SizeAll : StandardCursorType.Arrow : StandardCursorType.Cross;

    public override bool Delete()
    {
        if (selected < 0 || selected >= marks.Count) return false;
        marks.RemoveAt(selected);
        selected = -1;
        Change();
        return true;
    }

    public override void Draw(DrawingContext context, Matrix view, double zoom)
    {
        if (stroke.Count > 1)
        {
            // The stroke in progress; the preview draws the finished mark.
            var pen = new Pen(new SolidColorBrush(Avalonia.Media.Color.Parse(color)), Math.Max(1, width * zoom), lineCap: PenLineCap.Round);
            var points = kind is "freehand" or "highlight" ? stroke : [stroke[0], stroke[^1]];
            if (kind is "rectangle" or "ellipse" or "text" or "number" or "callout" or "magnifier" or "image")
            {
                var r = new Rect(stroke[0], stroke[^1]).TransformToAABB(view);
                if (kind == "ellipse" || kind == "magnifier" || kind == "number") context.DrawEllipse(null, pen, r.Center, r.Width / 2, r.Height / 2);
                else context.DrawRectangle(null, pen, r);
            }
            else for (var n = 1; n < points.Count; n++) context.DrawLine(pen, view.Transform(points[n - 1]), view.Transform(points[n]));
        }
        if (selecting && selected >= 0 && selected < marks.Count)
            context.DrawRectangle(null, new Pen(Ui.Gold, 2, new DashStyle([4, 3], 0)), marks[selected].Rect.Inflate(marks[selected].Stroke).TransformToAABB(view));
    }

    private MagickImage Stamp(string path)
    {
        lock (stamps)
        {
            if (!stamps.TryGetValue(path, out var small)) stamps[path] = small = ImageTools.PreviewCopy(path, 800).Image;
            return (MagickImage)small.Clone();
        }
    }

    public override Func<MagickImage>? Preview()
    {
        if (marks.Count == 0) return null;
        var source = Source!;
        var scaled = marks.Select(m => m.Scaled(1 / source.Scale)).ToList();
        return () => ImageTools.Annotate(source.Preview, scaled, Stamp);
    }

    public override void Reset()
    {
        marks.Clear();
        selected = -1;
        selecting = false;
    }

    public override EditorJob Job()
    {
        var all = marks.ToList();
        return Each(" - Annotated", (_, image) => ImageTools.Annotate(image, all), png: true);
    }
}
