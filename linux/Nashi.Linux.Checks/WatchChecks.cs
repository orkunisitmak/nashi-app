using Avalonia.Threading;
using Nashi.Linux;

/// Watch folders, on a fake clock and a recording route (cases after Filee's FolderWatcherTests).
static class WatchChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "watch ş inbox")).FullName;
        File.WriteAllText(Path.Combine(folder, "before.png"), "there first");
        var now = new DateTime(2026, 1, 1);
        var runs = new List<IReadOnlyList<string>>();
        // compress makes a PNG from a PNG, which compress takes too: the loop the watcher must not fall into.
        Task<IReadOnlyList<string>> Route(string route, IReadOnlyList<string> files, CancellationToken token)
        {
            runs.Add(files);
            IReadOnlyList<string> outputs = [.. files.Select(f => Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileNameWithoutExtension(f) + " Compressed.png"))];
            foreach (var output in outputs) File.WriteAllText(output, "smaller");
            return Task.FromResult(outputs);
        }
        var rule = new WatchFolder(folder, "compress");
        var watch = new FolderWatch(rule, Route, fresh: true, () => now);
        void At(double seconds) { now = now.AddSeconds(seconds); watch.Scan(); Wait(watch.Tick()); }

        At(10);
        check(runs.Count == 0, "Files already in a folder when it is added are left alone");
        var photo = Path.Combine(folder, "-new photo.png");
        File.WriteAllText(photo, "part");
        At(0);
        At(1);
        check(runs.Count == 0, "A new file waits until it has been still for 1.5 s");
        File.AppendAllText(photo, " and the rest");
        File.SetLastWriteTimeUtc(photo, DateTime.UtcNow.AddSeconds(1));
        At(1);
        check(runs.Count == 0, "A file still growing starts its wait again");
        File.WriteAllText(Path.Combine(folder, "download.png.crdownload"), "x");
        File.WriteAllText(Path.Combine(folder, ".hidden.png"), "x");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "x");
        At(1);
        At(1);
        check(runs is [[var only]] && only == photo, "Once still for 1.5 s it runs, alone: partial downloads, hidden files and files the route doesn't take are skipped");
        At(5);
        At(5);
        check(runs.Count == 1 && File.Exists(Path.Combine(folder, "-new photo Compressed.png")), "The output beside it is never taken as a new file");

        // Off: nothing runs. Back on (fresh): what arrived meanwhile stays. A restart (not fresh) resumes from the saved state.
        var service = new WatchService(Route);
        service.Sync([rule], startup: true, timers: false);
        check(service.Running.Count == 1, "The service starts a watcher for an enabled folder");
        service.Sync([rule with { Enabled = false }], timers: false);
        check(service.Running.Count == 0, "Turning a folder off stops its watcher");
        watch.Dispose();
        var whileOff = Path.Combine(folder, "while off.png");
        File.WriteAllText(whileOff, "x");
        service.Sync([rule], timers: false);
        var resumed = service.Running[folder];
        resumed.Scan();
        Wait(resumed.Tick());
        now = now.AddMinutes(1);
        check(runs.Count == 1, "Turned back on, files that came while it was off are left alone");
        service.Dispose();
        var whileClosed = Path.Combine(folder, "while closed.png");
        File.WriteAllText(whileClosed, "x");
        var restarted = new FolderWatch(rule, Route, fresh: false, () => now);
        restarted.Scan();
        now = now.AddSeconds(2);
        restarted.Scan();
        Wait(restarted.Tick());
        check(runs.Count == 2 && runs[1] is [var late] && late == whileClosed, "After a restart it resumes: only what arrived while Nashi was closed runs, not the old outputs");
        restarted.Dispose();

        // The real thing: inotify and the timer, on a short settle.
        var live = Directory.CreateDirectory(Path.Combine(root, "watch live")).FullName;
        using (var timed = new FolderWatch(rule with { Path = live }, Route, fresh: true) { Settle = TimeSpan.FromMilliseconds(200) })
        {
            timed.Start();
            File.WriteAllText(Path.Combine(live, "dropped in.png"), "x");
            for (var i = 0; i < 400 && !File.Exists(Path.Combine(live, "dropped in Compressed.png")); i++) Thread.Sleep(10);
            check(File.Exists(Path.Combine(live, "dropped in Compressed.png")), "With the watcher and timer running, a file dropped in is converted beside itself");
        }
    }

    static void Wait(Task task)
    {
        for (var i = 0; i < 1000 && !task.IsCompleted; i++) { Thread.Sleep(2); Dispatcher.UIThread.RunJobs(); }
    }
}
