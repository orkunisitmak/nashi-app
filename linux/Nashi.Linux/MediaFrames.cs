using Nashi.Core;

namespace Nashi.Linux;

/// Frame export uses the same times as the snapshots editor, publishing PNG/JPG copies in one transaction.
public static class MediaFrames
{
    public static async Task<IReadOnlyList<string>> ExportAsync(MediaEngine engine, IReadOnlyList<string> files, string format,
        string destination, MediaOptions options, IProgress<OperationProgress> progress, CancellationToken token)
    {
        if (format is not ("jpg" or "png")) throw new ArgumentException("Choose PNG or JPG for frames.");
        using var tx = new ExportTransaction(destination);
        var frames = await engine.ToolAsync("videoSnapshots", files, tx.StagingDirectory, options, progress, token);
        if (format == "png") return tx.Publish(frames, token);
        var outputs = new List<string>();
        foreach (var frame in frames)
        {
            token.ThrowIfCancellationRequested();
            var output = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(frame) + ".jpg");
            PhotoCodec.EncodePng(await File.ReadAllBytesAsync(frame, token), output, "jpg", 90, token: token);
            outputs.Add(output);
        }
        return tx.Publish(outputs, token);
    }
}
