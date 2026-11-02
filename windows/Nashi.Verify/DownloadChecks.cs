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
        using(var handler=new Handler((request,n)=>{var result=Response(request);result.Content.Headers.ContentLength=5;return result;})){try{await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));throw new Exception("Truncated download accepted");}catch(EndOfStreamException){check(handler.Calls==5&&!File.Exists(output),"Truncated download did not stop after five attempts");}}
        File.WriteAllText(output,"keep");using(var handler=new Handler((request,n)=>Response(request))){try{await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));throw new Exception("Existing output overwritten");}catch(IOException){check(File.ReadAllText(output)=="keep"&&handler.Calls==1,"Download changed an existing file");}}File.Delete(output);
        var mirrors=new[]{new Uri("https://mirror1.example/pkg"),new Uri("https://mirror2.example/pkg")};using(var handler=new Handler((request,n)=>Response(request,request.RequestUri!.Host=="mirror1.example"?new ByteArrayContent(new byte[]{9,9,9}):new ByteArrayContent(bytes)))){await VerifiedDownload.FetchAnyForTest(mirrors,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1),data=>File.ReadAllBytes(data).SequenceEqual(bytes));check(handler.Calls==2&&File.ReadAllBytes(output).SequenceEqual(bytes),"Multi-mirror validation fallback did not proceed to secondary mirror");File.Delete(output);}
        using(var handler=new Handler((request,n)=>Response(request))){try{await VerifiedDownload.FetchAnyForTest([new Uri("https://mirror1.example/pkg")],output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1),_=>throw new ApplicationException("Custom validator failure"));throw new Exception("Validator exception swallowed");}catch(ApplicationException){check(!File.Exists(output)&&Directory.GetFiles(root,"*.part").Length==0,"Validator exception left behind a temporary .part file");}}
        // A permanent 4xx repeats identically on every attempt: one request,
        // one failure, no partial-file orphan, no burned backoff chain.
        using(var handler=new Handler((request,n)=>new HttpResponseMessage(HttpStatusCode.NotFound){RequestMessage=request,Content=new ByteArrayContent(bytes)})){try{await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));throw new Exception("A permanent HTTP 404 was retried");}catch(HttpRequestException){check(handler.Calls==1&&!File.Exists(output)&&!File.Exists(output+".part"),"Permanent HTTP 404 was treated as a transient failure");}}
        // A 206 whose start byte differs from the requested range cannot be a
        // continuation: its body must not land in a fresh file as if it were
        // the whole artifact. The response is discarded and a range-less
        // request restarts the transfer.
        using(var handler=new Handler((request,n)=>{if(n==1){var result=new HttpResponseMessage(HttpStatusCode.PartialContent){RequestMessage=request,Content=new ByteArrayContent(new byte[]{5,6,7})};result.Content.Headers.ContentRange=new System.Net.Http.Headers.ContentRangeHeaderValue(5,7,10);return result;}return Response(request);})){await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));check(handler.Calls==2&&File.ReadAllBytes(output).SequenceEqual(bytes)&&!File.Exists(output+".part"),"Mismatched 206 range was saved as the complete download");File.Delete(output);}
        // A 206 matching the requested start must still carry every remaining
        // byte: "bytes 3-4/10" completes 3+2=5 of 10, so the accumulated size
        // is compared with the advertised total and the transfer resumes.
        File.WriteAllBytes(output+".part",new byte[]{4,5,6});
        using(var handler=new Handler((request,n)=>{if(n==1){var result=new HttpResponseMessage(HttpStatusCode.PartialContent){RequestMessage=request,Content=new ByteArrayContent(new byte[]{7,8})};result.Content.Headers.ContentRange=new System.Net.Http.Headers.ContentRangeHeaderValue(3,4,10);return result;}return Response(request);})){await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));check(handler.Calls==2&&File.ReadAllBytes(output).SequenceEqual(bytes)&&!File.Exists(output+".part"),"A 206 shorter than the advertised total was accepted");File.Delete(output);}
    }
}
