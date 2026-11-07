namespace Nashi.Core;

public static class SafePaths
{
    private static readonly HashSet<string> Devices = new(StringComparer.OrdinalIgnoreCase)
    { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM\u00b9", "COM\u00b2", "COM\u00b3", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT\u00b9", "LPT\u00b2", "LPT\u00b3" };

    public static string ArchiveEntry(string root, string entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);
        var name = entry.Replace('\\', '/');
        var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (name.StartsWith('/') || parts.Length == 0 || parts.Any(p => p is "." or ".." ||
            p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(['<', '>', ':', '"', '|', '?', '*', '\0']) >= 0 ||
            Devices.Contains(p.Split('.')[0])))
            throw new InvalidDataException("The archive contains an unsafe path.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(parts)));
        if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The archive entry leaves its destination.");
        return destination;
    }

    private static readonly char[] WindowsInvalid = ['<', '>', ':', '"', '|', '?', '*', '\0'];
    /// A file name typed by the user, reduced to a safe stem: no path, no invalid characters, no extension that
    /// Nashi adds itself, no trailing dots or spaces and no reserved device name. Falls back when nothing is left.
    public static string FileStem(string? name, string fallback, params string[] extensions)
    {
        // The Windows set on every platform, so a name typed on one system is valid on the others.
        var invalid = Path.GetInvalidFileNameChars().Concat(WindowsInvalid).Concat(Enumerable.Range(0, 32).Select(n => (char)n)).ToHashSet();
        var stem = new string((name ?? "").Where(c => !invalid.Contains(c) && c is not ('/' or '\\')).ToArray()).Trim();
        foreach (var extension in extensions)
            if (stem.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) { stem = stem[..^extension.Length]; break; }
        stem = stem.TrimEnd('.', ' ');
        if (stem.Length > 120) stem = stem[..120].TrimEnd('.', ' ');
        if (stem.Length == 0) return fallback;
        // Windows treats "CON.report" like "CON", so the underscore goes right after the device name.
        var head = stem.Split('.')[0];
        return Devices.Contains(head) ? head + "_" + stem[head.Length..] : stem;
    }
    public static string Unique(string directory, string filename)
    {
        if (Path.GetFileName(filename) != filename || filename.IndexOfAny(['/', '\\', ':', '\0']) >= 0)
            throw new ArgumentException("Choose a plain output filename.");
        var stem = Path.GetFileNameWithoutExtension(filename); var extension = Path.GetExtension(filename);
        for (var number = 1; number < 100_000; number++)
        {
            var candidate = Path.Combine(directory, number == 1 ? filename : $"{stem} {number}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("Too many files already use this name.");
    }
}
