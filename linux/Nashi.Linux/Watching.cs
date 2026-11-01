// Adapted from Filee (MIT, © KnifeLemon and contributors): src/Filee.Core/Watching/FolderWatcher.cs and
// GeneratedWatchOutputs.cs. Nashi keeps the approach (a FileSystemWatcher plus a periodic rescan, a file taken only
// once its size and time have held still, temporary files skipped, outputs remembered so they never loop back in)
// with one route per folder and outputs beside the source.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nashi.Core;

namespace Nashi.Linux;

/// One watched folder. New files in it (not in subfolders) run the folder's route once their size and modified
/// time have been stable for `Settle`; what a run makes is remembered and never taken as new. The state file under
/// AppPaths.Data keeps what was seen, so after a restart only files that arrived meanwhile are new. Scan and Tick
/// are public and the clock is injected, so the checks drive it without threads; Start adds the watcher and timer.
public sealed class FolderWatch : IDisposable
{
    public static readonly TimeSpan DefaultSettle = TimeSpan.FromSeconds(1.5);
    private static readonly HashSet<string> Temporary = new(StringComparer.OrdinalIgnoreCase) { ".tmp", ".temp", ".part", ".partial", ".crdownload", ".download", ".filepart", ".swp" };

    private sealed record Stamp(long Length, long Ticks);
    private sealed record Candidate(Stamp Stamp, DateTime Since);

    private readonly object gate = new();
    private readonly Dictionary<string, Stamp> seen = [];
    private readonly Dictionary<string, Candidate> pending = [];
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> run;
    private readonly Func<DateTime> clock;
    private readonly CancellationTokenSource stop = new();
    private FileSystemWatcher? watcher;
    private Timer? timer;
    private Task working = Task.CompletedTask;

    public WatchFolder Rule { get; }
    public TimeSpan Settle { get; init; } = DefaultSettle;

    /// Each finished run: its inputs and outputs (or the error).
    public event Action<IReadOnlyList<string>, IReadOnlyList<string>, Exception?>? Ran;

    /// `run(route, files, token)` makes the outputs beside the files. `fresh`: take everything there now as already
    /// seen (a folder just added or turned back on); otherwise the saved state says what is new.
    public FolderWatch(WatchFolder rule, Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> run, bool fresh, Func<DateTime>? clock = null)
    {
        Rule = rule;
        this.run = run;
        this.clock = clock ?? (() => DateTime.UtcNow);
        if (!fresh && Load()) return;
        foreach (var file in Files()) seen[file] = StampOf(file)!;
        Save();
    }

    public static string StateFile(string folder) =>
        Path.Combine(AppPaths.Data, "watch", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..16].ToLowerInvariant() + ".json");

    /// Watches with inotify and rescans every two seconds for anything it missed.
    public void Start()
    {
        try
        {
            watcher = new FileSystemWatcher(Rule.Path) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite };
            watcher.Created += (_, e) => Note(e.FullPath);
            watcher.Changed += (_, e) => Note(e.FullPath);
            watcher.Renamed += (_, e) => Note(e.FullPath);
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException) { watcher = null; }
        timer = new Timer(_ => { Scan(now: false); Tick(); }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    private int scans;

    /// Looks for files not seen yet (every fourth call when timed: two seconds).
    public void Scan(bool now = true)
    {
        if (!now && ++scans % 4 != 0) return;
        foreach (var file in Files()) Note(file);
    }

    private void Note(string path)
    {
        if (!Wanted(path) || StampOf(path) is not { } stamp) return;
        lock (gate)
        {
            if (seen.TryGetValue(path, out var known) && known == stamp) return;
            if (!pending.TryGetValue(path, out var candidate) || candidate.Stamp != stamp) pending[path] = new(stamp, clock());
        }
    }

    /// Runs the route on the files that have held still for Settle (one batch at a time); the batch's task.
    public Task Tick()
    {
        List<string> ready;
        lock (gate)
        {
            if (!working.IsCompleted || stop.IsCancellationRequested) return working;
            var now = clock();
            ready = [];
            foreach (var (path, candidate) in pending.ToList())
            {
                var stamp = StampOf(path);
                if (stamp is null) pending.Remove(path);
                else if (stamp != candidate.Stamp) pending[path] = new(stamp, now);
                else if (now - candidate.Since >= Settle) ready.Add(path);
            }
            foreach (var path in ready) pending.Remove(path);
            ready = ready.Where(Fits).Order(StringComparer.Ordinal).ToList();
            if (ready.Count == 0) return working;
            return working = RunAsync(ready);
        }
    }

    private async Task RunAsync(IReadOnlyList<string> files)
    {
        IReadOnlyList<string> outputs = [];
        Exception? failure = null;
        try { outputs = await run(Rule.Route, files, stop.Token); }
        catch (Exception error) { failure = error; }
        lock (gate)
        {
            // The inputs are done either way (a failure is said once, not retried forever); the outputs are never new.
            foreach (var path in files.Concat(outputs))
                if (StampOf(path) is { } stamp) seen[path] = stamp;
            Save();
        }
        Ran?.Invoke(files, outputs, failure);
    }

    /// Whether the route applies to this file on its own.
    private bool Fits(string file) => Rule.Route.StartsWith("convert:", StringComparison.Ordinal)
        ? Capabilities.Formats([file]).Contains(Rule.Route[8..]) : Capabilities.Tools([file]).Contains(Rule.Route);

    private IEnumerable<string> Files()
    {
        try { return Directory.EnumerateFiles(Rule.Path).Where(Wanted).ToList(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }

    private static bool Wanted(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length > 0 && !name.StartsWith('.') && !name.EndsWith('~') && !Temporary.Contains(Path.GetExtension(name)) && File.Exists(path);
    }

    private static Stamp? StampOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new(info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private sealed record State(string Folder, Dictionary<string, Stamp> Seen);

    private bool Load()
    {
        try
        {
            var file = StateFile(Rule.Path);
            if (!File.Exists(file)) return false;
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(file));
            if (state?.Folder != Rule.Path || state.Seen is null) return false;
            foreach (var (path, stamp) in state.Seen) if (stamp is not null) seen[path] = stamp;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    private void Save()
    {
        try
        {
            var file = StateFile(Rule.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // Forget files that are gone, so the state stays the size of the folder.
            var live = seen.Where(p => File.Exists(p.Key)).ToDictionary();
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(new State(Rule.Path, live)));
            File.Move(file + ".tmp", file, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        stop.Cancel();
        watcher?.Dispose();
        timer?.Dispose();
    }
}

/// The background instance's watchers: one per enabled folder in Settings, kept in step with it.
public sealed class WatchService(Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> run) : IDisposable
{
    private readonly Dictionary<string, FolderWatch> running = [];
    private HashSet<string> known = [];

    /// Each run, for the success card and the Library.
    public event Action<WatchFolder, IReadOnlyList<string>, IReadOnlyList<string>, Exception?>? Ran;

    public IReadOnlyDictionary<string, FolderWatch> Running => running;

    /// Starts and stops watchers to match `folders`. A folder newly added or turned on starts fresh (what is there
    /// now stays untouched); one that was already on resumes from its saved state (`startup`: the first sync).
    public void Sync(IReadOnlyList<WatchFolder> folders, bool startup = false, bool timers = true)
    {
        foreach (var (path, watch) in running.ToList())
            if (!folders.Any(f => f.Enabled && f.Path == path && f.Route == watch.Rule.Route)) { watch.Dispose(); running.Remove(path); }
        foreach (var folder in folders.Where(f => f.Enabled && Directory.Exists(f.Path) && !running.ContainsKey(f.Path)))
        {
            var watch = new FolderWatch(folder, run, fresh: !startup && !known.Contains(folder.Path + "\n" + folder.Route));
            watch.Ran += (inputs, outputs, error) => Ran?.Invoke(folder, inputs, outputs, error);
            running[folder.Path] = watch;
            if (timers) watch.Start();
        }
        known = [.. folders.Where(f => f.Enabled).Select(f => f.Path + "\n" + f.Route)];
    }

    public void Dispose()
    {
        foreach (var watch in running.Values) watch.Dispose();
        running.Clear();
    }
}
