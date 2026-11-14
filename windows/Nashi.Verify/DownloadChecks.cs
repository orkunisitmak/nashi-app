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
    // A stream that pauses briefly between reads, simulating a download that stalls
    // but resumes well within the 120-second production idle timeout.
    private sealed class SlowStream:Stream
    {
        private readonly byte[] data;private int offset;
        public SlowStream(byte[] data){this.data=data;}
        public override bool CanRead=>true;public override bool CanWrite=>false;public override bool CanSeek=>false;
        public override long Length=>data.Length;public override long Position{get=>offset;set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){await Task.Delay(100,token);var count=System.Math.Min(buffer.Length,data.Length-offset);if(count>0){data.AsSpan(offset,count).CopyTo(buffer.Span);offset+=count;}return count;}
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
        // YouTube rejects old yt-dlp releases, so web downloads require at least the pinned one.
        check(Toolchain.CompareVersions("2026.07.04",Toolchain.PinnedYtDlpVersion)<0&&Toolchain.CompareVersions(Toolchain.PinnedYtDlpVersion,Toolchain.PinnedYtDlpVersion)==0&&Toolchain.CompareVersions("2026.10.26.232113","2026.10.26")>0&&Toolchain.CompareVersions("2027.01.02","2026.12.31")>0,"yt-dlp release dates compared out of order");
        check(WebDownload.MayNeedNewerExtractor("WARNING: Your yt-dlp version (2026.07.04) is older than 90 days!\nERROR: unable to download video data: HTTP Error 403: Forbidden")&&!WebDownload.MayNeedNewerExtractor("ERROR: Unsupported URL: https://example.com/"),"An outdated-extractor failure was not recognized");
        check(WebDownload.FriendlyError("ERROR: unable to download video data: HTTP Error 403: Forbidden")=="The site did not allow this download. The link may have expired or need a login."&&WebDownload.FriendlyError("ERROR: Sign in to confirm you're not a bot")=="This link requires a login or is restricted. Try a public share link; Nashi does not read your browser sessions.","yt-dlp failures were summarized differently from the Mac");
        var details=WebDownload.Details("[youtube] abc: Downloading webpage\nNASHI:1:2\nERROR: unable to download https://rr1.googlevideo.com/videoplayback?sig=secret: HTTP Error 403");
        check(details.StartsWith("ERROR:",StringComparison.Ordinal)&&!details.Contains("sig=secret",StringComparison.Ordinal)&&!details.Contains("NASHI:",StringComparison.Ordinal),"yt-dlp details kept signed links or progress lines");
        // HTML printing serves only regular files inside the page's folder.
        var htmlRoot=Path.Combine(root,"Html printing");var site=Path.Combine(htmlRoot,"Html site");Directory.CreateDirectory(Path.Combine(site,"assets"));File.WriteAllText(Path.Combine(site,"assets","style.css"),"p{color:#123}");File.WriteAllText(Path.Combine(htmlRoot,"Outside.txt"),"private");
        try
        {
            check(HtmlPdf.Resolve(site,"/assets/style.css?v=1")==Path.GetFullPath(Path.Combine(site,"assets","style.css"))&&HtmlPdf.Resolve(site,"/../Outside.txt") is null&&HtmlPdf.Resolve(site,"/%2e%2e/Outside.txt") is null&&HtmlPdf.Resolve(site,"/assets") is null&&HtmlPdf.Resolve(site,"/C:/Windows/win.ini") is null&&HtmlPdf.Resolve(site,"assets/style.css") is null,"HTML printing served a file outside the page's folder");
            if(HtmlPdf.Browser() is {} browser)
            {
                // Like the Mac WebKit print: scripts run (an e-invoice QR is drawn by script) and remote resources are blocked without hanging.
                var page=Path.Combine(site,"Invoice.html");File.WriteAllText(page,"<!doctype html><html><head><meta charset=\"utf-8\"><link rel=\"stylesheet\" href=\"assets/style.css\"></head><body><p>Fatura çğış</p><canvas id=\"qr\" width=\"120\" height=\"120\"></canvas><img src=\"https://example.invalid/remote.png\"><script>document.getElementById('qr').getContext('2d').fillRect(10,10,100,100);document.body.insertAdjacentHTML('beforeend','<p>Script ran</p>');</script></body></html>");
                var printed=Path.Combine(htmlRoot,"Invoice.pdf");await HtmlPdf.PrintAsync(browser,page,printed,CancellationToken.None);
                using var pdf=UglyToad.PdfPig.PdfDocument.Open(printed);var text=string.Concat(pdf.GetPages().Select(p=>p.Text));
                check(text.Contains("Script ran",StringComparison.Ordinal)&&text.Contains("Fatura",StringComparison.Ordinal),"The browser print did not run page scripts");
            }
        }
        finally{Directory.Delete(htmlRoot,true);}
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
        // The production idle timeout is 120 seconds; a download that stalls briefly and resumes must still finish.
        check(VerifiedDownload.DefaultIdle==TimeSpan.FromSeconds(120),"Tool download idle timeout is not 120 seconds");
        var slowBytes=new byte[]{10,11,12};using var slowHandler=new Handler((request,n)=>Response(request,new StreamContent(new SlowStream(slowBytes))));await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,slowHandler,VerifiedDownload.DefaultIdle);check(File.ReadAllBytes(output).SequenceEqual(slowBytes)&&!File.Exists(output+".part"),"Download with brief stall under 120s idle timeout failed");File.Delete(output);
        using var slowAnyHandler=new Handler((request,n)=>Response(request,new StreamContent(new SlowStream(slowBytes))));await VerifiedDownload.FetchAnyForTest(new[]{uri},output,10,null,CancellationToken.None,slowAnyHandler,VerifiedDownload.DefaultIdle);check(File.ReadAllBytes(output).SequenceEqual(slowBytes)&&!File.Exists(output+".part"),"Any-download with brief stall under 120s idle timeout failed");File.Delete(output);
    }
}
