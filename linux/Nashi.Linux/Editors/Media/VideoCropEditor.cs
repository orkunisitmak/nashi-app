using Nashi.Core;

namespace Nashi.Linux.Editors.Media;

/// Crop Video (macOS cropControls on a video frame): Crop Image's selection, handles, presets and fields on a frame
/// a third of the way in, in video pixels; Save crops every frame with MediaEngine.
public sealed class VideoCropEditor(IReadOnlyList<string> inputs, MediaTools tools) : CropEditor(inputs, "cropVideo")
{
    private string? still;

    public override async Task<string> StillAsync(CancellationToken token) => still = await MediaEditor.StillFrameAsync(tools, Inputs[0], token);

    // The frame is the video's displayed size, so the selection always fits.
    public override string? Validate() => null;

    public override EditorJob Job()
    {
        var r = Selection!.Value;
        var options = new MediaOptions { Regions = [new MediaRegion(r.X, r.Y, r.Width, r.Height)] };
        var engine = tools.Engine;
        return (files, destination, progress, token) => engine.ToolAsync("cropVideo", files, destination, options, progress, token);
    }

    public override void Dispose() => MediaEditor.DeleteStill(still);
}
