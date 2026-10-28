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
    }

    public IReadOnlyList<string> Publish(IEnumerable<string> outputs, CancellationToken token, bool allowEmptyFiles = false)
    {
        var files = outputs.ToList();
        if (files.Count == 0) throw new InvalidDataException("No output was produced.");
        foreach (var item in files)
        {
            token.ThrowIfCancellationRequested();
            var full = Path.GetFullPath(item);
            if (!full.StartsWith(StagingDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("An output is outside its staging directory.");
            if (!Directory.Exists(full) && (!File.Exists(full) || (!allowEmptyFiles && new FileInfo(full).Length == 0)))
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

    public void Dispose()
    {
        if (!committed) foreach (var item in published)
        {
            if (Directory.Exists(item)) Directory.Delete(item, recursive: true);
            else if (File.Exists(item)) File.Delete(item);
        }
        if (Directory.Exists(StagingDirectory)) Directory.Delete(StagingDirectory, recursive: true);
    }
}
