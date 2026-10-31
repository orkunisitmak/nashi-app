using System.Runtime.InteropServices;
using Avalonia;
using ImageMagick;
using Nashi.Core;
using SkiaSharp;

namespace Nashi.Linux;

/// Edit Photo: adjustments, then flip, turn and size (as macOS and Windows). Width/Height describe the finished image;
/// Scale and LongestEdge size each image by its own dimensions.
public sealed record PhotoOptions
{
    public PhotoAdjustments Adjustments { get; init; } = new();
    public int Rotation { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? Scale { get; init; }
    public int? LongestEdge { get; init; }
}

/// Redact Photo: areas in source pixels, covered solid, blurred or pixelated (macOS ImageRedactionOptions).
public sealed record RedactOptions
{
    public string Style { get; init; } = "solid";
    public string Color { get; init; } = "#000000";
    public double BlurRadius { get; init; } = 24;
    public double PixelSize { get; init; } = 16;
    public Rect[] Areas { get; init; } = [];
}

/// Compress: a quality, an optional target (bytes or % smaller) and an optional size (macOS ImageEngine.compress).
public sealed record CompressOptions
{
    public int Quality { get; init; } = 80;
    public long? TargetBytes { get; init; }
    public double? ReductionPercent { get; init; }
    public double? Scale { get; init; }
    public int? LongestEdge { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
}

/// The macOS image tools on Linux: Magick.NET decodes, adjusts and encodes; Skia draws frames, collages and marks.
/// Every Apply stages through ExportTransaction and names outputs as macOS does ("photo - Cropped.jpg").
public static partial class ImageTools
{
    /// Live previews render on a copy whose longest edge is at most this.
    public const int PreviewEdge = 1400;

    /// The image for an editor's preview, and how many source pixels one preview pixel stands for.
    public static (MagickImage Image, double Scale, int Width, int Height) PreviewCopy(string path, int edge = PreviewEdge)
    {
        var image = PhotoCodec.Read(path);
        int width = (int)image.Width, height = (int)image.Height;
        var longest = Math.Max(width, height);
        if (longest > edge)
        {
            var f = edge / (double)longest;
            image.Resize((uint)Math.Max(1, Math.Round(width * f)), (uint)Math.Max(1, Math.Round(height * f)));
        }
        return (image, width / (double)image.Width, width, height);
    }

    /// The extension an edited copy keeps: the source's, or PNG where Linux can't write it (HEIC, SVG) or transparency
    /// must survive.
    public static string OutputExtension(string input, bool png = false)
    {
        var ext = FeatureCatalog.Extension(input);
        return png || ext == "pdf" || !Capabilities.ImageFormats.Contains(ext) ? "png" : ext;
    }

    /// One edited copy per input beside the others in one transaction: "name{suffix}.ext".
    public static IReadOnlyList<string> Each(IReadOnlyList<string> inputs, string destination, string suffix, Func<string, MagickImage, MagickImage> work, IProgress<OperationProgress>? progress, CancellationToken token, bool png = false)
    {
        if (inputs.Count == 0) throw new ArgumentException("Choose at least one image.");
        using var tx = new ExportTransaction(destination);
        var outputs = new List<string>();
        for (var n = 0; n < inputs.Count; n++)
        {
            token.ThrowIfCancellationRequested();
            using var source = PhotoCodec.Read(inputs[n]);
            using var result = work(inputs[n], source);
            var output = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(inputs[n]) + suffix + "." + OutputExtension(inputs[n], png));
            ImageOperations.Write(result, output, Path.GetExtension(output)[1..], 92, strip: true);
            outputs.Add(output);
            progress?.Report(new((n + 1) / (double)inputs.Count * .98, "Saving images…"));
        }
        return tx.Publish(outputs, token);
    }

    // ---- Crop

    /// The whole-pixel crop, which must fit inside the image (macOS ImageEngine.preview for cropImage).
    public static MagickImage Crop(MagickImage image, Rect crop)
    {
        double x = Math.Floor(crop.X), y = Math.Floor(crop.Y), w = Math.Floor(crop.Width), h = Math.Floor(crop.Height);
        if (!new[] { x, y, w, h }.All(double.IsFinite)) throw new ArgumentException("Crop coordinates must be finite.");
        if (x < 0 || y < 0 || w < 1 || h < 1) throw new ArgumentException("Crop origins must be nonnegative and dimensions at least 1 × 1 pixel.");
        if (x > image.Width || y > image.Height || w > image.Width - x || h > image.Height - y)
            throw new ArgumentException(L10n.F("The crop rectangle must fit completely inside the {0} × {1} pixel image.", image.Width, image.Height));
        var result = (MagickImage)image.Clone();
        result.Crop(new MagickGeometry((int)x, (int)y, (uint)w, (uint)h));
        result.ResetPage();
        return result;
    }

    // ---- Edit Photo

    public static MagickImage Edit(MagickImage image, PhotoOptions o)
    {
        // Core's Adjust caps every value at ±100; hue runs ±180°, so it is applied here (Adjust's own formula).
        var adjusted = new MagickImage(PhotoCodec.Adjust(image.ToByteArray(MagickFormat.Png), o.Adjustments with { Hue = 0 }), new MagickReadSettings { Format = MagickFormat.Png });
        if (!double.IsFinite(o.Adjustments.Hue)) throw new ArgumentException("Choose finite photo adjustments within their ranges.");
        if (o.Adjustments.Hue != 0) adjusted.Modulate(new Percentage(100), new Percentage(100), new Percentage(100 + o.Adjustments.Hue / 1.8));
        if (o.FlipHorizontal) adjusted.Flop();
        if (o.FlipVertical) adjusted.Flip();
        var turn = ((o.Rotation % 360) + 360) % 360;
        if (turn != 0) adjusted.Rotate(turn);
        var (width, height) = TargetSize((int)adjusted.Width, (int)adjusted.Height, o.Scale, o.LongestEdge, o.Width, o.Height);
        if (width != adjusted.Width || height != adjusted.Height) adjusted.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
        return adjusted;
    }

    /// One named side keeps the aspect ratio; presets scale every image by its own size.
    public static (int Width, int Height) TargetSize(int w, int h, double? scale, int? longestEdge, int? width, int? height)
    {
        var factor = scale ?? (longestEdge is { } edge ? Math.Min(1, edge / (double)Math.Max(w, h)) : null);
        var size = factor is { } f ? (Math.Max(1, (int)Math.Round(w * f)), Math.Max(1, (int)Math.Round(h * f)))
            : (width, height) switch
            {
                ({ } a, { } b) => (a, b),
                ({ } a, null) => (a, Math.Max(1, (int)Math.Round(h * (double)a / w))),
                (null, { } b) => (Math.Max(1, (int)Math.Round(w * (double)b / h)), b),
                _ => (w, h),
            };
        if (size.Item1 < 1 || size.Item2 < 1 || size.Item1 > 30000 || size.Item2 > 30000 || (long)size.Item1 * size.Item2 > 100_000_000)
            throw new ArgumentException("Choose dimensions between 1 and 30,000 pixels (100 MP limit).");
        return size;
    }

    // ---- Redact Photo

    public static MagickImage Redact(MagickImage image, RedactOptions o)
    {
        if (o.Areas.Length == 0) throw new ArgumentException("Draw at least one area to redact.");
        if (!double.IsFinite(o.BlurRadius) || !double.IsFinite(o.PixelSize)) throw new ArgumentException("Redaction strength must be finite.");
        foreach (var r in o.Areas)
            if (!new[] { r.X, r.Y, r.Width, r.Height }.All(double.IsFinite) || r.X < 0 || r.Y < 0 || r.Width <= 0 || r.Height <= 0 || r.X > image.Width || r.Y > image.Height || r.Width > image.Width - r.X || r.Height > image.Height - r.Y)
                throw new ArgumentException(L10n.F("Every redaction area must have positive dimensions and fit completely inside the {0} × {1} pixel image. Adjust the areas for this image and try again.", image.Width, image.Height));
        var result = (MagickImage)image.Clone();
        foreach (var r in o.Areas)
        {
            // Integral bounds, as macOS: the whole of every touched pixel is covered.
            int x = (int)Math.Floor(r.X), y = (int)Math.Floor(r.Y);
            var w = (uint)Math.Min(image.Width - x, Math.Ceiling(r.Right) - x);
            var h = (uint)Math.Min(image.Height - y, Math.Ceiling(r.Bottom) - y);
            using var part = o.Style == "solid" ? new MagickImage(Opaque(o.Color), w, h) : (MagickImage)result.CloneArea(x, y, w, h);
            part.ResetPage();
            if (o.Style == "blur") part.Blur(0, Math.Max(1, o.BlurRadius));
            else if (o.Style == "pixelate")
            {
                var size = Math.Max(2, o.PixelSize);
                part.Sample((uint)Math.Max(1, Math.Ceiling(w / size)), (uint)Math.Max(1, Math.Ceiling(h / size)));
                part.Sample(w, h);
            }
            result.Composite(part, x, y, CompositeOperator.Copy);
        }
        return result;
    }

    // Solid redaction never lets the original show through an editor colour's alpha.
    static MagickColor Opaque(string hex)
    {
        var c = Color(hex);
        return MagickColor.FromRgb(c.Red, c.Green, c.Blue);
    }

    // ---- Compress

    /// Writes the compressed copy of `input` to `output` (macOS ImageEngine.compress): lower the quality, then the size,
    /// until the target fits; never a bigger file than the original when the size is unchanged.
    public static void Compress(string input, string output, string ext, CompressOptions o, CancellationToken token, IProgress<OperationProgress>? progress = null)
    {
        var original = new FileInfo(input).Length;
        if (o.ReductionPercent is { } percent && (!double.IsFinite(percent) || percent <= 0 || percent >= 100)) throw new ArgumentException("Enter a reduction greater than 0% and less than 100%.");
        if (o.TargetBytes is <= 0) throw new ArgumentException("Choose a positive target file size.");
        var target = o.TargetBytes ?? (o.ReductionPercent is { } p ? (long)(original * (1 - p / 100)) : null);
        if (target is <= 0) throw new ArgumentException("Choose a positive target file size.");
        using var image = PhotoCodec.Read(input);
        int sourceWidth = (int)image.Width, sourceHeight = (int)image.Height;
        var (width, height) = TargetSize(sourceWidth, sourceHeight, o.Scale, o.LongestEdge, o.Width, o.Height);
        if (width != sourceWidth || height != sourceHeight) image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
        var quality = Math.Clamp(o.Quality, 1, 100);
        var sourceExt = FeatureCatalog.Extension(input);
        for (var n = 0; n < 24; n++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new(null, target is null ? "Saving images…" : L10n.F("Fitting image · attempt {0}…", n + 1)));
            if (File.Exists(output)) File.Delete(output);
            using (var attempt = (MagickImage)image.Clone()) ImageOperations.Write(attempt, output, ext, quality, strip: true);
            var size = new FileInfo(output).Length;
            if (target is null || size <= target)
            {
                if (image.Width == sourceWidth && image.Height == sourceHeight && sourceExt == ext && original < size) File.Copy(input, output, true);
                return;
            }
            if (quality > 15 && ext != "png") quality = Math.Max(1, (int)(quality * .72));
            else image.Resize(new MagickGeometry((uint)Math.Max(1, image.Width * 8 / 10), (uint)Math.Max(1, image.Height * 8 / 10)) { IgnoreAspectRatio = true });
        }
        throw new IOException("The requested file size could not be reached. Choose a larger target.");
    }

    // ---- Read QR Codes

    /// The distinct QR texts in an image, in reading order.
    public static IReadOnlyList<string> QrCodes(MagickImage image)
    {
        using var copy = (MagickImage)image.Clone();
        copy.Alpha(AlphaAction.Remove);
        var pixels = copy.GetPixelsUnsafe().ToByteArray(PixelMapping.RGB) ?? [];
        var reader = new ZXing.BarcodeReaderGeneric { AutoRotate = true, Options = new ZXing.Common.DecodingOptions { TryHarder = true, PossibleFormats = [ZXing.BarcodeFormat.QR_CODE] } };
        var found = reader.DecodeMultiple(pixels, (int)copy.Width, (int)copy.Height, ZXing.RGBLuminanceSource.BitmapFormat.RGB24) ?? [];
        return found.OrderBy(r => r.ResultPoints.Length > 0 ? r.ResultPoints.Min(p => p.Y) : 0).Select(r => r.Text).Distinct().ToList();
    }

    // ---- Magick ↔ Skia

    /// Straight RGBA as a Skia bitmap.
    internal static SKBitmap ToSkia(MagickImage image)
    {
        using var copy = (MagickImage)image.Clone();
        copy.Alpha(AlphaAction.Set);
        var bytes = copy.GetPixelsUnsafe().ToByteArray(PixelMapping.RGBA) ?? throw new InvalidDataException("The image could not be read.");
        var bitmap = new SKBitmap(new SKImageInfo((int)copy.Width, (int)copy.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return bitmap;
    }

    /// A drawn canvas back as a Magick image (straight RGBA, sRGB).
    internal static MagickImage FromSkia(SKBitmap bitmap)
    {
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bytes = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.PeekPixels().ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes); }
        finally { handle.Free(); }
        var image = new MagickImage();
        image.ReadPixels(bytes, new PixelReadSettings((uint)bitmap.Width, (uint)bitmap.Height, StorageType.Char, PixelMapping.RGBA));
        image.ColorSpace = ColorSpace.sRGB;
        return image;
    }

    /// A premultiplied canvas of a size Nashi accepts.
    internal static SKBitmap Canvas(int width, int height)
    {
        if (width < 1 || height < 1 || width > 30000 || height > 30000 || (long)width * height > 100_000_000) throw new ArgumentException("Choose a canvas within 100 MP and 30,000 pixels.");
        return new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
    }

    /// "#RRGGBB" or "#RRGGBBAA" (the macOS editor's background hex).
    public static SKColor Color(string hex)
    {
        var digits = hex.TrimStart('#');
        if (digits.Length is not (6 or 8) || !uint.TryParse(digits, System.Globalization.NumberStyles.HexNumber, null, out var value))
            throw new ArgumentException("Choose a hex background color, such as #FFFFFF or #00FFFFFF.");
        return digits.Length == 6 ? new SKColor((byte)(value >> 16), (byte)(value >> 8), (byte)value) : new SKColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    /// Draws `image` centered in `area`, filling it (cropping the overflow) or fitting inside it.
    internal static void DrawFitted(SKCanvas canvas, SKBitmap image, SKRect area, bool fill)
    {
        var scale = fill ? Math.Max(area.Width / image.Width, area.Height / image.Height) : Math.Min(area.Width / image.Width, area.Height / image.Height);
        float w = image.Width * scale, h = image.Height * scale;
        canvas.Save();
        canvas.ClipRect(area, antialias: true);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        canvas.DrawBitmap(image, new SKRect(area.MidX - w / 2, area.MidY - h / 2, area.MidX + w / 2, area.MidY + h / 2), paint);
        canvas.Restore();
    }
}
