using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Nashi.Linux;

public enum LaunchMode { Home, Background, Wheel, Actions, WheelClipboard, InstallIntegration, UninstallIntegration, Version }

/// What one `Nashi` invocation asks for. File paths are made absolute here, so another process can use them.
public sealed record Launch(LaunchMode Mode, IReadOnlyList<string> Files)
{
    public static Launch Parse(IReadOnlyList<string> args)
    {
        if (args.Contains("--version")) return new(LaunchMode.Version, []);
        if (args.Count == 0) return new(LaunchMode.Home, []);
        return args[0] switch
        {
            "--background" => new(LaunchMode.Background, []),
            "--wheel-clipboard" => new(LaunchMode.WheelClipboard, []),
            "--install-integration" => new(LaunchMode.InstallIntegration, []),
            "--uninstall-integration" => new(LaunchMode.UninstallIntegration, []),
            "--wheel" => new(LaunchMode.Wheel, Absolute(args.Skip(1))),
            "--actions" => new(LaunchMode.Actions, Absolute(args.Skip(1))),
            _ => new(LaunchMode.Wheel, Absolute(args)), // the desktop entry's %F
        };
    }

    static string[] Absolute(IEnumerable<string> paths) => paths.Where(p => p.Length > 0).Select(Path.GetFullPath).ToArray();

    /// What to hand a running instance, with the launcher's DESKTOP_STARTUP_ID when there is one;
    /// null for --background (one is already running).
    public InstanceMessage? Message => Mode switch
    {
        LaunchMode.Home => new("show", null, StartupId),
        LaunchMode.Wheel => new("wheel", [.. Files], StartupId),
        LaunchMode.Actions => new("actions", [.. Files], StartupId),
        LaunchMode.WheelClipboard => new("wheel-clipboard", null, StartupId),
        _ => null,
    };

    static string? StartupId => Environment.GetEnvironmentVariable("DESKTOP_STARTUP_ID") is { Length: > 0 } id ? id : null;
}

/// One JSON line on the instance socket: show, wheel or actions (with files), wheel-clipboard or quit.
/// Startup is the sender's DESKTOP_STARTUP_ID; its _TIME<n> is the timestamp for activating the wheel.
public sealed record InstanceMessage(string Cmd, string[]? Files = null, string? Startup = null);

/// The one running Nashi per user. It holds `<socket>.lock` (flock) for its lifetime and listens on a 0600 Unix socket;
/// later invocations connect, send one message, and exit. Both ends check the peer is the same user (SO_PEERCRED).
public sealed class Instance : IDisposable
{
    public const int MaxMessage = 1 << 20;
    const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly FileStream lockFile;
    readonly Socket listener;
    readonly string path;

    Instance(FileStream lockFile, Socket listener, string path) => (this.lockFile, this.listener, this.path) = (lockFile, listener, path);

    [DllImport("libc")]
    static extern uint getuid();

    public static uint Uid { get; } = getuid();

    public static string SocketPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } run && Path.IsPathRooted(run) ? run : "/tmp", $"nashi-{Uid}.sock");

    /// Becomes the instance (replacing a stale socket file), or null when another live process already is.
    /// Connections wait in the backlog until Start.
    // ponytail: in the /tmp fallback another user could pre-create the lock file and block us; XDG_RUNTIME_DIR avoids that.
    public static Instance? Claim(string path)
    {
        FileStream lockFile;
        try { lockFile = new FileStream(path + ".lock", new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None, UnixCreateMode = Private }); }
        catch (IOException) { return null; }
        try
        {
            File.Delete(path); // whoever bound it no longer holds the lock: stale
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            File.SetUnixFileMode(path, Private);
            listener.Listen(16);
            return new Instance(lockFile, listener, path);
        }
        catch { lockFile.Dispose(); throw; }
    }

    /// True when a live instance holds the lock.
    public static bool IsRunning(string path)
    {
        using var claimed = Claim(path);
        return claimed is null;
    }

    /// Accepts messages on a background thread; `received` runs there, once per valid message.
    public void Start(Action<InstanceMessage> received) => _ = Task.Run(async () =>
    {
        while (true)
        {
            Socket client;
            try { client = await listener.AcceptAsync(); }
            catch (Exception error) when (error is ObjectDisposedException or SocketException) { return; }
            _ = Task.Run(async () =>
            {
                using (client)
                {
                    if (PeerUid(client) != Uid || await ReadAsync(client) is not { } message) return;
                    try { await client.SendAsync("1"u8.ToArray(), SocketFlags.None); } catch (SocketException) { }
                    received(message);
                }
            });
        }
    });

    /// Sends one message; true when the instance accepted it.
    public static bool Send(string path, InstanceMessage message)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(path));
            if (PeerUid(socket) != Uid) return false;
            socket.Send([.. JsonSerializer.SerializeToUtf8Bytes(message, Json), (byte)'\n']);
            socket.ReceiveTimeout = 5000;
            var reply = new byte[1];
            return socket.Receive(reply) == 1 && reply[0] == (byte)'1';
        }
        catch (SocketException) { return false; }
    }

    static async Task<InstanceMessage?> ReadAsync(Socket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var n = await socket.ReceiveAsync(chunk, SocketFlags.None, timeout.Token);
                if (n == 0) break;
                var end = Array.IndexOf(chunk, (byte)'\n', 0, n);
                buffer.Write(chunk, 0, end >= 0 ? end : n);
                if (buffer.Length > MaxMessage) return null;
                if (end >= 0) break;
            }
            return JsonSerializer.Deserialize<InstanceMessage>(buffer.ToArray(), Json) is { Cmd: not null } message ? message : null;
        }
        catch (Exception error) when (error is OperationCanceledException or SocketException or JsonException) { return null; }
    }

    static uint? PeerUid(Socket socket)
    {
        var credentials = new byte[12]; // struct ucred { pid_t pid; uid_t uid; gid_t gid; }
        try { return socket.GetRawSocketOption(1 /* SOL_SOCKET */, 17 /* SO_PEERCRED */, credentials) == 12 ? BitConverter.ToUInt32(credentials, 4) : null; }
        catch (SocketException) { return null; }
    }

    public void Dispose()
    {
        listener.Dispose();
        try { File.Delete(path); } catch (IOException) { }
        lockFile.Dispose();
    }
}
