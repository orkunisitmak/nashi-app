using System.Text;
using Nashi.Core;

namespace Nashi.Linux;

/// A tool the choice needs was not found: ffmpeg and ffprobe for media, LibreOffice for Office work, Deno for Pages.
public sealed class MissingToolsException(string message = MissingToolsException.Media) : Exception(message)
{
    public const string Media = "Media tools are needed for this. Install ffmpeg, or set up media tools first.";
    public const string Office = "LibreOffice is needed for this. Install it, then try again.";
    public const string Pages = "Pages documents need Deno. Download the missing tools in setup.";
}

/// Turns a wheel choice plus selected files into output files, using the shared engine.
public sealed class OperationRunner(Toolchain tools)
{
    public async Task<IReadOnlyList<string>> RunAsync(string choiceId, IReadOnlyList<string> files, string destination, IProgress<OperationProgress> progress, CancellationToken token)
    {
        var convert = choiceId.StartsWith("convert:", StringComparison.Ordinal) ? choiceId[8..].ToLowerInvariant() : null;
        if (convert is not null ? !Capabilities.Formats(files).Contains(convert) : !Capabilities.Tools(files).Contains(choiceId))
            throw new NotSupportedException($"{choiceId} is not available for this selection on Linux.");
        token.ThrowIfCancellationRequested();

        if (convert is not null)
        {
            if (files.All(FeatureCatalog.IsImage)) return await Task.Run(() => ImageOperations.Convert(files, convert, destination, progress, token), token);
            if (files.All(f => FeatureCatalog.IsAudio(f) || FeatureCatalog.IsVideo(f))) return await Media(progress, (e, p) => e.ConvertAsync(files, convert, destination, new MediaOptions(), p, token));
            if (files.All(DocumentOperations.Handles)) return await Task.Run(() => new DocumentOperations(AppPaths.Office(), AppPaths.Deno(tools)).ConvertAsync(files, convert, destination, progress, token), token);
            if (files.All(FeatureCatalog.IsArchive)) return await Task.Run(() => Each(files, destination, token, (f, d) => ArchiveEngine.Convert(f, convert, d, null, progress, token)), token);
            return await Task.Run(() => Subtitles(files, convert, destination, token), token);
        }
        switch (choiceId)
        {
            case "createPDF": return await Task.Run(() => ImageOperations.Convert(files, "pdf", destination, progress, token), token);
            case "mergePDF": return await Task.Run(() => DocumentEngine.Pdf("mergePDFs", files, destination, token: token), token);
            case "extractArchive": return await Task.Run(() => Each(files, destination, token, (f, d) => ArchiveEngine.Extract(f, d, null, progress, token)), token);
            case "compress": return await Compress(files, destination, progress, token);
            default: return await Media(progress, (e, p) => e.ToolAsync(choiceId, files, destination, new MediaOptions(), p, token)); // muteVideo, normalizeAudio
        }
    }

    /// One ExportTransaction around many per-file engine calls, so a cancel or failure leaves nothing behind.
    static IReadOnlyList<string> Each(IReadOnlyList<string> files, string destination, CancellationToken token, Func<string, string, IReadOnlyList<string>> work)
    {
        using var tx = new ExportTransaction(destination);
        var outputs = files.SelectMany(f => { token.ThrowIfCancellationRequested(); return work(f, tx.StagingDirectory); }).ToList();
        return tx.Publish(outputs, token);
    }

    static IReadOnlyList<string> Subtitles(IReadOnlyList<string> files, string format, string destination, CancellationToken token)
    {
        using var tx = new ExportTransaction(destination);
        var outputs = new List<string>();
        foreach (var input in files)
        {
            token.ThrowIfCancellationRequested();
            var path = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(input) + "." + format);
            File.WriteAllText(path, SubtitleCodec.Convert(LegacyTextCodec.Decode(File.ReadAllBytes(input)), format), new UTF8Encoding(false));
            outputs.Add(path);
        }
        return tx.Publish(outputs, token);
    }

    // Compress routes per file kind; each group is its own transaction, and a later failure or cancel removes earlier groups' outputs.
    async Task<IReadOnlyList<string>> Compress(IReadOnlyList<string> files, string destination, IProgress<OperationProgress> progress, CancellationToken token)
    {
        var images = files.Where(FeatureCatalog.IsImage).ToList();
        var pdfs = files.Where(f => FeatureCatalog.Extension(f) == "pdf").ToList();
        var media = files.Where(f => FeatureCatalog.IsAudio(f) || FeatureCatalog.IsVideo(f)).ToList();
        if (media.Count > 0) Engine(); // fail before any group starts
        var groups = new List<Func<IProgress<OperationProgress>, Task<IReadOnlyList<string>>>>();
        if (images.Count > 0) groups.Add(p => Task.Run(() => ImageOperations.Compress(images, destination, p, token), token));
        if (pdfs.Count > 0) groups.Add(p => Task.Run(() => DocumentEngine.Pdf("compress", pdfs, destination, token: token), token));
        if (media.Count > 0) groups.Add(p => Engine().ToolAsync("compress", media, destination, new MediaOptions(), p, token));
        var outputs = new List<string>();
        try
        {
            for (var n = 0; n < groups.Count; n++)
                outputs.AddRange(await groups[n](new Slice(progress, n, groups.Count)));
        }
        catch
        {
            foreach (var done in outputs) try { File.Delete(done); } catch (IOException) { }
            throw;
        }
        return outputs;
    }

    Task<IReadOnlyList<string>> Media(IProgress<OperationProgress> progress, Func<MediaEngine, IProgress<OperationProgress>, Task<IReadOnlyList<string>>> work) =>
        work(Engine(), progress);

    // ponytail: needs only ffmpeg+ffprobe, not the full Toolchain.Ready set (yt-dlp, deno are for downloads).
    MediaEngine Engine()
    {
        try { return new MediaEngine(tools.Executable("ffmpeg"), tools.Executable("ffprobe")); }
        catch (FileNotFoundException) { throw new MissingToolsException(); }
    }

    sealed class Slice(IProgress<OperationProgress> inner, int index, int count) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => inner.Report(value with { Fraction = value.Fraction is { } f ? (index + f) / count : null });
    }
}
