using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Core;

namespace Nashi.Linux.Editors.Media;

/// The media timeline (macOS MediaTimeline and MediaRangeInteraction): a filmstrip for video or a waveform for audio,
/// the In/Out range with gold handles, the playhead, split or snapshot points and redaction ranges, with times under it.
/// Click seeks; dragging a handle moves that end; dragging inside the range moves it; dragging elsewhere draws a new
/// range; dragging a point moves it. Every drag clamps to the media and keeps a range at least MinRange long.
public sealed class Timeline : Control
{
    private static class Copy
    {
        public const string StartHandle = "Start of the selected range";
        public const string EndHandle = "End of the selected range";
        public const string Playhead = "Playhead";
    }

    public const double MinRange = .05;
    const double Lane = 56, Labels = 18, Grab = 8;

    private readonly Border startHandle = Handle(Copy.StartHandle), endHandle = Handle(Copy.EndHandle);
    private enum Mode { None, Seek, Start, End, Move, Draw, Point }
    private Mode mode;
    private double anchor, before, beforeEnd;
    private Point pressed;

    public Timeline()
    {
        Height = Lane + Labels;
        ClipToBounds = false;
        Focusable = true;
        AutomationProperties.SetName(this, L10n.T(Copy.Playhead));
        foreach (var handle in new[] { startHandle, endHandle }) { VisualChildren.Add(handle); LogicalChildren.Add(handle); }
    }

    static Border Handle(string name)
    {
        var handle = new Border { Width = 10, Height = Lane, CornerRadius = new CornerRadius(3), Background = Ui.Gold, Cursor = new Cursor(StandardCursorType.SizeWestEast), Focusable = true };
        AutomationProperties.SetName(handle, L10n.T(name));
        return handle;
    }

    public double Duration { get; set { field = Math.Max(0, value); InvalidateArrange(); InvalidateVisual(); } }
    public double Playhead { get; set { field = Math.Clamp(value, 0, Duration); InvalidateVisual(); } }
    /// The In/Out range; shown with handles when ShowsRange.
    public double Start { get; private set; }
    public double End { get; private set; }
    public bool ShowsRange { get; set { field = value; startHandle.IsVisible = endHandle.IsVisible = value; InvalidateVisual(); } } = true;
    /// Split or snapshot times (sorted by the owner); the selected one is gold.
    public IReadOnlyList<double> Points { get; set { field = value; InvalidateVisual(); } } = [];
    public int SelectedPoint { get; set { field = value; InvalidateVisual(); } } = -1;
    /// Redaction ranges, drawn over the lane.
    public IReadOnlyList<MediaRange> Ranges { get; set { field = value; InvalidateVisual(); } } = [];
    public IReadOnlyList<Bitmap>? Thumbnails { get; set { field = value; InvalidateVisual(); } }
    public float[]? Peaks { get; set { field = value; InvalidateVisual(); } }
    /// A second waveform under the first (the right channel).
    public float[]? SecondPeaks { get; set { field = value; InvalidateVisual(); } }

    /// The user moved the playhead (click or scrub).
    public event Action<double>? Seeked;
    /// The In/Out range changed (handles, move or a new range).
    public event Action? RangeChanged;
    /// A point was dragged: its index and new time.
    public event Action<int, double>? PointMoved;
    /// A point was clicked: its index.
    public event Action<int>? PointChosen;

    /// Sets the range, clamped to the media and at least MinRange long.
    public void SetRange(double start, double end)
    {
        end = Math.Clamp(end, 0, Duration);
        start = Math.Clamp(start, 0, Math.Max(0, end - MinRange));
        if (end - start < MinRange) end = Math.Min(Duration, start + MinRange);
        (Start, End) = (start, end);
        InvalidateArrange();
        InvalidateVisual();
    }

    double X(double time) => Duration > 0 ? time / Duration * Bounds.Width : 0;
    double Time(double x) => Bounds.Width > 0 ? Math.Clamp(x / Bounds.Width * Duration, 0, Duration) : 0;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = finalSize.Width;
        double At(double t) => Duration > 0 ? t / Duration * width : 0;
        startHandle.Arrange(new Rect(At(Start) - 5, 0, 10, Lane));
        endHandle.Arrange(new Rect(At(End) - 5, 0, 10, Lane));
        return finalSize;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in new[] { startHandle, endHandle }) child.Measure(new Size(10, Lane));
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 400, Lane + Labels);
    }

    public static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var lane = new Rect(0, 0, width, Lane);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // the whole timeline takes the pointer
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xF0, 0, 0, 0)), null, new RoundedRect(lane, 8));
        using (context.PushClip(new RoundedRect(lane, 8)))
        {
            if (Thumbnails is { Count: > 0 } thumbs)
            {
                var step = width / thumbs.Count;
                for (var n = 0; n < thumbs.Count; n++)
                {
                    var b = thumbs[n];
                    var scale = Math.Max(step / b.Size.Width, Lane / b.Size.Height);
                    var w = b.Size.Width * scale;
                    using (context.PushClip(new Rect(n * step, 0, step, Lane)))
                        context.DrawImage(b, new Rect(b.Size), new Rect(n * step + (step - w) / 2, 0, w, b.Size.Height * scale));
                }
            }
            var wave = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF));
            void Wave(float[] peaks, double top, double height)
            {
                var mid = top + height / 2;
                for (var x = 0; x < (int)width; x++)
                {
                    var i = (int)((long)x * peaks.Length / Math.Max(1, (int)width));
                    var h = Math.Max(1, peaks[Math.Min(peaks.Length - 1, i)] * (height - 6));
                    context.FillRectangle(wave, new Rect(x, mid - h / 2, 1, h));
                }
            }
            if (Peaks is { Length: > 0 } p) Wave(p, 0, SecondPeaks is null ? Lane : Lane / 2);
            if (SecondPeaks is { Length: > 0 } q) Wave(q, Lane / 2, Lane / 2);
            foreach (var range in Ranges)
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), new Pen(Brushes.White, 1), new Rect(X(range.Start), 2, Math.Max(2, X(range.End) - X(range.Start)), Lane - 4));
            if (ShowsRange && Duration > 0)
            {
                var dim = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
                context.FillRectangle(dim, new Rect(0, 0, X(Start), Lane));
                context.FillRectangle(dim, new Rect(X(End), 0, width - X(End), Lane));
                context.DrawRectangle(null, new Pen(Ui.Gold, 2), new Rect(X(Start), 1, X(End) - X(Start), Lane - 2));
            }
        }
        for (var n = 0; n < Points.Count; n++)
        {
            var x = X(Points[n]);
            context.DrawLine(new Pen(n == SelectedPoint ? Ui.Gold : Brushes.White, 2), new Point(x, 0), new Point(x, Lane));
            context.DrawEllipse(n == SelectedPoint ? Ui.Gold : Brushes.White, null, new Point(x, 4), 4, 4);
        }
        var head = X(Playhead);
        context.DrawLine(new Pen(Brushes.Black, 3), new Point(head, -3), new Point(head, Lane + 3));
        context.DrawLine(new Pen(Brushes.White, 1.5), new Point(head, -3), new Point(head, Lane + 3));
        var ink = this.TryFindResource("NashiTextSecondary", ActualThemeVariant, out var brush) && brush is IBrush b2 ? b2 : Brushes.Gray;
        void Label(string text, double x, TextAlignment align)
        {
            var layout = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("monospace")), 10, ink);
            var left = align switch { TextAlignment.Right => x - layout.Width, TextAlignment.Center => x - layout.Width / 2, _ => x };
            context.DrawText(layout, new Point(Math.Clamp(left, 0, Math.Max(0, width - layout.Width)), Lane + 4));
        }
        if (ShowsRange && Duration > 0)
        {
            Label(Format(Start), X(Start), TextAlignment.Center);
            Label(Format(End), X(End), X(End) - X(Start) < 90 ? TextAlignment.Left : TextAlignment.Center);
        }
        else
        {
            Label(Format(0), 0, TextAlignment.Left);
            Label(Format(Duration), width, TextAlignment.Right);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Duration <= 0) return;
        pressed = e.GetPosition(this);
        var x = pressed.X;
        anchor = Time(x);
        (before, beforeEnd) = (Start, End);
        var point = -1;
        for (var n = 0; n < Points.Count; n++) if (Math.Abs(X(Points[n]) - x) <= Grab) point = n;
        if (point >= 0) { mode = Mode.Point; SelectedPoint = point; PointChosen?.Invoke(point); }
        else if (ShowsRange && Math.Abs(X(Start) - x) <= Grab) mode = Mode.Start;
        else if (ShowsRange && Math.Abs(X(End) - x) <= Grab) mode = Mode.End;
        else mode = Mode.Seek;
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (mode == Mode.None) return;
        var at = e.GetPosition(this);
        var t = Time(at.X);
        if (mode == Mode.Seek && Math.Abs(at.X - pressed.X) > 4 && ShowsRange)
            mode = anchor >= before && anchor <= beforeEnd ? Mode.Move : Mode.Draw;
        switch (mode)
        {
            case Mode.Start: SetRange(Math.Min(t, End - MinRange), End); RangeChanged?.Invoke(); Scrub(Start); break;
            case Mode.End: SetRange(Start, Math.Max(t, Start + MinRange)); RangeChanged?.Invoke(); Scrub(End); break;
            case Mode.Move:
                var length = beforeEnd - before;
                var start = Math.Clamp(before + t - anchor, 0, Duration - length);
                SetRange(start, start + length);
                RangeChanged?.Invoke();
                break;
            case Mode.Draw: SetRange(Math.Min(anchor, t), Math.Max(anchor, t)); RangeChanged?.Invoke(); break;
            case Mode.Point: PointMoved?.Invoke(SelectedPoint, t); break;
            case Mode.Seek: Scrub(t); break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (mode == Mode.Seek) Scrub(Time(e.GetPosition(this).X));
        mode = Mode.None;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        mode = Mode.None;
        base.OnPointerCaptureLost(e);
    }

    private void Scrub(double time)
    {
        Playhead = time;
        Seeked?.Invoke(Playhead);
    }
}
