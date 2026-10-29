namespace Nashi.Linux;

public sealed record AddResult(int Added, int Skipped);

/// The files the user has handed to Nashi: existing regular files, absolute, unique, in arrival order.
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
            var full = Path.GetFullPath(path);
            if (File.Exists(full) && !files.Contains(full)) { files.Add(full); added++; }
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
