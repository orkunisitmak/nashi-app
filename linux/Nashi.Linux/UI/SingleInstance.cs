using System.Net.Sockets;
using System.Text.Json;
namespace Nashi.Linux.UI;

/// What a launch asks the window to do: files from Open with, a Dolphin action
/// or a shortcut, the wheel mode, and whether to take the files from the clipboard.
internal sealed record LaunchRequest(string[] Files,string Mode="all",bool Clipboard=false,bool Wheel=false)
{
    internal static LaunchRequest Parse(IReadOnlyList<string> args)
    {
        var files=new List<string>();var mode="all";var clipboard=false;var wheel=false;
        for(var i=0;i<args.Count;i++)
        {
            switch(args[i])
            {
                case "--wheel":wheel=true;if(i+1<args.Count&&args[i+1] is "all" or "convert" or "tools" or "actions")mode=args[++i];break;
                case "--clipboard":clipboard=true;wheel=true;break;
                default:if(Desktop.LocalPath(args[i]) is {} path)files.Add(path);break;
            }
        }
        return new(files.ToArray(),mode,clipboard,wheel);
    }
}

/// One Nashi per user session. A second launch hands its request to the running
/// window over a private Unix socket in $XDG_RUNTIME_DIR and exits.
internal static class SingleInstance
{
    private static string SocketPath
    {
        get
        {
            var runtime=Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if(string.IsNullOrWhiteSpace(runtime)||!Directory.Exists(runtime))runtime=Path.GetTempPath();
            return Path.Combine(runtime,"nashi-"+Environment.UserName+".sock");
        }
    }
    internal static bool TryForward(LaunchRequest request)
    {
        try
        {
            using var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            using var stream=new NetworkStream(socket);using var writer=new StreamWriter(stream);
            writer.WriteLine(JsonSerializer.Serialize(request));writer.Flush();return true;
        }
        catch(SocketException){return false;}
    }
    internal static void Listen(Action<LaunchRequest> handle)
    {
        var path=SocketPath;
        try{if(File.Exists(path))File.Delete(path);}catch(IOException){return;}
        var listener=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        try{listener.Bind(new UnixDomainSocketEndPoint(path));if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);listener.Listen(8);}
        catch(SocketException){listener.Dispose();return;}
        _=Task.Run(async()=>
        {
            while(true)
            {
                using var client=await listener.AcceptAsync();
                try
                {
                    using var stream=new NetworkStream(client);using var reader=new StreamReader(stream);
                    var line=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    if(line is {Length:<1_000_000}&&JsonSerializer.Deserialize<LaunchRequest>(line) is {} request)handle(request);
                }
                catch(Exception error)when(error is IOException or JsonException or TimeoutException){}
            }
        });
        AppDomain.CurrentDomain.ProcessExit+=(_,_)=>{try{listener.Dispose();File.Delete(path);}catch(IOException){}};
    }
}
