using Nashi.Core;

namespace Nashi.Linux;

/// What the Linux wheel offers for a selection: only the routes the Linux runner implements.
/// For files of one kind that is every catalog route except HEIC output (Magick.NET on Linux has no HEIC encoder),
/// and every image tool.
public static class Capabilities
{
    static readonly IReadOnlySet<string> SupportedTools = new HashSet<string> { "compress", "muteVideo", "normalizeAudio", "extractArchive", "createPDF", "mergePDF" };
    /// The image editors (Editors/*), offered when every file is an image.
    internal static readonly IReadOnlySet<string> ImageEditors = new HashSet<string> { "cropImage" };
    internal static readonly string[] ImageFormats = ["png", "jpg", "webp", "avif", "bmp", "gif", "tiff", "pdf"];
    static readonly string[] ArchiveOutputs = ["zip", "tar", "gz"];
    static readonly string[] SubtitleOutputs = ["srt", "vtt", "txt"];

    public static string[] Formats(IReadOnlyList<string> files)
    {
        var routes = Routes(files).ToHashSet();
        return FeatureCatalog.Formats(files).Where(routes.Contains).ToArray();
    }

    public static string[] Tools(IReadOnlyList<string> files) =>
        FeatureCatalog.Tools(files).Where(t => SupportedTools.Contains(t) || ImageEditors.Contains(t) && files.All(FeatureCatalog.IsImage)).ToArray();

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
