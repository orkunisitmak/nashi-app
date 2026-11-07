namespace Nashi.Linux;

public sealed record AddResult(int Added, int Skipped);

/// Existing files and folders, absolute, unique, in arrival order.
public sealed class Selection
{
    private readonly List<string> files = [];

    public IReadOnlyList<string> Files => files;

    public event Action? Changed;

    public AddResult Add(IEnumerable<string> paths)
    {
        int added = 0, skipped = 0;
        foreach (var path in paths)
        {
            string full;
            try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { skipped++; continue; }
            if ((File.Exists(full) || Directory.Exists(full)) && !files.Contains(full)) { files.Add(full); added++; }
            else skipped++;
        }
        if (added > 0) Changed?.Invoke();
        return new AddResult(added, skipped);
    }

    public void Remove(string path)
    {
        if (files.Remove(Path.GetFullPath(path))) Changed?.Invoke();
    }

    public void Clear()
    {
        if (files.Count == 0) return;
        files.Clear();
        Changed?.Invoke();
    }
}
