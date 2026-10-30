using System.Text.Json;
using System.Text.RegularExpressions;
namespace Nashi.Core;
public sealed record WebPhoto(int Id,string Url,int Width=0,int Height=0);
public static class WebPhotos
{
    public static WebPhoto[] Instagram(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;var entries=root.TryGetProperty("entries",out var collection)&&collection.ValueKind==JsonValueKind.Array?collection.EnumerateArray().ToArray():[root];if(entries.Length==0||entries.Length>50)throw new InvalidDataException("Choose one Instagram post with up to 50 items.");var photos=new List<WebPhoto>();
        for(var n=0;n<entries.Length;n++)
        {
            var entry=entries[n];if(entry.TryGetProperty("duration",out var duration)&&duration.ValueKind==JsonValueKind.Number&&duration.TryGetDouble(out var seconds)&&seconds>0)continue;if(entry.TryGetProperty("formats",out var formats)&&formats.ValueKind==JsonValueKind.Array&&formats.EnumerateArray().Any(f=>f.ValueKind==JsonValueKind.Object&&f.TryGetProperty("url",out _)))continue;
            if(!entry.TryGetProperty("thumbnails",out var thumbs)||thumbs.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("A photo is unavailable. Try a fresh public post link.");
            var variants=new List<(WebPhoto Photo,int Uncropped,int Unscaled,int Area,int Position)>();var i=0;
            foreach(var thumbnail in thumbs.EnumerateArray())
            {
                if(thumbnail.ValueKind!=JsonValueKind.Object||!thumbnail.TryGetProperty("url",out var value)||value.ValueKind!=JsonValueKind.String||!Uri.TryCreate(value.GetString(),UriKind.Absolute,out var url)||url.Scheme!="https"||!new[]{"instagram.com","cdninstagram.com","fbcdn.net"}.Any(h=>url.Host==h||url.Host.EndsWith('.'+h,StringComparison.Ordinal)))continue;
                var w=thumbnail.TryGetProperty("width",out var width)&&width.ValueKind==JsonValueKind.Number&&width.TryGetInt32(out var wi)&&wi is >0 and <=16384?wi:0;var h=thumbnail.TryGetProperty("height",out var height)&&height.ValueKind==JsonValueKind.Number&&height.TryGetInt32(out var he)&&he is >0 and <=16384?he:0;var transform=Uri.UnescapeDataString(url.Query+url.AbsolutePath);
                variants.Add((new(n+1,url.AbsoluteUri,w,h),Regex.IsMatch(transform,@"(?:^|[_/=?])c[0-9]+[.]",RegexOptions.None,TimeSpan.FromSeconds(1))?0:1,Regex.IsMatch(transform,@"(?:^|[_/=?])[sp][0-9]+x[0-9]+",RegexOptions.None,TimeSpan.FromSeconds(1))?0:1,w*h,i++));
            }
            if(variants.Count==0)throw new InvalidDataException("A photo is unavailable. Try a fresh public post link.");photos.Add(variants.OrderBy(v=>v.Uncropped).ThenBy(v=>v.Unscaled).ThenBy(v=>v.Area).ThenBy(v=>v.Position).Last().Photo);
        }
        if(photos.Count==0)throw new InvalidDataException("This post contains videos. Use its Reel link to download video.");return photos.ToArray();
    }
    public static async Task<string> DownloadImageAsync(string url,string directory,IProgress<OperationProgress>? progress,CancellationToken token,string name="Image")
    {
        var uri=WebDownload.Validate(url);using var handler=new HttpClientHandler{AllowAutoRedirect=false};using var client=new HttpClient(handler){Timeout=TimeSpan.FromMinutes(10)};client.DefaultRequestHeaders.UserAgent.ParseAdd("Nashi");
        HttpResponseMessage? response=null;try
        {
            for(var n=0;n<6;n++){response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);if((int)response.StatusCode is >=300 and <400&&response.Headers.Location is {} location){var next=location.IsAbsoluteUri?location:new Uri(uri,location);WebDownload.Validate(next.AbsoluteUri);if(uri.Scheme=="https"&&next.Scheme!="https")throw new InvalidDataException("The media server redirected to an insecure URL.");response.Dispose();response=null;uri=next;continue;}break;}
            if(response is null)throw new IOException("Too many image redirects.");response.EnsureSuccessStatusCode();var mime=response.Content.Headers.ContentType?.MediaType??"";var extension=mime switch{"image/png"=>"png","image/bmp"=>"bmp","image/tiff"=>"tiff","image/jpeg"=>"jpg","image/webp"=>"webp","image/avif"=>"avif","image/gif"=>"gif","image/heic" or "image/heif"=>"heic","image/svg+xml"=>"svg",_=>throw new InvalidDataException("This link did not return a downloadable image.")};
            var length=response.Content.Headers.ContentLength;if(length>200_000_000)throw new InvalidDataException("Image exceeds 200 MB.");var file=SafePaths.Unique(directory,name+"."+extension);await using(var source=await response.Content.ReadAsStreamAsync(token))await using(var target=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true)){var buffer=new byte[81920];long total=0;int count;while((count=await source.ReadAsync(buffer,token))>0){total+=count;if(total>200_000_000)throw new InvalidDataException("Image exceeds 200 MB.");await target.WriteAsync(buffer.AsMemory(0,count),token);progress?.Report(new(length is >0?(double)total/length.Value*.98:null,"Downloading image…"));}if(total==0||length is {} expected&&total!=expected)throw new IOException("The image download ended early.");}return file;
        }finally{response?.Dispose();}
    }
    public static async Task<string> YoutubeThumbnail(string id,string directory,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        if(!Regex.IsMatch(id,@"^[A-Za-z0-9_-]{11}$",RegexOptions.None,TimeSpan.FromSeconds(1)))throw new ArgumentException("Invalid YouTube video ID.");
        string? fallback=null;foreach(var quality in new[]{"maxresdefault","sddefault","hq720","hqdefault"})
        {
            token.ThrowIfCancellationRequested();string? file=null;try{file=await DownloadImageAsync("https://i.ytimg.com/vi/"+id+"/"+quality+".jpg",directory,progress,token,"YouTube "+id);using var image=PhotoCodec.Read(file);if(image.Width>=640){if(fallback is not null)File.Delete(fallback);return file;}if(image.Width<=120){File.Delete(file);continue;}if(fallback is not null)File.Delete(fallback);fallback=file;}
            catch(HttpRequestException){if(file is not null&&File.Exists(file))File.Delete(file);}
        }return fallback??throw new IOException("No usable YouTube thumbnail was found.");
    }
}
