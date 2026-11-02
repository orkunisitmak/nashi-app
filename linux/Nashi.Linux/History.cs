using Nashi.Core;
using Avalonia;
using Avalonia.Threading;

namespace Nashi.Linux;

/// History references outputs in their real folders; it never moves them into the app's data directory.
public static class History
{
    public static event Action? Changed;
    public static string? Warning { get; private set; }
    public static LibraryStore Open() => new(AppPaths.Data);

    public static void Record(string action, IEnumerable<string> paths)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Data);
            using var held = Lock();
            Open().Record(action, paths); Warning = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Warning = "Files were saved, but Library history could not be updated: " + error.Message; }
        if (Application.Current is not null && !Dispatcher.UIThread.CheckAccess()) Dispatcher.UIThread.Post(() => Changed?.Invoke());
        else Changed?.Invoke();
    }

    static FileStream Lock()
    {
        for (var attempt = 0; ; attempt++)
            try { return new FileStream(Path.Combine(AppPaths.Data, "library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 10) { Thread.Sleep(25); }
    }
}
