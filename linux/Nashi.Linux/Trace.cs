namespace Nashi.Linux;

/// Diagnostics for desktop integration (drag and drop): NASHI_TRACE=<file> appends one line per event.
internal static class Trace
{
    private static readonly string? File=Environment.GetEnvironmentVariable("NASHI_TRACE");
    /// Diagnostics never stop the app: an unwritable or empty path is ignored.
    public static void Log(string line){if(string.IsNullOrWhiteSpace(File))return;try{System.IO.File.AppendAllText(File,$"{DateTime.Now:HH:mm:ss.fff} {line}\n");}catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException){}}
}
