using System.Text;
using Nashi.Core;

namespace Nashi.Linux;

/// A tool the choice needs was not found: ffmpeg and ffprobe for media, LibreOffice for Office work, Deno for Pages,
/// ExifTool for Metadata.
public sealed class MissingToolsException(string message = MissingToolsException.Media) : Exception(message)
{
    public const string Media = "Media tools are needed for this. Install ffmpeg, or set up media tools first.";
    public const string Office = "LibreOffice is needed for this. Install it, then try again.";
    public const string Pages = "Pages documents need Deno. Download the missing tools in setup.";
    public const string Metadata = "Metadata tools are needed for this. Download the missing tools in setup.";
}

/// Turns a wheel choice plus selected files into output files, using the shared engine.
public sealed class OperationRunner(Toolchain tools)
{
    /// What the last run wants the success card to add (English; translate at display), or null.
    public string? Note { get; private set; }

    public async Task<IReadOnlyList<string>> RunAsync(string choiceId, IReadOnlyList<string> files, string destination, IProgress<OperationProgress> progress, CancellationToken token)
    {
        Note = null;
        var convert = choiceId.StartsWith("convert:", StringComparison.Ordinal) ? choiceId[8..].ToLowerInvariant() : null;
        if (convert is not null ? !Capabilities.Formats(files).Contains(convert) : !Capabilities.Tools(files).Contains(choiceId))
            throw new NotSupportedException($"{choiceId} is not available for this selection on Linux.");
        token.ThrowIfCancellationRequested();

        if (convert is not null)
        {
            if (files.All(FeatureCatalog.IsImage)) return await Task.Run(() => ImageOperations.Convert(files, convert, destination, progress, token), token);
            if (files.All(f => FeatureCatalog.IsAudio(f) || FeatureCatalog.IsVideo(f))) return await Media(progress, (e, p) => e.ConvertAsync(files, convert, destination, new MediaOptions(), p, token));
            if (files.All(DocumentOperations.Handles))
            {
                var documents = new DocumentOperations(AppPaths.Office(), AppPaths.Deno(tools));
                var outputs = await Task.Run(() => documents.ConvertAsync(files, convert, destination, progress, token), token);
                Note = documents.Note;
                return outputs;
            }
            if (files.All(FeatureCatalog.IsArchive)) return await Task.Run(() => Each(files, destination, token, (f, d) => ArchiveEngine.Convert(f, convert, d, null, progress, token)), token);
            return await Task.Run(() => Subtitles(files, convert, destination, token), token);
        }
        switch (choiceId)
        {
            case "createPDF": return await Task.Run(() => ImageOperations.Convert(files, "pdf", destination, progress, token), token);
            case "mergePDF": return await Task.Run(() => DocumentEngine.Pdf("mergePDFs", files, destination, token: token), token);
            case "extractArchive": return await Task.Run(() => Each(files, destination, token, (f, d) => ArchiveEngine.Extract(f, d, null, progress, token)), token);
            case "compress": return await CompressAsync(files, destination, progress, token, files.Any(f => FeatureCatalog.IsAudio(f) || FeatureCatalog.IsVideo(f)) ? Engine() : null);
            // muteVideo and normalizeAudio at their defaults; every other tool takes its options from its editor (EditorJob).
            case "muteVideo" or "normalizeAudio": return await Media(progress, (e, p) => e.ToolAsync(choiceId, files, destination, new MediaOptions(), p, token));
            default: throw new NotSupportedException($"{choiceId} is not available for this selection on Linux.");
        }
    }

    /// Runs the choice with each file's outputs beside it: one run per source folder, in the order the folders first
    /// appear, so files from two folders get their outputs in their own folders. A choice that makes one file out of
    /// many (Create PDF, Merge PDFs, Create Collage, images to one PDF) writes it beside the first file. `destination` maps a source folder
    /// to where its outputs go: the folder itself, or one the user picks when it is read-only; null cancels before any
    /// work and returns null. A failure or cancel in a later folder removes what earlier folders published.
    /// `job` (an editor's Save) runs instead of the choice's own route.
    public async Task<IReadOnlyList<string>?> RunBesideSourcesAsync(string choiceId, IReadOnlyList<string> files, Func<string, Task<string?>> destination, IProgress<OperationProgress> progress, CancellationToken token, Editors.EditorJob? job = null)
    {
        static string Folder(string file) => Path.GetDirectoryName(Path.GetFullPath(file))!;
        var combines = choiceId is "createPDF" or "mergePDF" or "createCollage" or "joinVideos" || (choiceId == "convert:pdf" && files.All(FeatureCatalog.IsImage));
        IReadOnlyList<IReadOnlyList<string>> groups = combines ? [files] : [.. files.GroupBy(Folder).Select(g => (IReadOnlyList<string>)[.. g])];
        var targets = new List<string>();
        foreach (var group in groups)
        {
            if (await destination(Folder(group[0])) is not { } target) return null;
            targets.Add(target);
        }
        var outputs = new List<string>();
        string? note = null;
        try
        {
            for (var n = 0; n < groups.Count; n++)
            {
                var slice = groups.Count == 1 ? progress : new Slice(progress, n, groups.Count);
                if (job is not null) { Note = null; outputs.AddRange(await job(groups[n], targets[n], slice, token)); }
                else outputs.AddRange(await RunAsync(choiceId, groups[n], targets[n], slice, token));
                note ??= Note;
            }
        }
        catch
        {
            foreach (var done in outputs) Remove(done);
            throw;
        }
        Note = note;
        return outputs;
    }

    // An output we published: a file, or an extracted folder.
    static void Remove(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
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

    /// Compress routes per file kind (the wheel and the Compress editor for mixed, media and PDF selections); each
    /// group is its own transaction, and a later failure or cancel removes earlier groups' outputs. `engine` is
    /// required when there is media; the options default to Balanced.
    public static async Task<IReadOnlyList<string>> CompressAsync(IReadOnlyList<string> files, string destination, IProgress<OperationProgress> progress, CancellationToken token, MediaEngine? engine, CompressOptions? image = null, MediaOptions? media = null, string password = "")
    {
        var images = files.Where(FeatureCatalog.IsImage).ToList();
        var pdfs = files.Where(f => FeatureCatalog.Extension(f) == "pdf").ToList();
        var clips = files.Where(f => FeatureCatalog.IsAudio(f) || FeatureCatalog.IsVideo(f)).ToList();
        if (clips.Count > 0 && engine is null) throw new MissingToolsException(); // fail before any group starts
        var groups = new List<Func<IProgress<OperationProgress>, Task<IReadOnlyList<string>>>>();
        if (images.Count > 0) groups.Add(p => Task.Run(() => ImageOperations.Compress(images, destination, p, token, image), token));
        if (pdfs.Count > 0) groups.Add(p => Task.Run(() => DocumentEngine.Pdf("compress", pdfs, destination, token: token, password: password), token));
        if (clips.Count > 0) groups.Add(p => engine!.ToolAsync("compress", clips, destination, media ?? new MediaOptions(), p, token));
        var outputs = new List<string>();
        try
        {
            for (var n = 0; n < groups.Count; n++)
                outputs.AddRange(await groups[n](new Slice(progress, n, groups.Count)));
        }
        catch
        {
            foreach (var done in outputs) Remove(done);
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
