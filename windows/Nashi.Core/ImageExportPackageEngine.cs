using ImageMagick;
namespace Nashi.Core;

public static class ImageExportPackageEngine
{
    public static IReadOnlyList<string> Run(IReadOnlyList<string> inputs, ImageExportPackage package, string destination,
        IProgress<OperationProgress>? progress = null, CancellationToken token = default)
    {
        var plan = package.Plan(inputs);
        foreach (var input in inputs)
            if (!File.Exists(input)) throw new ArgumentException("Choose existing image files for the package.");
        token.ThrowIfCancellationRequested();
        using var transaction = new ExportTransaction(destination);
        var root = Path.Combine(transaction.StagingDirectory, package.Name);
        var completed = 0;
        for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
        {
            token.ThrowIfCancellationRequested();
            using var source = PhotoCodec.Read(inputs[inputIndex]);
            for (var variantIndex = 0; variantIndex < package.Variants.Length; variantIndex++)
            {
                token.ThrowIfCancellationRequested();
                var variant = package.Variants[variantIndex];
                var entry = plan[inputIndex * package.Variants.Length + variantIndex];
                var output = Path.Combine(root, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var image = source.Clone();
                var size = variant.Dimensions((int)source.Width, (int)source.Height);
                if (size.Width != source.Width || size.Height != source.Height)
                    image.Resize(new MagickGeometry((uint)size.Width, (uint)size.Height) { IgnoreAspectRatio = true });
                if (variant.Format == "jpg") { image.BackgroundColor = MagickColors.White; image.Alpha(AlphaAction.Remove); }
                image.Strip(); image.Quality = 85;
                image.Write(output, PhotoCodec.Format(variant.Format));
                if (new FileInfo(output).Length == 0) throw new InvalidDataException("An empty output was produced.");
                progress?.Report(new(++completed / (double)plan.Count * .98, "Preparing package outputs…"));
                token.ThrowIfCancellationRequested();
            }
        }
        progress?.Report(new(.98, "Saving your files…"));
        // Publishing the one root keeps all variants together if its name is already occupied.
        var published = transaction.Publish([root], token)[0];
        return plan.Select(entry => Path.Combine(published, entry.RelativePath)).ToArray();
    }
}
