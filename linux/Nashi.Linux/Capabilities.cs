using Nashi.Core;

namespace Nashi.Linux;

/// What the Linux wheel offers for a selection: only the routes the Linux runner implements.
public static class Capabilities
{
    static readonly IReadOnlySet<string> SupportedTools = new HashSet<string> { "compress", "muteVideo", "normalizeAudio", "extractArchive", "createPDF", "mergePDF" };
    static readonly string[] ImageOutputs = ["png", "jpg", "webp", "heic", "avif", "bmp", "gif", "tiff", "pdf"];
    static readonly string[] ArchiveOutputs = ["zip", "tar", "gz"];
    static readonly string[] SubtitleOutputs = ["srt", "vtt", "txt"];

    public static string[] Formats(IReadOnlyList<string> files)
    {
        var routes = Routes(files).ToHashSet();
        return FeatureCatalog.Formats(files).Where(routes.Contains).ToArray();
    }

    public static string[] Tools(IReadOnlyList<string> files) =>
        FeatureCatalog.Tools(files).Where(SupportedTools.Contains).ToArray();

    static IEnumerable<string> Routes(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return [];
        if (files.All(FeatureCatalog.IsImage)) return ImageOutputs;
        if (files.All(p => FeatureCatalog.IsAudio(p) || FeatureCatalog.IsVideo(p)))
            return MediaEngine.AudioFormats.Concat(MediaEngine.VideoFormats);
        if (files.All(FeatureCatalog.IsArchive)) return ArchiveOutputs;
        if (files.All(p => FeatureCatalog.Extension(p) is "srt" or "vtt")) return SubtitleOutputs;
        return [];
    }
}
