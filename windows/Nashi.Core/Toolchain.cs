using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SharpCompress.Readers;

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
    private static string FileName(string name)=>OperatingSystem.IsWindows()?name+".exe":name;
    private static bool Usable(string path)=>ToolInstallation.UsableFile(path)&&ToolInstallation.CanExecute(path);
    private string? Find(string name,string bundled){var path=Path.Combine(bundled,FileName(name));return Usable(path)?path:Installed(name);}
    private string? Installed(string name)
    {
        foreach(var entry in (searchPath??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            // Relative entries such as "." would resolve against whatever folder Nashi was started from.
            var folder=entry.Trim('"');if(folder.IndexOfAny(Path.GetInvalidPathChars())>=0||!Path.IsPathFullyQualified(folder))continue;
            var path=Path.Combine(folder,FileName(name));
            // Package managers such as WinGet publish tools as symbolic links whose own length is zero, so only the
            // availability check follows the link. The tool runs from its PATH location because launchers such as
            // /snap/bin/ffmpeg -> /usr/bin/snap pick their behaviour from the name they were started under.
            var checkedPath=path;
            try{if(new FileInfo(path).ResolveLinkTarget(true) is FileInfo target)checkedPath=target.FullName;}catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
            if(Usable(checkedPath))return path;
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
            var packages=Packages().Where(p=>p.Files.Any(f=>Installed(Path.GetFileNameWithoutExtension(f)) is null)).ToArray();
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
                else if(package.Url.EndsWith(".tar.xz",StringComparison.Ordinal))ExtractTarXz(archive,stage,package,token);
                else File.Move(archive,Path.Combine(stage,package.Files[0]));
                if(File.Exists(archive))File.Delete(archive);
                foreach(var name in package.Files)ToolInstallation.MakeExecutable(Path.Combine(stage,name));
            }
            token.ThrowIfCancellationRequested();
            // Publish replaces the whole folder, so carry over downloaded tools whose package was skipped because PATH has them.
            if(Directory.Exists(DirectoryPath))foreach(var file in Directory.GetFiles(DirectoryPath)){var kept=Path.Combine(stage,Path.GetFileName(file));if(!File.Exists(kept)&&ToolInstallation.UsableFile(file))File.Copy(file,kept);}
            ToolInstallation.Publish(stage,DirectoryPath,Complete);progress?.Report(new(1,"Media tools ready"));
        }
        finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    // Linux FFmpeg is pinned to a month-end BtbN build because daily builds are deleted after 14 days.
    private static Package[] Packages()=>OperatingSystem.IsWindows()?[
        new Package("FFmpeg 9.0","https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-full_build.zip","759d0a9831c436a0eb331ad36f236c06cb04aaa0005da46571f0e9d3d9206f6b",["ffmpeg.exe","ffprobe.exe"]),
        new Package("yt-dlp","https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe","66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a",["yt-dlp.exe"]),
        new Package("Deno","https://github.com/denoland/deno/releases/download/v2.9.7/deno-x86_64-pc-windows-msvc.zip","a0c3101b4158d1dfb7d6a78a7bf0f3de80c96bb423c152beec8beb22786f2238",["deno.exe"])
    ]:OperatingSystem.IsLinux()&&RuntimeInformation.OSArchitecture==Architecture.X64?[
        new Package("FFmpeg 9.0","https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-30-13-08/ffmpeg-n9.0.2-17-g2a571b6068-linux64-gpl-9.0.tar.xz","68ee646831adaae2495618346f3bba94ff207ff83bbd34d643e7004730d66269",["ffmpeg","ffprobe"]),
        new Package("yt-dlp","https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp_linux","58162f9bfdc27458ea47bfcb311cf47028f17d8154a8bf7d689861d46399230a",["yt-dlp"]),
        new Package("Deno","https://github.com/denoland/deno/releases/download/v2.9.7/deno-x86_64-unknown-linux-gnu.zip","c6527f24f4b16031d3ae4fa9f658d5f11534c8d84ce7dc8502420280919c3490",["deno"])
    ]:throw new PlatformNotSupportedException("Media tools setup supports Windows and Linux x64.");
    // .NET cannot read xz, so SharpCompress streams the tar and only the wanted files reach the disk.
    private static void ExtractTarXz(string archive,string stage,Package package,CancellationToken token)
    {
        using var file=File.OpenRead(archive);using var reader=ReaderFactory.OpenReader(file);var found=new HashSet<string>();var notice=false;
        while(reader.MoveToNextEntry())
        {
            token.ThrowIfCancellationRequested();var entry=reader.Entry;if(entry.IsDirectory)continue;var name=Path.GetFileName(entry.Key??"");string? output=null;
            if(package.Files.Contains(name)&&entry.Size>0&&entry.Size<256L*1024*1024){if(!found.Add(name))throw new InvalidDataException("The tool archive contains duplicate files.");output=Path.Combine(stage,name);}
            else if(!notice&&name.StartsWith("LICENSE",StringComparison.OrdinalIgnoreCase)&&entry.Size<1024*1024){notice=true;output=Path.Combine(stage,package.Name.Split(' ')[0]+"-LICENSE.txt");}
            if(output is null)continue;if(!string.IsNullOrEmpty(entry.LinkTarget))throw new InvalidDataException("The tool archive contains links.");
            using var source=reader.OpenEntryStream();using var target=new FileStream(output,FileMode.CreateNew);source.CopyTo(target);
        }
        if(found.Count!=package.Files.Length)throw new InvalidDataException("The downloaded tools are incomplete. Please try setup again.");
    }
    private sealed record Package(string Name,string Url,string Sha256,string[] Files);
}
public static class VerifiedDownload
{
    public static Task FetchAsync(Uri uri,string output,long maximum,Action<double>? progress,CancellationToken token)=>FetchCore(uri,output,maximum,progress,token,null,TimeSpan.FromSeconds(60));
    public static Task FetchAnyAsync(IReadOnlyList<Uri> uris,string output,long maximum,Action<double>? progress,CancellationToken token,Func<string,bool>? validator=null)=>FetchAnyCore(uris,output,maximum,progress,token,null,TimeSpan.FromSeconds(60),validator);
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
                for(var d=0;d<3&&File.Exists(temporary);d++)
                {
                    try{File.Delete(temporary);break;}
                    catch(IOException){await Task.Delay(50*(d+1),CancellationToken.None);}
                }
            }
        }
        throw last??new InvalidDataException("All download mirrors failed.");
    }
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
