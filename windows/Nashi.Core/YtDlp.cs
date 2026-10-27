namespace Nashi.Core;

/// Runs yt-dlp with patient retries. Some networks and HTTPS-scanning antivirus
/// break TLS session reuse, which yt-dlp reports as intermittent
/// "[SSL: INVALID_SESSION_ID]" failures (DPI tools such as GoodbyeDPI do this too).
/// Those failures arrive within milliseconds, so retries are quick rather than backed off.
public static class YtDlp
{
    private static readonly string[] Retries=["--retries","10","--extractor-retries","10","--fragment-retries","10","--retry-sleep","http:linear=0.2:1:0.2","--retry-sleep","extractor:linear=0.2:1:0.2","--retry-sleep","fragment:linear=0.2:1:0.2"];
    public static async Task<ProcessResult> RunAsync(Toolchain tools,IReadOnlyList<string> arguments,CancellationToken token=default,Action<string>? outputLine=null)
    {
        var separator=arguments.ToList().IndexOf("--");var values=arguments.ToList();values.InsertRange(separator<0?values.Count:separator,Retries);
        for(var attempt=1;;attempt++)
        {
            try{return await ProcessRunner.RunAsync(tools.Executable("yt-dlp"),values,token,outputLine).ConfigureAwait(false);}
            catch(IOException error)when(attempt<3&&IsTransient(error.Message)){await Task.Delay(TimeSpan.FromMilliseconds(400*attempt),token).ConfigureAwait(false);}
            catch(IOException error){throw new IOException(Explain(error.Message),error);}
        }
    }
    private static bool IsTransient(string message)=>message.Contains("SSL",StringComparison.OrdinalIgnoreCase)||message.Contains("timed out",StringComparison.OrdinalIgnoreCase)||message.Contains("Connection reset",StringComparison.OrdinalIgnoreCase);
    /// Keeps the ERROR lines (the actual reason) and drops the retry warnings around them.
    public static string Explain(string message)
    {
        var errors=message.Split('\n').Select(l=>l.Trim()).Where(l=>l.StartsWith("ERROR:",StringComparison.Ordinal)).Select(l=>l[6..].Trim()).ToArray();
        var reason=errors.Length>0?string.Join(Environment.NewLine,errors):message.Trim();
        if(reason.Contains("SSL",StringComparison.OrdinalIgnoreCase))reason+=Environment.NewLine+Environment.NewLine+"The secure connection kept dropping. This is often caused by a DPI tool (such as GoodbyeDPI), an antivirus or a network that inspects HTTPS traffic. Try again, or pause that tool for a moment.";
        return reason;
    }
}
