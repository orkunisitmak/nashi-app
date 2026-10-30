using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Nashi.Core;

public sealed record OperationProgress(double? Fraction, string Label);
public sealed record ProcessResult(string Output, string Error);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
        CancellationToken token = default, Action<string>? outputLine = null)
    {
        token.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("A fully qualified tool path is required.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        var values = arguments.ToList();
        if (Path.GetFileNameWithoutExtension(executable).ToLowerInvariant() is "ffmpeg" or "ffprobe" && !values.Contains("-protocol_whitelist"))
        { start.ArgumentList.Add("-protocol_whitelist"); start.ArgumentList.Add("file,pipe,crypto,data"); }
        foreach (var value in values) start.ArgumentList.Add(value);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("The processing tool could not start.");
        process.StandardInput.Close();
        using var cancellation = token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        var stdout = DrainAsync(process.StandardOutput, outputLine);
        var stderr = DrainAsync(process.StandardError, null);
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // Kill can fail (access denied, already exiting); never wait unbounded for a cancelled tool.
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(grace.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
            throw new OperationCanceledException(token);
        }
        var results = await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(results[1]) ? "The processing tool failed." : results[1][Math.Max(0, results[1].Length - 6000)..].Trim());
        return new(results[0], results[1]);
    }

    private static async Task<string> DrainAsync(StreamReader stream, Action<string>? onLine)
    {
        var capture = new StringBuilder(); var line = new StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0) break;
            capture.Append(buffer, 0, count);
            if (capture.Length > 2_000_000) capture.Remove(0, capture.Length - 2_000_000);
            if (onLine is null) continue;
            for (var index = 0; index < count; index++)
            {
                var character = buffer[index];
                if (character == '\n') { onLine(line.ToString().TrimEnd('\r')); line.Clear(); }
                else if (line.Length < 16_384) line.Append(character);
            }
        }
        if (onLine is not null && line.Length > 0) onLine(line.ToString());
        return capture.ToString();
    }
}

public sealed class FFmpegProgress(double? duration, Action<OperationProgress> report, string label)
{
    private double last;
    public void ReadLine(string line)
    {
        const string prefix = "out_time_us=";
        if (duration is not > 0 || !double.IsFinite(duration.Value) || !line.StartsWith(prefix, StringComparison.Ordinal) ||
            !double.TryParse(line[prefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var microseconds) ||
            !double.IsFinite(microseconds) || microseconds < 0) return;
        var fraction = Math.Min(.995, microseconds / 1_000_000 / duration.Value);
        if (fraction <= last) return;
        last = fraction; report(new(fraction, label));
    }
}
