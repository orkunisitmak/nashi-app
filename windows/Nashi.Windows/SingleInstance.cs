using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
namespace Nashi.Windows;
internal sealed class SingleInstance:IDisposable
{
    private readonly Mutex mutex;private readonly string pipe;private readonly CancellationTokenSource lifetime=new();private NamedPipeServerStream? server;
    internal bool Primary{get;}
    internal SingleInstance(string? testName=null)
    {
        var name=testName??"Nashi.Desktop.v1."+Process.GetCurrentProcess().SessionId;pipe=name;mutex=new(false,"Local\\"+name);try{Primary=mutex.WaitOne(0);}catch(AbandonedMutexException){Primary=true;}
    }
    internal void Start(Action<string[]> receive)
    {
        if(!Primary)throw new InvalidOperationException("Only the primary instance can listen.");_ = Task.Run(async()=>
        {
            while(!lifetime.IsCancellationRequested)
            {
                try
                {
                    using var connection=new NamedPipeServerStream(pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);server=connection;await connection.WaitForConnectionAsync(lifetime.Token);var prefix=new byte[4];await connection.ReadExactlyAsync(prefix,lifetime.Token);var length=BinaryPrimitives.ReadInt32LittleEndian(prefix);if(length is <2 or >131072)continue;var data=new byte[length];await connection.ReadExactlyAsync(data,lifetime.Token);var paths=JsonSerializer.Deserialize<string[]>(data);if(paths is null||paths.Length>256||paths.Any(p=>p is null||p.Length>16384))continue;var validPaths=paths.Where(p=>!string.IsNullOrWhiteSpace(p)&&(File.Exists(p)||Directory.Exists(p))).Select(Path.GetFullPath).ToArray();try{receive(validPaths);}catch(Exception error){Console.Error.WriteLine(error);}await connection.WriteAsync(new byte[]{1},lifetime.Token);
                }
                catch(OperationCanceledException){break;}catch(ObjectDisposedException)when(lifetime.IsCancellationRequested){break;}catch(Exception error)when(error is IOException or JsonException or UnauthorizedAccessException){if(!lifetime.IsCancellationRequested)await Task.Delay(100);}
                finally{server=null;}
            }
        });
    }
    internal async Task Notify(string[] paths)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));using var client=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await client.ConnectAsync(timeout.Token);var data=JsonSerializer.SerializeToUtf8Bytes(paths.Take(256));if(data.Length>131072)throw new IOException("Choose fewer files to open.");var prefix=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(prefix,data.Length);await client.WriteAsync(prefix,timeout.Token);await client.WriteAsync(data,timeout.Token);var ack=new byte[1];await client.ReadExactlyAsync(ack,timeout.Token);
    }
    public void Dispose(){lifetime.Cancel();server?.Dispose();if(Primary)mutex.ReleaseMutex();mutex.Dispose();}
}
