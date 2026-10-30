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
        var uri=new Uri("https://download.example/fixture");var output=Path.Combine(root,"Download.bin");var bytes=new byte[]{1,2,3};
        HttpResponseMessage Response(HttpRequestMessage request,HttpContent? content=null)=>new(HttpStatusCode.OK){RequestMessage=request,Content=content??new ByteArrayContent(bytes)};
        using(var handler=new Handler((request,n)=>n==1?throw new HttpRequestException("Transient connection failure"):Response(request))){await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));check(handler.Calls==2&&File.ReadAllBytes(output).SequenceEqual(bytes),"Interrupted download did not retry safely");File.Delete(output);}
        using(var handler=new Handler((request,n)=>Response(request,new ByteArrayContent(new byte[11])))){try{await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));throw new Exception("Oversized download accepted");}catch(InvalidDataException){check(handler.Calls==1&&!File.Exists(output),"Oversized download retried or left a partial file");}}
        using(var handler=new Handler((request,n)=>Response(new HttpRequestMessage(HttpMethod.Get,"http://download.example/unsafe")))){try{await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));throw new Exception("HTTP redirect accepted");}catch(InvalidDataException){check(handler.Calls==1&&!File.Exists(output),"Unsafe redirect retried or wrote a file");}}
        using(var handler=new Handler((request,n)=>Response(request,n==1?new StreamContent(new Stalled()):null))){await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromMilliseconds(50));check(handler.Calls==2&&File.ReadAllBytes(output).SequenceEqual(bytes),"Stalled download did not release and retry its partial file");File.Delete(output);}
        using(var cancel=new CancellationTokenSource(80))using(var handler=new Handler((request,n)=>Response(request,new StreamContent(new Stalled())))){try{await VerifiedDownload.FetchForTest(uri,output,10,null,cancel.Token,handler,TimeSpan.FromSeconds(1));throw new Exception("Cancelled download continued");}catch(OperationCanceledException){check(handler.Calls==1&&!File.Exists(output),"Cancelled download retried or left a partial file");}}
        using(var handler=new Handler((request,n)=>{var result=Response(request);result.Content.Headers.ContentLength=5;return result;})){try{await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));throw new Exception("Truncated download accepted");}catch(EndOfStreamException){check(handler.Calls==3&&!File.Exists(output),"Truncated download did not stop after three attempts");}}
        File.WriteAllText(output,"keep");using(var handler=new Handler((request,n)=>Response(request))){try{await VerifiedDownload.FetchForTest(uri,output,10,null,CancellationToken.None,handler,TimeSpan.FromSeconds(1));throw new Exception("Existing output overwritten");}catch(IOException){check(File.ReadAllText(output)=="keep"&&handler.Calls==1,"Download changed an existing file");}}File.Delete(output);
        foreach(var blocked in new[]{
            "http://127.0.0.1/file","http://127.1.2.3/file","http://[::1]/file","http://0.0.0.0/file",
            "http://2130706433/file","http://0x7f000001/file",
            "http://10.1.2.3/file","http://172.16.0.1/file","http://172.31.255.254/file","http://192.168.1.10/file",
            "http://100.64.0.1/file","http://169.254.169.254/latest/meta-data/","http://224.0.0.1/file",
            "http://[fe80::1]/file","http://[fc00::1]/file","http://[::ffff:127.0.0.1]/file",
            "http://localhost/file","http://localhost./file","http://app.localhost/file","http://media.local/file",
            "http://metadata.google.internal/computeMetadata/v1/"})
        {
            try{WebDownload.Validate(blocked);throw new Exception("Private web address accepted: "+blocked);}catch(ArgumentException){check(true,"Private web address rejected: "+blocked);}
        }
        foreach(var allowed in new[]{"https://example.com/file","https://8.8.8.8/file","https://[2001:4860:4860::8888]/file"})
        {
            try{check(WebDownload.Validate(allowed).AbsoluteUri==allowed,"Public web address changed: "+allowed);}catch(ArgumentException){throw new Exception("Public web address rejected: "+allowed);}
        }
    }
}
