namespace Nashi.Core;

/// <summary>
/// Discovers and configures LibreOffice binaries across local managed runtimes,
/// standard Program Files locations, and system PATH.
/// </summary>
public static class OfficeLocator
{
    public const string DisabledPythonLoader = "pythonloaderlo.dll.disabled-by-nashi";

    /// <summary>
    /// Checks whether the Python loader is currently disabled without altering files.
    /// </summary>
    public static bool IsPythonLoaderDisabled(string application)
    {
        var loader = Path.Combine(application, "program", "pythonloaderlo.dll");
        return !File.Exists(loader);
    }

    /// <summary>
    /// Disables the bundled Python loader extension in a private LibreOffice runtime.
    /// Prevents hanging on systems where Lightproof Python registration deadlocks.
    /// </summary>
    public static bool DisablePythonLoader(string application)
    {
        var loader = Path.Combine(application, "program", "pythonloaderlo.dll");
        try
        {
            if (File.Exists(loader))
            {
                File.Move(loader, Path.Combine(application, "program", DisabledPythonLoader), true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return !File.Exists(loader);
    }

    /// <summary>
    /// Finds the best available soffice executable. When applyWorkarounds is true, disables the Python loader
    /// if needed; when false (e.g. for dependencies inspection), performs read-only checks without modifying files.
    /// </summary>
    public static string? ResolveExecutable(string root, string? searchPath = null, bool applyWorkarounds = false)
    {
        var managed = Path.Combine(root, "office-26.8.1");
        var safe = !Directory.Exists(managed) || (applyWorkarounds ? DisablePythonLoader(managed) : IsPythonLoaderDisabled(managed));
        return Candidates(root, safe, searchPath).FirstOrDefault(ToolInstallation.UsableFile);
    }

    /// <summary>
    /// Enumerates candidate soffice paths in order of preference depending on whether the private runtime is safe.
    /// Preserves exact app search locations (managed runtime and 64-bit Program Files).
    /// </summary>
    public static string[] Candidates(string root, bool privateRuntimeSafe, string? searchPath = null)
    {
        string[] managed =
        [
            Path.Combine(root, "office-26.8.1", "program", "soffice.com"),
            Path.Combine(root, "office-26.8.1", "program", "soffice.exe")
        ];
        var programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program");
        var installed = new List<string>
        {
            Path.Combine(programFiles, "soffice.com"),
            Path.Combine(programFiles, "soffice.exe")
        };
        if (!string.IsNullOrEmpty(searchPath))
        {
            foreach (var entry in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var folder = entry.Trim('"');
                if (folder.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(folder))
                {
                    installed.Add(Path.Combine(folder, "soffice.com"));
                    installed.Add(Path.Combine(folder, "soffice.exe"));
                    installed.Add(Path.Combine(folder, "soffice"));
                }
            }
        }
        return privateRuntimeSafe ? [..managed, ..installed] : [..installed, ..managed];
    }
}
