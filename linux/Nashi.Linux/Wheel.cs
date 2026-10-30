using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nashi.Core;

namespace Nashi.Linux;

/// One petal. A disabled one (Float, not built yet) shows dimmed, never takes the gold highlight and says Coming soon.
public sealed record WheelItem(string Id, string Title, bool Enabled = true);

/// A petal label fitted inside its petal: lines centered on `Center` (in the 320-point wheel), the icon above them.
public sealed record WheelLabel(Point Center, double FontSize, IReadOnlyList<string> Lines, bool HasIcon, bool Fits)
{
    public const double IconSize = 20, IconGap = 4;
    public double LineHeight => FontSize * 1.18;
    public double Height => Lines.Count * LineHeight + (HasIcon ? IconSize + IconGap : 0);
    public double Top => Center.Y - Height / 2;
}

/// The macOS ring (Sources/NashiApp/Wheel.swift, as the web playground draws it): up to 8 rounded petals per page
/// around a center disc that names the highlighted item. Geometry follows WheelGeometry.swift and
/// docs/assets/wheel-geometry.mjs: a 320-point wheel, radius 0.44, inner 0.407 of it, item 0 at the top, clockwise.
public sealed class Wheel : Control
{
    private static class Copy
    {
        public const string Previous = "‹ Previous";
        public const string Next = "Next ›";
        public const string Soon = "Coming soon";
    }

    public const double Size = 320, Radius = Size * 0.44, Inner = Radius * 0.407;
    private const double MaxLabelWidth = 84, MinLabelWidth = 36, Padding = 3.5;
    private const int PageSize = 8;
    private const int PagedSize = 6; // with Previous and Next taking the last two petals
    private const string PreviousId = "page:previous", NextId = "page:next";
    private static readonly Point Middle = new(Size / 2, Size / 2);

    private static readonly Color Gold = Color.FromRgb(0xE3, 0xB8, 0x59), GoldEnd = Color.FromRgb(0xF5, 0xD1, 0x7D);
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x2A, 0x26, 0x20)); // neutral dark ink on gold
    private static readonly IBrush Selected = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Gold, 0), new GradientStop(GoldEnd, 1) },
    };
    private static readonly Typeface Face = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Dictionary<(int, int, string, bool), WheelLabel> labels = [];

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
        set { items = value; page = 0; highlighted = -1; HighlightedIndex = Step(-1, 1); InvalidateVisual(); }
    }

    /// The gold petal, or -1. Disabled petals are never highlighted.
    public int HighlightedIndex
    {
        get => highlighted;
        set
        {
            var segments = Segments;
            var next = value >= 0 && value < segments.Count && segments[value].Enabled ? value : -1;
            if (next == highlighted) return;
            highlighted = next;
            if (next >= 0) WheelFeedback.Tick();
            InvalidateVisual();
        }
    }

    public int PageCount => items.Count <= PageSize ? 1 : (items.Count + PagedSize - 1) / PagedSize;

    /// The petals on screen: all items, or one page plus Previous and Next.
    public IReadOnlyList<WheelItem> Segments => items.Count <= PageSize ? items
        : [.. items.Skip(page * PagedSize).Take(PagedSize), new(PreviousId, L10n.T(Copy.Previous)), new(NextId, L10n.T(Copy.Next))];

    /// Which petal a point falls in, or -1 in the center disc or outside the ring. Pure geometry.
    public static int IndexAt(Point p, Size size, int count)
    {
        if (count <= 0) return -1;
        var scale = Math.Min(size.Width, size.Height) / Size;
        var dx = p.X - size.Width / 2;
        var dy = p.Y - size.Height / 2;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < Inner * scale || distance > Radius * scale) return -1;
        var delta = 360.0 / count;
        var clockwiseFromTop = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        var start = (90 - StartAngle(count) + 360) % 360;
        return (int)((clockwiseFromTop - start + delta / 2 + 720) % 360 / delta) % count;
    }

    // Counter-clockwise degrees from the +x axis, y up (as on macOS); three items start on the left.
    private static double StartAngle(int count) => count == 3 ? 180 : 90;
    private static double Angle(int count, int index) => StartAngle(count) - index * 360.0 / count;

    // The next enabled petal from `from` in direction `step` (±1), or -1 when none is.
    private int Step(int from, int step)
    {
        var segments = Segments;
        for (var i = 1; i <= segments.Count; i++)
        {
            var next = ((from < 0 ? (step > 0 ? -1 : 0) : from) + step * i % segments.Count + segments.Count * 2) % segments.Count;
            if (segments[next].Enabled) return next;
        }
        return -1;
    }

    private void Choose(int index)
    {
        var segments = Segments;
        if (index < 0 || index >= segments.Count || !segments[index].Enabled) return;
        switch (segments[index].Id)
        {
            case PreviousId: TurnPage(-1); break;
            case NextId: TurnPage(1); break;
            default:
                WheelFeedback.PlaySelection();
                Chosen?.Invoke(segments[index]);
                break;
        }
    }

    private void TurnPage(int step)
    {
        if (PageCount < 2) return;
        page = (page + step + PageCount) % PageCount;
        highlighted = -1;
        HighlightedIndex = Step(-1, 1);
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
            case Key.Right or Key.Down: HighlightedIndex = Step(highlighted, 1); break;
            case Key.Left or Key.Up: HighlightedIndex = Step(highlighted, -1); break;
            case Key.Home: HighlightedIndex = Step(-1, 1); break;
            case Key.End: HighlightedIndex = Step(0, -1); break;
            case Key.PageDown: TurnPage(1); break;
            case Key.PageUp: TurnPage(-1); break;
            case Key.Enter or Key.Space: Choose(highlighted); break;
            default: e.Handled = false; break;
        }
    }

    // As on macOS: the petal under the pointer is highlighted; the center disc highlights nothing.
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var segments = Segments;
        var index = IndexAt(e.GetPosition(this), Bounds.Size, segments.Count);
        HighlightedIndex = index;
        ToolTip.SetTip(this, index >= 0 && !segments[index].Enabled ? L10n.T(Copy.Soon) : null);
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
        var scale = Math.Min(Bounds.Width, Bounds.Height) / Size;
        var segments = Segments;
        var label = Brush("NashiWheelLabel");
        var line = new Pen(new SolidColorBrush(Resource("NashiWheelLine")), 0.7);
        // Everything below is in the 320-point wheel's coordinates.
        using var _ = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - Size * scale) / 2, (Bounds.Height - Size * scale) / 2));

        // The glass rim: a soft glow just outside the backdrop, fading to nothing at the window's edge.
        var glow = Resource("NashiWheelGlow");
        context.DrawEllipse(new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Colors.Transparent, 0.9), new GradientStop(glow, 0.955), new GradientStop(Colors.Transparent, 1) },
        }, null, Middle, Size / 2, Size / 2);
        var backdrop = Size * (0.5 - 0.035);
        context.DrawEllipse(Brush("NashiWheelBackdrop"), line, Middle, backdrop, backdrop);

        for (var i = 0; i < segments.Count; i++)
        {
            var item = segments[i];
            var on = i == highlighted;
            context.DrawGeometry(on ? Selected : Brush("NashiWheelPetal"), line, Petal(segments.Count, i));
            var foreground = on ? Ink : label;
            using (context.PushOpacity(item.Enabled ? 1 : 0.4))
                DrawLabel(context, Layout(segments.Count, i, AppLocalization.Uppercase(item.Title, L10n.Language), WheelIcons.ForId(item.Id) is not null), WheelIcons.ForId(item.Id), foreground);
        }

        context.DrawEllipse(Brush("NashiWheelDisc"), null, Middle, Inner - 4, Inner - 4);
        var title = new FormattedText(highlighted >= 0 && highlighted < segments.Count ? segments[highlighted].Title : Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, label)
        {
            MaxTextWidth = (Inner - 4) * 2 - 16,
            MaxLineCount = 3,
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        context.DrawText(title, new Point(Middle.X - title.MaxTextWidth / 2, Middle.Y - title.Height / 2));
    }

    private static void DrawLabel(DrawingContext context, WheelLabel layout, Geometry? icon, IBrush brush)
    {
        var y = layout.Top;
        if (icon is not null)
        {
            var box = WheelLabel.IconSize / 24;
            using (context.PushTransform(Matrix.CreateScale(box, box) * Matrix.CreateTranslation(layout.Center.X - WheelLabel.IconSize / 2, y)))
                context.DrawGeometry(null, new Pen(brush, WheelIcons.Stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), icon);
            y += WheelLabel.IconSize + WheelLabel.IconGap;
        }
        foreach (var line in layout.Lines)
        {
            var text = Text(line, layout.FontSize, brush);
            context.DrawText(text, new Point(layout.Center.X - text.Width / 2, y + (layout.LineHeight - text.Height) / 2));
            y += layout.LineHeight;
        }
    }

    private Color Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is Color color ? color : Colors.Transparent;

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private static FormattedText Text(string text, double size, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush);

    private static readonly Dictionary<(string, double), double> widths = [];
    // Fitting measures the same words and lines many times.
    private static double Measure(string text, double size) =>
        widths.TryGetValue((text, size), out var width) ? width : widths[(text, size)] = Text(text, size, Brushes.Black).Width;

    /// The label for petal `index` of `count`, fitted as WheelLabelLayout.swift and wheel-geometry.mjs do: try the
    /// preferred center at full width, then narrower wraps and nearby centers, then smaller fonts (down by 3 points).
    /// At most two lines; words are never split. `Fits` is false only when nothing fits (then 6 points at the centroid).
    public static WheelLabel Layout(int count, int index, string title, bool hasIcon)
    {
        var key = (count, index, title, hasIcon);
        if (labels.TryGetValue(key, out var cached)) return cached;
        var petal = Petal(count, index);
        var centers = LabelCenters(count, index);
        var baseFont = hasIcon ? 9.5 : 13;
        WheelLabel Keep(WheelLabel label) => labels[key] = label;
        for (var reduction = 0.0; reduction <= 3; reduction += 0.25)
        {
            var font = baseFont - reduction;
            WheelLabel? best = null;
            var bestScore = double.MaxValue;
            for (var width = MaxLabelWidth; width >= MinLabelWidth; width -= 4)
            {
                if (Wrap(title, width, font) is not { Count: <= 2 } lines) continue;
                for (var ci = 0; ci < centers.Count; ci++)
                {
                    var candidate = new WheelLabel(centers[ci], font, lines, hasIcon, true);
                    if (!Rects(candidate).All(r => Contains(petal, r))) continue;
                    // Full width at the preferred center has the fewest lines: nothing later scores better.
                    if (width == MaxLabelWidth && ci == 0) return Keep(candidate);
                    var score = lines.Count * 10 + ci + (MaxLabelWidth - width) / MaxLabelWidth;
                    if (score < bestScore) { bestScore = score; best = candidate; }
                    break;
                }
            }
            if (best is not null) return Keep(best);
        }
        return Keep(new WheelLabel(centers[0], 6, Wrap(title, 80, 6) ?? [title], hasIcon, false));
    }

    /// The icon slot and each line's box, as drawn.
    public static IEnumerable<Rect> Rects(WheelLabel label)
    {
        var y = label.Top;
        if (label.HasIcon)
        {
            yield return new Rect(label.Center.X - WheelLabel.IconSize / 2, y, WheelLabel.IconSize, WheelLabel.IconSize);
            y += WheelLabel.IconSize + WheelLabel.IconGap;
        }
        foreach (var line in label.Lines)
        {
            var width = Measure(line, label.FontSize);
            yield return new Rect(label.Center.X - width / 2, y, width, label.LineHeight);
            y += label.LineHeight;
        }
    }

    // A curved inner edge can cut a rectangle even when its corners are inside: sample each padded edge.
    public static bool Contains(Geometry petal, Rect rect)
    {
        var box = rect.Inflate(Padding);
        for (var i = 0; i <= 8; i++)
        {
            var t = i / 8.0;
            if (!petal.FillContains(new Point(box.X + box.Width * t, box.Y)) || !petal.FillContains(new Point(box.X + box.Width * t, box.Bottom))
                || !petal.FillContains(new Point(box.X, box.Y + box.Height * t)) || !petal.FillContains(new Point(box.Right, box.Y + box.Height * t))) return false;
        }
        return true;
    }

    // Whole words, greedily; null when one word alone is wider than `width`.
    private static List<string>? Wrap(string title, double width, double font)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in title.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Measure(word, font) > width) return null;
            var next = line.Length > 0 ? line + " " + word : word;
            if (line.Length > 0 && Measure(next, font) > width) { lines.Add(line); line = word; }
            else line = next;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    // The area centroid first (wider petals pull the label inward), then nearby radii and small sideways nudges.
    private static List<Point> LabelCenters(int count, int index)
    {
        var sweep = (360.0 / count - 1.8) * Math.PI / 180;
        var centroid = count > 1
            ? 4 * Math.Sin(sweep / 2) * (Math.Pow(Radius, 3) - Math.Pow(Inner, 3)) / (3 * sweep * (Radius * Radius - Inner * Inner))
            : (Radius + Inner) / 2;
        var preferred = centroid / Radius;
        var fractions = new List<double> { preferred };
        for (var step = 1; step <= 12; step++) fractions.AddRange([preferred + step * 0.02, preferred - step * 0.02]);
        var theta = Angle(count, index) * Math.PI / 180;
        return fractions.Where(f => f > Inner / Radius && f < 0.86).Concat([0.73, 0.75, 0.71, 0.77, 0.69, 0.79, 0.67, 0.81])
            .SelectMany(f => new[] { 0.0, -4, 4, -8, 8 }.Select(t => new Point(
                Middle.X + Radius * f * Math.Cos(theta) - t * Math.Sin(theta),
                Middle.Y - Radius * f * Math.Sin(theta) - t * Math.Cos(theta))))
            .ToList();
    }

    // Point at counter-clockwise degrees (y up), so the math matches WheelGeometry.swift.
    private static Point At(double r, double degrees) =>
        new(Middle.X + r * Math.Cos(degrees * Math.PI / 180), Middle.Y - r * Math.Sin(degrees * Math.PI / 180));

    /// Petal `index` of `count` in the 320-point wheel: 0.9° gaps on each side and rounded corners.
    public static Geometry Petal(int count, int index)
    {
        const double gap = 0.9;
        var delta = 360.0 / count;
        var mid = Angle(count, index);
        double a = mid - delta / 2 + gap, b = mid + delta / 2 - gap;
        var corner = Math.Min(10, Inner * delta * Math.PI / 180 / 4);
        var outerInset = corner / Radius * 180 / Math.PI;
        var innerInset = corner / Inner * 180 / Math.PI;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            // y is flipped, so a counter-clockwise sweep on macOS is SweepDirection.CounterClockwise on screen too.
            g.BeginFigure(At(Radius - corner, a), true);
            g.QuadraticBezierTo(At(Radius, a), At(Radius, a + outerInset));
            g.ArcTo(At(Radius, b - outerInset), new Size(Radius, Radius), 0, b - a - 2 * outerInset > 180, SweepDirection.CounterClockwise);
            g.QuadraticBezierTo(At(Radius, b), At(Radius - corner, b));
            g.LineTo(At(Inner + corner, b));
            g.QuadraticBezierTo(At(Inner, b), At(Inner, b - innerInset));
            g.ArcTo(At(Inner, a + innerInset), new Size(Inner, Inner), 0, b - a - 2 * innerInset > 180, SweepDirection.Clockwise);
            g.QuadraticBezierTo(At(Inner, a), At(Inner + corner, a));
            g.EndFigure(true);
        }
        return geometry;
    }
}

/// Wheel feedback, as Sources/NashiApp/WheelFeedback.swift: the selection sound and the opening motion.
public static class WheelFeedback
{
    /// The selection sound setting (stub until Settings exists; on by default).
    public static bool SoundEnabled { get; set; } = true;
    /// The app's reduce-motion setting (stub until Settings exists; off by default).
    public static bool ReduceMotion { get; set; }
    /// GNOME's "enable-animations" off; read once. The checks set it.
    public static bool? SystemReducesMotion { get; set; }

    public static readonly TimeSpan Opening = TimeSpan.FromSeconds(0.2);

    public static bool MotionEnabled => !ReduceMotion && !(SystemReducesMotion ??=
        Desktop.Run("gsettings", ["get", "org.gnome.desktop.interface", "enable-animations"])?.Trim() == "false");

    public static string Sound => Path.Combine(AppContext.BaseDirectory, "Sounds", "WheelSelection.wav");

    /// The time source for the tick's rate limit; the checks swap in their own.
    public static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(0.05);
    private static DateTime lastTick = DateTime.MinValue;

    /// A choice: the sound at full volume.
    public static void PlaySelection() => Play(1);

    /// A highlight change, as macOS's selectionChanged: the same sound, softer (0.65), at most every 50 ms.
    public static void Tick()
    {
        var now = Clock();
        if (now - lastTick < MinimumInterval && now >= lastTick) return;
        lastTick = now;
        Play(0.65);
    }

    // Fire-and-forget through the first player present (PulseAudio, PipeWire, then ALSA, which has no volume);
    // silently nothing when the setting is off, the file is missing or no player exists.
    private static void Play(double volume)
    {
        if (!SoundEnabled || !File.Exists(Sound)) return;
        _ = Desktop.Start("paplay", ["--volume=" + (int)(65536 * volume), Sound], null)
            || Desktop.Start("pw-play", ["--volume=" + volume.ToString("0.##", CultureInfo.InvariantCulture), Sound], null)
            || Desktop.Start("aplay", ["-q", Sound], null);
    }
}
