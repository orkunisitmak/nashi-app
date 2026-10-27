using System.Text.Json;
namespace Nashi.Core;
public sealed partial class WebDownload(Toolchain tools)
{
    /// "Find media" already resolved the link; the download reuses that answer for a while
    /// instead of asking the site again (stream addresses stay valid for hours).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,(DateTime At,string Json)> resolved=new();
    private static void Remember(string url,string json){if(json.TrimStart().StartsWith('{'))resolved[url]=(DateTime.UtcNow,json);}
    private static string? Resolved(string url)=>resolved.TryGetValue(url,out var entry)&&DateTime.UtcNow-entry.At<TimeSpan.FromMinutes(45)?entry.Json:null;
    public static Uri Validate(string input)
    {
        if(!Uri.TryCreate(input,UriKind.Absolute,out var url)||url.Scheme is not ("http" or "https")||!string.IsNullOrEmpty(url.UserInfo)||url.IsLoopback||url.Host.EndsWith(".local",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Paste a public http(s) media or page link.");NetworkGuard.CheckHost(url);return url;
    }
    public async Task<IReadOnlyList<string>> DownloadAsync(string input,string kind,string destination,IProgress<OperationProgress>? progress=null,CancellationToken token=default,int? maximumHeight=null,IReadOnlySet<int>? selectedPhotos=null)
    {
        if(maximumHeight is <2 or >16384)throw new ArgumentException("Choose a supported maximum video height.");
        var payload=WebMediaPayload.Parse(input);var uri=Validate(payload?.Url??input);if(kind is not ("video" or "audio" or "thumbnail" or "photos" or "image"))throw new ArgumentException("Choose video, audio, thumbnail or images.");
        // The name must resolve to the public internet before anything (Nashi or yt-dlp) connects to it.
        await NetworkGuard.CheckResolvedAsync(uri,token);var zone=MarkOfTheWeb.Internet(uri);
        IReadOnlyList<string> Marked(IReadOnlyList<string> published){MarkOfTheWeb.Apply(published,zone);return published;}
        using var tx=new ExportTransaction(destination);
        if(kind=="image"||payload?.Kind=="image")return Marked(tx.Publish([await WebPhotos.DownloadImageAsync(uri.AbsoluteUri,tx.StagingDirectory,progress,token)],token));
        if(kind=="thumbnail"&&WebMediaPayload.YoutubeId(uri) is {} id)return Marked(tx.Publish([await WebPhotos.YoutubeThumbnail(id,tx.StagingDirectory,progress,token)],token));
        if(kind=="photos")
        {
            var photos=await InspectPhotosAsync(uri.AbsoluteUri,token);if(selectedPhotos is not null){if(selectedPhotos.Count==0||selectedPhotos.Any(id=>!photos.Any(p=>p.Id==id)))throw new ArgumentException("Select at least one available photo.");photos=photos.Where(p=>selectedPhotos.Contains(p.Id)).ToArray();}var outputs=new List<string>();var n=0;foreach(var photo in photos){var local=new ProgressScope(progress,n++,photos.Length);outputs.Add(await WebPhotos.DownloadImageAsync(photo.Url,tx.StagingDirectory,local,token,"Instagram "+uri.Segments.Last().Trim('/')+" "+photo.Id.ToString("00")));}return Marked(tx.Publish(outputs,token));
        }
        var args=new List<string>{"--ignore-config","--no-playlist","--no-part","--no-overwrites","--no-cache-dir","--restrict-filenames","--no-mtime","--progress","--newline","--no-quiet","--concurrent-fragments","4","--progress-template","download:NASHI:%(info.format_id)s:%(progress.downloaded_bytes)s:%(progress.total_bytes,progress.total_bytes_estimate)s","--print","before_dl:NASHIFORMAT:%(format_id)s","--print","after_move:filepath","--ffmpeg-location",tools.DirectoryPath,"--js-runtimes","deno:"+tools.Executable("deno"),"-P",tx.StagingDirectory,"-o","%(title).160B [%(id)s].%(ext)s"};
        if(kind=="audio")args.AddRange(["-x","--audio-format","mp3"]);else if(kind=="thumbnail")args.AddRange(["--skip-download","--write-thumbnail","--convert-thumbnails","jpg"]);else args.AddRange(["-f",maximumHeight is {} height?$"bv*[height<={height}]+ba/b[height<={height}]/bv*+ba/b":"bv*+ba/b","--merge-output-format","mp4"]);
        progress?.Report(new(null,"Opening the link…"));var meter=new DownloadMeter(progress);
        var known=Resolved(uri.AbsoluteUri);string? infoFile=null;
        try
        {
            if(known is not null)
            {
                infoFile=Path.Combine(Path.GetTempPath(),"Nashi-media-"+Guid.NewGuid().ToString("N")+".json");await File.WriteAllTextAsync(infoFile,known,token);
                try{await YtDlp.RunAsync(tools,[..args,"--load-info-json",infoFile],token,meter.Line);}
                catch(IOException){resolved.TryRemove(uri.AbsoluteUri,out _);known=null;foreach(var stale in Directory.GetFiles(tx.StagingDirectory))File.Delete(stale);}
            }
            if(known is null)await YtDlp.RunAsync(tools,[..args,"--",uri.AbsoluteUri],token,meter.Line);
        }
        finally{if(infoFile is not null)try{File.Delete(infoFile);}catch(IOException){}}
        var files=Directory.GetFiles(tx.StagingDirectory).Where(p=>new FileInfo(p).Length>0).ToList();
        if(files.Count==0)throw new IOException("No public downloadable media found. Some sites require sign-in or do not expose downloadable media.");
        if(kind=="video"&&maximumHeight is {} limit)
        {
            var engine=new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe"));for(var n=0;n<files.Count;n++){var file=files[n];var info=await engine.InspectAsync(file,token);if(info.Video is null)throw new InvalidDataException("The downloaded file contains no video.");if(info.Video.Height>limit){var resized=await engine.ConvertAsync([file],"mp4",tx.StagingDirectory,new(){Height=limit},progress,token);File.Delete(file);files[n]=resized.Single();}}
        }
        progress?.Report(new(.99,"Saving files…"));return Marked(tx.Publish(files,token));
    }
    public async Task<WebPhoto[]> InspectPhotosAsync(string input,CancellationToken token=default)
    {
        var uri=Validate(input);if(WebMediaPayload.Parse(input)?.Kind!="instagramPhotos")throw new ArgumentException("Choose an Instagram photo post link.");await NetworkGuard.CheckResolvedAsync(uri,token);var result=await YtDlp.RunAsync(tools,["--ignore-config","--no-cache-dir","--yes-playlist","--playlist-end","51","--ignore-no-formats-error","--skip-download","--dump-single-json","--no-progress","--",uri.AbsoluteUri],token);return WebPhotos.Instagram(result.Output);
    }
    /// One bar for the whole job: video and audio streams share it by weight, it never runs
    /// backwards, and the quiet phases (resolving, joining) say what is happening.
    private sealed class DownloadMeter(IProgress<OperationProgress>? progress)
    {
        private string[] streams=[];private double shown;
        internal void Line(string line)
        {
            if(line.StartsWith("NASHIFORMAT:",StringComparison.Ordinal)){streams=line[12..].Split('+');Report(.01,"Downloading media…");return;}
            if(line.StartsWith("NASHI:",StringComparison.Ordinal))
            {
                var parts=line.Split(':');if(parts.Length!=4||!double.TryParse(parts[2],System.Globalization.CultureInfo.InvariantCulture,out var current)||!double.TryParse(parts[3],System.Globalization.CultureInfo.InvariantCulture,out var total)||total<=0)return;
                var index=Math.Max(0,Array.IndexOf(streams,parts[1]));double Weight(int n)=>streams.Length<2?1:n==0?.85:.15/(streams.Length-1);
                var done=Enumerable.Range(0,index).Sum(Weight);Report((done+Weight(index)*Math.Clamp(current/total,0,1))*.9,"Downloading media…");return;
            }
            if(shown==0)
            {
                var stage=line.Contains("Downloading webpage",StringComparison.Ordinal)?"Opening the link…":line.Contains("JSON",StringComparison.Ordinal)||line.Contains("player",StringComparison.OrdinalIgnoreCase)?"Reading the media details…":line.Contains("challenge",StringComparison.OrdinalIgnoreCase)?"Unlocking the stream…":line.Contains("m3u8",StringComparison.Ordinal)||line.StartsWith("[info]",StringComparison.Ordinal)?"Choosing the best quality…":null;
                if(stage is not null)progress?.Report(new(null,stage));return;
            }
            if(line.StartsWith("[Merger]",StringComparison.Ordinal))Report(.93,"Joining video and audio…");
            else if(line.StartsWith("[ExtractAudio]",StringComparison.Ordinal))Report(.93,"Converting the audio…");
        }
        private void Report(double fraction,string label){shown=Math.Max(shown,fraction);progress?.Report(new(shown,label));}
    }
    private sealed class ProgressScope(IProgress<OperationProgress>? progress,int index,int total):IProgress<OperationProgress>{public void Report(OperationProgress value)=>progress?.Report(value with{Fraction=value.Fraction is {} n?(index+n)/total*.98:null,Label=$"Photo {index+1} of {total}…"});}
}
