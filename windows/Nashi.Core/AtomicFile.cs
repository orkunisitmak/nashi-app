using System.Text;

namespace Nashi.Core;

public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full) ?? throw new ArgumentException("A file path is required.");
        Directory.CreateDirectory(directory);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temporary, full, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void RecoverOrphan(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (directory is null || !Directory.Exists(directory)) return;
        var orphan = Directory.EnumerateFiles(directory, name + ".*.tmp")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault(candidate => File.Exists(candidate) && IsJson(candidate));
        if (orphan is null) return;
        try
        {
            if (!File.Exists(full)) File.Move(orphan, full);
            else if (File.GetLastWriteTimeUtc(orphan) > File.GetLastWriteTimeUtc(full)) File.Move(orphan, full, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static string CreateTemporaryDirectory(string prefix)=>CreateTemporaryDirectory(prefix,Path.GetTempPath());

    public static string CreateTemporaryDirectory(string prefix,string parent)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = Path.Combine(parent, prefix + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(candidate);
                var info = new DirectoryInfo(candidate);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Temporary directory is a symbolic link.");
                return info.FullName;
            }
            catch (IOException) { }
        }
        throw new IOException("Could not create a private temporary directory.");
    }

    private static bool IsJson(string file)
    {
        try { System.Text.Json.JsonDocument.Parse(File.ReadAllText(file)).Dispose(); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return false; }
    }
}
