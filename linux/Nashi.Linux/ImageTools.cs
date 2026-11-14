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

/// Redact Photo: areas in source pixels, covered solid, blurred or pixelated (macOS ImageRedactionOptions). Blur
/// and blocks grow with the area (a third of its short side at strength 1), never below BlurRadius and
/// PixelSize, so large text or faces don't stay legible.
public sealed record RedactOptions
{
    public string Style { get; init; } = "solid";
    public string Color { get; init; } = "#000000";
    public double BlurRadius { get; init; } = 24;
    public double PixelSize { get; init; } = 16;
    /// Scales the region-sized part of the blur and the blocks: 1 (strong) by default.
    public double Strength { get; init; } = 1;
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

    /// One edited copy per input beside the others in one transaction: "name{suffix}.ext". `work` may return the
    /// image it is given, changed, or dispose it once it has a copy (both are disposed after the write).
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
            // Metadata goes, the colour profile stays (as Convert does), so wide-gamut photos keep their colours.
            ImageOperations.Write(result, output, Path.GetExtension(output)[1..], 92, strip: false);
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

    /// Edit Photo on a copy of `image`: Core's PhotoCodec.Adjust steps, in its order, on the decoded pixels (no PNG
    /// round trip), with hue allowed to ±180°; then flip, turn and size.
    public static MagickImage Edit(MagickImage image, PhotoOptions o)
    {
        var adjusted = (MagickImage)image.Clone();
        try
        {
            Adjust(adjusted, o.Adjustments);
            if (o.FlipHorizontal) adjusted.Flop();
            if (o.FlipVertical) adjusted.Flip();
            var turn = ((o.Rotation % 360) + 360) % 360;
            if (turn != 0) adjusted.Rotate(turn);
            var (width, height) = TargetSize((int)adjusted.Width, (int)adjusted.Height, o.Scale, o.LongestEdge, o.Width, o.Height);
            if (width != adjusted.Width || height != adjusted.Height) adjusted.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
            return adjusted;
        }
        catch { adjusted.Dispose(); throw; }
    }

    /// PhotoCodec.Adjust in place: the same operations and formulas, one row of pixels at a time.
    static void Adjust(MagickImage image, PhotoAdjustments o)
    {
        foreach (var property in typeof(PhotoAdjustments).GetProperties())
        {
            var n = (double)property.GetValue(o)!;
            if (!double.IsFinite(n) || Math.Abs(n) > (property.Name == nameof(PhotoAdjustments.Hue) ? 180 : 100)) throw new ArgumentException("Choose finite photo adjustments within their ranges.");
        }
        if (o.Gamma <= 0 || o.Contrast < 0 || o.Saturation < 0) throw new ArgumentException("Gamma must be positive; contrast and saturation cannot be negative.");
        if (o.Exposure != 0) image.Evaluate(Channels.RGB, EvaluateOperator.Multiply, Math.Pow(2, o.Exposure));
        image.BrightnessContrast(new Percentage(o.Brightness * 100), new Percentage((o.Contrast - 1) * 50));
        if (o.Gamma != 1) image.GammaCorrect(o.Gamma, Channels.RGB);
        if (o.Shadows != 0 || o.Highlights != 0 || o.Whites != 0 || o.Blacks != 0 || o.Saturation != 1 || o.Vibrance != 0 || o.Temperature != 0 || o.Tint != 0 || o.Fade != 0)
        {
            var pixels = image.GetPixelsUnsafe();
            var width = image.Width;
            var fade = Math.Clamp(o.Fade, 0, 1) * .3;
            static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
            for (var y = 0; y < image.Height; y++)
            {
                var row = pixels.ToByteArray(0, y, width, 1, PixelMapping.RGB)!;
                for (var i = 0; i < row.Length; i += 3)
                {
                    double r = row[i] / 255d, g = row[i + 1] / 255d, b = row[i + 2] / 255d;
                    var lum = .2126 * r + .7152 * g + .0722 * b;
                    var tone = o.Shadows * Math.Pow(1 - lum, 2) * .3 + o.Highlights * lum * lum * .3 + o.Whites * Math.Pow(lum, 4) * .3 + o.Blacks * Math.Pow(1 - lum, 4) * .3;
                    var saturation = o.Saturation + o.Vibrance * (1 - (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))));
                    (r, g, b) = (lum + (r - lum) * saturation + tone + o.Temperature * .12 + o.Tint * .06, lum + (g - lum) * saturation + tone - o.Tint * .1, lum + (b - lum) * saturation + tone - o.Temperature * .12 + o.Tint * .06);
                    row[i] = Byte(r * (1 - fade) + fade);
                    row[i + 1] = Byte(g * (1 - fade) + fade);
                    row[i + 2] = Byte(b * (1 - fade) + fade);
                }
                image.ImportPixels(row, new PixelImportSettings(0, y, width, 1, StorageType.Char, PixelMapping.RGB));
            }
        }
        if (o.Hue != 0) image.Modulate(new Percentage(100), new Percentage(100), new Percentage(100 + o.Hue / 1.8));
        if (o.Dehaze != 0) image.BrightnessContrast(new Percentage(-o.Dehaze * 8), new Percentage(o.Dehaze * 25));
        if (o.NoiseReduction > 0) image.MedianFilter((uint)Math.Clamp(Math.Ceiling(o.NoiseReduction * 2), 1, 3));
        if (o.Blur > 0) image.Blur(0, Math.Min(50, o.Blur));
        if (o.Sharpen > 0 || o.Clarity > 0 || o.Texture > 0) image.UnsharpMask(0, 1 + Math.Max(0, o.Clarity) * 3, Math.Max(0, o.Sharpen) + Math.Max(0, o.Texture), .02);
        if (o.Grain > 0) image.AddNoise(NoiseType.Gaussian, Math.Min(1, o.Grain) * .15, Channels.RGB);
        if (o.Vignette > 0) image.Vignette(0, Math.Max(1, Math.Min(image.Width, image.Height) * o.Vignette * .25), 0, 0);
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
        if (!double.IsFinite(o.BlurRadius) || !double.IsFinite(o.PixelSize) || !double.IsFinite(o.Strength) || o.Strength <= 0) throw new ArgumentException("Redaction strength must be finite.");
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
            var side = Math.Min(w, h);
            if (o.Style == "blur") Blur(part, Math.Max(Math.Max(1, o.BlurRadius), side / 3.0 * o.Strength));
            else if (o.Style == "pixelate")
            {
                var size = Math.Max(Math.Max(2, o.PixelSize), side / 3.0 * o.Strength);
                // Each block is the average of its pixels (as CIPixellate), not one sampled pixel, which could keep a
                // stroke's full contrast.
                part.Scale(new MagickGeometry((uint)Math.Max(1, Math.Ceiling(w / size)), (uint)Math.Max(1, Math.Ceiling(h / size))) { IgnoreAspectRatio = true });
                part.Sample(new MagickGeometry(w, h) { IgnoreAspectRatio = true });
            }
            result.Composite(part, x, y, CompositeOperator.Copy);
        }
        return result;
    }

    /// A Gaussian blur of any size: past sigma 16 it blurs a smaller copy and scales it back, which looks the same and
    /// keeps a 4000 px area from taking minutes.
    static void Blur(MagickImage part, double sigma)
    {
        if (sigma <= 16) { part.Blur(0, sigma); return; }
        uint w = part.Width, h = part.Height;
        var k = sigma / 8;
        part.Resize(new MagickGeometry((uint)Math.Max(1, Math.Round(w / k)), (uint)Math.Max(1, Math.Round(h / k))) { IgnoreAspectRatio = true });
        part.Blur(0, 8);
        part.Resize(new MagickGeometry(w, h) { IgnoreAspectRatio = true });
    }

    // Solid redaction never lets the original show through an editor colour's alpha.
    static MagickColor Opaque(string hex)
    {
        var c = Color(hex);
        return MagickColor.FromRgb(c.Red, c.Green, c.Blue);
    }

    // ---- Compress

    /// Writes the compressed copy of `input` to `output` (macOS ImageEngine.compress): lower the quality, then the size,
    /// until the target fits; never a bigger JPG or PNG than the original when the size is unchanged.
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
                // The original is smaller: its own bytes, with the metadata cut out (never GPS or EXIF in a "stripped"
                // copy). Formats without a lossless cut keep the attempt.
                if (image.Width == sourceWidth && image.Height == sourceHeight && sourceExt == ext && original < size && StrippedBytes(File.ReadAllBytes(input), ext) is { } clean) File.WriteAllBytes(output, clean);
                return;
            }
            if (quality > 15 && ext != "png") quality = Math.Max(1, (int)(quality * .72));
            else image.Resize(new MagickGeometry((uint)Math.Max(1, image.Width * 8 / 10), (uint)Math.Max(1, image.Height * 8 / 10)) { IgnoreAspectRatio = true });
        }
        throw new IOException("The requested file size could not be reached. Choose a larger target.");
    }

    /// A JPG or PNG without its metadata, losslessly: JPEG APP1 (EXIF, XMP), APP3–APP13 (IPTC…), APP15 and comments
    /// go, the JFIF, ICC and Adobe segments stay; PNG eXIf, tEXt, zTXt, iTXt and tIME chunks go. Null for another
    /// format or a file that doesn't parse.
    public static byte[]? StrippedBytes(byte[] data, string ext)
    {
        try
        {
            using var output = new MemoryStream();
            if (ext == "jpg")
            {
                if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return null;
                output.Write(data, 0, 2);
                var i = 2;
                while (i + 1 < data.Length)
                {
                    if (data[i] != 0xFF) return null;
                    var marker = data[i + 1];
                    if (marker == 0xFF) { i++; continue; }
                    if (marker == 0xDA) { output.Write(data, i, data.Length - i); return output.ToArray(); } // scan data to the end
                    if (marker is >= 0xD0 and <= 0xD7 or 0x01) { output.Write(data, i, 2); i += 2; continue; }
                    var length = data[i + 2] << 8 | data[i + 3];
                    if (length < 2 || i + 2 + length > data.Length) return null;
                    var icc = marker == 0xE2 && length >= 14 && System.Text.Encoding.ASCII.GetString(data, i + 4, 12) == "ICC_PROFILE\0";
                    var keep = marker is not (>= 0xE1 and <= 0xEF or 0xFE) || icc || marker == 0xEE;
                    if (keep) output.Write(data, i, 2 + length);
                    i += 2 + length;
                }
                return null;
            }
            if (ext == "png")
            {
                byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
                if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(signature)) return null;
                output.Write(data, 0, 8);
                var i = 8;
                while (i + 12 <= data.Length)
                {
                    var length = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i));
                    if (length < 0 || i + 12 + length > data.Length) return null;
                    var type = System.Text.Encoding.ASCII.GetString(data, i + 4, 4);
                    if (type is not ("eXIf" or "tEXt" or "zTXt" or "iTXt" or "tIME")) output.Write(data, i, 12 + length);
                    i += 12 + length;
                    if (type == "IEND") return output.ToArray();
                }
                return null;
            }
            return null;
        }
        catch (ArgumentException) { return null; }
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

    /// The image as a premultiplied Skia bitmap, copied a row at a time (no second full-size buffer).
    internal static SKBitmap ToSkia(MagickImage image)
    {
        int width = (int)image.Width, height = (int)image.Height;
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var pixels = image.GetPixelsUnsafe();
        var alpha = image.HasAlpha;
        var row = new byte[width * 4];
        var target = bitmap.GetPixels();
        for (var y = 0; y < height; y++)
        {
            var data = pixels.ToByteArray(0, y, (uint)width, 1, alpha ? PixelMapping.RGBA : PixelMapping.RGB) ?? throw new InvalidDataException("The image could not be read.");
            for (int x = 0, from = 0; x < width; x++)
            {
                var a = alpha ? data[from + 3] : (byte)255;
                row[x * 4] = (byte)(data[from] * a / 255);
                row[x * 4 + 1] = (byte)(data[from + 1] * a / 255);
                row[x * 4 + 2] = (byte)(data[from + 2] * a / 255);
                row[x * 4 + 3] = a;
                from += alpha ? 4 : 3;
            }
            Marshal.Copy(row, 0, target + y * bitmap.RowBytes, row.Length);
        }
        return bitmap;
    }

    /// A drawn canvas back as a Magick image (straight RGBA, sRGB), a row at a time.
    internal static MagickImage FromSkia(SKBitmap bitmap)
    {
        var image = new MagickImage(MagickColors.Transparent, (uint)bitmap.Width, (uint)bitmap.Height);
        var info = new SKImageInfo(bitmap.Width, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var row = new byte[info.RowBytes];
        var handle = GCHandle.Alloc(row, GCHandleType.Pinned);
        try
        {
            using var source = bitmap.PeekPixels();
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (!source.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, y)) throw new InvalidDataException("The image could not be read.");
                image.ImportPixels(row, new PixelImportSettings(0, y, (uint)bitmap.Width, 1, StorageType.Char, PixelMapping.RGBA));
            }
        }
        finally { handle.Free(); }
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
