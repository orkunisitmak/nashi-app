using System.Net.Sockets;
using System.Text;
namespace Nashi.Linux;

/// "Open with Nashi" from a file manager starts a second process. It hands its files to the
/// running window over a Unix socket in $XDG_RUNTIME_DIR (private to the user) and exits.
public static class SingleInstance
{
    public static event Action<string[]>? FilesReceived;
    private static Socket? listener;
    private static string SocketPath
    {
        get
        {
            var runtime=Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if(string.IsNullOrEmpty(runtime)||!Directory.Exists(runtime))runtime=Path.Combine(Path.GetTempPath(),"nashi-"+Environment.UserName);
            Directory.CreateDirectory(runtime);return Path.Combine(runtime,"nashi.sock");
        }
    }
    /// True when another Nashi took the files; this process should then exit.
    public static bool HandOff(string[] files)
    {
        try
        {
            using var client=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);client.Connect(new UnixDomainSocketEndPoint(SocketPath));
            var payload=string.Join('\0',files.Select(f=>f.StartsWith("file://",StringComparison.Ordinal)?new Uri(f).LocalPath:Path.GetFullPath(f)));
            client.Send(Encoding.UTF8.GetBytes(payload.Length==0?"\u0001":payload));return true;
        }
        catch(SocketException){return false;}
    }
    public static void Listen()
    {
        var path=SocketPath;if(File.Exists(path))File.Delete(path);
        listener=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);listener.Bind(new UnixDomainSocketEndPoint(path));
        File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);listener.Listen(4);
        _=Task.Run(async()=>
        {
            while(listener is not null)
            {
                try
                {
                    using var connection=await listener.AcceptAsync();using var stream=new NetworkStream(connection);using var reader=new StreamReader(stream,Encoding.UTF8);
                    var text=await reader.ReadToEndAsync();if(text.Length>1_000_000)continue;
                    FilesReceived?.Invoke(text=="\u0001"?[]:text.Split('\0',StringSplitOptions.RemoveEmptyEntries));
                }
                catch(Exception error)when(error is SocketException or IOException or ObjectDisposedException){if(listener is null)return;}
            }
        });
    }
    public static void Stop(){var socket=listener;listener=null;socket?.Dispose();try{File.Delete(SocketPath);}catch(IOException){}}
}
