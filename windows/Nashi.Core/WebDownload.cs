using System.Globalization;
using System.Text.Json;
namespace Nashi.Core;
public sealed partial class WebDownload(Toolchain tools)
{
    /// Reads a "NASHI:downloaded:total" progress line. yt-dlp prints Python floats
    /// such as 1048576.0, so the numbers are parsed with the invariant culture;
    /// under tr-TR or de-DE the "." would otherwise be read as a group separator.
    internal static double? ProgressFraction(string line)
    {
        if(!line.StartsWith("NASHI:",StringComparison.Ordinal))return null;
        var parts=line.Split(':');
        if(parts.Length!=3||!double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var current)||!double.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out var total)||!double.IsFinite(current)||!double.IsFinite(total)||total<=0)return null;
        return Math.Clamp(current/total,0,.98);
    }
    public static Uri Validate(string input)
    {
        if(!Uri.TryCreate(input,UriKind.Absolute,out var url)||url.Scheme is not ("http" or "https")||!string.IsNullOrEmpty(url.UserInfo)||url.IsLoopback||url.Host.EndsWith(".local",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Paste a public http(s) media or page link.");return url;
    }
    internal string FfmpegLocation=>Path.GetDirectoryName(tools.Executable("ffmpeg"))!;
    public async Task<IReadOnlyList<string>> DownloadAsync(string input,string kind,string destination,IProgress<OperationProgress>? progress=null,CancellationToken token=default,int? maximumHeight=null,IReadOnlySet<int>? selectedPhotos=null)
    {
        if(maximumHeight is <2 or >16384)throw new ArgumentException("Choose a supported maximum video height.");
        var payload=WebMediaPayload.Parse(input);var uri=Validate(payload?.Url??input);if(kind is not ("video" or "audio" or "thumbnail" or "photos" or "image"))throw new ArgumentException("Choose video, audio, thumbnail or images.");
        using var tx=new ExportTransaction(destination);
        if(kind=="image"||payload?.Kind=="image")return tx.Publish([await WebPhotos.DownloadImageAsync(uri.AbsoluteUri,tx.StagingDirectory,progress,token)],token);
        if(kind=="thumbnail"&&WebMediaPayload.YoutubeId(uri) is {} id)return tx.Publish([await WebPhotos.YoutubeThumbnail(id,tx.StagingDirectory,progress,token)],token);
        if(kind=="photos")
        {
            var photos=await InspectPhotosAsync(uri.AbsoluteUri,token);if(selectedPhotos is not null){if(selectedPhotos.Count==0||selectedPhotos.Any(id=>!photos.Any(p=>p.Id==id)))throw new ArgumentException("Select at least one available photo.");photos=photos.Where(p=>selectedPhotos.Contains(p.Id)).ToArray();}var outputs=new List<string>();var n=0;foreach(var photo in photos){var local=new ProgressScope(progress,n++,photos.Length);outputs.Add(await WebPhotos.DownloadImageAsync(photo.Url,tx.StagingDirectory,local,token,"Instagram "+uri.Segments.Last().Trim('/')+" "+photo.Id.ToString("00")));}return tx.Publish(outputs,token);
        }
        var args=BaseArguments(false);args.AddRange(["--no-part","--no-overwrites","--windows-filenames","--trim-filenames","180","--no-mtime","--progress","--newline","--progress-template","download:NASHI:%(progress.downloaded_bytes)s:%(progress.total_bytes,progress.total_bytes_estimate)s","--print","after_move:filepath",
            // yt-dlp tests formats in temporary files under its home path, which defaults to the working directory.
            "--paths","home:"+tx.StagingDirectory,"--paths","temp:"+tx.StagingDirectory,"-o","%(title).160B [%(id)s].%(ext)s"]);
        if(kind=="audio")args.AddRange(["-f","bestaudio/best","-x","--audio-format","mp3","--audio-quality","0"]);else if(kind=="thumbnail")args.AddRange(["--skip-download","--write-thumbnail","--convert-thumbnails","jpg"]);
        else{var limit=maximumHeight is {} height?$"[height<=?{height}]":"";args.AddRange(["-f",$"bestvideo[ext=mp4]{limit}+bestaudio[ext=m4a]/best[ext=mp4]{limit}/bestvideo{limit}+bestaudio/best{limit}","--merge-output-format","mp4","--remux-video","mp4"]);}
        args.AddRange(["--",uri.AbsoluteUri]);
        progress?.Report(new(null,"Resolving media…"));
        await RunYtDlpAsync(args,progress,token,line=>
        {
            if(ProgressFraction(line) is {} fraction)progress?.Report(new(fraction,"Downloading media…"));
        },()=>{foreach(var partial in Directory.GetFiles(tx.StagingDirectory))File.Delete(partial);});
        var files=Directory.GetFiles(tx.StagingDirectory).Where(p=>new FileInfo(p).Length>0).ToList();
        if(files.Count==0)throw new IOException("No public downloadable media found. Some sites require sign-in or do not expose downloadable media.");
        if(kind=="video"&&maximumHeight is {} limit)
        {
            var engine=new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe"));for(var n=0;n<files.Count;n++){var file=files[n];var info=await engine.InspectAsync(file,token);if(info.Video is null)throw new InvalidDataException("The downloaded file contains no video.");if(info.Video.Height>limit){var resized=await engine.ConvertAsync([file],"mp4",tx.StagingDirectory,new(){Height=limit},progress,token);File.Delete(file);files[n]=resized.Single();}}
        }
        progress?.Report(new(.99,"Saving files…"));return tx.Publish(files,token);
    }
    public async Task<WebPhoto[]> InspectPhotosAsync(string input,CancellationToken token=default)
    {
        var uri=Validate(input);if(WebMediaPayload.Parse(input)?.Kind!="instagramPhotos")throw new ArgumentException("Choose an Instagram photo post link.");var args=BaseArguments(true);args.AddRange(["--ignore-no-formats-error","--skip-download","--dump-single-json","--no-progress","--",uri.AbsoluteUri]);var result=await RunYtDlpAsync(args,null,token);return WebPhotos.Instagram(result.Output);
    }
    /// The same yt-dlp options as the Mac engine: no user config, plugins, self-update checks or remote
    /// scripts; Nashi's FFmpeg and Deno; bounded retries. Windows also asks for UTF-8 so Turkish titles
    /// and messages are not mangled by the console code page.
    internal List<string> BaseArguments(bool photoPost)
    {
        var args=new List<string>{"--ignore-config","--no-plugin-dirs","--no-update","--no-cache-dir","--no-remote-components","--no-js-runtimes","--js-runtimes","deno:"+tools.Executable("deno"),"--ffmpeg-location",FfmpegLocation,"--socket-timeout","20","--retries","2","--extractor-retries","1","--no-colors","--encoding","utf-8"};
        args.AddRange(photoPost?new[]{"--yes-playlist","--playlist-end","51"}:new[]{"--no-playlist","--playlist-items","1"});
        return args;
    }
    /// Runs a yt-dlp that is at least the pinned release. When the site still rejects it (YouTube changes often),
    /// Nashi's own copy updates itself once to the newest stable release and the request is retried.
    internal async Task<ProcessResult> RunYtDlpAsync(List<string> args,IProgress<OperationProgress>? progress,CancellationToken token,Action<string>? outputLine=null,Action? beforeRetry=null)
    {
        var executable=await tools.CurrentYtDlpAsync(progress,token);
        try{return await ProcessRunner.RunAsync(executable,args,token,outputLine);}
        catch(IOException error)when(!token.IsCancellationRequested&&MayNeedNewerExtractor(error.Message))
        {
            progress?.Report(new(null,"Updating the web downloader…"));
            if(!await tools.TryUpdateYtDlpAsync(token))throw Friendly(error);
            beforeRetry?.Invoke();progress?.Report(new(null,"Resolving media…"));
            try{return await ProcessRunner.RunAsync(await tools.CurrentYtDlpAsync(progress,token),args,token,outputLine);}
            catch(IOException retry)when(!token.IsCancellationRequested){throw Friendly(retry);}
        }
        catch(IOException error)when(!token.IsCancellationRequested){throw Friendly(error);}
    }
    internal static bool MayNeedNewerExtractor(string diagnostic)
    {
        var lower=diagnostic.ToLowerInvariant();
        return lower.Contains("http error 403")||lower.Contains("older than 90 days")||lower.Contains("unable to extract")||lower.Contains("nsig")||lower.Contains("signature")||lower.Contains("precondition check failed")||lower.Contains("http error 400");
    }
    private static WebDownloadException Friendly(IOException error)=>new(FriendlyError(error.Message),Details(error.Message),error);
    /// Mirrors the Mac WebDownloadEngine.friendlyError wording, which is translated in the shared catalog.
    public static string FriendlyError(string diagnostic)
    {
        var lower=diagnostic.ToLowerInvariant();
        if(lower.Contains("sign in")||lower.Contains("login")||lower.Contains("cookies")||lower.Contains("private")||lower.Contains("age-restricted"))return "This link requires a login or is restricted. Try a public share link; Nashi does not read your browser sessions.";
        if(lower.Contains("drm")||lower.Contains("protected"))return "This media is protected and cannot be downloaded here.";
        if(lower.Contains("unsupported url")||lower.Contains("no video")||lower.Contains("no media"))return "No downloadable media was found. Use the video's share link, or Copy Image Address for a photo.";
        if(lower.Contains("429")||lower.Contains("rate limit")||lower.Contains("too many requests"))return "The site is limiting requests. Wait a little and try again.";
        if(lower.Contains("403")||lower.Contains("forbidden")||lower.Contains("not available")||lower.Contains("unavailable"))return "The site did not allow this download. The link may have expired or need a login.";
        return "The site could not provide this media. Try a fresh public share link or a direct media URL.";
    }
    /// The last meaningful yt-dlp lines, ERROR lines first. Links can carry signed tokens and are removed;
    /// the profile folder is shortened to ~.
    public static string Details(string diagnostic)
    {
        var lines=diagnostic.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Where(l=>!l.StartsWith("NASHI:",StringComparison.Ordinal)&&!l.StartsWith("[download]",StringComparison.Ordinal)).ToArray();
        var chosen=lines.Where(l=>l.StartsWith("ERROR:",StringComparison.Ordinal)).ToArray();if(chosen.Length==0)chosen=lines.TakeLast(3).ToArray();
        var text=string.Join("\n",chosen.TakeLast(4));
        text=System.Text.RegularExpressions.Regex.Replace(text,@"https?://\S+","<link>");
        var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);if(home.Length>3)text=text.Replace(home,"~",StringComparison.OrdinalIgnoreCase);
        return text.Length>1200?text[^1200..]:text;
    }
    private sealed class ProgressScope(IProgress<OperationProgress>? progress,int index,int total):IProgress<OperationProgress>{public void Report(OperationProgress value)=>progress?.Report(value with{Fraction=value.Fraction is {} n?(index+n)/total*.98:null,Label=$"Photo {index+1} of {total}…"});}
}
/// A yt-dlp failure with a translatable summary (Message) and the cleaned diagnostic lines (Details).
public sealed class WebDownloadException(string message,string details,Exception inner):IOException(message,inner)
{
    public string Details{get;}=details;
}
