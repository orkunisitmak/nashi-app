using Nashi.Core;

namespace Nashi.Linux;

/// The Tools face: quick operations and editors share contextual groups, with file actions in one outer ring.
public static class WheelMenu
{
    private static class Copy
    {
        public const string Background = "Background";
        public const string MarkUp = "Mark Up";
        public const string Create = "Create";
        public const string Edit = "Edit";
        public const string Frames = "Frames";
        public const string Pages = "Pages";
        public const string Extract = "Extract";
        public const string FileActions = "File Actions";
        public const string Audio = "Audio";
        public const string RotateRight = "Rotate Right";
        public const string RotateLeft = "Rotate Left";
        public const string FlipHorizontal = "Flip Horizontal";
        public const string FlipVertical = "Flip Vertical";
        public const string Grayscale = "Grayscale";
        public const string RemoveBackground = "Remove Background";
        public const string Zip = "Pack into ZIP";
    }

    public static string Name(string id) => L10n.T(id switch
    {
        "quick:rotate" => Copy.RotateRight, "quick:rotateLeft" => Copy.RotateLeft,
        "quick:flip" => Copy.FlipHorizontal, "quick:flipVertical" => Copy.FlipVertical,
        "quick:grayscale" => Copy.Grayscale, "quick:removeBackground" => Copy.RemoveBackground,
        "quick:zip" => Copy.Zip,
        _ => FeatureCatalog.ToolNames.GetValueOrDefault(id, id),
    });

    public static List<WheelItem> Tools(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return [];
        var available = Capabilities.Tools(files).Concat(Capabilities.QuickTools(files)).Distinct().ToList();
        var items = new List<WheelItem>();
        void Tool(string id)
        {
            if (available.Remove(id)) items.Add(new(id, Name(id)));
        }
        void Group(string key, string title, params string[] ids)
        {
            var children = ids.Where(available.Remove).Select(id => new WheelItem(id, Name(id))).ToArray();
            if (children.Length > 0) items.Add(new("group:" + key, L10n.T(title), Group: children));
        }

        if (files.All(FeatureCatalog.IsImage))
        {
            Group("edit", Copy.Edit, "cropImage", "editImage", "quick:rotate", "quick:rotateLeft", "quick:flip", "quick:flipVertical", "quick:grayscale");
            Group("background", Copy.Background, "quick:removeBackground", "frameImage");
            Group("markup", Copy.MarkUp, "annotateImage", "redactImage");
        }
        else
        {
            Group("pages", Copy.Pages, "organizePDF", "splitPDF", "mergePDF");
            Group("edit", Copy.Edit, "trimVideo", "splitVideo", "trimAudio", "cropVideo", "changeVideoSpeed", "redactVideo", "redactAudio");
            Group("audio", Copy.Audio, "muteVideo", "normalizeAudio", "audioChannels");
            Group("frames", Copy.Frames, "videoSnapshots");
        }
        Group("extract", Copy.Extract, "extractArchive");
        Group("create", Copy.Create, "createPDF", "createCollage", "joinVideos", "audioToVideo", "quick:zip");
        Tool("compress");
        Tool("removeMetadata");
        Tool("readQRCode");
        items.Add(new("group:files", L10n.T(Copy.FileActions), Group:
            [.. FileActions.Ids.Select(id => new WheelItem("action:" + id, L10n.T(FileActions.Title(id)), FileActions.Available(id)))]));
        return items;
    }
}
