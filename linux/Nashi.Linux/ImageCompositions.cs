using Avalonia;
using ImageMagick;
using Nashi.Core;
using SkiaSharp;

namespace Nashi.Linux;

/// Add BG (macOS ImageFrameOptions): the photo on a solid, gradient or image background with padding, rounded
/// corners, a soft shadow and a position. Without Width/Height the canvas is the image plus twice the padding.
public sealed record FrameOptions
{
    public string Backdrop { get; init; } = "solid";
    public string Color { get; init; } = "#FFFFFF";
    public string[] Gradient { get; init; } = ["#FF6B79", "#FFD85C", "#895CE0"];
    public double GradientAngle { get; init; } = 45;
    public string? BackgroundImage { get; init; }
    public double BackgroundBlur { get; init; }
    public int Padding { get; init; } = 160;
    public double CornerRadius { get; init; } = 24;
    public double ShadowRadius { get; init; } = 24;
    public double ShadowOpacity { get; init; } = .25;
    public double? AspectRatio { get; init; }
    public double X { get; init; } = .5;
    public double Y { get; init; } = .5;
    public double ImageScale { get; init; } = 1;
    public bool Transparent => ImageTools.Color(Color).Alpha < 255 && Backdrop == "solid";
}

/// Create Collage (macOS CollageOptions): grid, row, column or featured cells on a canvas `Width` wide and
/// Width / AspectRatio tall.
public sealed record CollageOptions
{
    public string Layout { get; init; } = "grid";
    public int Columns { get; init; } = 2;
    public int Width { get; init; } = 2048;
    public double AspectRatio { get; init; } = 1;
    public int Spacing { get; init; } = 12;
    public int Padding { get; init; } = 16;
    public double CornerRadius { get; init; }
    public string Color { get; init; } = "#FFFFFF";
    public string? BackgroundImage { get; init; }
    public bool Fill { get; init; } = true;
    /// Empty keeps the selection order; otherwise a permutation of the inputs.
    public int[] Order { get; init; } = [];
}

/// One annotation in source pixels from the top left (macOS ImageAnnotation). Kinds: text, arrow, line, rectangle,
/// ellipse, freehand, highlight, number, callout, magnifier, image.
public sealed record Mark(string Kind, Rect Rect)
{
    public Point[] Points { get; init; } = [];
    public string Text { get; init; } = "";
    public string Color { get; init; } = "#FF5200";
    public double Stroke { get; init; } = 3;
    public double FontSize { get; init; } = 24;
    public string Font { get; init; } = "system";
    public int Number { get; init; } = 1;
    public string? ImagePath { get; init; }
    public double Magnification { get; init; } = 2;

    public static readonly string[] Kinds = ["arrow", "line", "rectangle", "ellipse", "freehand", "highlight", "text", "number", "callout", "magnifier", "image"];

    /// The same mark on an image `f` times the size (previews).
    public Mark Scaled(double f) => this with
    {
        Rect = new Rect(Rect.X * f, Rect.Y * f, Rect.Width * f, Rect.Height * f),
        Points = [.. Points.Select(p => p * f)],
        Stroke = Stroke * f,
        FontSize = FontSize * f,
    };

    /// One mark from a drag (macOS AnnotationDrawing.draft): shapes and straight lines keep only the ends; null
    /// when the drag is too small to draw.
    public static Mark? Draft(Mark style, IReadOnlyList<Point> points)
    {
        if (points.Count < 2 || points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) return null;
        var freehand = style.Kind is "freehand" or "highlight";
        var linear = freehand || style.Kind is "line" or "arrow";
        IReadOnlyList<Point> geometry = freehand ? points : [points[0], points[^1]];
        double minX = geometry.Min(p => p.X), maxX = geometry.Max(p => p.X), minY = geometry.Min(p => p.Y), maxY = geometry.Max(p => p.Y);
        if (linear ? Math.Max(maxX - minX, maxY - minY) <= 1 : maxX - minX < 1 || maxY - minY < 1) return null;
        return style with { Rect = new Rect(minX, minY, maxX - minX, maxY - minY), Points = freehand ? [.. points] : style.Kind is "line" or "arrow" ? [points[0], points[^1]] : [] };
    }
}

public static partial class ImageTools
{
    // ---- Add BG

    /// `load` opens the background image (the editor passes a cached preview copy). With `consume` the image is
    /// disposed once copied, so a full-size frame holds at most two full-size copies at a time.
    public static MagickImage Frame(MagickImage image, FrameOptions o, double scale = 1, Func<string, MagickImage>? load = null, bool consume = false)
    {
        if (!new[] { o.X, o.Y, o.ImageScale, o.CornerRadius, o.BackgroundBlur, o.ShadowRadius, o.ShadowOpacity, o.GradientAngle }.All(double.IsFinite) || o.ImageScale <= 0)
            throw new ArgumentException("Frame settings must be finite, and image scale must be positive.");
        var padding = (int)Math.Round(Math.Max(0, o.Padding) * scale);
        if (o.Padding > 32000 || o.ImageScale > 100) throw new ArgumentException("Frame padding or image scale is too large.");
        int width = (int)image.Width + padding * 2, height = (int)image.Height + padding * 2;
        if (o.AspectRatio is { } ratio)
        {
            if (!double.IsFinite(ratio) || ratio <= 0) throw new ArgumentException("Canvas aspect ratio must be positive.");
            if (width / (double)height < ratio) width = (int)Math.Ceiling(height * ratio);
            else height = (int)Math.Ceiling(width / ratio);
        }
        int imageWidth = (int)image.Width, imageHeight = (int)image.Height;
        var photo = ToSkia(image);
        if (consume) image.Dispose();
        using var bitmap = Canvas(width, height);
        using (photo)
        using (var canvas = new SKCanvas(bitmap))
        {
            var bounds = new SKRect(0, 0, width, height);
            canvas.Clear(Color(o.Color));
            if (o.Backdrop == "gradient")
            {
                var colors = (o.Gradient.Length == 0 ? [o.Color, o.Color] : o.Gradient.Length == 1 ? [o.Gradient[0], o.Gradient[0]] : o.Gradient).Select(Color).ToArray();
                // macOS draws in y-up coordinates; the same angle runs bottom-left to top-right here.
                var angle = o.GradientAngle * Math.PI / 180;
                float dx = (float)(Math.Cos(angle) * width / 2), dy = (float)(Math.Sin(angle) * height / 2);
                using var shader = SKShader.CreateLinearGradient(new SKPoint(width / 2f - dx, height / 2f + dy), new SKPoint(width / 2f + dx, height / 2f - dy), colors, SKShaderTileMode.Clamp);
                using var paint = new SKPaint { Shader = shader };
                canvas.DrawRect(bounds, paint);
            }
            else if (o.Backdrop == "image")
            {
                if (o.BackgroundImage is null) throw new ArgumentException("Choose a background image.");
                using var back = load?.Invoke(o.BackgroundImage) ?? PhotoCodec.Read(o.BackgroundImage);
                if (o.BackgroundBlur > 0) back.Blur(0, Math.Min(60, o.BackgroundBlur) * scale * Math.Max(back.Width / (double)width, back.Height / (double)height));
                using var backdrop = ToSkia(back);
                DrawFitted(canvas, backdrop, bounds, fill: true);
            }
            int availableWidth = Math.Max(1, width - padding * 2), availableHeight = Math.Max(1, height - padding * 2);
            var fit = Math.Min(1, Math.Min(availableWidth / (double)imageWidth, availableHeight / (double)imageHeight)) * o.ImageScale;
            double w = imageWidth * fit, h = imageHeight * fit;
            double x = (width - w) * Math.Clamp(o.X, 0, 1), y = (height - h) * Math.Clamp(o.Y, 0, 1);
            var rect = new SKRect((float)x, (float)y, (float)(x + w), (float)(y + h));
            var radius = (float)Math.Max(0, Math.Min(o.CornerRadius * scale, Math.Min(w, h) / 2));
            var rounded = new SKRoundRect(rect, radius);
            var shadow = o.ShadowRadius * scale;
            if (shadow > 0 && o.ShadowOpacity > 0)
            {
                // Core Graphics' shadow blur is about twice the Gaussian sigma; it falls a quarter of its radius below.
                using var paint = new SKPaint { Color = new SKColor(0, 0, 0, (byte)Math.Round(Math.Clamp(o.ShadowOpacity, 0, 1) * 255)), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(shadow / 2)) };
                canvas.Save();
                canvas.Translate(0, (float)(shadow / 4));
                canvas.DrawRoundRect(rounded, paint);
                canvas.Restore();
            }
            canvas.Save();
            canvas.ClipRoundRect(rounded, antialias: true);
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true }) canvas.DrawBitmap(photo, rect, paint);
            canvas.Restore();
        }
        return FromSkia(bitmap);
    }

    // ---- Create Collage

    /// The images, in order, on one canvas. Each loader is called once, its image drawn and disposed before the next
    /// one loads, so a collage of large photos holds one of them at a time.
    public static MagickImage Collage(IReadOnlyList<Func<MagickImage>> inputs, CollageOptions o, double scale = 1, MagickImage? background = null)
    {
        if (inputs.Count < 2) throw new ArgumentException("Choose at least two images for a collage.");
        if (!double.IsFinite(o.AspectRatio) || o.AspectRatio <= 0 || !double.IsFinite(o.CornerRadius)) throw new ArgumentException("Collage ratio and corner radius must be valid.");
        IReadOnlyList<Func<MagickImage>> images = inputs;
        if (o.Order.Length > 0)
        {
            if (o.Order.Length != inputs.Count || !o.Order.Order().SequenceEqual(Enumerable.Range(0, inputs.Count))) throw new ArgumentException("Image order must include every image exactly once.");
            images = [.. o.Order.Select(i => inputs[i])];
        }
        if (o.Width < 1 || o.Width > 32000 || o.Width / o.AspectRatio < 1 || o.Width / o.AspectRatio > 32000) throw new ArgumentException("Collage dimensions must be between 1 and 32,000 pixels.");
        var width = (int)Math.Round(o.Width * scale);
        var height = (int)Math.Round(o.Width / o.AspectRatio * scale);
        using var bitmap = Canvas(Math.Max(1, width), Math.Max(1, height));
        using (var canvas = new SKCanvas(bitmap))
        {
            var bounds = new SKRect(0, 0, width, height);
            canvas.Clear(Color(o.Color));
            if (background is not null) { using var back = ToSkia(background); DrawFitted(canvas, back, bounds, fill: true); }
            double padding = Math.Max(0, o.Padding) * scale, gap = Math.Max(0, o.Spacing) * scale;
            var area = new Rect(padding, padding, width - 2 * padding, height - 2 * padding);
            if (area.Width <= 0 || area.Height <= 0) throw new ArgumentException("Collage padding leaves no space for images.");
            var cells = CollageCells(images.Count, o.Layout, o.Columns, area, gap);
            for (var n = 0; n < images.Count; n++)
            {
                var cell = new SKRect((float)cells[n].X, (float)cells[n].Y, (float)cells[n].Right, (float)cells[n].Bottom);
                var radius = (float)Math.Max(0, Math.Min(o.CornerRadius * scale, Math.Min(cell.Width, cell.Height) / 2));
                SKBitmap image;
                using (var loaded = images[n]()) image = ToSkia(loaded);
                using var _ = image;
                canvas.Save();
                canvas.ClipRoundRect(new SKRoundRect(cell, radius), antialias: true);
                DrawFitted(canvas, image, cell, o.Fill);
                canvas.Restore();
            }
        }
        return FromSkia(bitmap);
    }

    /// The cells in drawing order, top to bottom: featured puts the first image in the left two thirds.
    public static IReadOnlyList<Rect> CollageCells(int count, string layout, int columns, Rect area, double gap)
    {
        var cells = new List<Rect>();
        if (layout == "featured")
        {
            var feature = (area.Width - gap) * 2 / 3;
            var other = area.Width - gap - feature;
            var otherHeight = (area.Height - gap * (count - 2)) / (count - 1);
            if (feature <= 0 || other <= 0 || otherHeight <= 0) throw new ArgumentException("Reduce collage spacing or increase the canvas.");
            cells.Add(new Rect(area.X, area.Y, feature, area.Height));
            for (var n = 1; n < count; n++) cells.Add(new Rect(area.X + feature + gap, area.Y + (n - 1) * (otherHeight + gap), other, otherHeight));
            return cells;
        }
        var across = layout == "row" ? count : layout == "column" ? 1 : Math.Max(1, Math.Min(count, columns));
        var rows = (count + across - 1) / across;
        var cellWidth = (area.Width - gap * (across - 1)) / across;
        var cellHeight = (area.Height - gap * (rows - 1)) / rows;
        if (cellWidth <= 0 || cellHeight <= 0) throw new ArgumentException("Reduce collage spacing or increase the canvas.");
        for (var n = 0; n < count; n++) cells.Add(new Rect(area.X + n % across * (cellWidth + gap), area.Y + n / across * (cellHeight + gap), cellWidth, cellHeight));
        return cells;
    }

    // ---- Annotate Photo

    /// The marks drawn over the image (macOS ImageEngine.drawAnnotations). `load` opens image stamps.
    /// With `consume` the image is disposed once copied: the marks draw on that copy, so a full-size annotation holds
    /// at most two full-size copies (plus a snapshot for magnifiers, when there are any).
    public static MagickImage Annotate(MagickImage image, IReadOnlyList<Mark> marks, Func<string, MagickImage>? load = null, bool consume = false)
    {
        using var bitmap = ToSkia(image);
        if (consume) image.Dispose();
        using (var canvas = new SKCanvas(bitmap))
        using (var source = marks.Any(m => m.Kind == "magnifier") ? bitmap.Copy() : null)
            foreach (var mark in marks) Draw(canvas, mark, source ?? bitmap, load);
        return FromSkia(bitmap);
    }

    static void Draw(SKCanvas canvas, Mark item, SKBitmap source, Func<string, MagickImage>? load)
    {
        var rect = item.Rect;
        if (!new[] { rect.X, rect.Y, rect.Width, rect.Height, item.Stroke, item.FontSize, item.Magnification }.All(double.IsFinite)) throw new ArgumentException("Annotation coordinates must be finite.");
        var r = new SKRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom).Standardized;
        var color = Color(item.Color);
        using var stroke = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Max(.5, item.Stroke), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true };
        using var fill = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        SKPoint P(Point p) => new((float)p.X, (float)p.Y);
        switch (item.Kind)
        {
            case "rectangle": canvas.DrawRect(r, stroke); break;
            case "ellipse": canvas.DrawOval(r, stroke); break;
            case "line" or "arrow":
            {
                var points = item.Points.Length >= 2 ? item.Points.Select(P).ToArray() : [new(r.Left, r.Top), new(r.Right, r.Bottom)];
                using var path = new SKPath();
                path.MoveTo(points[0]);
                foreach (var next in points.Skip(1)) path.LineTo(next);
                canvas.DrawPath(path, stroke);
                if (item.Kind == "arrow")
                {
                    var (end, previous) = (points[^1], points[^2]);
                    var angle = Math.Atan2(end.Y - previous.Y, end.X - previous.X);
                    var size = Math.Max(10, item.Stroke * 4);
                    using var head = new SKPath();
                    head.MoveTo((float)(end.X - size * Math.Cos(angle - Math.PI / 6)), (float)(end.Y - size * Math.Sin(angle - Math.PI / 6)));
                    head.LineTo(end);
                    head.LineTo((float)(end.X - size * Math.Cos(angle + Math.PI / 6)), (float)(end.Y - size * Math.Sin(angle + Math.PI / 6)));
                    canvas.DrawPath(head, stroke);
                }
                break;
            }
            case "freehand" or "highlight":
            {
                var points = item.Points.Length > 0 ? item.Points.Select(P).ToArray() : [new(r.Left, r.Top), new(r.Right, r.Bottom)];
                if (item.Kind == "highlight")
                {
                    // A marker: translucent and multiplied, so the text under it stays readable.
                    stroke.Color = color.WithAlpha((byte)Math.Round(color.Alpha * .35));
                    stroke.BlendMode = SKBlendMode.Multiply;
                    stroke.StrokeWidth = (float)Math.Max(12, item.Stroke);
                }
                using var path = new SKPath();
                path.MoveTo(points[0]);
                foreach (var next in points.Skip(1)) path.LineTo(next);
                canvas.DrawPath(path, stroke);
                break;
            }
            case "text":
                DrawText(canvas, item.Text, r, color, item.FontSize, item.Font);
                break;
            case "number":
                canvas.DrawOval(r, fill);
                DrawCentered(canvas, item.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), r, SKColors.White, Math.Min(r.Width, r.Height) * .58);
                break;
            case "callout":
            {
                var body = new SKRect(r.Left, r.Top, r.Right, r.Top + r.Height * .8f);
                canvas.DrawRoundRect(body, 8, 8, fill);
                using var tail = new SKPath();
                tail.MoveTo(body.Left + body.Width * .18f, body.Bottom - 1);
                tail.LineTo(body.Left + body.Width * .1f, r.Bottom);
                tail.LineTo(body.Left + body.Width * .4f, body.Bottom - 1);
                tail.Close();
                canvas.DrawPath(tail, fill);
                var inset = new SKRect(body.Left + 6, body.Top + 2, body.Right - 6, body.Bottom - 2);
                DrawCentered(canvas, item.Text.Length == 0 ? item.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : item.Text, inset, SKColors.White, item.FontSize);
                break;
            }
            case "magnifier":
            {
                if (item.Magnification <= 0) throw new ArgumentException("Magnification must be positive.");
                var m = (float)item.Magnification;
                var from = SKRect.Intersect(new SKRect(r.MidX - r.Width / m / 2, r.MidY - r.Height / m / 2, r.MidX + r.Width / m / 2, r.MidY + r.Height / m / 2), new SKRect(0, 0, source.Width, source.Height));
                if (from.IsEmpty) break;
                canvas.Save();
                using (var clip = new SKPath()) { clip.AddOval(r); canvas.ClipPath(clip, antialias: true); }
                using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High }) canvas.DrawBitmap(source, from, r, paint);
                canvas.Restore();
                canvas.DrawOval(r, stroke);
                break;
            }
            case "image":
            {
                if (item.ImagePath is null) throw new ArgumentException("Choose an image for the annotation.");
                using var overlay = load?.Invoke(item.ImagePath) ?? PhotoCodec.Read(item.ImagePath);
                using var stamp = ToSkia(overlay);
                DrawFitted(canvas, stamp, r, fill: false);
                break;
            }
            default: throw new ArgumentException("Choose an annotation tool.");
        }
    }

    static SKTypeface Typeface(string family, bool bold = true) =>
        SKTypeface.FromFamilyName(family switch { "serif" => "serif", "monospaced" => "monospace", "rounded" => "sans-serif", _ => "sans-serif" }, bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;

    // Wrapped at word boundaries inside the rectangle, from its top (macOS draws a framesetter frame).
    static void DrawText(SKCanvas canvas, string text, SKRect rect, SKColor color, double size, string family)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true, TextSize = (float)Math.Max(1, size), Typeface = Typeface(family) };
        var metrics = paint.FontMetrics;
        var lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        var y = rect.Top - metrics.Ascent;
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && paint.MeasureText(next) > rect.Width)
                {
                    canvas.DrawText(line, rect.Left, y, paint);
                    y += lineHeight;
                    line = word;
                }
                else line = next;
            }
            canvas.DrawText(line, rect.Left, y, paint);
            y += lineHeight;
        }
    }

    static void DrawCentered(SKCanvas canvas, string text, SKRect rect, SKColor color, double size)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true, TextSize = (float)Math.Max(1, size), Typeface = Typeface("system"), TextAlign = SKTextAlign.Center };
        var metrics = paint.FontMetrics;
        canvas.DrawText(text, rect.MidX, rect.MidY - (metrics.Ascent + metrics.Descent) / 2, paint);
    }
}
