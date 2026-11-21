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
        var args=new List<string>{"--ignore-config","--no-playlist","--no-part","--no-overwrites","--no-cache-dir","--restrict-filenames","--no-mtime","--progress","--newline","--progress-template","download:NASHI:%(progress.downloaded_bytes)s:%(progress.total_bytes,progress.total_bytes_estimate)s","--print","after_move:filepath","--ffmpeg-location",FfmpegLocation,"--js-runtimes","deno:"+tools.Executable("deno"),"-P",tx.StagingDirectory,"-o","%(title).160B [%(id)s].%(ext)s"};
        if(kind=="audio")args.AddRange(["-x","--audio-format","mp3"]);else if(kind=="thumbnail")args.AddRange(["--skip-download","--write-thumbnail","--convert-thumbnails","jpg"]);else args.AddRange(["-f",maximumHeight is {} height?$"bv*[height<={height}]+ba/b[height<={height}]/bv*+ba/b":"bv*+ba/b","--merge-output-format","mp4"]);
        args.AddRange(["--",uri.AbsoluteUri]);
        progress?.Report(new(null,"Resolving media…"));
        await ProcessRunner.RunAsync(tools.Executable("yt-dlp"),args,token,line=>
        {
            if(ProgressFraction(line) is {} fraction)progress?.Report(new(fraction,"Downloading media…"));
        });
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
        var uri=Validate(input);if(WebMediaPayload.Parse(input)?.Kind!="instagramPhotos")throw new ArgumentException("Choose an Instagram photo post link.");var result=await ProcessRunner.RunAsync(tools.Executable("yt-dlp"),["--ignore-config","--no-cache-dir","--yes-playlist","--playlist-end","51","--ignore-no-formats-error","--skip-download","--dump-single-json","--no-progress","--",uri.AbsoluteUri],token);return WebPhotos.Instagram(result.Output);
    }
    private sealed class ProgressScope(IProgress<OperationProgress>? progress,int index,int total):IProgress<OperationProgress>{public void Report(OperationProgress value)=>progress?.Report(value with{Fraction=value.Fraction is {} n?(index+n)/total*.98:null,Label=$"Photo {index+1} of {total}…"});}
}
