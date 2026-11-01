// Adapted from Filee (MIT, © KnifeLemon and contributors): src/Filee.App/Controls/DonutGeometry.cs (SubSpan,
// SubMidAngle, SubIndexAt) and DonutMenu.Groups.cs (a group slice fans out a second ring after the pointer rests on
// it; the ring stays while the pointer is on it or crossing to it, and another slice or the hole folds it away).
// Drawn in Nashi's look: glass petals, the gold selection, a tick on every highlight change.
using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Nashi.Core;

namespace Nashi.Linux;

public sealed partial class Wheel
{
    /// Gap between the ring and a group's outer ring, and that ring's thickness (in the 320-point wheel).
    public const double SubGap = 6, SubBand = 46;
    public const double SubInner = Radius + SubGap, SubOuter = SubInner + SubBand;

    /// Room a window needs around the 320-point wheel to show an outer ring and its labels.
    public const double SubRingSpace = SubGap + SubBand + 10;

    /// How far a group's fan reaches into each neighbouring petal, as a share of a petal; the narrowest sub petal.
    private const double SubOverhang = 0.25, MinSubWidth = 44;

    /// How long the pointer rests on a group petal before its ring fans out: passing over on the way elsewhere doesn't.
    public static TimeSpan GroupDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// The rest timer's clock; the checks set their own.
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    private int openGroup = -1, subHighlighted = -1, resting = -1;
    private DateTime restSince;

    /// The petal whose ring is fanned out, or -1.
    public int OpenGroupIndex => openGroup;

    /// The highlighted petal of the open ring, or -1.
    public int SubHighlightedIndex => subHighlighted;

    /// The open group's items.
    public IReadOnlyList<WheelItem> SubItems => openGroup >= 0 && openGroup < Segments.Count ? Segments[openGroup].Group ?? [] : [];

    /// Degrees one sub petal covers: the fan spans the group's petal and a quarter of each neighbour, widened so a
    /// petal is at least 44 points along its middle, and never more than once round.
    public static double SubSpan(double parentSpan, int count)
    {
        if (count <= 0) return 0;
        var span = parentSpan * (1 + 2 * SubOverhang) / count;
        span = Math.Max(span, MinSubWidth / ((SubInner + SubOuter) / 2) * 180 / Math.PI);
        return Math.Min(span, 360.0 / count);
    }

    /// Centre angle (counter-clockwise degrees from +x, as Angle) of sub petal `sub` of `count`, clockwise in order.
    public static double SubMid(double parentMid, int sub, int count, double span) => parentMid - (sub - (count - 1) / 2.0) * span;

    /// Which sub petal of a fan around `parentMid` holds the point (in the 320-point wheel), or -1.
    public static int SubIndexAt(Point p, double parentMid, int count, double span)
    {
        var (dx, dy) = (p.X - Middle.X, p.Y - Middle.Y);
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (count <= 0 || distance < SubInner || distance > SubOuter) return -1;
        var angle = Math.Atan2(-dy, dx) * 180 / Math.PI;
        var delta = Math.IEEERemainder(parentMid - angle, 360);
        var sub = (int)Math.Floor(delta / span + count / 2.0);
        return sub >= 0 && sub < count ? sub : -1;
    }

    /// The point in the 320-point wheel for a point in this control.
    private Point Normal(Point p)
    {
        var scale = Math.Min(Bounds.Width, Bounds.Height) / Size;
        if (scale <= 0) scale = 1;
        return new((p.X - (Bounds.Width - Size * scale) / 2) / scale, (p.Y - (Bounds.Height - Size * scale) / 2) / scale);
    }

    private (double Mid, double Span, int Count) Fan()
    {
        var count = Segments.Count;
        var children = SubItems.Count;
        return (Angle(count, openGroup), SubSpan(360.0 / count, children), children);
    }

    /// The open ring's sub petal under a point in this control, or null.
    public int? SubAt(Point p)
    {
        if (openGroup < 0) return null;
        var (mid, span, count) = Fan();
        var sub = SubIndexAt(Normal(p), mid, count, span);
        return sub >= 0 && SubItems[sub].Enabled ? sub : null;
    }

    /// The pointer (or a drag) is at `p`: on the ring it highlights the petal, and resting on a group petal opens its
    /// ring; on the open ring it highlights a sub petal; crossing the gap keeps the ring; another petal or the hole
    /// folds it away.
    private void Track(Point p)
    {
        var index = IndexAt(p, Bounds.Size, Segments.Count);
        if (openGroup >= 0)
        {
            if (SubAt(p) is { } sub) { SetSub(sub); return; }
            var n = Normal(p);
            var distance = Math.Sqrt(Math.Pow(n.X - Middle.X, 2) + Math.Pow(n.Y - Middle.Y, 2));
            if (index == openGroup || (index < 0 && distance > Radius - 1 && distance < SubOuter + 8)) { SetSub(-1); return; }
            ResetGroup();
        }
        Highlight(index);
        if (index >= 0 && Segments[index].Group is { Count: > 0 })
        {
            if (resting != index)
            {
                (resting, restSince) = (index, Clock());
                Desktop.After(GroupDelay, Rest);
            }
        }
        else resting = -1;
    }

    /// Opens the group the pointer has rested on for GroupDelay (the timer; the checks call it with their clock).
    public void Rest()
    {
        if (resting >= 0 && resting == highlighted && openGroup < 0 && Clock() - restSince >= GroupDelay) OpenGroup(resting);
    }

    /// Fans out the ring of group petal `index`.
    public void OpenGroup(int index)
    {
        if (index < 0 || index >= Segments.Count || Segments[index].Group is not { Count: > 0 }) return;
        openGroup = index;
        subHighlighted = -1;
        highlighted = index;
        WheelFeedback.Tick();
        InvalidateVisual();
        Turned?.Invoke();
    }

    private void ResetGroup()
    {
        if (openGroup < 0 && resting < 0) return;
        var was = openGroup;
        (openGroup, subHighlighted, resting) = (-1, -1, -1);
        InvalidateVisual();
        if (was >= 0) Turned?.Invoke();
    }

    private void SetSub(int sub)
    {
        if (sub == subHighlighted) return;
        subHighlighted = sub;
        if (sub >= 0) WheelFeedback.Tick();
        InvalidateVisual();
    }

    private bool ChooseSub(int sub)
    {
        var items = SubItems;
        if (sub < 0 || sub >= items.Count || !items[sub].Enabled) return false;
        WheelFeedback.PlaySelection();
        Chosen?.Invoke(items[sub]);
        return true;
    }

    /// Keys while a ring is open: arrows move along it, Enter or Space chooses, Esc or Backspace folds it away.
    private bool GroupKey(Key key)
    {
        if (openGroup < 0) return false;
        var count = SubItems.Count;
        switch (key)
        {
            case Key.Right or Key.Down: SetSub((subHighlighted + 1) % count); return true;
            case Key.Left or Key.Up: SetSub(subHighlighted <= 0 ? count - 1 : subHighlighted - 1); return true;
            case Key.Enter or Key.Space: if (subHighlighted >= 0) ChooseSub(subHighlighted); return true;
            case Key.Escape or Key.Back: ResetGroup(); return true;
            default: return false;
        }
    }

    /// Whether a drop at `p` (in the 320-point wheel) lands on something to run, from a snapshot of the petals: any
    /// thread may ask (the XDND target does). Before any petals, the ring itself counts.
    public Func<Point, bool> DropSnapshot()
    {
        var segments = Segments.ToArray();
        var open = openGroup;
        var children = SubItems.ToArray();
        var (mid, span, _) = open >= 0 ? Fan() : (0, 0, 0);
        return p =>
        {
            if (segments.Length == 0) return IndexAt(p, new Size(Size, Size), 8) >= 0;
            if (open >= 0 && SubIndexAt(p, mid, children.Length, span) is var sub and >= 0) return children[sub].Enabled;
            var index = IndexAt(p, new Size(Size, Size), segments.Length);
            return index >= 0 && segments[index].Enabled;
        };
    }

    private string CenterTitle(IReadOnlyList<WheelItem> segments) =>
        openGroup >= 0 && subHighlighted >= 0 ? SubItems[subHighlighted].Title
        : highlighted >= 0 && highlighted < segments.Count ? segments[highlighted].Title : Title;

    private void RenderGroup(DrawingContext context, IBrush label, Pen line)
    {
        if (openGroup < 0) return;
        var (mid, span, count) = Fan();
        const double gap = 0.9;
        for (var k = 0; k < count; k++)
        {
            var center = SubMid(mid, k, count, span);
            double a = center - span / 2 + gap, b = center + span / 2 - gap;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(At(SubOuter, a), true);
                g.ArcTo(At(SubOuter, b), new Size(SubOuter, SubOuter), 0, b - a > 180, SweepDirection.CounterClockwise);
                g.LineTo(At(SubInner, b));
                g.ArcTo(At(SubInner, a), new Size(SubInner, SubInner), 0, b - a > 180, SweepDirection.Clockwise);
                g.EndFigure(true);
            }
            var on = k == subHighlighted;
            context.DrawGeometry(on ? Selected : Brush("NashiWheelPetal"), line, geometry);
            var item = SubItems[k];
            var text = new FormattedText(AppLocalization.Uppercase(item.Title, L10n.Language), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 9.5, on ? Ink : label)
            {
                MaxTextWidth = Math.Max(30, span * Math.PI / 180 * (SubInner + SubOuter) / 2 - 6),
                MaxLineCount = 2,
                TextAlignment = TextAlignment.Center,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            var at = At((SubInner + SubOuter) / 2, center);
            using (context.PushOpacity(item.Enabled ? 1 : 0.4))
                context.DrawText(text, new Point(at.X - text.MaxTextWidth / 2, at.Y - text.Height / 2));
        }
    }
}
