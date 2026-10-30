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
    public static Task FetchAsync(Uri uri,string output,long maximum,Action<double>? progress,CancellationToken token)=>FetchCore(uri,output,maximum,progress,token,null,TimeSpan.FromSeconds(60));
    internal static Task FetchForTest(Uri uri,string output,long maximum,Action<double>? progress,CancellationToken token,HttpMessageHandler handler,TimeSpan idle)=>FetchCore(uri,output,maximum,progress,token,handler,idle);
    private static async Task FetchCore(Uri uri,string output,long maximum,Action<double>? progress,CancellationToken token,HttpMessageHandler? handler,TimeSpan idle)
    {
        if(uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo))throw new InvalidDataException("An HTTPS download is required.");
        using var client=handler is null?new HttpClient(new SocketsHttpHandler{ConnectTimeout=TimeSpan.FromSeconds(30)}):new HttpClient(handler,false);client.Timeout=Timeout.InfiniteTimeSpan;client.DefaultRequestHeaders.UserAgent.ParseAdd("Nashi/0.7");
        for(var attempt=0;attempt<3;attempt++)
        {
            try{await FetchOnce(client,uri,output,maximum,progress,token,idle);return;}
            catch(Exception error)when(!token.IsCancellationRequested&&attempt<2&&error is HttpRequestException or EndOfStreamException or TimeoutException){await Task.Delay(500*(attempt+1),token);}
        }
    }
    private static async Task FetchOnce(HttpClient client,Uri uri,string output,long maximum,Action<double>? progress,CancellationToken token,TimeSpan idle)
    {
        var created=false;
        try
        {
            using var inactivity=CancellationTokenSource.CreateLinkedTokenSource(token);inactivity.CancelAfter(idle);
            using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,inactivity.Token);response.EnsureSuccessStatusCode();
            if(response.RequestMessage?.RequestUri?.Scheme!="https")throw new InvalidDataException("Unsafe download redirect.");
            var length=response.Content.Headers.ContentLength;if(length>maximum)throw new InvalidDataException("Download is larger than the supported limit.");
            await using var input=await response.Content.ReadAsStreamAsync(inactivity.Token);await using var target=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);created=true;
            var buffer=new byte[81920];long total=0;
            while(true)
            {
                inactivity.CancelAfter(idle);int count;try{count=await input.ReadAsync(buffer,inactivity.Token);}catch(IOException error){throw new HttpRequestException("The download connection was interrupted.",error);}if(count==0)break;
                total+=count;if(total>maximum)throw new InvalidDataException("Download exceeded its limit.");await target.WriteAsync(buffer.AsMemory(0,count),token);if(length>0)progress?.Invoke((double)total/length.Value);
            }
            if(length is not null&&total!=length)throw new EndOfStreamException("Download ended early.");
        }
        catch(OperationCanceledException error)when(!token.IsCancellationRequested){if(created)File.Delete(output);throw new TimeoutException("The download stopped responding. Please try again.",error);}
        catch{if(created)File.Delete(output);throw;}
    }
}
