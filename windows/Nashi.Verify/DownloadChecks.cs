using System.Net;
using Nashi.Core;
internal static class DownloadChecks
{
    private sealed class Handler(Func<HttpRequestMessage,int,HttpResponseMessage> respond):HttpMessageHandler
    {
        public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(respond(request,++Calls));}
    }
    private sealed class Stalled:Stream
    {
        public override bool CanRead=>true;public override bool CanWrite=>false;public override bool CanSeek=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){await Task.Delay(Timeout.Infinite,token);return 0;}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long length)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    public static async Task Run(Action<bool,string> check,string root)
    {
        var culture=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach(var name in new[]{"en-US","tr-TR","de-DE"})
            {
                System.Globalization.CultureInfo.CurrentCulture=new(name);
                check(WebDownload.ProgressFraction("NASHI:524288:1048576.0")==.5&&WebDownload.ProgressFraction("NASHI:1048576:1048576.0")==.98,$"yt-dlp progress parsed differently under {name}");
            }
            check(WebDownload.ProgressFraction("NASHI:12:NA") is null&&WebDownload.ProgressFraction("NASHI:1:0") is null&&WebDownload.ProgressFraction("[download] 5%") is null&&WebDownload.ProgressFraction("NASHI:NaN:1048576.0") is null&&WebDownload.ProgressFraction("NASHI:5:NaN") is null&&WebDownload.ProgressFraction("NASHI:5:Infinity") is null,"Unusable yt-dlp progress lines must be ignored");
        }
        finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
        var toolDirectory=Path.Combine(root,"Web download tools");Directory.CreateDirectory(toolDirectory);var suffix=OperatingSystem.IsWindows()?".exe":"";
        foreach(var name in new[]{"ffmpeg","ffprobe","yt-dlp","deno"}){var path=Path.Combine(toolDirectory,name+suffix);File.WriteAllText(path,"fixture");ToolInstallation.MakeExecutable(path);}File.WriteAllText(Path.Combine(toolDirectory,Toolchain.ToolsetMarkerName),Toolchain.ToolsetVersion);
        string executable="";string[] arguments=[];var expectedName="Canozan - düşman benim [4vrscEdUU-w].mp3";
        Task<ProcessResult> Capture(string path,IEnumerable<string> values,CancellationToken token,Action<string>? outputLine)
        {
            token.ThrowIfCancellationRequested();executable=path;arguments=[..values];var destination=arguments[Array.IndexOf(arguments,"-P")+1];var output=Path.Combine(destination,expectedName);File.WriteAllText(output,"fixture");return Task.FromResult(new ProcessResult(output+Environment.NewLine,""));
        }
        var downloadDirectory=Path.Combine(root,"Web download output");var downloaded=await new WebDownload(new Toolchain(toolDirectory,""),Capture).DownloadAsync("https://www.youtube.com/watch?v=4vrscEdUU-w","audio",downloadDirectory);
        var template=Array.IndexOf(arguments,"-o");check(Path.GetFileNameWithoutExtension(executable)=="yt-dlp"&&arguments.Contains("--windows-filenames")&&template>=0&&template+1<arguments.Length&&arguments[template+1]=="%(title).160B [%(id)s].%(ext)s"&&Path.GetFileName(downloaded.Single())==expectedName&&File.ReadAllText(downloaded.Single())=="fixture","Web download path must preserve Unicode titles and source IDs in filenames");
        Directory.Delete(downloadDirectory,true);Directory.Delete(toolDirectory,true);
        check(WebDownload.YoutubeFailure(new("https://www.youtube.com/watch?v=fixture"),"ERROR: HTTP Error 403: Forbidden")?.Contains("PO token",StringComparison.Ordinal)==true,"YouTube 403 failures must explain current-tool and access requirements");
        check(WebDownload.YoutubeFailure(new("https://example.com/video"),"ERROR: HTTP Error 403: Forbidden") is null&&WebDownload.YoutubeFailure(new("https://youtu.be/fixture"),"WARNING: SABR streaming was requested\nERROR: Postprocessing failed: No space left on device") is null&&WebDownload.YoutubeFailure(new("https://youtu.be/fixture"),"unrelated failure") is null,"Unrelated yt-dlp failures must preserve their original diagnostics");
        // Like the Mac: servers also send image/jpg, image/pjpeg or upper-case types for JPEG photos.
        try
        {
            foreach(var name in new[]{"en-US","tr-TR"})
            {
                System.Globalization.CultureInfo.CurrentCulture=new(name);
                check(WebPhotos.ImageExtension("image/pjpeg")=="jpg"&&WebPhotos.ImageExtension("image/jpg")=="jpg"&&WebPhotos.ImageExtension("IMAGE/JPEG")=="jpg",$"A photo served with an alias or upper-case content type was refused under {name}");
            }
        }
        finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
        check(WebPhotos.ImageExtension("image/png")=="png"&&WebPhotos.ImageExtension("binary/octet-stream") is null&&WebPhotos.ImageExtension("application/octet-stream") is null&&WebPhotos.ImageExtension("text/html") is null,"A non-image response was saved as an image");
        var uri=new Uri("https://download.example/fixture");var output=Path.Combine(root,"Download.bin");var bytes=new byte[]{1,2,3};
        HttpResponseMessage Response(HttpRequestMessage request,HttpContent? content=null)=>new(HttpStatusCode.OK){RequestMessage=request,Content=content??new ByteArrayContent(bytes)};
        using(var handler=new Handler((request,n)=>n==1?throw new HttpRequestException("Transient connection failure"):Response(request))){await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));check(handler.Calls==2&&File.ReadAllBytes(output).SequenceEqual(bytes),"Interrupted download did not retry safely");File.Delete(output);}
        using(var handler=new Handler((request,n)=>Response(request,new ByteArrayContent(new byte[11])))){try{await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));throw new Exception("Oversized download accepted");}catch(InvalidDataException){check(handler.Calls==1&&!File.Exists(output),"Oversized download retried or left a partial file");}}
        using(var handler=new Handler((request,n)=>Response(new HttpRequestMessage(HttpMethod.Get,"http://download.example/unsafe")))){try{await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));throw new Exception("HTTP redirect accepted");}catch(InvalidDataException){check(handler.Calls==1&&!File.Exists(output),"Unsafe redirect retried or wrote a file");}}
        using(var handler=new Handler((request,n)=>Response(request,n==1?new StreamContent(new Stalled()):null))){await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromMilliseconds(50));check(handler.Calls==2&&File.ReadAllBytes(output).SequenceEqual(bytes),"Stalled download did not release and retry its partial file");File.Delete(output);}
        using(var cancel=new CancellationTokenSource(80))using(var handler=new Handler((request,n)=>Response(request,new StreamContent(new Stalled())))){try{await VerifiedDownload.FetchForTest(uri,output,10,null,cancel.Token,handler,TimeSpan.FromSeconds(1));throw new Exception("Cancelled download continued");}catch(OperationCanceledException){check(handler.Calls==1&&!File.Exists(output),"Cancelled download retried or left a partial file");}}
        using(var handler=new Handler((request,n)=>{var result=Response(request);result.Content.Headers.ContentLength=5;return result;})){try{await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));throw new Exception("Truncated download accepted");}catch(EndOfStreamException){check(handler.Calls==3&&!File.Exists(output),"Truncated download did not stop after three attempts");}}
        File.WriteAllText(output,"keep");using(var handler=new Handler((request,n)=>Response(request))){try{await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));throw new Exception("Existing output overwritten");}catch(IOException){check(File.ReadAllText(output)=="keep"&&handler.Calls==1,"Download changed an existing file");}}File.Delete(output);
        var mirrors=new[]{new Uri("https://mirror1.example/pkg"),new Uri("https://mirror2.example/pkg")};using(var handler=new Handler((request,n)=>Response(request,request.RequestUri!.Host=="mirror1.example"?new ByteArrayContent(new byte[]{9,9,9}):new ByteArrayContent(bytes)))){await VerifiedDownload.FetchAnyForTest(mirrors,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1),data=>File.ReadAllBytes(data).SequenceEqual(bytes));check(handler.Calls==2&&File.ReadAllBytes(output).SequenceEqual(bytes),"Multi-mirror validation fallback did not proceed to secondary mirror");File.Delete(output);}
        using(var handler=new Handler((request,n)=>Response(request))){try{await VerifiedDownload.FetchAnyForTest([new Uri("https://mirror1.example/pkg")],output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1),_=>throw new ApplicationException("Custom validator failure"));throw new Exception("Validator exception swallowed");}catch(ApplicationException){check(!File.Exists(output)&&Directory.GetFiles(root,"*.part").Length==0,"Validator exception left behind a temporary .part file");}}
    }
    public static async Task RunLiveWindowsDownload(Action<bool,string> check,string root,Toolchain tools)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("The live web download check requires Windows.");
        const string url="https://www.youtube.com/watch?v=4vrscEdUU-w";const string expected="Canozan - düşman benim [4vrscEdUU-w].mp3";
        var output=Path.Combine(root,"Live web download");Directory.CreateDirectory(output);using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            IReadOnlyList<string> downloaded;try{downloaded=await new WebDownload(tools).DownloadAsync(url,"audio",output,token:timeout.Token);}catch(OperationCanceledException)when(timeout.IsCancellationRequested){throw new TimeoutException("The live Windows web download did not finish within three minutes.");}
            check(downloaded.Count==1&&Path.GetFileName(downloaded[0])==expected&&new FileInfo(downloaded[0]).Length>0,"Live Windows download did not preserve the Unicode title and source video ID");
            var media=await new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe")).InspectAsync(downloaded[0],timeout.Token);check(media.HasAudio&&media.Duration>0,"Live Windows download did not produce a readable audio file");
        }
        finally{if(Directory.Exists(output))Directory.Delete(output,true);}
    }
}
