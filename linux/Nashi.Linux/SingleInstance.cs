using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
namespace Nashi.Linux;

/// Only the first Nashi process monitors desktop drags; later launches forward file arguments.
internal sealed class SingleInstance:IDisposable
{
    private readonly Mutex mutex=new(false,"NashiLinux-"+Environment.UserName);
    private readonly CancellationTokenSource lifetime=new();
    private readonly ConcurrentQueue<string[]> pending=new();
    private readonly string socketPath;
    private Socket? listener;
    private Action<string[]>? handler;
    public bool IsPrimary{get;}
    public SingleInstance()
    {
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Runtime");Directory.CreateDirectory(root);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        socketPath=Path.Combine(root,"instance.sock");
        if(Encoding.UTF8.GetByteCount(socketPath)>100)throw new IOException("The Nashi runtime path is too long for a Unix socket.");
        try{IsPrimary=mutex.WaitOne(0);}catch(AbandonedMutexException){IsPrimary=true;}
        if(!IsPrimary)return;
        if(File.Exists(socketPath))File.Delete(socketPath);
        listener=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);listener.Bind(new UnixDomainSocketEndPoint(socketPath));listener.Listen(4);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(socketPath,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        _ = Listen();
    }
    public void SetHandler(Action<string[]> onLaunch){handler=onLaunch;Dispatch();}
    private void Dispatch(){if(handler is null)return;while(pending.TryDequeue(out var args)){var callback=handler;Dispatcher.UIThread.Post(()=>callback(args));}}
    public void Forward(string[] args)
    {
        using var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        var normalized=args.Select(arg=>arg.StartsWith("--",StringComparison.Ordinal)||Uri.TryCreate(arg,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https"?arg:Path.GetFullPath(arg)).ToArray();
        var data=JsonSerializer.SerializeToUtf8Bytes(normalized);if(data.Length>65536)throw new IOException("Select fewer files per launch.");
        var sent=0;while(sent<data.Length)sent+=socket.Send(data.AsSpan(sent));socket.Shutdown(SocketShutdown.Send);
    }
    private async Task Listen()
    {
        try
        {
            while(!lifetime.IsCancellationRequested)
            {
                using var socket=await listener!.AcceptAsync(lifetime.Token);using var request=new MemoryStream();var buffer=new byte[4096];
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    int count;while((count=await socket.ReceiveAsync(buffer,SocketFlags.None,timeout.Token))>0){if(request.Length+count>65536)break;request.Write(buffer,0,count);}
                    if(count!=0)continue;var args=JsonSerializer.Deserialize<string[]>(request.ToArray());if(args is null||args.Length>1000||args.Any(a=>a is null))continue;pending.Enqueue(args);Dispatch();
                }catch(JsonException){}catch(OperationCanceledException)when(!lifetime.IsCancellationRequested){}catch(SocketException){}
            }
        }catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(SocketException)when(lifetime.IsCancellationRequested){}
    }
    public void Dispose()
    {
        lifetime.Cancel();listener?.Dispose();if(IsPrimary){if(File.Exists(socketPath))File.Delete(socketPath);mutex.ReleaseMutex();}mutex.Dispose();
    }
}
