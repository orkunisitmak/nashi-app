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
        // A real listener on loopback: the refusal must happen before the request reaches it, not because the port is closed.
        var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();
        try
        {
            var port=((IPEndPoint)listener.LocalEndpoint).Port;
            using var handler=WebDownload.PublicOnlyHandler();handler.UseProxy=false;using var client=new HttpClient(handler);
            try{await client.GetAsync($"http://localhost:{port}/");check(false,"A name that resolves to this computer was reached");}
            catch(HttpRequestException error){check(!listener.Pending()&&error.ToString().Contains("private network"),"A name that resolves to this computer is refused before the connection is made");}
        }
        finally{listener.Stop();}
        // The proxy used for yt-dlp: private targets get 403 before any connection; allowed targets are piped through.
        async Task<string> ProxyRequest(PublicOnlyProxy proxy,string request)
        {
            using var tcp=new System.Net.Sockets.TcpClient();await tcp.ConnectAsync(IPAddress.Loopback,int.Parse(proxy.Url[(proxy.Url.LastIndexOf(':')+1)..]));
            var stream=tcp.GetStream();await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(request));
            var buffer=new byte[512];var read=await stream.ReadAsync(buffer);return System.Text.Encoding.ASCII.GetString(buffer,0,read);
        }
        var secret=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);secret.Start();
        try
        {
            var secretPort=((IPEndPoint)secret.LocalEndpoint).Port;
            using(var strict=new PublicOnlyProxy())
            {
                foreach(var target in new[]{"localhost","127.0.0.1","10.0.0.5","192.168.1.9","169.254.169.254","198.18.0.1","198.19.255.1","[::1]"})
                {
                    var answer=await ProxyRequest(strict,$"CONNECT {target}:{secretPort} HTTP/1.1\r\nHost: {target}\r\n\r\n");
                    check(answer.StartsWith("HTTP/1.1 403"),"Proxy let CONNECT reach "+target);
                }
                check((await ProxyRequest(strict,$"GET http://localhost:{secretPort}/x HTTP/1.1\r\nHost: localhost\r\n\r\n")).StartsWith("HTTP/1.1 403"),"Proxy let a plain HTTP request reach localhost");
                check((await ProxyRequest(strict,"CONNECT\r\n\r\n")).StartsWith("HTTP/1.1 400"),"Proxy accepted a malformed request");
                check(!secret.Pending(),"A refused request still reached the local service");
            }
            using var open=new PublicOnlyProxy(_=>true);
            var accept=secret.AcceptTcpClientAsync();
            var connected=await ProxyRequest(open,$"CONNECT 127.0.0.1:{secretPort} HTTP/1.1\r\nHost: x\r\n\r\n");
            check(connected.StartsWith("HTTP/1.1 200")&&await Task.WhenAny(accept,Task.Delay(3000))==accept,"Proxy did not connect an allowed target");
        }
        finally{secret.Stop();}
        foreach(var name in new[]{"http://127.1/a","http://2130706433/a","http://0x7f.1/a","http://167772161/a","http://198.18.0.1/a","http://198.19.255.1/a"})try{WebDownload.Validate(name);check(false,"Short or numeric loopback form accepted: "+name);}catch(ArgumentException){check(true,"Short and numeric IPv4 forms of private addresses are refused");}
        check(WebDownload.IsPrivate(IPAddress.Parse("198.18.0.1"))&&WebDownload.IsPrivate(IPAddress.Parse("198.19.255.255"))&&!WebDownload.IsPrivate(IPAddress.Parse("198.17.255.255"))&&!WebDownload.IsPrivate(IPAddress.Parse("198.20.0.1")),"The 198.18.0.0/15 benchmarking range is private and its neighbours are not");
        foreach(var name in new[]{"http://localhost./a","http://app.localhost./a","http://printer.local./a"})try{WebDownload.Validate(name);check(false,"Trailing-dot local name accepted: "+name);}catch(ArgumentException){check(true,"Trailing-dot local names are refused");}
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
}
