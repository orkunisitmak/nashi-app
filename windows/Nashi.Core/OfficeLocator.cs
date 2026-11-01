namespace Nashi.Core;

/// <summary>
/// Discovers and configures LibreOffice binaries across local managed runtimes,
/// standard Program Files locations, and system PATH.
/// </summary>
public static class OfficeLocator
{
    public const string DisabledPythonLoader = "pythonloaderlo.dll.disabled-by-nashi";

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
    /// Finds the best available soffice executable, checking managed runtimes, installed locations, and PATH.
    /// </summary>
    public static string? ResolveExecutable(string root, string? searchPath = null)
    {
        var managed = Path.Combine(root, "office-26.8.1");
        var safe = !Directory.Exists(managed) || DisablePythonLoader(managed);
        return Candidates(root, safe, searchPath).FirstOrDefault(ToolInstallation.UsableFile);
    }

    /// <summary>
    /// Enumerates candidate soffice paths in order of preference depending on whether the private runtime is safe.
    /// </summary>
    public static string[] Candidates(string root, bool privateRuntimeSafe, string? searchPath = null)
    {
        string[] managed =
        [
            Path.Combine(root, "office-26.8.1", "program", "soffice.com"),
            Path.Combine(root, "office-26.8.1", "program", "soffice.exe")
        ];
        var programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program");
        var programFilesX86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program");
        var installed = new List<string>
        {
            Path.Combine(programFiles, "soffice.com"),
            Path.Combine(programFiles, "soffice.exe"),
            Path.Combine(programFilesX86, "soffice.com"),
            Path.Combine(programFilesX86, "soffice.exe")
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
