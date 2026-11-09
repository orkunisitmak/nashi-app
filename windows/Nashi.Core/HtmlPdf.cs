using System.Net;
using System.Net.Sockets;
using System.Text;
namespace Nashi.Core;

/// HTML to PDF the way the Mac does it: a real browser engine prints the page, so page scripts run and
/// script-drawn content such as e-invoice QR codes appears. Windows ships Microsoft Edge, which prints
/// headlessly with an isolated, throw-away profile. Like the Mac:
/// - only regular files inside the page's folder load. A loopback server bound to this one request
///   serves them; the http origin cannot load file:// URLs, so nothing else on disk can be embedded;
/// - every remote resource is blocked: all hosts resolve to nothing and every other connection,
///   including other loopback ports and WebRTC, goes to an unreachable proxy;
/// - no camera, microphone or location is granted, and no user cookies or extensions are used.
/// When no Chromium browser is installed the caller falls back to the office runtime.
public static class HtmlPdf
{
    public static string? Browser()
    {
        if(!OperatingSystem.IsWindows())return null;
        foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)})
        {
            if(string.IsNullOrEmpty(root))continue;
            foreach(var relative in new[]{@"Microsoft\Edge\Application\msedge.exe",@"Google\Chrome\Application\chrome.exe"})
            {var path=Path.Combine(root,relative);if(File.Exists(path))return path;}
        }
        return null;
    }
    public static async Task PrintAsync(string browser,string input,string output,CancellationToken token)
    {
        var folder=Path.GetDirectoryName(Path.GetFullPath(input))!;
        var profile=Path.Combine(Path.GetTempPath(),"Nashi-Print-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(profile);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var port=((IPEndPoint)listener.LocalEndpoint).Port;
            var serving=ServeAsync(listener,folder,stop.Token);
            var page=$"http://127.0.0.1:{port}/"+Uri.EscapeDataString(Path.GetFileName(input));
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                await ProcessRunner.RunAsync(browser,["--headless","--disable-gpu","--no-first-run","--no-default-browser-check","--disable-extensions","--disable-sync","--disable-background-networking","--disable-component-update","--disable-default-apps","--mute-audio",
                    "--user-data-dir="+profile,"--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1","--proxy-server=127.0.0.1:9","--proxy-bypass-list=<-loopback>;127.0.0.1:"+port,
                    "--force-webrtc-ip-handling-policy=disable_non_proxied_udp","--deny-permission-prompts","--virtual-time-budget=10000","--run-all-compositor-stages-before-draw",
                    "--no-pdf-header-footer","--print-to-pdf-no-header","--print-to-pdf="+output,page],timeout.Token);
            }
            catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new TimeoutException("The page took too long to print.");}
            stop.Cancel();try{await serving;}catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(SocketException){}
            if(!File.Exists(output)||new FileInfo(output).Length<5)throw new InvalidDataException("The browser did not produce a PDF.");
            using(var check=File.OpenRead(output)){var head=new byte[5];if(await check.ReadAsync(head,token)<5||Encoding.ASCII.GetString(head)!="%PDF-")throw new InvalidDataException("The browser did not produce a PDF.");}
        }
        finally
        {
            stop.Cancel();listener.Stop();
            for(var attempt=0;attempt<5&&Directory.Exists(profile);attempt++){try{Directory.Delete(profile,true);}catch(IOException){await Task.Delay(200,CancellationToken.None);}catch(UnauthorizedAccessException){await Task.Delay(200,CancellationToken.None);}}
        }
    }
    private static async Task ServeAsync(TcpListener listener,string folder,CancellationToken token)
    {
        var clients=new List<Task>();
        while(!token.IsCancellationRequested)
        {
            TcpClient client;
            try{client=await listener.AcceptTcpClientAsync(token);}catch(OperationCanceledException){break;}catch(ObjectDisposedException){break;}catch(SocketException){break;}
            clients.Add(Task.Run(async()=>{using(client){try{await RespondAsync(client.GetStream(),folder,token);}catch(IOException){}catch(OperationCanceledException){}catch(SocketException){}}},CancellationToken.None));
        }
        await Task.WhenAll(clients);
    }
    private static async Task RespondAsync(NetworkStream stream,string folder,CancellationToken token)
    {
        using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,leaveOpen:true);
        var request=await reader.ReadLineAsync(token);if(request is null)return;
        for(var line=await reader.ReadLineAsync(token);!string.IsNullOrEmpty(line);line=await reader.ReadLineAsync(token)){}
        var parts=request.Split(' ');
        var file=parts.Length==3&&parts[0] is "GET" or "HEAD"?Resolve(folder,parts[1]):null;
        if(file is null){await WriteAsync(stream,"404 Not Found","text/plain",[],token);return;}
        byte[] body=parts[0]=="HEAD"?[]:await File.ReadAllBytesAsync(file,token);
        await WriteAsync(stream,"200 OK",ContentType(file),body,token);
    }
    /// A request path maps only to an existing regular file inside the page's folder: no traversal, links or devices.
    internal static string? Resolve(string folder,string target)
    {
        var path=target.Split('?','#')[0];if(!path.StartsWith('/'))return null;
        string decoded;try{decoded=Uri.UnescapeDataString(path[1..]);}catch(UriFormatException){return null;}
        if(decoded.Length==0||decoded.Contains('\0')||decoded.Contains(':')||decoded.StartsWith('/')||decoded.StartsWith('\\'))return null;
        var root=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string full;try{full=Path.GetFullPath(Path.Combine(root,decoded.Replace('/',Path.DirectorySeparatorChar)));}catch(ArgumentException){return null;}catch(NotSupportedException){return null;}catch(PathTooLongException){return null;}
        if(!full.StartsWith(root,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))return null;
        try
        {
            var info=new FileInfo(full);if(!info.Exists||(info.Attributes&(FileAttributes.ReparsePoint|FileAttributes.Device|FileAttributes.Directory))!=0||info.Length>200_000_000)return null;
            for(var parent=info.Directory;parent is not null&&parent.FullName.Length>=root.Length-1;parent=parent.Parent)if((parent.Attributes&FileAttributes.ReparsePoint)!=0)return null;
        }
        catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
        return full;
    }
    private static async Task WriteAsync(NetworkStream stream,string status,string type,byte[] body,CancellationToken token)
    {
        var head=Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head,token);if(body.Length>0)await stream.WriteAsync(body,token);await stream.FlushAsync(token);
    }
    private static string ContentType(string file)=>Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" or ".htm"=>"text/html",".xhtml"=>"application/xhtml+xml",".css"=>"text/css",".js" or ".mjs"=>"text/javascript",".json"=>"application/json",".svg"=>"image/svg+xml",
        ".png"=>"image/png",".jpg" or ".jpeg"=>"image/jpeg",".gif"=>"image/gif",".webp"=>"image/webp",".avif"=>"image/avif",".bmp"=>"image/bmp",".ico"=>"image/x-icon",
        ".woff"=>"font/woff",".woff2"=>"font/woff2",".ttf"=>"font/ttf",".otf"=>"font/otf",".txt"=>"text/plain; charset=utf-8",".xml"=>"application/xml",_=>"application/octet-stream"
    };
}
