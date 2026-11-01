using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nashi.Core;

namespace Nashi.Linux;

/// One petal. A disabled one (Float, not built yet) shows dimmed, never takes the gold highlight and says Coming soon.
public sealed record WheelItem(string Id, string Title, bool Enabled = true, IReadOnlyList<WheelItem>? Group = null);

/// One side of a two-sided wheel (Convert, Tools, Actions): Tab, the mouse wheel or a "side:N" petal turns to it.
public sealed record WheelSide(string Title, IReadOnlyList<WheelItem> Items, string Key = "");

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
public sealed partial class Wheel : Control
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
    private IReadOnlyList<WheelSide> sides = [];
    private int page, side, highlighted = -1;

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
        set { items = value; page = 0; highlighted = -1; ResetGroup(); HighlightedIndex = Step(-1, 1); InvalidateVisual(); Turned?.Invoke(); }
    }

    /// The sides of a two-sided wheel; setting them shows the first. Empty for a one-sided wheel.
    public IReadOnlyList<WheelSide> Sides
    {
        get => sides;
        set { sides = value; side = 0; if (value.Count > 0) { Title = value[0].Title; Items = value[0].Items; } }
    }

    /// The side on show (0 without sides).
    public int Side => side;

    /// The page on show of the current side.
    public int Page => page;

    /// The petals changed: a page or side turned, or new items (UI thread).
    public event Action? Turned;

    /// Shows side `index` from its first page.
    public void ShowSide(int index)
    {
        if (sides.Count == 0) return;
        side = (index % sides.Count + sides.Count) % sides.Count;
        Title = sides[side].Title;
        Items = sides[side].Items;
        WheelFeedback.Tick();
    }

    /// Shows side `index` as the wheel opens (no tick).
    public void StartAt(int index)
    {
        if (sides.Count == 0) return;
        side = (index % sides.Count + sides.Count) % sides.Count;
        Title = sides[side].Title;
        Items = sides[side].Items;
    }

    /// Tab, Shift+Tab and the mouse wheel: the next (or previous) page of petals, then the next side; every turn ticks.
    public void Flip(int step)
    {
        if (PageCount > 1 && page + step >= 0 && page + step < PageCount) { TurnPage(step); return; }
        if (sides.Count > 1)
        {
            ShowSide(side + step);
            if (step < 0 && PageCount > 1) SetPage(PageCount - 1);
            return;
        }
        TurnPage(step);
    }

    /// The petal under a point in this control's coordinates (-1: none).
    public int IndexAt(Point p) => IndexAt(p, Bounds.Size, Segments.Count);

    /// Highlights the petal under a point (a drag passing over), ticking when it changes.
    public void HighlightAt(Point p) => Track(p);

    /// Chooses the petal under a point (a drop); false on none, a disabled one, or a page or side turn.
    public bool ChooseAt(Point p)
    {
        if (SubAt(p) is { } sub) return ChooseSub(sub);
        var index = IndexAt(p);
        var segments = Segments;
        if (index < 0 || !segments[index].Enabled) return false;
        var id = segments[index].Id;
        Choose(index);
        return id is not (PreviousId or NextId) && !id.StartsWith("side:", StringComparison.Ordinal) && segments[index].Group is not { Count: > 0 };
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
            case var id when id.StartsWith("side:", StringComparison.Ordinal): ShowSide(int.Parse(id[5..], CultureInfo.InvariantCulture)); break;
            case var _ when segments[index].Group is { Count: > 0 }: OpenGroup(index); break;
            default:
                WheelFeedback.PlaySelection();
                Chosen?.Invoke(segments[index]);
                break;
        }
    }

    private void TurnPage(int step)
    {
        if (PageCount < 2) return;
        SetPage((page + step + PageCount) % PageCount);
        WheelFeedback.Tick();
    }

    private void SetPage(int value)
    {
        page = value;
        ResetGroup();
        highlighted = -1;
        HighlightedIndex = Step(-1, 1);
        InvalidateVisual();
        Turned?.Invoke();
    }

    // Every highlight the user moves (pointer, arrows, Tab) ticks, as macOS's selectionChanged; opening and page turns
    // do not.
    private void Highlight(int index)
    {
        var before = highlighted;
        HighlightedIndex = index;
        if (highlighted != before && highlighted >= 0) WheelFeedback.Tick();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var count = Segments.Count;
        if (e.Handled || count == 0) return;
        if (GroupKey(e.Key)) { e.Handled = true; return; }
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Right or Key.Down: Highlight(Step(highlighted, 1)); break;
            case Key.Left or Key.Up: Highlight(Step(highlighted, -1)); break;
            // Tab turns a paged or two-sided wheel; on a single page it walks the petals.
            case Key.Tab when PageCount > 1 || sides.Count > 1: Flip(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); break;
            case Key.Tab: Highlight(Step(highlighted, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1)); break;
            case Key.Home: Highlight(Step(-1, 1)); break;
            case Key.End: Highlight(Step(0, -1)); break;
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
        Track(e.GetPosition(this));
        ToolTip.SetTip(this, index >= 0 && !segments[index].Enabled ? L10n.T(Copy.Soon) : null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (PageCount < 2 && sides.Count < 2 || e.Delta.Y == 0) return;
        Flip(e.Delta.Y < 0 ? 1 : -1);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var index = IndexAt(e.GetPosition(this), Bounds.Size, Segments.Count);
        if (SubAt(e.GetPosition(this)) is { } sub) ChooseSub(sub);
        else if (index >= 0) Choose(index);
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

        RenderGroup(context, label, line);
        context.DrawEllipse(Brush("NashiWheelDisc"), null, Middle, Inner - 4, Inner - 4);
        var title = new FormattedText(CenterTitle(segments), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, label)
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

    /// Plays the sound at a volume (0 to 1): in-process through PulseAudio (PipeWire serves it too), else through
    /// paplay, pw-play or aplay. The checks swap in their own, so nothing reaches the speakers.
    public static Action<double> Player { get; set; } = volume => { if (!Pulse.Play(volume)) PlayThroughPlayers(volume); };

    // PulseAudio, PipeWire, then ALSA (which has no volume). Players found missing are skipped for the rest of the run.
    private static readonly string[] Players = ["paplay", "pw-play", "aplay"];
    private static readonly HashSet<string> missing = [];
    private static readonly object gate = new();

    /// Players found missing so far (the checks clear it).
    public static ISet<string> Missing => missing;

    /// A choice: the sound at full volume.
    public static void PlaySelection() => Play(1);

    /// Every highlight change the user makes, as macOS's selectionChanged: the same sound, softer (0.65). None is
    /// dropped; a newer one restarts the sound.
    public static void Tick() => Play(0.65);

    // Silently nothing when the setting is off or the file is missing.
    private static void Play(double volume)
    {
        if (SoundEnabled && File.Exists(Sound)) Player(volume);
    }

    /// Starts a player program: a way to stop it, or null when it would not start. The checks swap in their own.
    public static Func<string, IReadOnlyList<string>, Action<int>, Action?> StartPlayer { get; set; } = (program, args, exited) =>
    {
        var process = new Process { StartInfo = new ProcessStartInfo(program, args) { UseShellExecute = false }, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            using (process) exited(process.ExitCode);
        };
        try
        {
            if (process.Start()) return () =>
            {
                try { process.Kill(); }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception) { } // already gone
            };
        }
        catch (Win32Exception) { } // not installed
        process.Dispose();
        return null;
    };

    private static Action? playing; // stops the fallback player that is running, if any
    private static int generation;

    /// The fallback: the first player that starts; one that exits non-zero (no sound server, say) hands over to the
    /// next. At most one runs: a newer sound stops the one playing and restarts, so quick ticks never pile up processes.
    /// Nothing when no player works.
    public static void PlayThroughPlayers(double volume)
    {
        int mine;
        Action? stop;
        lock (gate)
        {
            (stop, playing) = (playing, null);
            mine = ++generation;
        }
        stop?.Invoke();
        Try(0);

        void Try(int from)
        {
            for (var i = from; i < Players.Length; i++)
            {
                var player = Players[i];
                lock (gate)
                {
                    if (generation != mine) return;
                    if (missing.Contains(player)) continue;
                }
                var next = i + 1;
                var ended = false;
                var started = StartPlayer(player, Arguments(player, volume), code =>
                {
                    lock (gate)
                    {
                        ended = true;
                        if (generation != mine) return; // stopped for a newer sound: no hand-over
                        playing = null;
                    }
                    if (code != 0) Try(next);
                });
                if (started is not null)
                {
                    var stale = false;
                    lock (gate)
                    {
                        if (generation != mine) stale = true;
                        else if (!ended) playing = started;
                    }
                    if (stale) started();
                    return;
                }
                lock (gate) missing.Add(player);
            }
        }
    }

    /// 16-bit PCM from a WAV file, or null for anything else. Chunks other than fmt and data (LIST, say) are skipped.
    public static Pcm? DecodeWav(byte[] wav)
    {
        if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") return null;
        int format = 0, channels = 0, rate = 0, bits = 0;
        for (var at = 12; at + 8 <= wav.Length;)
        {
            var id = Encoding.ASCII.GetString(wav, at, 4);
            var body = at + 8;
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(at + 4)), (uint)(wav.Length - body));
            if (id == "fmt " && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 14));
            }
            else if (id == "data")
            {
                // 1 is PCM; 0xFFFE (extensible) with 16 bits is PCM too.
                if (format is not (1 or 0xFFFE) || bits != 16 || channels < 1 || rate < 1) return null;
                var samples = new short[size / 2];
                for (var i = 0; i < samples.Length; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(body + i * 2));
                return new Pcm(rate, channels, samples);
            }
            at = body + size + (size & 1);
        }
        return null;
    }

    /// The samples at a volume, as macOS's player.volume: scaled and clamped.
    public static void Scale(ReadOnlySpan<short> from, Span<short> to, double volume)
    {
        for (var i = 0; i < from.Length; i++) to[i] = (short)Math.Clamp(Math.Round(from[i] * volume), short.MinValue, short.MaxValue);
    }

    private static string[] Arguments(string player, double volume) => player switch
    {
        "paplay" => ["--volume=" + (int)(65536 * volume), Sound],
        "pw-play" => ["--volume=" + volume.ToString("0.##", CultureInfo.InvariantCulture), Sound],
        _ => ["-q", Sound],
    };
}

/// Decoded 16-bit PCM: interleaved samples.
public sealed record Pcm(int Rate, int Channels, short[] Samples);

// One persistent PulseAudio stream through libpulse-simple (PipeWire-pulse serves it too), fed 10 ms at a time by a
// background thread. A new request flushes what is queued and restarts the sound, so quick ticks never pile up.
// A short target buffer (30 ms) keeps the latency low. Everything that can block (reading and decoding the wav once,
// connecting to the server, writing) happens on that thread, never on the UI thread: a stalled server can't freeze
// the wheel. When the stream can't open or breaks, that sound goes to the players and the next ticks do too, until a
// retry: after 1 s, then 2, 4… up to a minute.
internal static class Pulse
{
    /// The server to use; null is the default one. The checks point it at nothing to test the fallback.
    internal static string? Server { get; set; }
    /// The time source for the retry backoff; the checks swap in their own.
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    private static readonly object gate = new();
    private static readonly Lazy<Pcm?> pcm = new(() => File.Exists(WheelFeedback.Sound) ? WheelFeedback.DecodeWav(File.ReadAllBytes(WheelFeedback.Sound)) : null);
    private static Thread? writer;
    private static double? pending;
    private static int failures;
    private static DateTime retryAt = DateTime.MinValue;

    /// True while a failure's backoff runs (the players play meanwhile).
    internal static bool Down
    {
        get { lock (gate) return Clock() < retryAt; }
    }

    /// The wait before the next try after `failures` failures in a row: 1 s, doubling, at most 60 s.
    internal static TimeSpan Backoff(int failures) => TimeSpan.FromSeconds(Math.Min(60, 1 << Math.Min(failures - 1, 6)));

    /// Hands the sound to the writer thread; false while PulseAudio is down (backing off), so the caller falls back.
    public static bool Play(double volume)
    {
        lock (gate)
        {
            if (Clock() < retryAt) return false;
            pending = volume;
            if (writer is null)
            {
                writer = new Thread(Run) { IsBackground = true, Name = "Nashi sound" };
                writer.Start();
            }
            Monitor.Pulse(gate);
            return true;
        }
    }

    private static nint Open()
    {
        try
        {
            if (pcm.Value is not { } sound) return 0;
            var spec = new SampleSpec { Format = 3 /* PA_SAMPLE_S16LE */, Rate = (uint)sound.Rate, Channels = (byte)sound.Channels };
            var attr = new BufferAttr { MaxLength = uint.MaxValue, TLength = (uint)(sound.Rate * sound.Channels * 2 * 30 / 1000), PreBuf = uint.MaxValue, MinReq = uint.MaxValue, FragSize = uint.MaxValue };
            return pa_simple_new(Server, "Nashi", 1 /* PA_STREAM_PLAYBACK */, null, "Wheel", ref spec, 0, ref attr, out _);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    // This sound goes to the players; the next ones too until the backoff ends.
    private static void Failed(double volume)
    {
        WheelFeedback.PlayThroughPlayers(volume);
        lock (gate)
        {
            failures++;
            retryAt = Clock() + Backoff(failures);
            pending = null;
        }
    }

    private static void Run()
    {
        nint handle = 0;
        while (true)
        {
            double volume;
            lock (gate)
            {
                while (pending is null) Monitor.Wait(gate);
                volume = pending.Value;
                pending = null;
            }
            if (handle == 0 && (handle = Open()) == 0)
            {
                Failed(volume);
                continue;
            }
            lock (gate) failures = 0;
            var sound = pcm.Value!;
            var chunk = sound.Rate / 100 * sound.Channels;
            var buffer = new short[chunk];
            pa_simple_flush(handle, out _);
            for (var at = 0; at < sound.Samples.Length; at += chunk)
            {
                lock (gate) if (pending is not null) break;
                var count = Math.Min(chunk, sound.Samples.Length - at);
                WheelFeedback.Scale(sound.Samples.AsSpan(at, count), buffer, volume);
                if (pa_simple_write(handle, buffer, (nuint)(count * 2), out _) >= 0) continue;
                pa_simple_free(handle); // the server went away: reopen on a later tick
                handle = 0;
                Failed(volume);
                break;
            }
        }
    }

    /// A playback stream for the media editors' previews: interleaved PCM s16 at `rate` × `channels`, about 100 ms
    /// buffered. Null when PulseAudio (or PipeWire's pulse server) is not there. Only the thread that writes may close it.
    internal static Editors.Media.AudioSink? Stream(int rate, int channels)
    {
        try
        {
            var spec = new SampleSpec { Format = 3 /* PA_SAMPLE_S16LE */, Rate = (uint)rate, Channels = (byte)channels };
            var attr = new BufferAttr { MaxLength = uint.MaxValue, TLength = (uint)(rate * channels * 2 / 10), PreBuf = uint.MaxValue, MinReq = uint.MaxValue, FragSize = uint.MaxValue };
            var handle = pa_simple_new(Server, "Nashi", 1, null, "Preview", ref spec, 0, ref attr, out _);
            if (handle == 0) return null;
            return new((data, count) => pa_simple_write_bytes(handle, data, (nuint)count, out _) >= 0, () => { pa_simple_flush(handle, out _); pa_simple_free(handle); });
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SampleSpec
    {
        public int Format;
        public uint Rate;
        public byte Channels;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BufferAttr
    {
        public uint MaxLength, TLength, PreBuf, MinReq, FragSize;
    }

    [DllImport("libpulse-simple.so.0")]
    private static extern nint pa_simple_new(string? server, string name, int direction, string? device, string streamName, ref SampleSpec spec, nint map, ref BufferAttr attr, out int error);
    [DllImport("libpulse-simple.so.0")] private static extern int pa_simple_write(nint stream, short[] data, nuint bytes, out int error);
    [DllImport("libpulse-simple.so.0", EntryPoint = "pa_simple_write")] private static extern int pa_simple_write_bytes(nint stream, byte[] data, nuint bytes, out int error);
    [DllImport("libpulse-simple.so.0")] private static extern int pa_simple_flush(nint stream, out int error);
    [DllImport("libpulse-simple.so.0")] private static extern void pa_simple_free(nint stream);
}
