using System.Text.Json;
using Nashi.Core;
namespace Nashi.Linux;
internal static class MediaMetadata
{
    internal static readonly string[] Editable=["title","artist","album","date","genre","comment","copyright","composer","description","creation_time","track","disc","publisher"];
    internal static string Label(string key){var parts=key.Split(':');return parts.Length==3&&parts[0] is "stream" or "chapter"&&int.TryParse(parts[1],out var index)?(parts[0]=="stream"?"Stream ":"Chapter ")+(index+1)+" · "+Caption(parts[2]):Caption(key);}
    private static string Caption(string value)=>value switch{""=>"","creation_time"=>"Created","handler_name"=>"Track name",_=>char.ToUpper(value[0])+value[1..]};
    internal static async Task<IReadOnlyDictionary<string,string>> Read(Toolchain tools,string input,CancellationToken token=default)
    {
        var result=await ProcessRunner.RunAsync(tools.Executable("ffprobe"),["-v","error","-show_format","-show_streams","-show_chapters","-of","json",input],token);using var json=JsonDocument.Parse(result.Output);var root=json.RootElement;var values=new Dictionary<string,string>{{"SourceFile",input}};
        if(root.TryGetProperty("format",out var format)&&format.TryGetProperty("tags",out var tags))foreach(var tag in tags.EnumerateObject())values[tag.Name.ToLowerInvariant()]=tag.Value.ToString();
        if(root.TryGetProperty("streams",out var streams))foreach(var stream in streams.EnumerateArray())
        {
            var index=stream.GetProperty("index").GetInt32();var prefix="stream:"+index+":";var editable=FeatureCatalog.Extension(input) is "mp4" or "mov" or "m4a" or "mkv" or "webm";foreach(var name in editable?FeatureCatalog.Extension(input) is "mkv" or "webm"?new[]{"title","language"}:new[]{"handler_name","language"}:Array.Empty<string>())values[prefix+name]="";if(stream.TryGetProperty("tags",out tags))foreach(var tag in tags.EnumerateObject()){var name=tag.Name.ToLowerInvariant();values[editable?prefix+name:$"Stream {index+1} · {name}"]=tag.Value.ToString();if(FeatureCatalog.IsAudio(input)&&Editable.Contains(name)&&!values.ContainsKey(name))values[name]=tag.Value.ToString();}
            foreach(var key in new[]{"codec_name","codec_type","width","height","sample_rate","channels"})if(stream.TryGetProperty(key,out var value))values[$"Stream {index+1} · {key}"]=value.ToString();
        }
        if(root.TryGetProperty("chapters",out var chapters)){var index=0;foreach(var chapter in chapters.EnumerateArray()){var prefix="chapter:"+index+":";values[prefix+"title"]="";if(chapter.TryGetProperty("tags",out tags))foreach(var tag in tags.EnumerateObject())values[prefix+tag.Name.ToLowerInvariant()]=tag.Value.ToString();foreach(var key in new[]{"start_time","end_time"})if(chapter.TryGetProperty(key,out var value))values[$"Chapter {index+1} · {key}"]=value.ToString();index++;}}
        return values;
    }
}
