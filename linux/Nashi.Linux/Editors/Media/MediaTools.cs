using System.Diagnostics;
using System.Globalization;
using Nashi.Core;

namespace Nashi.Linux.Editors.Media;

/// ffmpeg and ffprobe for one media editor, and the raw pipes its previews read: filmstrip thumbnails, waveform peaks
/// and the player's frames and samples. No shell, no new packages: ffmpeg writes raw RGBA or PCM to stdout.
public sealed record MediaTools(string Ffmpeg, string Ffprobe)
{
    /// The tools a toolchain holds; MissingToolsException (which opens setup) when either is missing.
    public static MediaTools From(Toolchain tools)
    {
        try { return new(tools.Executable("ffmpeg"), tools.Executable("ffprobe")); }
        catch (FileNotFoundException) { throw new MissingToolsException(); }
    }

    public MediaEngine Engine => new(Ffmpeg, Ffprobe);

    public static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// ffmpeg writing raw output to a pipe the caller reads; disposing it kills it.
    public FfmpegPipe Start(IEnumerable<string> arguments) => new(Ffmpeg, arguments);

    /// One frame at `time`, `width` × `height`, as straight RGBA (empty when there is none).
    public async Task<byte[]> FrameAsync(string path, double time, int width, int height, CancellationToken token, string? filter = null)
    {
        using var pipe = Start(["-ss", N(Math.Max(0, time)), "-i", path, "-map", "0:v:0", "-frames:v", "1", "-vf", (filter is null ? "" : filter + ",") + $"scale={width}:{height}", "-f", "rawvideo", "-pix_fmt", "rgba", "-"]);
        using var registration = token.Register(pipe.Dispose);
        var frame = new byte[width * height * 4];
        var read = await pipe.ReadAsync(frame);
        token.ThrowIfCancellationRequested();
        return read == frame.Length ? frame : [];
    }

    /// `count` frames spread over the video, each `width` × `height` RGBA, one after another.
    public async Task<byte[][]> FilmstripAsync(string path, double duration, int count, int width, int height, CancellationToken token)
    {
        var frames = new byte[count][];
        for (var n = 0; n < count; n++) frames[n] = await FrameAsync(path, duration * (n + .5) / count, width, height, token);
        return frames;
    }

    /// The loudest sample of each of `buckets` slices (0 to 1), decoded as mono 8 kHz PCM s16, off the UI thread.
    /// `channel` picks the left (0) or right (1) channel; null mixes them.
    public async Task<float[]> PeaksAsync(string path, double duration, int buckets, CancellationToken token, int? channel = null)
    {
        const int rate = 8000;
        var mix = channel is { } c ? $"pan=mono|c0=c{c}" : "aformat=channel_layouts=mono";
        using var pipe = Start(["-i", path, "-map", "0:a:0", "-af", mix, "-ar", rate.ToString(CultureInfo.InvariantCulture), "-f", "s16le", "-acodec", "pcm_s16le", "-"]);
        using var registration = token.Register(pipe.Dispose);
        var peaks = new float[buckets];
        var total = Math.Max(1, duration * rate);
        var buffer = new byte[64 * 1024];
        long sample = 0;
        int read, carry = 0;
        while ((read = await pipe.ReadSomeAsync(buffer.AsMemory(carry))) > 0)
        {
            var bytes = carry + read;
            for (var i = 0; i + 1 < bytes; i += 2, sample++)
            {
                var value = Math.Abs((float)BitConverter.ToInt16(buffer, i)) / 32768f;
                var bucket = (int)Math.Min(buckets - 1, sample * buckets / total);
                if (value > peaks[bucket]) peaks[bucket] = value;
            }
            carry = bytes % 2;
            if (carry == 1) buffer[0] = buffer[bytes - 1];
        }
        token.ThrowIfCancellationRequested();
        return peaks;
    }

    /// The ffmpeg processes the editors started that still run (the checks look for none after a close).
    public static int Live => FfmpegPipe.Live;
}

/// One ffmpeg process with stdout piped. Dispose kills it (and only it: the PID this started) and waits for it.
public sealed class FfmpegPipe : IDisposable
{
    private static readonly HashSet<int> live = [];
    private readonly Process process;
    private readonly Stream output;
    private int disposed;

    internal static int Live { get { lock (live) return live.Count; } }

    public FfmpegPipe(string ffmpeg, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true };
        foreach (var a in (string[])["-hide_banner", "-nostdin", "-v", "error", "-protocol_whitelist", "file,pipe", .. arguments]) start.ArgumentList.Add(a);
        process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("The processing tool could not start.");
        lock (live) live.Add(process.Id);
        process.StandardInput.Close();
        process.ErrorDataReceived += (_, _) => { }; // drained, so a chatty ffmpeg never blocks
        process.BeginErrorReadLine();
        output = process.StandardOutput.BaseStream;
    }

    /// Fills `buffer` unless the stream ends first; the count read.
    public async Task<int> ReadAsync(Memory<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await ReadSomeAsync(buffer[total..]);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    /// Fills `buffer` on this thread unless the stream ends first (the player's reader threads).
    public int Read(byte[] buffer)
    {
        var total = 0;
        try
        {
            while (total < buffer.Length)
            {
                var n = output.Read(buffer, total, buffer.Length - total);
                if (n == 0) break;
                total += n;
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { }
        return total;
    }

    public async Task<int> ReadSomeAsync(Memory<byte> buffer)
    {
        try { return await output.ReadAsync(buffer); }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { return 0; }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        try { process.WaitForExit(2000); } catch (InvalidOperationException) { }
        lock (live) live.Remove(process.Id);
        process.Dispose();
    }
}
