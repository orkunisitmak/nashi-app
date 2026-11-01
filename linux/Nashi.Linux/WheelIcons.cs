using Avalonia.Media;

namespace Nashi.Linux;

/// The web playground's 24×24 stroked icons (docs/assets/playground.js `icons`), and which one each wheel id shows.
/// Stroke 1.65 with round caps and joins, as the playground draws them. Format petals have none, as in the playground.
public static class WheelIcons
{
    public const double Stroke = 1.65;

    public static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>
    {
        ["previous"] = "M5 4v16 M19 4 7 12l12 8z",
        ["next"] = "M19 4v16 M5 4l12 8-12 8z",
        ["pause"] = "M8 5v14 M16 5v14",
        ["lock"] = "M6 10h12v10H6z M8 10V7a4 4 0 0 1 8 0v3",
        ["image"] = "M4 4h16v16H4z M4 16l5-5 4 4 3-3 4 5 M8 8h.01",
        ["video"] = "M3 5h13v14H3z M16 10l5-3v10l-5-3",
        ["audio"] = "M9 18V5l11-2v13 M9 7l11-2 M9 18c0 3-6 3-6 0s6-3 6 0 M20 16c0 3-6 3-6 0s6-3 6 0",
        ["file"] = "M5 3h9l5 5v13H5z M14 3v6h5 M8 13h8 M8 17h6",
        ["crop"] = "M7 3v14h14 M3 7h14v14",
        ["compress"] = "M4 4l6 6 M4 10h6V4 M20 20l-6-6 M14 20v-6h6",
        ["edit"] = "M4 6h16 M4 12h16 M4 18h16 M8 3v6 M16 9v6 M10 15v6",
        ["annotate"] = "M4 17l11-11 4 4L8 21H4z M14 7l3-3 4 4-3 3",
        ["frame"] = "M3 3h18v18H3z M7 7h10v10H7z",
        ["metadata"] = "M4 4h10l7 7-10 10L4 14z M9 9h.01",
        ["mute"] = "M4 9h4l5-5v16l-5-5H4z M17 9l5 6 M22 9l-5 6",
        ["trim"] = "M7 7l12 12 M7 17L19 5 M5 3a3 3 0 1 0 0 6 3 3 0 0 0 0-6 M5 15a3 3 0 1 0 0 6 3 3 0 0 0 0-6",
        ["split"] = "M11 4v16 M3 4h18v16H3z",
        ["merge"] = "M4 3h10v14H4z M9 7h10v14H9",
        ["speed"] = "M4 16a9 9 0 1 1 16 0 M12 12l5-5 M12 4v2 M5 9l2 1 M19 9l-2 1",
        ["snapshot"] = "M3 6h5l2-3h4l2 3h5v14H3z M16 13a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
        ["redact"] = "M3 3l18 18 M4 8c-2 2-3 4-3 4s4 7 11 7c2 0 4-1 6-2 M8 5l4-1c7 0 11 8 11 8l-3 4",
        ["qr"] = "M3 8V3h5 M16 3h5v5 M21 16v5h-5 M8 21H3v-5 M7 7h3v3H7z M14 7h3v3h-3z M7 14h3v3H7z M14 14h3v3h-3z",
        ["archive"] = "M4 3h16v18H4z M10 3v3h4v3h-4v3h4v3h-4v3",
        ["copy"] = "M8 8h13v13H8z M16 8V3H3v13h5",
        ["float"] = "M3 6h18v15H3z M3 10h18 M17 3v8 M14 6l3-3 3 3",
        ["move"] = "M3 6h7l2-3h9v17H3z M6 12h11 M13 9l4 3-4 3",
        ["path"] = "M9 15l6-6 M7 11l-3 3a4 4 0 0 0 6 6l3-3 M11 7l3-3a4 4 0 0 1 6 6l-3 3",
        ["terminal"] = "M3 4h18v16H3z M6 8l4 4-4 4 M13 16h5",
        ["share"] = "M12 16V2 M7 7l5-5 5 5 M5 12H3v10h18V12h-2",
        ["download"] = "M12 3v12 M7 10l5 5 5-5 M4 17v4h16v-4",
        ["play"] = "M8 4l13 8-13 8z",
        ["library"] = "M4 3h4v18H4z M10 3h4v18h-4z M17 3l4 17-4 1-4-17",
        ["check"] = "M4 12l5 5L20 6",
    };

    // playground.js `tools`, the actions wheel and the paging petals. "convert" is Linux-only (macOS opens it with a modifier).
    static readonly Dictionary<string, string> ById = new()
    {
        ["compress"] = "compress",
        ["removeMetadata"] = "metadata",
        ["editImage"] = "edit",
        ["annotateImage"] = "annotate",
        ["frameImage"] = "frame",
        ["cropImage"] = "crop",
        ["normalizeAudio"] = "audio",
        ["muteVideo"] = "mute",
        ["trimVideo"] = "trim",
        ["splitPDF"] = "split",
        ["organizePDF"] = "file",
        ["extractArchive"] = "archive",
        ["createPDF"] = "merge",
        ["mergePDF"] = "merge",
        ["cropVideo"] = "crop",
        ["changeVideoSpeed"] = "speed",
        ["joinVideos"] = "merge",
        ["videoSnapshots"] = "snapshot",
        ["splitVideo"] = "split",
        ["audioToVideo"] = "video",
        ["trimAudio"] = "trim",
        ["audioChannels"] = "audio",
        ["createCollage"] = "image",
        ["redactImage"] = "redact",
        ["redactVideo"] = "redact",
        ["redactAudio"] = "mute",
        ["readQRCode"] = "qr",
        ["convert"] = "file",
        ["action:share"] = "share", ["action:float"] = "float", ["action:copy"] = "copy", ["action:move"] = "move", ["action:copyPath"] = "path", ["action:terminal"] = "terminal",
        ["page:previous"] = "previous", ["page:next"] = "next",
        ["group:background"] = "frame", ["group:photo"] = "edit", ["group:rotate"] = "edit",
        ["group:markup"] = "annotate", ["group:create"] = "merge", ["group:more"] = "edit",
        ["group:files"] = "move", ["group:trim"] = "trim", ["group:audio"] = "audio",
        ["group:edit"] = "edit", ["group:frames"] = "snapshot", ["group:pages"] = "file", ["group:extract"] = "archive",
        ["group:text & data"] = "file", ["group:images"] = "image", ["group:video"] = "video", ["group:documents"] = "file", ["group:archives"] = "archive",
        ["quick:rotate"] = "edit", ["quick:rotateLeft"] = "edit", ["quick:flip"] = "edit", ["quick:flipVertical"] = "edit",
        ["quick:grayscale"] = "image", ["quick:removeBackground"] = "redact", ["quick:zip"] = "archive",
    };

    /// The icon key for a wheel item id, or null (formats, unknown ids).
    public static string? Key(string id) => ById.GetValueOrDefault(id);

    static readonly Dictionary<string, Geometry> parsed = [];

    /// The icon's path in its 24×24 box, or null when the id has none.
    public static Geometry? ForId(string id) => Key(id) is { } key ? Named(key) : null;

    /// A file card's type icon when there is no thumbnail, as the playground's file sheet: image, video, audio or file.
    public static Geometry ForFile(string path) => Named(
        Nashi.Core.FeatureCatalog.IsImage(path) ? "image" : Nashi.Core.FeatureCatalog.IsVideo(path) ? "video" : Nashi.Core.FeatureCatalog.IsAudio(path) ? "audio" : "file");

    static Geometry Named(string key) => parsed.TryGetValue(key, out var g) ? g : parsed[key] = StreamGeometry.Parse(Paths[key]);
}
