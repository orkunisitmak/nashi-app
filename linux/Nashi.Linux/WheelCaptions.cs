namespace Nashi.Linux;

/// Compact captions belong on petals; WheelItem.Title stays the full action name for the description and jobs.
public static class WheelCaptions
{
    private static readonly IReadOnlyDictionary<string, string> Short = new Dictionary<string, string>
    {
        ["group:files"] = "Files", ["group:text & data"] = "Text/Data", ["group:markup"] = "Markup",
        ["group:rotate"] = "Transform", ["group:photo"] = "Photo",
        ["readQRCode"] = "Scan QR", ["quick:removeBackground"] = "Remove BG", ["frameImage"] = "Add BG",
        ["editImage"] = "Adjust", ["annotateImage"] = "Annotate", ["redactImage"] = "Redact",
        ["cropImage"] = "Crop", ["createCollage"] = "Collage", ["createPDF"] = "PDF", ["mergePDF"] = "Merge",
        ["quick:zip"] = "ZIP", ["quick:rotate"] = "Right 90°", ["quick:rotateLeft"] = "Left 90°",
        ["quick:flip"] = "Flip H", ["quick:flipVertical"] = "Flip V", ["quick:grayscale"] = "Grayscale",
        ["trimVideo"] = "Trim", ["trimAudio"] = "Trim", ["splitVideo"] = "Split", ["splitPDF"] = "Split",
        ["organizePDF"] = "Organize", ["cropVideo"] = "Crop", ["changeVideoSpeed"] = "Speed",
        ["joinVideos"] = "Join", ["videoSnapshots"] = "Snapshots", ["audioToVideo"] = "Visualize",
        ["normalizeAudio"] = "Normalize", ["muteVideo"] = "Mute", ["audioChannels"] = "Channels",
        ["redactAudio"] = "Bleep", ["redactVideo"] = "Redact", ["extractArchive"] = "Extract",
    };

    public static string For(WheelItem item) => Short.TryGetValue(item.Id, out var caption) ? L10n.T(caption) : item.Title;
}
