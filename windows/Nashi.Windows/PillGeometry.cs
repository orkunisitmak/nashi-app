using System.Windows;
using System.Windows.Media;

namespace Nashi.Windows;

internal static class PillGeometry
{
    internal const double CollapsedWidth = 190;
    internal const double CollapsedHeight = 32;
    internal const double ExpandedWidth = 640;
    internal const double ExpandedHeight = 260;

    internal static StreamGeometry Create(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1)
            throw new ArgumentOutOfRangeException(nameof(width));
        var topRadius = height <= CollapsedHeight + 1 ? Math.Min(16, height / 2) : 10;
        var bottomRadius = height <= CollapsedHeight + 1 ? Math.Min(16, height / 2) : 28;
        var w = Math.Max(topRadius * 2, width);
        var h = Math.Max(topRadius + bottomRadius, height);
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(topRadius, 0), true, true);
            path.LineTo(new Point(w - topRadius, 0), true, false);
            path.BezierTo(new Point(w - topRadius * .28, 0), new Point(w, topRadius * .28), new Point(w, topRadius), true, false);
            path.LineTo(new Point(w, h - bottomRadius), true, false);
            path.BezierTo(new Point(w, h - bottomRadius * .44), new Point(w - bottomRadius * .44, h), new Point(w - bottomRadius, h), true, false);
            path.LineTo(new Point(bottomRadius, h), true, false);
            path.BezierTo(new Point(bottomRadius * .44, h), new Point(0, h - bottomRadius * .44), new Point(0, h - bottomRadius), true, false);
            path.LineTo(new Point(0, topRadius), true, false);
            path.BezierTo(new Point(0, topRadius * .28), new Point(topRadius * .28, 0), new Point(topRadius, 0), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    internal static bool TryParseHotkey(string value, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (value == "Ctrl+Alt+N") { modifiers = 0x0002 | 0x0001; virtualKey = 0x4E; return true; }
        if (value == "Ctrl+Shift+N") { modifiers = 0x0002 | 0x0004; virtualKey = 0x4E; return true; }
        if (value == "Ctrl+Alt+Space") { modifiers = 0x0002 | 0x0001; virtualKey = 0x20; return true; }
        return false;
    }
}
