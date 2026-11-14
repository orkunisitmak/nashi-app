using Nashi.Core;

namespace Nashi.Linux;

/// What the Linux wheel offers for a selection. Every catalog tool, as on macOS (each opens its editor or runs); for
/// files of one kind every catalog route, using FFmpeg for HEIC encoding.
public static class Capabilities
{
    internal static readonly string[] ImageFormats = ["png", "jpg", "webp", "heic", "avif", "bmp", "gif", "tiff", "pdf"];
    static readonly string[] ArchiveOutputs = ["zip", "tar", "gz"];
    static readonly string[] SubtitleOutputs = ["srt", "vtt", "txt"];

    public static IEnumerable<string> InputExtensions => FeatureCatalog.Conversions.Keys.Concat(["aac", "m4v"]);
    internal static readonly string[] AudioOutputs = ["mp3", "wav", "flac", "aac", "m4a", "ogg", "opus", "aiff", "wma"];
    internal static readonly string[] VideoOutputs = ["mp4", "mov", "mkv", "webm", "avi", "wmv", "gif"];

    /// Targets for the entire selection first, followed by explicitly counted partial-selection targets.
    public static string[] Formats(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return [];
        return files.SelectMany(Targets).Distinct()
            .Where(format => !files.All(file => FeatureCatalog.Extension(file) == format))
            .OrderByDescending(format => ConversionInputs(files, format).Count).ToArray();
    }

    /// Files this target can process. An already-target-format input is retained as a fresh copy in a mixed batch.
    public static IReadOnlyList<string> ConversionInputs(IReadOnlyList<string> files, string format) =>
        files.Where(file => Targets(file).Contains(format)
            || !Directory.Exists(file) && FeatureCatalog.Extension(file) == format && InputExtensions.Contains(format)).ToArray();

    static IEnumerable<string> Targets(string file)
    {
        if (Directory.Exists(file) && FeatureCatalog.Extension(file) != "rtfd") return [];
        if (FeatureCatalog.IsImage(file)) return ImageFormats;
        if (FeatureCatalog.IsAudio(file)) return AudioOutputs;
        if (FeatureCatalog.IsVideo(file)) return VideoOutputs
            .Concat(FeatureCatalog.Extension(file) == "gif" ? [] : AudioOutputs).Concat(["jpg", "png"]);
        return FeatureCatalog.Formats([file]).Where(Routes([file]).Contains);
    }

    public static string[] Tools(IReadOnlyList<string> files) => files.Any(Directory.Exists) ? [] : FeatureCatalog.Tools(
        files.Select(file => FeatureCatalog.Extension(file) switch
        {
            "aac" => Path.ChangeExtension(file, "m4a"), "m4v" => Path.ChangeExtension(file, "mp4"), _ => file,
        }).ToArray());

    /// The wheel's quick tools (its back face), for what fits: crop (its editor), rotate 90°, flip, grayscale and
    /// remove background for images; one PDF from images or PDFs; one ZIP of anything.
    public static string[] QuickTools(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return [];
        var tools = Tools(files);
        var quick = new List<string>();
        if (files.All(f => !Directory.Exists(f) && FeatureCatalog.IsImage(f) && FeatureCatalog.Extension(f) != "svg"))
        {
            if (tools.Contains("cropImage")) quick.Add("cropImage");
            quick.AddRange(["quick:rotate", "quick:rotateLeft", "quick:flip", "quick:flipVertical", "quick:grayscale", "quick:removeBackground"]);
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
