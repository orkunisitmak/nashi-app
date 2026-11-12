using System.Net;
using System.Net.Sockets;
using System.Text;
namespace Nashi.Core;
// yt-dlp resolves names and follows redirects on its own, so it gets this loopback proxy and every connection it opens can only reach public addresses.
public sealed class PublicWebProxy:IAsyncDisposable
{
    private readonly TcpListener listener=new(IPAddress.Loopback,0);private readonly CancellationTokenSource stop=new();private readonly Task accepting;
    private PublicWebProxy(){listener.Start();accepting=AcceptAsync();}
    public static PublicWebProxy Start()=>new();
    // A proxy the user configured already decides where connections go, so yt-dlp keeps using that one.
    public static PublicWebProxy? For(Uri uri)=>HttpClient.DefaultProxy.GetProxy(uri) is null?Start():null;
    public string Url=>"http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;
    public string[] Arguments=>["--proxy",Url];
    private async Task AcceptAsync()
    {
        var connections=new List<Task>();
        try{while(true)connections.Add(ServeAsync(await listener.AcceptTcpClientAsync(stop.Token)));}catch(Exception error)when(error is OperationCanceledException or ObjectDisposedException or SocketException){}
        await Task.WhenAll(connections);
    }
    private async Task ServeAsync(TcpClient client)
    {
        using(client)try
        {
            var stream=client.GetStream();var buffer=new byte[16384];var length=0;int end;
            using(var header=CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                header.CancelAfter(TimeSpan.FromSeconds(30));
                while((end=buffer.AsSpan(0,length).IndexOf("\r\n\r\n"u8))<0){if(length==buffer.Length)return;var count=await stream.ReadAsync(buffer.AsMemory(length),header.Token);if(count==0)return;length+=count;}
            }
            var lines=Encoding.Latin1.GetString(buffer,0,end).Split("\r\n");var request=lines[0].Split(' ');if(request.Length!=3)return;var tunnel=request[0]=="CONNECT";
            if(!Uri.TryCreate(tunnel?"https://"+request[1]:request[1],UriKind.Absolute,out var target)||target.Scheme!=(tunnel?"https":"http")||!string.IsNullOrEmpty(target.UserInfo)||tunnel&&target.PathAndQuery!="/"||!PublicWebHost.Allows(target.IdnHost)){await RefuseAsync(stream);return;}
            var addresses=await Dns.GetHostAddressesAsync(target.IdnHost,stop.Token);if(addresses.Length==0||!addresses.All(PublicWebHost.IsPublic)){await RefuseAsync(stream);return;}
            using var socket=new Socket(SocketType.Stream,ProtocolType.Tcp){NoDelay=true};await socket.ConnectAsync(addresses,target.Port,stop.Token);using var remote=new NetworkStream(socket,true);
            if(tunnel)await stream.WriteAsync("HTTP/1.1 200 Connection established\r\n\r\n"u8.ToArray(),stop.Token);
            else
            {
                // One request per connection, so a reused proxy connection can't carry a request for another host.
                var forwarded=new StringBuilder(request[0]+" "+target.PathAndQuery+" "+request[2]+"\r\n");foreach(var line in lines.Skip(1))if(!line.StartsWith("Proxy-",StringComparison.OrdinalIgnoreCase)&&!line.StartsWith("Connection:",StringComparison.OrdinalIgnoreCase)&&!line.StartsWith("Keep-Alive:",StringComparison.OrdinalIgnoreCase))forwarded.Append(line).Append("\r\n");
                await remote.WriteAsync(Encoding.Latin1.GetBytes(forwarded.Append("Connection: close\r\n\r\n").ToString()),stop.Token);
            }
            await remote.WriteAsync(buffer.AsMemory(end+4,length-end-4),stop.Token);
            using var done=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);await Task.WhenAny(stream.CopyToAsync(remote,done.Token),remote.CopyToAsync(stream,done.Token));done.Cancel();
        }catch{}
    }
    private async Task RefuseAsync(NetworkStream stream)=>await stream.WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(),stop.Token);
    public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();await accepting;stop.Dispose();}
}
