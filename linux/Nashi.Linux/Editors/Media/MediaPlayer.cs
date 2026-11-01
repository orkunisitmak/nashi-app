using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Nashi.Core;

namespace Nashi.Linux.Editors.Media;

/// Where the player's sound goes: Write takes interleaved PCM s16 (false when the output broke); Close ends it.
public sealed record AudioSink(Func<byte[], int, bool> Write, Action Close);

/// The editor preview player, with no new packages: ffmpeg decodes frames to raw RGBA at the preview size through a
/// pipe into a WriteableBitmap, paced by the clock at the source rate (at most 30 fps), and decodes the sound to PCM
/// for the in-process PulseAudio stream. Seeking restarts both pipes at the new time. Speed, AudioFilter and
/// VideoFilter preview a processed result live (speed change, channels, bleeps, loudness). Without PulseAudio the
/// frames still play, silently, and Silent says so. Every ffmpeg it starts is killed on Pause, Seek and Dispose.
public sealed class MediaPlayer : IDisposable
{
    /// Opens the sound output (rate, channels); null when there is none. The checks swap in a fake.
    public static Func<int, int, AudioSink?> OpenSink { get; set; } = Pulse.Stream;
    /// Seconds from any fixed start. The checks swap in their own.
    public static Func<double> Clock { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    const int Rate = 48000;
    private readonly MediaTools tools;
    private readonly string path;
    private readonly bool video, audio;
    private readonly PixelSize size;
    private readonly double fps;
    private readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly List<FfmpegPipe> pipes = [];
    private readonly CancellationTokenSource lifetime = new();
    private long generation;
    private double startedAt, startedFrom;
    private double? pendingStill;
    private bool stillRunning, disposed;

    public double Duration { get; }
    public double Position { get; private set; }
    public bool Playing { get; private set; }
    /// The last play had no sound output: PulseAudio is not there.
    public bool Silent { get; private set; }
    /// The current frame (video only), preview size.
    public WriteableBitmap? Frame { get; private set; }
    /// Playback speed, 0.25 to 4 (Change Speed's live preview).
    public double Speed { get; set; } = 1;
    /// An ffmpeg audio filter for a live processed preview (channels, bleeps, loudness).
    public string? AudioFilter { get; set; }
    public string? VideoFilter { get; set; }
    /// Where playing stops (Play Selected Range); null plays to the end.
    public double? Until { get; private set; }
    /// Why the last play stopped on its own: ffmpeg could not start or failed (English; translate at display).
    public string? Error { get; private set; }
    /// Position, frame or playing changed (UI thread).
    public event Action? Changed;

    public MediaPlayer(MediaTools tools, string path, MediaInfo info, int frameWidth, int frameHeight)
    {
        this.tools = tools;
        this.path = path;
        Duration = info.Duration;
        video = info.Video is not null && frameWidth > 0;
        audio = info.HasAudio;
        fps = Math.Clamp(info.Video?.FrameRate is > 0 and var r && double.IsFinite(r) ? r : 30, 1, 30);
        size = new PixelSize(Math.Max(2, frameWidth / 2 * 2), Math.Max(2, frameHeight / 2 * 2));
        if (video) Frame = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        ticker.Tick += (_, _) => Tick();
    }

    public void Toggle()
    {
        if (Playing) Pause();
        else Play();
    }

    public void Play(double? from = null, double? until = null)
    {
        if (disposed) return;
        Stop();
        Until = until;
        var end = until ?? Duration;
        Position = Math.Clamp(from ?? (Position >= end - .02 ? 0 : Position), 0, Duration);
        Playing = true;
        var id = generation;
        (startedAt, startedFrom) = (Clock(), Position);
        Silent = false;
        Error = null;
        if (video) StartVideo(id);
        if (audio) StartAudio(id);
        ticker.Start();
        Changed?.Invoke();
    }

    public void Pause()
    {
        if (!Playing) return;
        Tick();
        Stop();
        Playing = false;
        Changed?.Invoke();
    }

    /// Moves the playhead; playing restarts the pipes there, paused shows that frame.
    public void Seek(double time)
    {
        if (disposed) return;
        Position = Math.Clamp(time, 0, Duration);
        if (Playing) Play(Position, Until is { } u && u > Position ? u : null);
        else
        {
            Changed?.Invoke();
            Still(Position);
        }
    }

    /// Shows the frame at `time` (latest wins while one decodes).
    public async void Still(double time)
    {
        if (!video || disposed) return;
        pendingStill = time;
        if (stillRunning) return;
        stillRunning = true;
        try
        {
            while (pendingStill is { } t && !disposed && !Playing)
            {
                pendingStill = null;
                var filter = VideoFilter;
                var bytes = await Task.Run(() => tools.FrameAsync(path, Math.Min(t, Math.Max(0, Duration - .05)), size.Width, size.Height, lifetime.Token, filter));
                if (bytes.Length > 0 && !disposed && !Playing) Show(bytes);
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException) { }
        finally { stillRunning = false; }
    }

    private void Tick()
    {
        if (!Playing) { ticker.Stop(); return; }
        Position = Math.Min(Duration, startedFrom + (Clock() - startedAt) * Speed);
        if (Position >= (Until ?? Duration) - .001)
        {
            Position = Until ?? Duration;
            Stop();
            Playing = false;
        }
        Changed?.Invoke();
    }

    private void Stop()
    {
        generation++;
        ticker.Stop();
        FfmpegPipe[] running;
        lock (pipes) { running = [.. pipes]; pipes.Clear(); }
        foreach (var pipe in running) pipe.Dispose();
    }

    private FfmpegPipe? Pipe(long id, IEnumerable<string> arguments)
    {
        var pipe = tools.Start(arguments);
        lock (pipes)
        {
            if (id == generation && !disposed) { pipes.Add(pipe); return pipe; }
        }
        pipe.Dispose();
        return null;
    }

    private static string Tempo(double speed)
    {
        var parts = new List<string>();
        while (speed > 2) { parts.Add("atempo=2"); speed /= 2; }
        while (speed < .5) { parts.Add("atempo=0.5"); speed *= 2; }
        parts.Add("atempo=" + MediaTools.N(speed));
        return string.Join(',', parts);
    }

    private void StartVideo(long id)
    {
        var from = Position;
        var filter = (VideoFilter is null ? "" : VideoFilter + ",") + $"setpts=(PTS-STARTPTS)/{MediaTools.N(Speed)},fps={MediaTools.N(fps)},scale={size.Width}:{size.Height}";
        var frameBytes = size.Width * size.Height * 4;
        Background(id, "Nashi preview frames", () =>
        {
            using var pipe = Pipe(id, ["-ss", MediaTools.N(from), "-i", path, "-map", "0:v:0", "-vf", filter, "-an", "-f", "rawvideo", "-pix_fmt", "rgba", "-"]);
            if (pipe is null) return;
            for (var n = 0; Interlocked.Read(ref generation) == id; n++)
            {
                var frame = new byte[frameBytes];
                if (pipe.Read(frame) < frameBytes) return;
                // Each frame waits for its time on the clock (n / fps after the start).
                while (Interlocked.Read(ref generation) == id && Clock() - startedAt < n / fps) Thread.Sleep(4);
                Dispatcher.UIThread.Post(() => { if (generation == id) Show(frame); });
            }
        });
    }

    private void StartAudio(long id)
    {
        var from = Position;
        // The filter sees source times (a bleep at 0:05 stays there after a seek), then the tempo.
        var filter = string.Join(',', new[] { AudioFilter is null ? null : $"asetpts=PTS+{MediaTools.N(from)}/TB,{AudioFilter}", Speed == 1 ? null : Tempo(Speed) }.Where(f => f is not null));
        Background(id, "Nashi preview sound", () =>
        {
            var sink = OpenSink(Rate, 2);
            if (sink is null)
            {
                Dispatcher.UIThread.Post(() => { if (generation == id) { Silent = true; Changed?.Invoke(); } });
                return;
            }
            try
            {
                using var pipe = Pipe(id, ["-ss", MediaTools.N(from), "-i", path, "-map", "0:a:0", "-vn", .. filter.Length > 0 ? new[] { "-af", filter } : [], "-ac", "2", "-ar", Rate.ToString(System.Globalization.CultureInfo.InvariantCulture), "-f", "s16le", "-acodec", "pcm_s16le", "-"]);
                if (pipe is null) return;
                var chunk = new byte[Rate * 2 * 2 / 40]; // 25 ms
                while (Interlocked.Read(ref generation) == id)
                {
                    var read = pipe.Read(chunk);
                    if (read == 0 || !sink.Write(chunk, read)) return;
                }
            }
            finally { sink.Close(); }
        });
    }

    /// A preview thread whose failure (ffmpeg missing or refusing to start) stops playback and sets Error instead of
    /// taking the app down.
    private void Background(long id, string name, Action body) => new Thread(() =>
    {
        try { body(); }
        catch (Exception error)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != id || disposed) return;
                Stop();
                Playing = false;
                Error = error.Message;
                Changed?.Invoke();
            });
        }
    }) { IsBackground = true, Name = name }.Start();

    private void Show(byte[] pixels)
    {
        if (Frame is null || disposed) return;
        using (var buffer = Frame.Lock())
            for (var y = 0; y < size.Height; y++)
                Marshal.Copy(pixels, y * size.Width * 4, buffer.Address + y * buffer.RowBytes, size.Width * 4);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        disposed = true;
        lifetime.Cancel();
        Playing = false;
    }
}
