using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
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
        JobObjectTracker.Track(process);
        process.StandardInput.Close();
        using var cancellation = token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var stdout = DrainAsync(process.StandardOutput, outputLine);
        var stderr = DrainAsync(process.StandardError, null);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
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

internal static class JobObjectTracker
{
    private static readonly IntPtr JobHandle = InitJob();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Process, byte> FallbackProcesses = new();
    private static int exitRegistered;
    private static void EnsureExitHandler()
    {
        if (System.Threading.Interlocked.Exchange(ref exitRegistered, 1) != 0) return;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var p in FallbackProcesses.Keys)
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
                catch { }
            }
        };
    }
    private static IntPtr InitJob()
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        try
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return IntPtr.Zero;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = 0x2000 // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };
            var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(job, 9, ptr, (uint)length))
                {
                    CloseHandle(job);
                    return IntPtr.Zero;
                }
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return job;
        }
        catch { return IntPtr.Zero; }
    }
    internal static void Track(Process process)
    {
        if (JobHandle != IntPtr.Zero)
        {
            try
            {
                if (!process.HasExited && AssignProcessToJobObject(JobHandle, process.Handle))
                    return;
                var error = Marshal.GetLastWin32Error();
                Trace.TraceWarning("Failed to assign process to job object: Win32 error {0}.", error);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Exception assigning process to job object: {0}.", ex.Message);
            }
        }
        try
        {
            if (process.HasExited) return;
            EnsureExitHandler();
            FallbackProcesses.TryAdd(process, 0);
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => FallbackProcesses.TryRemove(process, out _);
            }
            catch { }
            if (process.HasExited) FallbackProcesses.TryRemove(process, out _);
        }
        catch { }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInformationClass, IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryLimit, PeakJobMemoryLimit;
    }
}
