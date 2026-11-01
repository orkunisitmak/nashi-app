using Nashi.Core;

namespace Nashi.Linux;

/// What the Linux wheel offers for a selection. Every catalog tool, as on macOS (each opens its editor or runs); for
/// files of one kind every catalog route except HEIC output (Magick.NET on Linux has no HEIC encoder).
public static class Capabilities
{
    internal static readonly string[] ImageFormats = ["png", "jpg", "webp", "avif", "bmp", "gif", "tiff", "pdf"];
    static readonly string[] ArchiveOutputs = ["zip", "tar", "gz"];
    static readonly string[] SubtitleOutputs = ["srt", "vtt", "txt"];

    public static string[] Formats(IReadOnlyList<string> files)
    {
        var routes = Routes(files).ToHashSet();
        return FeatureCatalog.Formats(files).Where(routes.Contains).ToArray();
    }

    public static string[] Tools(IReadOnlyList<string> files) => FeatureCatalog.Tools(files);

    /// The wheel's quick tools (its back face), for what fits: crop (its editor), rotate 90°, flip, grayscale and
    /// remove background for images; one PDF from images or PDFs; one ZIP of anything.
    public static string[] QuickTools(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return [];
        var tools = Tools(files);
        var quick = new List<string>();
        if (files.All(f => FeatureCatalog.IsImage(f) && FeatureCatalog.Extension(f) != "svg"))
        {
            if (tools.Contains("cropImage")) quick.Add("cropImage");
            quick.AddRange(["quick:rotate", "quick:flip", "quick:grayscale", "quick:removeBackground"]);
        }
        if (tools.Contains("createPDF")) quick.Add("createPDF");
        else if (tools.Contains("mergePDF")) quick.Add("mergePDF");
        quick.Add("quick:zip");
        return [.. quick];
    }

    static IEnumerable<string> Routes(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return [];
        if (files.All(FeatureCatalog.IsImage)) return ImageFormats;
        if (files.All(p => FeatureCatalog.IsAudio(p) || FeatureCatalog.IsVideo(p)))
            return MediaEngine.AudioFormats.Concat(MediaEngine.VideoFormats);
        if (files.All(FeatureCatalog.IsArchive)) return ArchiveOutputs;
        // Every document route is offered whether or not LibreOffice is installed: choosing one without it opens setup,
        // which says how to install it. A hidden route would leave a PDF or DOCX with an empty wheel.
        if (files.All(DocumentOperations.Handles)) return FeatureCatalog.Formats(files);
        if (files.All(p => FeatureCatalog.Extension(p) is "srt" or "vtt")) return SubtitleOutputs;
        return [];
    }
}
