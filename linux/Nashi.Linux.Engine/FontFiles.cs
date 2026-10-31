using Nashi.Core;
namespace Nashi.Windows;

public static class FontFiles
{
    public static async Task<string> ResolveAsync(string family,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(string.IsNullOrWhiteSpace(family))throw new ArgumentException("Choose a font.");
        if(Path.IsPathFullyQualified(family)&&File.Exists(family))return family;
        var matcher=SystemTools.Find("fc-match")??throw new IOException("Install fontconfig to use image text annotations.");
        var match=await ProcessRunner.RunAsync(matcher,["--format=%{file}","--",family],token);
        var path=match.Output.Trim();
        if(!Path.IsPathFullyQualified(path)||!File.Exists(path))throw new IOException("No usable font was found. Install a font and refresh the fontconfig cache.");
        return path;
    }
}
