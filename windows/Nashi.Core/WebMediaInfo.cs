using System.Text.Json;
namespace Nashi.Core;
public sealed record WebMediaInfo(string Title,string Site,double? Duration,int[] Heights,bool AudioOnly);
public sealed partial class WebDownload
{
    public async Task<WebMediaInfo> InspectAsync(string input,CancellationToken token=default)
    {
        var uri=Validate(WebMediaPayload.Parse(input)?.Url??input);var result=await ProcessRunner.RunAsync(tools.Executable("yt-dlp"),["--ignore-config","--no-playlist","--no-cache-dir","--skip-download","--dump-single-json","--no-progress","--js-runtimes","deno:"+tools.Executable("deno"),"--",uri.AbsoluteUri],token);return ParseInfo(result.Output,uri.Host);
    }
    public static WebMediaInfo ParseInfo(string json,string site)
    {
        using var doc=JsonDocument.Parse(json);var item=doc.RootElement;
        if(item.TryGetProperty("is_live",out var live)&&live.ValueKind==JsonValueKind.True||item.TryGetProperty("live_status",out var state)&&state.ValueKind==JsonValueKind.String&&state.GetString() is "is_live" or "is_upcoming")throw new InvalidDataException("Choose a finished video instead of a live or upcoming stream.");
        if(item.TryGetProperty("has_drm",out var drm)&&drm.ValueKind==JsonValueKind.True)throw new InvalidDataException("Protected media cannot be downloaded.");
        var formats=item.TryGetProperty("formats",out var source)&&source.ValueKind==JsonValueKind.Array?source.EnumerateArray().Where(f=>f.ValueKind==JsonValueKind.Object&&(!f.TryGetProperty("has_drm",out var d)||d.ValueKind!=JsonValueKind.True)).ToArray():[];
        if(source.ValueKind==JsonValueKind.Array&&source.GetArrayLength()>0&&formats.Length==0)throw new InvalidDataException("No unprotected downloadable format is available.");
        var heights=formats.Select(f=>f.TryGetProperty("height",out var h)&&h.ValueKind==JsonValueKind.Number&&h.TryGetInt32(out var n)?n:0).Where(n=>n is >0 and <=16384).Distinct().Order().ToArray();
        var audio=item.TryGetProperty("vcodec",out var codec)&&codec.ValueKind==JsonValueKind.String&&codec.GetString()=="none"||formats.Length>0&&formats.All(f=>f.TryGetProperty("vcodec",out var c)&&c.ValueKind==JsonValueKind.String&&c.GetString()=="none");
        var duration=item.TryGetProperty("duration",out var seconds)&&seconds.ValueKind==JsonValueKind.Number&&seconds.TryGetDouble(out var value)&&double.IsFinite(value)&&value>0?(double?)value:null;var title=item.TryGetProperty("title",out var name)&&name.ValueKind==JsonValueKind.String?name.GetString()??"Web media":"Web media";return new(title[..Math.Min(300,title.Length)],site,duration,heights,audio);
    }
}
