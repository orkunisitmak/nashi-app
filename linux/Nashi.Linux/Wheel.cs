using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nashi.Core;

namespace Nashi.Linux;

public sealed record WheelItem(string Id, string Title);

/// The macOS-style ring: up to 8 petals per page around a center disc that names the highlighted item.
/// Geometry follows Sources/NashiApp/WheelGeometry.swift (radius 0.44, inner 0.407 of it, item 0 at the top, clockwise).
public sealed class Wheel : Control
{
    private static class Copy
    {
        public const string Previous = "‹ Previous";
        public const string Next = "Next ›";
    }

    private const int PageSize = 8;
    private const int PagedSize = 6; // with Previous and Next taking the last two petals
    private const string PreviousId = "page:previous", NextId = "page:next";

    private static readonly Color Gold = Color.FromRgb(0xE3, 0xB8, 0x59), GoldEnd = Color.FromRgb(0xF5, 0xD1, 0x7D);
    private static readonly IBrush Line = new SolidColorBrush(Color.FromArgb(0x3D, 0x80, 0x80, 0x80));
    private static readonly Typeface Face = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private IReadOnlyList<WheelItem> items = [];
    private int page, highlighted = -1;

    public Wheel()
    {
        Focusable = true;
        ClipToBounds = false;
    }

    public event Action<WheelItem>? Chosen;
    /// A click outside the petals: the window closes.
    public event Action? Dismissed;

    /// Shown in the center disc while nothing is highlighted.
    public string Title { get; set; } = "";

    public IReadOnlyList<WheelItem> Items
    {
        get => items;
        set { items = value; page = 0; highlighted = value.Count > 0 ? 0 : -1; InvalidateVisual(); }
    }

    public int HighlightedIndex
    {
        get => highlighted;
        set { var next = value >= -1 && value < Segments.Count ? value : -1; if (next == highlighted) return; highlighted = next; InvalidateVisual(); }
    }

    public int PageCount => items.Count <= PageSize ? 1 : (items.Count + PagedSize - 1) / PagedSize;

    /// The petals on screen: all items, or one page plus Previous and Next.
    public IReadOnlyList<WheelItem> Segments => items.Count <= PageSize ? items
        : [.. items.Skip(page * PagedSize).Take(PagedSize), new(PreviousId, L10n.T(Copy.Previous)), new(NextId, L10n.T(Copy.Next))];

    /// Which petal a point falls in, or -1 in the center disc or outside the ring. Pure geometry.
    public static int IndexAt(Point p, Size size, int count)
    {
        if (count <= 0) return -1;
        var (center, radius, inner) = Ring(size);
        var dx = p.X - center.X;
        var dy = p.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < inner || distance > radius) return -1;
        var delta = 360.0 / count;
        var clockwiseFromTop = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        var start = (90 - StartAngle(count) + 360) % 360;
        return (int)((clockwiseFromTop - start + delta / 2 + 720) % 360 / delta) % count;
    }

    private static (Point Center, double Radius, double Inner) Ring(Size size)
    {
        var radius = Math.Min(size.Width, size.Height) * 0.44;
        return (new Point(size.Width / 2, size.Height / 2), radius, radius * 0.407);
    }

    // Counter-clockwise degrees from the +x axis, y up (as on macOS); three items start on the left.
    private static double StartAngle(int count) => count == 3 ? 180 : 90;

    private void Choose(int index)
    {
        var segments = Segments;
        if (index < 0 || index >= segments.Count) return;
        switch (segments[index].Id)
        {
            case PreviousId: TurnPage(-1); break;
            case NextId: TurnPage(1); break;
            default: Chosen?.Invoke(segments[index]); break;
        }
    }

    private void TurnPage(int step)
    {
        if (PageCount < 2) return;
        page = (page + step + PageCount) % PageCount;
        highlighted = 0;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var count = Segments.Count;
        if (e.Handled || count == 0) return;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Right or Key.Down: HighlightedIndex = (highlighted + 1) % count; break;
            case Key.Left or Key.Up: HighlightedIndex = ((highlighted < 0 ? 0 : highlighted) - 1 + count) % count; break;
            case Key.Home: HighlightedIndex = 0; break;
            case Key.End: HighlightedIndex = count - 1; break;
            case Key.PageDown: TurnPage(1); break;
            case Key.PageUp: TurnPage(-1); break;
            case Key.Enter or Key.Space: Choose(highlighted); break;
            default: e.Handled = false; break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var index = IndexAt(e.GetPosition(this), Bounds.Size, Segments.Count);
        if (index >= 0) HighlightedIndex = index;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var index = IndexAt(e.GetPosition(this), Bounds.Size, Segments.Count);
        if (index >= 0) Choose(index);
        else Dismissed?.Invoke();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var (center, radius, inner) = Ring(size);
        var scale = Math.Min(size.Width, size.Height) / 320;
        var segments = Segments;
        var text = Brush("NashiText");

        var backdrop = Math.Min(size.Width, size.Height) * (0.5 - 0.035);
        context.DrawEllipse(Brush("NashiInset"), new Pen(Line, 0.7), center, backdrop, backdrop);

        var selected = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Gold, 0), new GradientStop(GoldEnd, 1) },
        };
        for (var i = 0; i < segments.Count; i++)
        {
            var on = i == highlighted;
            context.DrawGeometry(on ? selected : Brush("NashiSurface"), new Pen(Line, 0.7), Petal(i, segments.Count, center, radius, inner, scale));
            var label = Label(AppLocalization.Uppercase(segments[i].Title, L10n.Language), 13 * scale, 84 * scale, on ? Brush("NashiInk") : text);
            var at = LabelCenter(i, segments.Count, center, radius, inner);
            context.DrawText(label, new Point(at.X - label.MaxTextWidth / 2, at.Y - label.Height / 2));
        }

        var disc = inner - 4 * scale;
        context.DrawEllipse(Brush("NashiSurface"), new Pen(Line, 0.7), center, disc, disc);
        var title = Label(highlighted >= 0 && highlighted < segments.Count ? segments[highlighted].Title : Title, 11 * scale, disc * 2 - 16 * scale, text);
        context.DrawText(title, new Point(center.X - title.MaxTextWidth / 2, center.Y - title.Height / 2));
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    // Centered inside a box MaxTextWidth wide, so callers offset by MaxTextWidth / 2.
    // ponytail: labels wrap by word inside a fixed width; the macOS fitter also shrinks fonts per petal, add if long Turkish names clip.
    private static FormattedText Label(string text, double fontSize, double maxWidth, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, fontSize, brush)
        {
            MaxTextWidth = maxWidth,
            TextAlignment = TextAlignment.Center,
        };

    // Screen point at counter-clockwise degrees (y up), so the math matches WheelGeometry.swift.
    private static Point At(Point center, double r, double degrees) =>
        new(center.X + r * Math.Cos(degrees * Math.PI / 180), center.Y - r * Math.Sin(degrees * Math.PI / 180));

    private static Point LabelCenter(int index, int count, Point center, double radius, double inner)
    {
        // The centroid of the annular sector: wider petals pull the label inward.
        var sweep = (360.0 / count - 1.8) * Math.PI / 180;
        var r = count > 1
            ? 4 * Math.Sin(sweep / 2) * (Math.Pow(radius, 3) - Math.Pow(inner, 3)) / (3 * sweep * (radius * radius - inner * inner))
            : (radius + inner) / 2;
        return At(center, r, StartAngle(count) - index * 360.0 / count);
    }

    private static Geometry Petal(int index, int count, Point center, double radius, double inner, double scale)
    {
        const double gap = 0.9;
        var delta = 360.0 / count;
        var mid = StartAngle(count) - index * delta;
        double a = mid - delta / 2 + gap, b = mid + delta / 2 - gap;
        var corner = Math.Min(10 * scale, inner * delta * Math.PI / 180 / 4);
        var outerInset = corner / radius * 180 / Math.PI;
        var innerInset = corner / inner * 180 / Math.PI;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            // y is flipped, so a counter-clockwise sweep on macOS is SweepDirection.CounterClockwise on screen too.
            g.BeginFigure(At(center, radius - corner, a), true);
            g.QuadraticBezierTo(At(center, radius, a), At(center, radius, a + outerInset));
            g.ArcTo(At(center, radius, b - outerInset), new Size(radius, radius), 0, b - a - 2 * outerInset > 180, SweepDirection.CounterClockwise);
            g.QuadraticBezierTo(At(center, radius, b), At(center, radius - corner, b));
            g.LineTo(At(center, inner + corner, b));
            g.QuadraticBezierTo(At(center, inner, b), At(center, inner, b - innerInset));
            g.ArcTo(At(center, inner, a + innerInset), new Size(inner, inner), 0, b - a - 2 * innerInset > 180, SweepDirection.Clockwise);
            g.QuadraticBezierTo(At(center, inner, a), At(center, inner + corner, a));
            g.EndFigure(true);
        }
        return geometry;
    }
}
