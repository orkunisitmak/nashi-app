namespace Nashi.Linux;

/// Diagnostics for desktop integration (drag and drop): NASHI_TRACE=<file> appends one line per event.
internal static class Trace
{
    private static readonly string? File=Environment.GetEnvironmentVariable("NASHI_TRACE");
    public static void Log(string line){if(File is null)return;try{System.IO.File.AppendAllText(File,$"{DateTime.Now:HH:mm:ss.fff} {line}\n");}catch(IOException){}}
}
