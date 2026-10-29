using System.Net;
using System.Text.RegularExpressions;
namespace Nashi.Core;
public sealed record WebMediaPayload(string Url,string Kind,string? Thumbnail=null)
{
    public static WebMediaPayload? Parse(string? text,string? html=null,string? sourceHint=null)
    {
        if(text?.Length>32768||html?.Length>2_000_000||sourceHint?.Length>16384)return null;
        var candidates=new List<string>();if(sourceHint is not null)candidates.Add(sourceHint);
        if(html is not null)foreach(Match match in Regex.Matches(html,"(?:href|src)\\s*=\\s*(?:\"([^\"]+)\"|'([^']+)'|([^\\s>\"']+))",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1)))
            candidates.Add(WebUtility.HtmlDecode(match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Success?match.Groups[2].Value:match.Groups[3].Value));
        if(text is not null)candidates.AddRange(text.Split(['\r','\n','\t'],StringSplitOptions.RemoveEmptyEntries).Select(t=>t.Trim()));
        var urls=candidates.Where(c=>Uri.TryCreate(c,UriKind.Absolute,out var u)&&u.Scheme is "http" or "https"&&string.IsNullOrEmpty(u.UserInfo)).Distinct().Select(c=>new Uri(c)).ToArray();
        foreach(var url in urls)
        {
            var host=url.Host.ToLowerInvariant();if(host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be"||host.EndsWith(".ytimg.com",StringComparison.Ordinal)||host=="i.ytimg.com")
            {
                var id=YoutubeId(url);if(id is not null)return new("https://www.youtube.com/watch?v="+id,"youtube",urls.FirstOrDefault(u=>u.Host.EndsWith(".ytimg.com",StringComparison.Ordinal))?.AbsoluteUri);
            }
        }
        foreach(var url in urls)
        {
            if(url.Host is not ("instagram.com" or "www.instagram.com"))continue;var match=Regex.Match(url.AbsolutePath,@"^/(p|reel|reels|tv)/([A-Za-z0-9_-]+)/?",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));if(match.Success)return new("https://www.instagram.com/"+(match.Groups[1].Value is "reels" or "tv"?"reel":match.Groups[1].Value)+"/"+match.Groups[2].Value+"/",match.Groups[1].Value=="p"?"instagramPhotos":"instagramReel");
        }
        var first=urls.FirstOrDefault();if(first is null)return null;var ext=Path.GetExtension(first.AbsolutePath).ToLowerInvariant();return new(first.AbsoluteUri,new[]{".jpg",".jpeg",".png",".webp",".avif",".gif",".heic",".svg",".bmp",".tiff",".tif"}.Contains(ext)?"image":"media");
    }
    public static string? YoutubeId(Uri url)
    {
        string? id=null;var path=url.AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);
        if(url.Host=="youtu.be")id=path.FirstOrDefault();else if(url.Host.EndsWith(".ytimg.com",StringComparison.Ordinal)&&path.Length>=2&&path[0] is "vi" or "vi_webp")id=path[1];
        else if(path.Length>=2&&path[0] is "shorts" or "embed" or "live")id=path[1];else foreach(var pair in url.Query.TrimStart('?').Split('&')){var parts=pair.Split('=',2);if(parts.Length==2&&parts[0]=="v")id=Uri.UnescapeDataString(parts[1]);}
        return id is not null&&Regex.IsMatch(id,@"^[A-Za-z0-9_-]{11}$",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1))?id:null;
    }
}
