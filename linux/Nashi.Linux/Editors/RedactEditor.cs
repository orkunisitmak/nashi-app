using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ImageMagick;

namespace Nashi.Linux.Editors;

/// Redact Photo (macOS redactionControls): drag to add areas, click one to select it, drag it to move it, Delete to
/// remove it; each is covered solid, blurred or pixelated. Face and text detection need Vision on macOS and are not
/// offered here.
public sealed class RedactEditor(IReadOnlyList<string> inputs) : EditorPanel("redactImage", inputs)
{
    private static class Copy
    {
        public const string Hint = "Fine-tune it. Save a fresh copy.";
        public const string Primary = "Save redacted copy";
        public const string Style = "Style";
        public const string Solid = "Solid";
        public const string Blur = "Blur";
        public const string Pixelate = "Pixelate";
        public const string Color = "Color";
        public const string Drag = "Drag on the preview to add an area. Each area is saved into the new copy.";
        public const string Select = "Click an area to select it, drag it to move it, and press Delete to remove it.";
        public const string One = "1 area selected";
        public const string Many = "{0} areas selected";
        public const string Undo = "Undo Area";
        public const string Delete = "Delete Area";
    }

    public static readonly string[] Colors = ["#000000", "#FFFFFF", "#3A3A3C", "#E3B859", "#FF6B79", "#4B79A1"];

    private readonly List<Rect> areas = [];
    private string style = "solid";
    private string color = "#000000";
    private int selected = -1;
    private Rect? drawing;
    private Point anchor;
    private Rect before;
    private bool moving;
    private TextBlock? count;
    private Button? delete;

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public override bool Interactive => true;
    public override bool CanSave => areas.Count > 0 && drawing is null;
    public IReadOnlyList<Rect> Areas => areas;
    public int Selected => selected;
    public RedactOptions Settings => new() { Style = style, Color = color, Areas = [.. areas] };

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 12 };
        var styles = new Segments([("solid", Copy.Solid), ("blur", Copy.Blur), ("pixelate", Copy.Pixelate)], style);
        styles.Chosen += value => { style = value; Change(); };
        stack.Children.Add(styles);
        stack.Children.Add(Ui.Labeled(Copy.Color, Ui.Swatches(Colors, color, hex => { color = hex; Change(); })));
        stack.Children.Add(Ui.Caption(Copy.Drag));
        stack.Children.Add(Ui.Caption(Copy.Select));
        count = new TextBlock { FontSize = 12 };
        stack.Children.Add(count);
        delete = Ui.Button(Copy.Delete, () => Delete());
        stack.Children.Add(Ui.Row(Ui.Button(Copy.Undo, () => { if (areas.Count > 0) { areas.RemoveAt(areas.Count - 1); selected = -1; Change(); } }), delete));
        Changed -= Show;
        Changed += Show;
        Show();
        return stack;
    }

    public void SetStyle(string value) { style = value; Change(); }

    /// Adds an area (source pixels), clipped to the image.
    public void Add(Rect area)
    {
        var clipped = area.Intersect(new Rect(Source!.Size));
        if (clipped.Width < 1 || clipped.Height < 1) return;
        areas.Add(clipped);
        selected = areas.Count - 1;
        Change();
    }

    private void Show()
    {
        if (count is null) return;
        count.Text = areas.Count == 1 ? L10n.T(Copy.One) : L10n.F(Copy.Many, areas.Count);
        delete!.IsEnabled = selected >= 0;
    }

    public override void Press(Point point, KeyModifiers modifiers, double tolerance)
    {
        anchor = point;
        selected = areas.FindLastIndex(a => a.Contains(point));
        if (selected >= 0) { moving = true; before = areas[selected]; }
        else drawing = new Rect(point, point);
        Change();
    }

    public override void Drag(Point point, KeyModifiers modifiers)
    {
        if (!double.IsFinite(point.X)) return;
        var bounds = Source!.Size;
        if (moving && selected >= 0) areas[selected] = CropGeometry.Move(before, anchor, point, bounds);
        else if (drawing is not null) drawing = CropGeometry.Selection(anchor, point, bounds, null);
    }

    public override void Release(Point point, KeyModifiers modifiers)
    {
        Drag(point, modifiers);
        if (drawing is { Width: >= 2, Height: >= 2 } area) { areas.Add(area); selected = areas.Count - 1; }
        drawing = null;
        moving = false;
        Change();
    }

    public override StandardCursorType Cursor(Point point, double tolerance) => areas.Any(a => a.Contains(point)) ? StandardCursorType.SizeAll : StandardCursorType.Cross;

    public override bool Delete()
    {
        if (selected < 0 || selected >= areas.Count) return false;
        areas.RemoveAt(selected);
        selected = -1;
        Change();
        return true;
    }

    public override void Draw(DrawingContext context, Matrix view, double zoom)
    {
        var outline = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), 1, new DashStyle([4, 3], 0));
        for (var n = 0; n < areas.Count; n++)
            context.DrawRectangle(null, n == selected ? new Pen(Ui.Gold, 2) : outline, areas[n].TransformToAABB(view));
        if (drawing is { } r)
        {
            context.DrawRectangle(new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x40, 0xE3, 0xB8, 0x59)), new Pen(Ui.Gold, 2), r.TransformToAABB(view));
        }
    }

    public override Func<MagickImage>? Preview()
    {
        if (areas.Count == 0) return null;
        var source = Source!;
        var f = source.Scale;
        var bounds = new Rect(0, 0, source.Preview.Width, source.Preview.Height);
        // The preview copy is 1/f the size: areas, blur and blocks shrink with it.
        var o = Settings with
        {
            Areas = [.. areas.Select(a => new Rect(a.X / f, a.Y / f, a.Width / f, a.Height / f).Intersect(bounds)).Where(a => a.Width > 0 && a.Height > 0)],
            BlurRadius = 24 / f,
            PixelSize = Math.Max(2, 16 / f),
        };
        return o.Areas.Length == 0 ? null : () => ImageTools.Redact(source.Preview, o);
    }

    public override void Reset()
    {
        areas.Clear();
        selected = -1;
        style = "solid";
        color = "#000000";
    }

    public override EditorJob Job()
    {
        var o = Settings;
        return Each(" - Redacted", (_, image) => ImageTools.Redact(image, o));
    }
}
