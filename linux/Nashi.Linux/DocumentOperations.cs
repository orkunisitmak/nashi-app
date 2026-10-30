using Nashi.Core;

namespace Nashi.Linux;

/// PDF and document conversions: the Linux port of Windows' DocumentConversion.
public static class DocumentOperations
{
    const int Dpi = 144;

    /// Inputs this class converts.
    public static bool Handles(string path) => FeatureCatalog.Extension(path) == "pdf";

    /// One image per page, named "<name> Page N" like Windows.
    public static async Task<IReadOnlyList<string>> ConvertAsync(IReadOnlyList<string> inputs, string format, string destination, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        if (format is not ("png" or "jpg")) throw new NotSupportedException($"Converting to {format} is not supported on Linux.");
        using var tx = new ExportTransaction(destination);
        var outputs = new List<string>();
        for (var i = 0; i < inputs.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var input = inputs[i];
            progress?.Report(new(null, $"Preparing {Path.GetFileName(input)}…"));
            var pages = await PdfRaster.RenderAsync(input, Dpi, token);
            for (var page = 0; page < pages.Length; page++)
            {
                token.ThrowIfCancellationRequested();
                var output = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(input) + $" Page {page + 1}." + format);
                if (format == "png") await File.WriteAllBytesAsync(output, pages[page], token);
                else PhotoCodec.EncodePng(pages[page], output, "jpg", 90);
                outputs.Add(output);
                progress?.Report(new((i + (page + 1d) / pages.Length) / inputs.Count * .98, $"Rendering page {page + 1}"));
            }
        }
        return tx.Publish(outputs, token);
    }
}
