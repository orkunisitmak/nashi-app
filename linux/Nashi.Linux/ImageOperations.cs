using ImageMagick;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Nashi.Linux;

/// Image convert and compress for the Linux wheel. Outputs match the Windows engine's naming.
public static class ImageOperations
{
    public static IReadOnlyList<string> Convert(IReadOnlyList<string> inputs, string format, string destination, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        if (inputs.Count == 0) throw new ArgumentException("Choose at least one image.");
        format = format.ToLowerInvariant();
        if (!Capabilities.ImageFormats.Contains(format)) throw new NotSupportedException($"Converting to {format} is not supported on Linux.");
        using var tx = new ExportTransaction(destination);
        token.ThrowIfCancellationRequested();
        if (format == "pdf")
        {
            var file = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(inputs[0]) + ".pdf");
            using var pdf = new PdfDocument();
            for (var n = 0; n < inputs.Count; n++)
            {
                token.ThrowIfCancellationRequested();
                using var image = PhotoCodec.Read(inputs[n]);
                using var png = new MemoryStream(image.ToByteArray(MagickFormat.Png));
                using var xImage = XImage.FromStream(png);
                var page = pdf.AddPage();
                page.Width = XUnit.FromPoint(image.Width * 72 / 96d);
                page.Height = XUnit.FromPoint(image.Height * 72 / 96d);
                using var g = XGraphics.FromPdfPage(page);
                g.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
                progress?.Report(new((n + 1) / (double)inputs.Count * .98, "Adding pages…"));
            }
            pdf.Save(file);
            return tx.Publish([file], token);
        }
        var outputs = new List<string>();
        for (var n = 0; n < inputs.Count; n++)
        {
            token.ThrowIfCancellationRequested();
            using var image = PhotoCodec.Read(inputs[n]);
            var output = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(inputs[n]) + "." + format);
            Write(image, output, format, 85, strip: false);
            outputs.Add(output);
            progress?.Report(new((n + 1) / (double)inputs.Count * .98, "Saving images…"));
        }
        return tx.Publish(outputs, token);
    }

    public static IReadOnlyList<string> Compress(IReadOnlyList<string> inputs, string destination, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        if (inputs.Count == 0) throw new ArgumentException("Choose at least one image.");
        using var tx = new ExportTransaction(destination);
        var outputs = new List<string>();
        for (var n = 0; n < inputs.Count; n++)
        {
            token.ThrowIfCancellationRequested();
            var input = inputs[n];
            var sourceExt = FeatureCatalog.Extension(input);
            if (sourceExt == "jpeg") sourceExt = "jpg";
            var ext = sourceExt;
            if (ext is not ("jpg" or "png" or "webp" or "heic" or "avif") || !Capabilities.ImageFormats.Contains(ext)) ext = "jpg";
            var output = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(input) + " Compressed." + ext);
            using (var image = PhotoCodec.Read(input)) Write(image, output, ext, 70, strip: true);
            // Never hand back a bigger file than the original.
            if (sourceExt == ext && new FileInfo(input).Length < new FileInfo(output).Length) File.Copy(input, output, true);
            outputs.Add(output);
            progress?.Report(new((n + 1) / (double)inputs.Count * .98, "Saving images…"));
        }
        return tx.Publish(outputs, token);
    }

    internal static void Write(MagickImage image, string output, string format, int quality, bool strip)
    {
        // Convert drops EXIF/GPS but keeps the colour profile so colours don't shift; Compress drops everything.
        var icc = strip ? null : image.GetColorProfile();
        image.Strip();
        if (icc is not null) image.SetProfile(icc);
        // JPEG and BMP have no usable alpha; flatten onto white rather than turning transparency black.
        if (format is "jpg" or "bmp" && image.HasAlpha) image.ColorAlpha(MagickColors.White);
        image.Quality = (uint)quality;
        // Through a stream: ImageMagick reads "name:" in a path as a format prefix, so "-ğüş: photo.png" would go astray.
        using var file = new FileStream(output, FileMode.Create, FileAccess.Write);
        image.Write(file, PhotoCodec.Format(format));
    }
}
