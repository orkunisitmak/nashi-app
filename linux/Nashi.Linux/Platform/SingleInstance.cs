using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
namespace Nashi.Linux;

/// "Open with Nashi" from a file manager starts a second process. It hands its files to the
/// running window over a Unix socket in $XDG_RUNTIME_DIR (private to the user) and exits.
public static class SingleInstance
{
    public static event Action<string[]>? FilesReceived;
    private static Socket? listener;
    [DllImport("libc",SetLastError=true)]private static extern unsafe int lstat(string path,byte* buffer);
    [DllImport("libc")]private static extern uint getuid();
    /// The socket's path, or null when no private folder can be trusted (single-instance is then skipped).
    private static string? SocketPath
    {
        get
        {
            var runtime=Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if(!string.IsNullOrEmpty(runtime)&&Directory.Exists(runtime)){var socket=Path.Combine(runtime,"nashi.sock");return System.Text.Encoding.UTF8.GetByteCount(socket)<100?socket:null;}
            // Without XDG_RUNTIME_DIR: a folder in /tmp, which another local user could have made first.
            // Use it only when it is ours, not a link and closed to others (0700); /tmp's sticky bit
            // then stops anyone else from swapping it.
            var folder=Path.Combine(Path.GetTempPath(),"nashi-"+Environment.UserName);
            try{if(!Directory.Exists(folder)&&!File.Exists(folder))Directory.CreateDirectory(folder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);}catch(IOException){return null;}
            // A socket path longer than sun_path (108 bytes) can't be used either.
            var path=Path.Combine(folder,"nashi.sock");return Private(folder)&&System.Text.Encoding.UTF8.GetByteCount(path)<100?path:null;
        }
    }
    /// lstat: a real directory (not a symlink), owned by this user, with no group or other access.
    private static unsafe bool Private(string folder)
    {
        byte* stat=stackalloc byte[256];if(lstat(folder,stat)!=0)return false;
        var mode=*(uint*)(stat+24);var owner=*(uint*)(stat+28);   // x86-64 struct stat: st_mode at 24, st_uid at 28
        return (mode&0xF000)==0x4000&&owner==getuid()&&(mode&0x3F)==0;   // S_IFDIR, no rwx for group/other
    }
    /// True when another Nashi took the files; this process should then exit.
    public static bool HandOff(string[] files)
    {
        try
        {
            if(SocketPath is not {} path)return false;
            using var client=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);client.Connect(new UnixDomainSocketEndPoint(path));
            var payload=string.Join('\0',files.Select(f=>f.StartsWith("file://",StringComparison.Ordinal)?new Uri(f).LocalPath:Path.GetFullPath(f)));
            client.Send(Encoding.UTF8.GetBytes(payload.Length==0?"\u0001":payload));return true;
        }
        catch(SocketException){return false;}
    }
    public static void Listen()
    {
        if(SocketPath is not {} path)return;if(File.Exists(path))File.Delete(path);
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
    public static void Stop(){var socket=listener;listener=null;if(socket is null)return;socket.Dispose();try{if(SocketPath is {} path)File.Delete(path);}catch(IOException){}}
}
