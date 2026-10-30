using System.Text.Json;
namespace Nashi.Core;

/// The checked-in contract is compared with Swift's catalog by the parity check.
public static class FeatureCatalog
{
    private sealed record Contract(Dictionary<string,string[]> ConversionMatrix,Dictionary<string,string[]> SingleTools,Dictionary<string,string[]> MultipleTools,Dictionary<string,string> ToolNames);
    private static readonly Contract Data=Load();
    private static Contract Load(){using var stream=typeof(FeatureCatalog).Assembly.GetManifestResourceStream("Nashi.FeatureCatalog.json")!;return JsonSerializer.Deserialize<Contract>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;}
    public static IReadOnlyDictionary<string,string[]> Conversions=>Data.ConversionMatrix;
    public static IReadOnlyDictionary<string,string> ToolNames=>Data.ToolNames;
    public static string Extension(string path)=>Path.GetExtension(path).TrimStart('.').ToLowerInvariant() switch{"jpeg"=>"jpg","tif"=>"tiff","heif"=>"heic","aif"=>"aiff","markdown"=>"md","htm"=>"html","gzip" or "tgz"=>"gz",var ext=>ext};
    public static bool IsImage(string path)=>new[]{"jpg","png","webp","heic","tiff","svg","avif","bmp"}.Contains(Extension(path));
    public static bool IsVideo(string path)=>new[]{"mp4","mov","mkv","webm","avi","wmv","m4v","gif"}.Contains(Extension(path));
    public static bool IsAudio(string path)=>new[]{"mp3","m4a","wav","flac","ogg","opus","aiff","wma","aac"}.Contains(Extension(path));
    public static bool IsArchive(string path)=>new[]{"zip","tar","gz","rar"}.Contains(Extension(path));
    public static string[] Formats(IReadOnlyList<string> inputs)=>Intersect(inputs,Data.ConversionMatrix);
    public static string[] Tools(IReadOnlyList<string> inputs)
    {
        var result=Intersect(inputs,inputs.Count>1?Data.MultipleTools:Data.SingleTools).ToList();
        if(inputs.Count>0&&inputs.All(p=>IsImage(p)||Extension(p)=="pdf"))result.Add("readQRCode");return result.ToArray();
    }
    private static string[] Intersect(IReadOnlyList<string> inputs,Dictionary<string,string[]> table)
    {
        if(inputs.Count==0||inputs.Any(p=>Directory.Exists(p)&&Extension(p) is not ("pages" or "rtfd")))return [];
        return table.GetValueOrDefault(Extension(inputs[0]),[]).Where(f=>inputs.All(p=>table.GetValueOrDefault(Extension(p),[]).Contains(f))).ToArray();
    }
}
