namespace Nashi.Linux;

/// Compact captions belong on petals; WheelItem.Title stays the full action name for the description and jobs.
public static class WheelCaptions
{
    private static readonly IReadOnlyDictionary<string, string> Short = new Dictionary<string, string>
    {
        ["action:copyPath"] = "Path", ["group:files"] = "Files", ["group:text & data"] = "Text/Data", ["group:markup"] = "Markup",
        ["group:rotate"] = "Transform", ["group:photo"] = "Photo",
        ["recognizeText"] = "OCR", ["group:read"] = "Read", ["readQRCode"] = "Scan QR", ["quick:removeBackground"] = "Cutout", ["frameImage"] = "Add BG",
        ["editImage"] = "Adjust", ["annotateImage"] = "Annotate", ["redactImage"] = "Redact",
        ["cropImage"] = "Crop", ["createCollage"] = "Collage", ["createPDF"] = "PDF", ["mergePDF"] = "Merge",
        ["quick:zip"] = "ZIP", ["quick:rotate"] = "90° R", ["quick:rotateLeft"] = "90° L",
        ["quick:flip"] = "Flip H", ["quick:flipVertical"] = "Flip V", ["quick:grayscale"] = "B&W",
        ["trimVideo"] = "Trim", ["trimAudio"] = "Trim", ["splitVideo"] = "Split", ["splitPDF"] = "Split",
        ["organizePDF"] = "Organize", ["cropVideo"] = "Crop", ["changeVideoSpeed"] = "Speed",
        ["joinVideos"] = "Join", ["videoSnapshots"] = "Frames", ["audioToVideo"] = "Visualize",
        ["normalizeAudio"] = "Level", ["muteVideo"] = "Mute", ["audioChannels"] = "Channels",
        ["redactAudio"] = "Bleep", ["redactVideo"] = "Redact", ["extractArchive"] = "Extract",
    };

    public static string For(WheelItem item) => Short.TryGetValue(item.Id, out var caption) ? L10n.T(caption) : item.Title;
}
