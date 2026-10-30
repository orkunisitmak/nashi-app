namespace Nashi.Core;

/// Stages beside the destination. A failed/cancelled batch publishes nothing and
/// removes only files owned by this transaction; originals are never overwritten.
public sealed class ExportTransaction : IDisposable
{
    public string StagingDirectory { get; }
    public string Destination { get; }
    private readonly List<string> published = [];
    private bool committed;

    public ExportTransaction(string destination)
    {
        Destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Destination);
        StagingDirectory = Path.Combine(Destination, ".nashi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StagingDirectory);
        if ((File.GetAttributes(StagingDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Staging cannot be a symbolic link.");
        // A leading dot hides it on macOS; Windows needs the attribute.
        File.SetAttributes(StagingDirectory, File.GetAttributes(StagingDirectory) | FileAttributes.Hidden);
    }

    public IReadOnlyList<string> Publish(IEnumerable<string> outputs, CancellationToken token)
    {
        var files = outputs.ToList();
        if (files.Count == 0) throw new InvalidDataException("No output was produced.");
        foreach (var item in files)
        {
            token.ThrowIfCancellationRequested();
            var full = Path.GetFullPath(item);
            if (!full.StartsWith(StagingDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("An output is outside its staging directory.");
            if (!Directory.Exists(full) && (!File.Exists(full) || new FileInfo(full).Length == 0))
                throw new InvalidDataException("An empty output was produced.");
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("An output cannot be a symbolic link.");
        }
        foreach (var item in files)
        {
            token.ThrowIfCancellationRequested();
            var destination = SafePaths.Unique(Destination, Path.GetFileName(item));
            if (Directory.Exists(item)) Directory.Move(item, destination);
            else File.Move(item, destination, overwrite: false);
            published.Add(destination);
        }
        token.ThrowIfCancellationRequested();
        committed = true;
        return published.ToArray();
    }

    /// Cleanup never replaces the batch's real outcome. A tool killed on cancel can hold a
    /// file for a moment (yt-dlp's ffmpeg child, for example), which used to turn "Cancelled"
    /// into "file is being used by another process" and leave the staging folder behind.
    /// A staging folder that is still locked is retried in the background, so callers on the
    /// UI thread never wait for it. Published names are tried only once: after a delay the
    /// name could belong to a new file.
    public void Dispose()
    {
        if (!committed) foreach (var path in published) TryDelete(path);
        if (TryDelete(StagingDirectory)) return;
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(250);
                if (TryDelete(StagingDirectory)) return;
            }
        });
    }

    private static bool TryDelete(string path)
    {
        try
        {
            // Exists() is also false when the folder can't be reached; only "not found" means gone.
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) Directory.Delete(path, recursive: true);
            else File.Delete(path);
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
