using System.Net;
using System.Net.Sockets;
using System.Text;
namespace Nashi.Core;

/// A loopback-only HTTP/CONNECT proxy for tools that fetch links themselves (yt-dlp). Every connection, including
/// redirects, is resolved here and refused when any address points to this computer or a private network, so DNS tricks
/// and rebinding cannot reach local services. It forwards bytes and never reads or stores the content.
internal sealed class PublicOnlyProxy:IDisposable
{
    private readonly TcpListener listener=new(IPAddress.Loopback,0);
    private readonly CancellationTokenSource stop=new();
    private readonly Func<IPAddress,bool> allowed;
    public PublicOnlyProxy(Func<IPAddress,bool>? allowed=null)
    {
        this.allowed=allowed??(address=>!WebDownload.IsPrivate(address));
        listener.Start();_=AcceptLoop();
    }
    public string Url=>"http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;
    /// Null when the user already routes traffic through a proxy: that proxy resolves names, so Nashi leaves it alone.
    internal static PublicOnlyProxy? ForRequest(Uri uri)
    {
        try{if(!HttpClient.DefaultProxy.IsBypassed(uri))return null;}catch(Exception error)when(error is ArgumentException or InvalidOperationException or NotSupportedException){return null;}
        return new PublicOnlyProxy();
    }
    private async Task AcceptLoop()
    {
        try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);_=Serve(client);}}
        catch(Exception error)when(error is OperationCanceledException or ObjectDisposedException or SocketException){}
    }
    private async Task Serve(TcpClient client)
    {
        using(client)
        try
        {
            client.NoDelay=true;var stream=client.GetStream();
            var (head,extra)=await ReadHead(stream,stop.Token).ConfigureAwait(false);
            if(head is null){return;}
            var lines=head.Split("\r\n");var first=lines[0].Split(' ');
            if(first.Length<3){await Reply(stream,"400 Bad Request");return;}
            string host;int port;byte[]? forward=null;
            if(first[0].Equals("CONNECT",StringComparison.OrdinalIgnoreCase))
            {
                if(!Uri.TryCreate("tcp://"+first[1],UriKind.Absolute,out var authority)||authority.Port<1){await Reply(stream,"400 Bad Request");return;}
                host=authority.DnsSafeHost;port=authority.Port;
            }
            else
            {
                if(!Uri.TryCreate(first[1],UriKind.Absolute,out var target)||target.Scheme!=Uri.UriSchemeHttp){await Reply(stream,"400 Bad Request");return;}
                host=target.DnsSafeHost;port=target.Port;
                var rebuilt=new StringBuilder().Append(first[0]).Append(' ').Append(target.PathAndQuery).Append(" HTTP/1.1\r\n");
                foreach(var line in lines.Skip(1))if(line.Length>0&&!line.StartsWith("Proxy-",StringComparison.OrdinalIgnoreCase)&&!line.StartsWith("Connection:",StringComparison.OrdinalIgnoreCase))rebuilt.Append(line).Append("\r\n");
                rebuilt.Append("Connection: close\r\n\r\n");forward=[..Encoding.ASCII.GetBytes(rebuilt.ToString()),..extra];
            }
            IPAddress[] addresses;
            try{addresses=IPAddress.TryParse(host,out var literal)?[literal]:await Dns.GetHostAddressesAsync(host,stop.Token).ConfigureAwait(false);}
            catch(SocketException){await Reply(stream,"502 Bad Gateway");return;}
            if(addresses.Length==0||addresses.Any(address=>!allowed(address))){await Reply(stream,"403 Forbidden");return;}
            using var upstream=new Socket(SocketType.Stream,ProtocolType.Tcp){NoDelay=true};
            try{await upstream.ConnectAsync(addresses,port,stop.Token).ConfigureAwait(false);}
            catch(SocketException){await Reply(stream,"502 Bad Gateway");return;}
            using var remote=new NetworkStream(upstream,false);
            if(forward is null)await Reply(stream,"200 Connection Established");else await remote.WriteAsync(forward,stop.Token).ConfigureAwait(false);
            var up=stream.CopyToAsync(remote,stop.Token);var down=remote.CopyToAsync(stream,stop.Token);
            await Task.WhenAny(up,down).ConfigureAwait(false);
        }
        catch(Exception error)when(error is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException){}
    }
    private static Task Reply(Stream stream,string status)=>stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 "+status+"\r\nConnection: close\r\nContent-Length: 0\r\n\r\n")).AsTask();
    private static async Task<(string? Head,byte[] Extra)> ReadHead(Stream stream,CancellationToken token)
    {
        var buffer=new byte[16*1024];var length=0;
        while(length<buffer.Length)
        {
            var read=await stream.ReadAsync(buffer.AsMemory(length),token).ConfigureAwait(false);if(read==0)return(null,[]);length+=read;
            var end=buffer.AsSpan(0,length).IndexOf("\r\n\r\n"u8);
            if(end>=0)return(Encoding.ASCII.GetString(buffer,0,end),buffer[(end+4)..length]);
        }
        return(null,[]);
    }
    public void Dispose(){stop.Cancel();listener.Stop();stop.Dispose();}
}
