using System.IO.Compression;
using System.Security.Cryptography;

namespace Nashi.Core;

/// First-use setup downloads directly from upstream. Nashi does not redistribute
/// the upstream executable archives or silently import browser credentials.
/// With a search path, tools the user already installed are reused and setup
/// downloads only the missing packages; downloaded tools still take priority.
public sealed class Toolchain(string directory,string? searchPath=null)
{
    private static readonly string[] Names=["ffmpeg","ffprobe","yt-dlp","deno"];
    public string DirectoryPath=>Path.GetFullPath(directory);
    public string Executable(string name)
    {
        if(!Names.Contains(name))throw new ArgumentException("Unknown tool.");
        return Find(name,DirectoryPath)??throw new FileNotFoundException("Choose Set up media tools first.");
    }
    private string? Find(string name,string bundled){var path=Path.Combine(bundled,name+".exe");return ToolInstallation.UsableFile(path)?path:Installed(name);}
    private string? Installed(string name)
    {
        foreach(var entry in (searchPath??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            // Relative entries such as "." would resolve against whatever folder Nashi was started from.
            var folder=entry.Trim('"');if(folder.IndexOfAny(Path.GetInvalidPathChars())>=0||!Path.IsPathFullyQualified(folder))continue;
            var path=Path.Combine(folder,name+".exe");
            // Package managers such as WinGet publish tools as symbolic links whose own length is zero.
            try{if(new FileInfo(path).ResolveLinkTarget(true) is FileInfo target)path=target.FullName;}catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
            if(ToolInstallation.UsableFile(path))return path;
        }
        return null;
    }
    private bool Complete(string directory)=>Names.All(n=>Find(n,directory) is not null);
    public bool Ready=>Complete(DirectoryPath);
    public async Task InstallAsync(IProgress<OperationProgress>? progress=null,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();if(Ready){progress?.Report(new(1,"Media tools ready"));return;}
        var parent=Path.GetDirectoryName(DirectoryPath)!;Directory.CreateDirectory(parent);
        var stage=Path.Combine(parent,"tools-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try
        {
            var packages=new[]{
                new Package("FFmpeg 9.0","https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-full_build.zip","759d0a9831c436a0eb331ad36f236c06cb04aaa0005da46571f0e9d3d9206f6b",["ffmpeg.exe","ffprobe.exe"]),
                new Package("yt-dlp","https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe","66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a",["yt-dlp.exe"]),
                new Package("Deno","https://github.com/denoland/deno/releases/download/v2.9.7/deno-x86_64-pc-windows-msvc.zip","a0c3101b4158d1dfb7d6a78a7bf0f3de80c96bb423c152beec8beb22786f2238",["deno.exe"])
            }.Where(p=>p.Files.Any(f=>Installed(Path.GetFileNameWithoutExtension(f)) is null)).ToArray();
            for(var n=0;n<packages.Length;n++)
            {
                var package=packages[n];var archive=Path.Combine(stage,"package-"+n);
                await VerifiedDownload.FetchAsync(new(package.Url),archive,512L*1024*1024,p=>progress?.Report(new((n+p)/packages.Length, "Downloading "+package.Name+"…")),token);
                string hash; await using(var downloaded=File.OpenRead(archive))hash=Convert.ToHexString(await SHA256.HashDataAsync(downloaded,token)).ToLowerInvariant();
                if(hash!=package.Sha256)throw new InvalidDataException("Upstream download checksum mismatch. Nothing was installed.");
                if(package.Url.EndsWith(".zip",StringComparison.Ordinal))
                {
                    using var zip=ZipFile.OpenRead(archive);
                    foreach(var name in package.Files)
                    {
                        var entry=zip.Entries.Single(e=>Path.GetFileName(e.FullName)==name&&e.Length>0&&e.Length<256L*1024*1024);
                        entry.ExtractToFile(Path.Combine(stage,name));
                    }
                    var notice=zip.Entries.FirstOrDefault(e=>Path.GetFileName(e.FullName).StartsWith("LICENSE",StringComparison.OrdinalIgnoreCase)&&e.Length<1024*1024);
                    notice?.ExtractToFile(Path.Combine(stage,package.Name.Split(' ')[0]+"-LICENSE.txt"));
                }
                else File.Move(archive,Path.Combine(stage,package.Files[0]));
                if(File.Exists(archive))File.Delete(archive);
            }
            token.ThrowIfCancellationRequested();
            // Publish replaces the whole folder, so carry over downloaded tools whose package was skipped because PATH has them.
            if(Directory.Exists(DirectoryPath))foreach(var file in Directory.GetFiles(DirectoryPath)){var kept=Path.Combine(stage,Path.GetFileName(file));if(!File.Exists(kept)&&ToolInstallation.UsableFile(file))File.Copy(file,kept);}
            ToolInstallation.Publish(stage,DirectoryPath,Complete);progress?.Report(new(1,"Media tools ready"));
        }
        finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    private sealed record Package(string Name,string Url,string Sha256,string[] Files);
}
public static class VerifiedDownload
{
    public static Task FetchAsync(Uri uri,string output,long maximum,Action<double>? progress,CancellationToken token)=>FetchCore(uri,output,maximum,progress,token,null,TimeSpan.FromSeconds(90));
    public static Task FetchAnyAsync(IReadOnlyList<Uri> uris,string output,long maximum,Action<double>? progress,CancellationToken token,Func<string,bool>? validator=null)=>FetchAnyCore(uris,output,maximum,progress,token,null,TimeSpan.FromSeconds(90),validator);
    internal static Task FetchForTest(Uri uri,string output,long maximum,Action<double>? progress,CancellationToken token,HttpMessageHandler handler,TimeSpan idle)=>FetchCore(uri,output,maximum,progress,token,handler,idle);
    internal static Task FetchAnyForTest(IReadOnlyList<Uri> uris,string output,long maximum,Action<double>? progress,CancellationToken token,HttpMessageHandler handler,TimeSpan idle,Func<string,bool>? validator=null)=>FetchAnyCore(uris,output,maximum,progress,token,handler,idle,validator);
    private static async Task FetchAnyCore(IReadOnlyList<Uri> uris,string output,long maximum,Action<double>? progress,CancellationToken token,HttpMessageHandler? handler,TimeSpan idle,Func<string,bool>? validator=null)
    {
        if(uris.Count==0)throw new ArgumentException("At least one download URI is required.");
        Exception? last=null;
        for(var index=0;index<uris.Count;index++)
        {
            token.ThrowIfCancellationRequested();
            var temporary=output+"."+Guid.NewGuid().ToString("N")+".part";
            try
            {
                await FetchCore(uris[index],temporary,maximum,progress,token,handler,idle);
                if(validator is not null&&!validator(temporary))throw new InvalidDataException("Downloaded package validation failed for mirror: "+uris[index]);
                File.Move(temporary,output);
                return;
            }
            catch(Exception error)when(!token.IsCancellationRequested&&error is HttpRequestException or IOException or TimeoutException or InvalidDataException)
            {
                last=error;
            }
            finally
            {
                // Nested ".part" of a per-mirror temporary must also vanish, or the
                // validator test's "no leftover *.part" invariant breaks.
                for(var d=0;d<3&&File.Exists(temporary);d++){try{File.Delete(temporary);break;}catch(IOException){await Task.Delay(50*(d+1),CancellationToken.None);}}
                for(var d=0;d<3&&File.Exists(temporary+".part");d++){try{File.Delete(temporary+".part");break;}catch(IOException){await Task.Delay(50*(d+1),CancellationToken.None);}}
            }
        }
        throw last??new InvalidDataException("All download mirrors failed.");
    }
    private static async Task FetchCore(Uri uri,string output,long maximum,Action<double>? progress,CancellationToken token,HttpMessageHandler? handler,TimeSpan idle)
    {
        if(uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo))throw new InvalidDataException("An HTTPS download is required.");
        using var client=handler is null?new HttpClient(new SocketsHttpHandler{ConnectTimeout=TimeSpan.FromSeconds(30)}):new HttpClient(handler,false);client.Timeout=Timeout.InfiniteTimeSpan;client.DefaultRequestHeaders.UserAgent.ParseAdd("Nashi/0.7");
        // Interrupted transfers resume from a ".part" sibling: restarting a
        // ~400 MB archive from zero on every flaky-network blip used to read
        // as a timeout even while connectivity was healthy.
        var partial=output+".part";
        for(var attempt=0;attempt<5;attempt++)
        {
            client.DefaultRequestHeaders.Range=null;
            try
            {
                if(File.Exists(partial)&&new FileInfo(partial).Length>0)client.DefaultRequestHeaders.Range=new System.Net.Http.Headers.RangeHeaderValue(new FileInfo(partial).Length,null);
                await FetchOnce(client,uri,partial,output,maximum,progress,token,idle);return;
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){TryDelete(partial);throw;}
            // Permanent 4xx answers (404/403/…) repeat identically on every
            // retry — fail fast instead of burning five attempts and the full
            // backoff chain; transient 5xx/408/429 responses and connection
            // errors still retry.
            catch(Exception error)when(!token.IsCancellationRequested&&attempt<4&&(error is EndOfStreamException or TimeoutException||(error is HttpRequestException http&&TransientStatus(http.StatusCode)))){await Task.Delay(TimeSpan.FromSeconds(1<<attempt)+TimeSpan.FromMilliseconds(Random.Shared.Next(250)),token);}
        }
    }
    private static void TryDelete(string path){try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
    // Network failures carry a null status; a null status or one of the
    // transient HTTP codes is worth another attempt, a settled 4xx is not.
    private static bool TransientStatus(System.Net.HttpStatusCode? status)=>status is null||status==System.Net.HttpStatusCode.RequestTimeout||status==System.Net.HttpStatusCode.TooManyRequests||status>=System.Net.HttpStatusCode.InternalServerError;
    private static async Task FetchOnce(HttpClient client,Uri uri,string partial,string output,long maximum,Action<double>? progress,CancellationToken token,TimeSpan idle)
    {
        var resumeFrom=0L;
        if(File.Exists(partial))resumeFrom=new FileInfo(partial).Length;
        try
        {
            using var inactivity=CancellationTokenSource.CreateLinkedTokenSource(token);inactivity.CancelAfter(idle);
            using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,inactivity.Token);response.EnsureSuccessStatusCode();
            if(response.RequestMessage?.RequestUri?.Scheme!="https")throw new InvalidDataException("Unsafe download redirect.");
            var range=response.Content.Headers.ContentRange;
            // Only a 206 that maps exactly onto our partial bytes is a genuine
            // continuation: a different start byte would append misaligned
            // data and a total smaller than what we hold would corrupt the
            // tail, so the response is dropped and the transfer restarts
            // cleanly without a Range header.
            var rangeTotal=0L;var ranged=response.StatusCode==System.Net.HttpStatusCode.PartialContent&&resumeFrom>0&&range?.From==resumeFrom&&(rangeTotal=range.Length??-1)>=resumeFrom;
            if(!ranged&&response.StatusCode==System.Net.HttpStatusCode.PartialContent&&(resumeFrom>0||range?.From>0)){TryDelete(partial);resumeFrom=0;throw new HttpRequestException("The server answered the range request with an unmatched 206; restarting without a range.");}
            if(!ranged&&resumeFrom>0){File.Delete(partial);resumeFrom=0;}
            var full=ranged?rangeTotal:response.Content.Headers.ContentLength;
            if(full>maximum)throw new InvalidDataException("Download is larger than the supported limit.");
            await using var input=await response.Content.ReadAsStreamAsync(inactivity.Token);await using var target=new FileStream(partial,ranged?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.None,81920,true);
            var buffer=new byte[81920];long total=0;
            while(true)
            {
                inactivity.CancelAfter(idle);int count;try{count=await input.ReadAsync(buffer,inactivity.Token);}catch(IOException error){throw new HttpRequestException("The download connection was interrupted.",error);}if(count==0)break;
                total+=count;if(resumeFrom+total>maximum)throw new InvalidDataException("Download exceeded its limit.");await target.WriteAsync(buffer.AsMemory(0,count),token);if(full>0)progress?.Invoke((double)(resumeFrom+total)/full.Value);
            }
            var length=response.Content.Headers.ContentLength;if(length is not null&&total!=length)throw new EndOfStreamException("Download ended early.");
            // The advertised Content-Range total is the promise of completeness:
            // a narrow range (bytes 3-4/10 against a 3-byte partial) passes the
            // body-length check yet leaves the file truncated, so the partial
            // stays in place and the next attempt resumes past it.
            if(ranged&&resumeFrom+total!=rangeTotal)throw new EndOfStreamException("The ranged response ended before the advertised total.");
        }
        catch(OperationCanceledException error)when(!token.IsCancellationRequested){throw new TimeoutException("The download stopped responding. Please try again.",error);}
        // Failures keep the partial so the next attempt resumes; the caller's
        // staging-directory cleanup or the SHA-256 check is the final safety net.
        if(File.Exists(output)){TryDelete(partial);throw new IOException("Download target already exists.");}
        File.Move(partial,output);
    }
}
