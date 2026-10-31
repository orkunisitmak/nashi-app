using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ImageMagick;

namespace Nashi.Linux.Editors;

/// macOS CropSelectionGeometry: image-space selection, resize and move, independent of the preview's scale.
public static class CropGeometry
{
    /// nw, n, ne, e, se, s, sw, w: where each handle sits on the rectangle (0, ½ or 1 of its width and height).
    public static readonly (double X, double Y)[] Handles = [(0, 0), (.5, 0), (1, 0), (1, .5), (1, 1), (.5, 1), (0, 1), (0, .5)];

    public static Rect Selection(Point anchor, Point point, Size bounds, double? aspectRatio)
    {
        var start = new Point(Math.Min(bounds.Width, Math.Max(0, anchor.X)), Math.Min(bounds.Height, Math.Max(0, anchor.Y)));
        double dx = point.X - start.X, dy = point.Y - start.Y;
        var maxWidth = dx < 0 ? start.X : bounds.Width - start.X;
        var maxHeight = dy < 0 ? start.Y : bounds.Height - start.Y;
        double width = Math.Min(Math.Abs(dx), maxWidth), height = Math.Min(Math.Abs(dy), maxHeight);
        if (Valid(aspectRatio) is { } ratio)
        {
            width = Math.Max(Math.Abs(dx), Math.Abs(dy) * ratio);
            width = Math.Min(width, Math.Min(maxWidth, maxHeight * ratio));
            height = width / ratio;
        }
        return new Rect(dx < 0 ? start.X - width : start.X, dy < 0 ? start.Y - height : start.Y, width, height);
    }

    public static Rect Resize(Rect rect, int handle, Point point, Size bounds, double? aspectRatio)
    {
        var (hx, hy) = Handles[handle];
        if (Valid(aspectRatio) is not { } ratio)
        {
            double left = rect.Left, right = rect.Right, top = rect.Top, bottom = rect.Bottom;
            if (hx == 0) left = Math.Max(0, Math.Min(right - 1, point.X));
            if (hx == 1) right = Math.Min(bounds.Width, Math.Max(left + 1, point.X));
            if (hy == 0) top = Math.Max(0, Math.Min(bottom - 1, point.Y));
            if (hy == 1) bottom = Math.Min(bounds.Height, Math.Max(top + 1, point.Y));
            return new Rect(left, top, right - left, bottom - top);
        }
        if (hx != .5 && hy != .5)
        {
            var anchor = new Point(hx == 0 ? rect.Right : rect.Left, hy == 0 ? rect.Bottom : rect.Top);
            var target = new Point(hx == 0 ? Math.Min(anchor.X - 1, point.X) : Math.Max(anchor.X + 1, point.X), hy == 0 ? Math.Min(anchor.Y - 1, point.Y) : Math.Max(anchor.Y + 1, point.Y));
            return Selection(anchor, target, bounds, ratio);
        }
        // Side handles keep the opposite side and the perpendicular centre fixed.
        if (hx != .5)
        {
            var anchor = hx == 0 ? rect.Right : rect.Left;
            var available = hx == 0 ? anchor : bounds.Width - anchor;
            var maxHeight = 2 * Math.Min(rect.Center.Y, bounds.Height - rect.Center.Y);
            var width = Math.Min(Math.Max(1, hx == 0 ? anchor - point.X : point.X - anchor), Math.Min(available, maxHeight * ratio));
            return new Rect(hx == 0 ? anchor - width : anchor, rect.Center.Y - width / ratio / 2, width, width / ratio);
        }
        var edge = hy == 0 ? rect.Bottom : rect.Top;
        var room = hy == 0 ? edge : bounds.Height - edge;
        var maxWidth = 2 * Math.Min(rect.Center.X, bounds.Width - rect.Center.X);
        var height = Math.Min(Math.Max(1, hy == 0 ? edge - point.Y : point.Y - edge), Math.Min(room, maxWidth / ratio));
        return new Rect(rect.Center.X - height * ratio / 2, hy == 0 ? edge - height : edge, height * ratio, height);
    }

    public static Rect Move(Rect rect, Point start, Point point, Size bounds) =>
        new(Math.Max(0, Math.Min(bounds.Width - rect.Width, rect.X + point.X - start.X)), Math.Max(0, Math.Min(bounds.Height - rect.Height, rect.Y + point.Y - start.Y)), rect.Width, rect.Height);

    /// The handle within `tolerance` of the point, or -1.
    public static int HandleAt(Rect rect, Point point, double tolerance)
    {
        for (var n = 0; n < Handles.Length; n++)
        {
            var at = new Point(rect.X + rect.Width * Handles[n].X, rect.Y + rect.Height * Handles[n].Y);
            if (Math.Abs(at.X - point.X) <= tolerance && Math.Abs(at.Y - point.Y) <= tolerance) return n;
        }
        return -1;
    }

    static double? Valid(double? ratio) => ratio is { } r && double.IsFinite(r) && r > 0 ? r : null;
}

/// Crop Image (macOS cropControls): drag to draw, handles to resize, inside to move, Shift to lock the ratio,
/// aspect presets, W/H and X/Y fields in step with the selection, Select All and Clear Selection.
public sealed class CropEditor(IReadOnlyList<string> inputs) : EditorPanel("cropImage", inputs)
{
    private static class Copy
    {
        public const string Hint = "Keep the good part.";
        public const string Primary = "Save cropped copy";
        public const string Drag = "Drag across the preview to select the area to keep.";
        public const string Keys = "Shift locks the ratio · Return crops";
        public const string Handles = "Drag inside to move; drag a handle to resize.";
        public const string Every = "This crop applies to every selected image and must fit inside each one.";
        public const string Aspect = "Aspect ratio";
        public const string SelectAll = "Select All";
        public const string Clear = "Clear Selection";
        public const string Free = "Free";
        public const string Original = "Original";
        public const string Fits = "{0}: The crop rectangle must fit completely inside the {1} × {2} pixel image.";
    }

    public static readonly string[] Aspects = [Copy.Free, Copy.Original, "1:1", "4:3", "3:2", "16:9", "9:16"];

    private Rect? crop;
    private string aspect = Copy.Free;
    private enum Mode { None, Draw, Move, Resize }
    private Mode mode;
    private int handle;
    private Point anchor;
    private Rect before;
    private NumberBox? width, height, x, y;
    private Segments? aspects;

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public override bool ComparesResult => false;
    public override bool Interactive => true;
    public override bool CanSave => mode == Mode.None && crop is { } r && Source is { } s && r.X >= 0 && r.Y >= 0 && r.Width >= 1 && r.Height >= 1 && r.Right <= s.Width && r.Bottom <= s.Height;
    /// The selection in source pixels, whole pixels once a drag ends.
    public Rect? Selection => crop;
    public string Aspect => aspect;

    private Size Bounds => Source!.Size;

    /// The preset's width / height; Original is the image's own.
    public double? Ratio => aspect switch
    {
        Copy.Free => null,
        Copy.Original => Bounds.Width / Bounds.Height,
        _ => double.Parse(aspect.Split(':')[0], System.Globalization.CultureInfo.InvariantCulture) / double.Parse(aspect.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture),
    };

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(Ui.Caption(Copy.Drag));
        var keys = new StackPanel { Spacing = 2 };
        keys.Children.Add(Ui.Caption(Copy.Keys));
        keys.Children.Add(Ui.Caption(Copy.Handles));
        stack.Children.Add(keys);
        if (Inputs.Count > 1) stack.Children.Add(Ui.Caption(Copy.Every));
        aspects = new Segments(Aspects, aspect);
        aspects.Chosen += SetAspect;
        var aspectRow = new StackPanel { Spacing = 8 };
        aspectRow.Children.Add(new TextBlock { Text = L10n.T(Copy.Aspect), FontSize = 12 });
        aspectRow.Children.Add(aspects);
        stack.Children.Add(aspectRow);
        width = Ui.Number(Source!.Width, v => Field(v, isWidth: true), 110);
        height = Ui.Number(Source.Height, v => Field(v, isWidth: false), 110);
        x = Ui.Number(0, v => Origin(v, null), 110);
        y = Ui.Number(0, v => Origin(null, v), 110);
        stack.Children.Add(Ui.Row(Ui.Labeled("W", width), Ui.Labeled("H", height), new TextBlock { Text = "px", FontSize = 12, Classes = { "secondary" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }));
        stack.Children.Add(Ui.Row(Ui.Labeled("X", x), Ui.Labeled("Y", y)));
        stack.Children.Add(Ui.Row(Ui.Button(Copy.SelectAll, () => { aspect = Copy.Free; aspects.Select(aspect); Set(new Rect(Bounds)); }), Ui.Button(Copy.Clear, () => { crop = null; Sync(); Change(); })));
        Sync();
        return stack;
    }

    /// An aspect preset: the largest centred selection with that ratio (macOS).
    public void SetAspect(string value)
    {
        aspect = value;
        aspects?.Select(value);
        if (Ratio is { } ratio)
        {
            var w = Math.Min(Bounds.Width, Bounds.Height * ratio);
            var h = w / ratio;
            Set(new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h));
        }
        else Change();
    }

    /// Sets the selection in whole pixels (macOS setCrop) and updates the fields.
    public void Set(Rect rect)
    {
        crop = new Rect(Math.Floor(rect.X), Math.Floor(rect.Y), Math.Floor(rect.Width), Math.Floor(rect.Height));
        Sync();
        Change();
    }

    private void Field(int value, bool isWidth)
    {
        if (value < 1) return;
        var rect = crop ?? new Rect(Bounds);
        double w = rect.Width, h = rect.Height;
        if (isWidth) { w = value; if (Ratio is { } r) h = w / r; }
        else { h = value; if (Ratio is { } r) w = h * r; }
        crop = new Rect(rect.X, rect.Y, Math.Floor(w), Math.Floor(h));
        Sync(except: isWidth ? width : height);
        Change();
    }

    private void Origin(int? left, int? top)
    {
        if (crop is not { } rect) return;
        crop = new Rect(left ?? rect.X, top ?? rect.Y, rect.Width, rect.Height);
        Change();
    }

    private void Sync(NumberBox? except = null)
    {
        var r = crop ?? new Rect(Bounds);
        void Put(NumberBox? box, double v) { if (box is not null && box != except) box.Show((int)v); }
        Put(width, r.Width);
        Put(height, r.Height);
        Put(x, crop is null ? 0 : r.X);
        Put(y, crop is null ? 0 : r.Y);
        if (x?.Parent?.Parent is Control row) row.IsVisible = crop is not null;
    }

    // Shift locks the ratio: a new selection is square, an existing one keeps its own.
    private double? Lock(KeyModifiers modifiers, Rect? current) =>
        Ratio ?? ((modifiers & KeyModifiers.Shift) != 0 ? current is { Width: > 0, Height: > 0 } c ? c.Width / c.Height : 1 : null);

    public override void Press(Point point, KeyModifiers modifiers, double tolerance)
    {
        anchor = point;
        if (crop is { } rect && CropGeometry.HandleAt(rect, point, tolerance) is >= 0 and var h) { mode = Mode.Resize; handle = h; before = rect; }
        else if (crop is { } inside && inside.Contains(point)) { mode = Mode.Move; before = inside; }
        else { mode = Mode.Draw; crop = null; }
        Change();
    }

    public override void Drag(Point point, KeyModifiers modifiers)
    {
        if (mode == Mode.None || !double.IsFinite(point.X)) return;
        crop = mode switch
        {
            Mode.Resize => CropGeometry.Resize(before, handle, point, Bounds, Lock(modifiers, before)),
            Mode.Move => CropGeometry.Move(before, anchor, point, Bounds),
            _ => CropGeometry.Selection(anchor, point, Bounds, Lock(modifiers, null)),
        };
        Sync();
        Change();
    }

    public override void Release(Point point, KeyModifiers modifiers)
    {
        if (mode == Mode.None) return;
        if (double.IsFinite(point.X)) Drag(point, modifiers);
        mode = Mode.None;
        if (crop is { } rect && (rect.Width < 1 || rect.Height < 1)) crop = null;
        if (crop is { } done) Set(done);
        else { Sync(); Change(); }
    }

    public override StandardCursorType Cursor(Point point, double tolerance)
    {
        if (crop is not { } rect) return StandardCursorType.Cross;
        return CropGeometry.HandleAt(rect, point, tolerance) switch
        {
            0 => StandardCursorType.TopLeftCorner, 1 => StandardCursorType.TopSide, 2 => StandardCursorType.TopRightCorner, 3 => StandardCursorType.RightSide,
            4 => StandardCursorType.BottomRightCorner, 5 => StandardCursorType.BottomSide, 6 => StandardCursorType.BottomLeftCorner, 7 => StandardCursorType.LeftSide,
            _ => rect.Contains(point) ? StandardCursorType.SizeAll : StandardCursorType.Cross,
        };
    }

    public override void Draw(DrawingContext context, Matrix view, double zoom)
    {
        if (crop is not { } rect) return;
        var image = new Rect(Bounds).TransformToAABB(view);
        var r = rect.TransformToAABB(view);
        // Everything outside the selection dims, as one shape so no seams show between its parts.
        context.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(image), new RectangleGeometry(r)));
        context.DrawRectangle(null, new Pen(Ui.Gold, 2), r);
        foreach (var (hx, hy) in CropGeometry.Handles)
            context.DrawRectangle(Ui.Gold, null, new Rect(r.X + r.Width * hx - 4, r.Y + r.Height * hy - 4, 8, 8), 1.5, 1.5);
    }

    public override void Reset()
    {
        crop = null;
        aspect = Copy.Free;
    }

    /// Every image must hold the crop (one crop applies to all of them).
    public override string? Validate()
    {
        if (crop is not { } r) return null;
        foreach (var input in Inputs)
        {
            var info = new MagickImageInfo(input);
            var (w, h) = info.Orientation is ImageMagick.Orientation.LeftTop or ImageMagick.Orientation.RightTop or ImageMagick.Orientation.RightBottom or ImageMagick.Orientation.LeftBottom ? (info.Height, info.Width) : (info.Width, info.Height);
            if (r.Right > w || r.Bottom > h) return L10n.F(Copy.Fits, Path.GetFileName(input), w, h);
        }
        return null;
    }

    public override EditorJob Job()
    {
        var rect = crop!.Value;
        return Each(" - Cropped", (_, image) => ImageTools.Crop(image, rect));
    }
}
