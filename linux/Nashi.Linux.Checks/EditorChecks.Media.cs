using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nashi.Core;
using Nashi.Linux;
using Nashi.Linux.Editors;
using Nashi.Linux.Editors.Media;

static partial class EditorChecks
{
    // ---- Every catalog tool is offered and resolves to an editor or a direct route.

    static void Parity(string work)
    {
        var catalog = FeatureCatalog.Conversions.Keys.ToArray();
        var selections = catalog.SelectMany(e => new[] { new[] { "x." + e }, ["x." + e, "y." + e] })
            .Concat(catalog.SelectMany(a => catalog.Select(b => new[] { "x." + a, "y." + b }))).ToList();
        var differ = selections.Where(s => !Capabilities.Tools(s).SequenceEqual(FeatureCatalog.Tools(s))).Select(s => string.Join("+", s)).ToList();
        check(differ.Count == 0, $"Parity gate: the Tools wheel offers exactly the catalog's tools for all {selections.Count} single, same-type and mixed selections; differ: " + string.Join(" ", differ.Take(10)));

        // Each offered tool opens its editor or runs directly, for every kind of file (fake tools stand in).
        var root = Directory.CreateDirectory(Path.Combine(work, "parity")).FullName;
        var bin = Directory.CreateDirectory(Path.Combine(root, "bin")).FullName;
        foreach (var name in new[] { "ffmpeg", "ffprobe" })
        {
            File.WriteAllText(Path.Combine(bin, name), "#!/bin/sh\nexit 1\n");
            File.SetUnixFileMode(Path.Combine(bin, name), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var exif = Directory.CreateDirectory(Path.Combine(root, "exif", "metadata-13.59")).FullName;
        File.WriteAllText(Path.Combine(exif, "exiftool"), "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(Path.Combine(exif, "exiftool"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var saved = AppPaths.MetadataRoot;
        AppPaths.MetadataRoot = Path.GetDirectoryName(exif);
        var tools = new Toolchain(Directory.CreateDirectory(Path.Combine(root, "none")).FullName, bin);
        var direct = new HashSet<string> { "createPDF", "readQRCode", "extractArchive" };
        var unresolved = new List<string>();
        try
        {
            foreach (var selection in selections.Where(s => s.Length == 1 || s[0][2..] == s[1][2..]))
                foreach (var tool in Capabilities.Tools(selection))
                {
                    var files = selection.Select(f => Path.Combine(root, f)).ToArray();
                    foreach (var file in files) if (!File.Exists(file)) File.WriteAllText(file, "x");
                    if (ToolEditorWindow.For(tool, files, tools) is null && !direct.Contains(tool)) unresolved.Add(tool + ":" + string.Join("+", selection));
                }
        }
        finally { AppPaths.MetadataRoot = saved; }
        check(unresolved.Count == 0, "Every offered tool for one file or several of a kind opens its editor or runs directly; unresolved: " + string.Join(" ", unresolved));
        check(ThrowsMissing(() => ToolEditorWindow.For("trimVideo", [Path.Combine(root, "x.mp4")], new Toolchain(Path.Combine(root, "none"), ""))), "A media editor without ffmpeg opens setup instead");
    }

    // ---- The timeline on its own: handles drag and clamp, a drag elsewhere draws a range, the handles have names.

    static void TimelineChecks()
    {
        var timeline = new Timeline { Duration = 10, Width = 500 };
        timeline.SetRange(2, 6);
        var window = new Window { Width = 540, Height = 140, Content = new Border { Padding = new Thickness(20), Child = timeline } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Point At(double seconds, double y = 20) => timeline.TranslatePoint(new Point(seconds / 10 * timeline.Bounds.Width, y), window) ?? default;
        void Drag(double from, double to)
        {
            window.MouseDown(At(from), MouseButton.Left);
            window.MouseMove(At((from + to) / 2), RawInputModifiers.LeftMouseButton);
            window.MouseMove(At(to), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(to), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        Drag(2, -3);
        check(timeline.Start == 0 && timeline.End == 6, "Dragging the In handle past the start clamps it at 0");
        Drag(6, 0);
        check(timeline.End - timeline.Start >= Timeline.MinRange - 1e-9 && timeline.Start == 0 && timeline.End <= .2, "Dragging the Out handle onto the In handle keeps the range 0.05 s long (" + timeline.Start + "–" + timeline.End + ")");
        timeline.SetRange(2, 4);
        Drag(3, 9.9);
        check(Math.Abs(timeline.End - timeline.Start - 2) < .01 && timeline.End <= 10 && timeline.End > 9.9, "Dragging inside moves the range, kept inside the media");
        Drag(1, 3);
        check(Math.Abs(timeline.Start - 1) < .05 && Math.Abs(timeline.End - 3) < .05, "Dragging outside the range draws a new one");
        var seeks = new List<double>();
        timeline.Seeked += seeks.Add;
        window.MouseDown(At(5), MouseButton.Left);
        window.MouseUp(At(5), MouseButton.Left);
        check(seeks.Count > 0 && Math.Abs(seeks[^1] - 5) < .05 && Math.Abs(timeline.Playhead - 5) < .05, "A click on the timeline seeks there");
        var names = timeline.GetVisualDescendants().OfType<Border>().Select(AutomationProperties.GetName).ToList();
        check(names.Contains("Start of the selected range") && names.Contains("End of the selected range"), "The range handles have accessible names");
        check(Timeline.Format(83.456) == "1:23.45" && Timeline.Format(3725) == "1:02:05", "Timeline times read m:ss.hh, or h:mm:ss past an hour");
        window.Close();
    }

    // ---- The media editors on real fixtures (NASHI_VERIFY_FFMPEG and NASHI_VERIFY_FFPROBE).

    static int FfmpegChildren() =>
        Directory.EnumerateDirectories("/proc").Select(Path.GetFileName).Where(p => p!.All(char.IsDigit)).Count(pid =>
        {
            try
            {
                var status = File.ReadAllLines($"/proc/{pid}/status");
                return status.Any(l => l == "Name:\tffmpeg") && status.Any(l => l == "PPid:\t" + Environment.ProcessId);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        });

    static void MediaEditors(string work)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("NASHI_VERIFY_FFMPEG");
        var ffprobe = Environment.GetEnvironmentVariable("NASHI_VERIFY_FFPROBE");
        if (string.IsNullOrEmpty(ffmpeg) || string.IsNullOrEmpty(ffprobe))
        {
            Console.WriteLine("Media editor checks: ffmpeg absent; skipped (set NASHI_VERIFY_FFMPEG and NASHI_VERIFY_FFPROBE)");
            return;
        }
        var toolchain = new Toolchain(Directory.CreateDirectory(Path.Combine(work, "media-tools")).FullName, string.Join(':', Path.GetDirectoryName(ffmpeg), Path.GetDirectoryName(ffprobe)));
        var tools = Nashi.Linux.Editors.Media.MediaTools.From(toolchain);
        var engine = tools.Engine;
        var folder = Directory.CreateDirectory(Path.Combine(work, "media")).FullName;
        void Make(string path, params string[] args) => Wait(ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-v", "error", "-y", .. args, path]));
        var video = Path.Combine(folder, Name + ".mp4");
        Make(video, "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=30:duration=4", "-f", "lavfi", "-i", "sine=frequency=440:duration=4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", "-metadata", "title=Original title", "-metadata", "comment=secret place");
        var silentClip = Path.Combine(folder, "--second clip.mov");
        Make(silentClip, "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=25:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p");
        var tone = Path.Combine(folder, Name + " tone.wav");
        Make(tone, "-filter_complex", "sine=frequency=440:duration=3[a];sine=frequency=880:duration=3[b];[a][b]amerge=inputs=2,volume=0.05");
        var hashes = new[] { video, silentClip, tone }.ToDictionary(f => f, Hash);
        MediaInfo Probe(string path) => Wait(engine.InspectAsync(path));
        int Channels(string path) => int.Parse(Wait(ProcessRunner.RunAsync(ffprobe, ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=channels", "-of", "csv=p=0", path])).Output.Trim(), CultureInfo.InvariantCulture);
        double Loudness(string path)
        {
            var log = Wait(ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostats", "-i", path, "-af", "ebur128", "-f", "null", "-"])).Error;
            var summary = log[log.LastIndexOf("Summary:", StringComparison.Ordinal)..];
            return double.Parse(Regex.Match(summary, @"I:\s+(-?[\d.]+) LUFS").Groups[1].Value, CultureInfo.InvariantCulture);
        }
        IReadOnlyList<string> Save(ToolEditorWindow window, params string[] files)
        {
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            check(window.Job is not null, window.Panel.Tool + " saves with Return");
            var outputs = window.Job is null ? [] : Run(window.Job, files.Length > 0 ? files : window.Panel.Inputs, folder);
            check(outputs.Count > 0 && outputs.All(o => Path.GetDirectoryName(o) == folder || Path.GetDirectoryName(Path.GetDirectoryName(o)) == folder), window.Panel.Tool + " writes beside the source");
            return outputs;
        }
        MediaEditor Media(ToolEditorWindow window) => (MediaEditor)window.Panel;

        // Trim: I/O keys set the range from the playhead, Space plays and pauses (fake clock and sink), Return saves.
        var trim = Open(new MediaEditor("trimVideo", [video], tools));
        var trimmer = Media(trim);
        check(trim.Panel.Primary == "Save trimmed copy" && Texts(trim).Contains("Straight to the good stuff.") && trimmer.Player is { Duration: > 3.9 }, "Trim Video opens with the macOS title, hint and the video's length");
        trimmer.Player!.Seek(1);
        trim.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.None);
        trimmer.Player.Seek(2.5);
        trim.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.None);
        check(Math.Abs(trimmer.Settings.Start - 1) < 1e-6 && Math.Abs(trimmer.Settings.End!.Value - 2.5) < 1e-6 && trimmer.Timeline.Start == 1, "I and O set the range at the playhead");
        trim.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        check(Math.Abs(trimmer.Player.Position - 3.5) < 1e-6, "→ steps a second");
        trim.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        check(Math.Abs(trimmer.Player.Position - (3.5 - 1 / 30.0)) < 1e-6, "Shift+← steps one frame");
        trim.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        var playing = trimmer.Player.Playing;
        for (var i = 0; i < 30; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
        var running = Nashi.Linux.Editors.Media.MediaTools.Live;
        trim.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        for (var i = 0; i < 100 && Nashi.Linux.Editors.Media.MediaTools.Live > 0; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); } // a paused frame may still be decoding
        check(playing && running >= 1 && !trimmer.Player.Playing && Nashi.Linux.Editors.Media.MediaTools.Live == 0, $"Space plays (frames and sound decode: {running} ffmpeg) and pauses (they stop; {playing} {trimmer.Player.Playing} {Nashi.Linux.Editors.Media.MediaTools.Live})");
        check(!Texts(trim).Contains("Sound preview is off: PulseAudio isn’t available. The picture still plays."), "With a sound output the silent note stays hidden");
        var outputs = Save(trim, video);
        check(outputs.Count == 1 && Path.GetFileName(outputs[0]) == Name + " Trimmed.mp4" && Math.Abs(Probe(outputs[0]).Duration - 1.5) <= .1, "Trim Video keeps In to Out within 0.1 s (" + (outputs.Count > 0 ? Probe(outputs[0]).Duration : 0) + " s)");

        // Pulse missing: the frames still play, silently, and the editor says so.
        var sink = MediaPlayer.OpenSink;
        MediaPlayer.OpenSink = (_, _) => null;
        var quiet = Open(new MediaEditor("muteVideo", [video], tools));
        quiet.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        for (var i = 0; i < 100 && !Media(quiet).Player!.Silent; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
        check(Media(quiet).Player!.Silent && Media(quiet).Player!.Playing && Texts(quiet).Contains("Sound preview is off: PulseAudio isn’t available. The picture still plays."), "Without PulseAudio the video plays silently and the editor says so");
        MediaPlayer.OpenSink = sink;
        // Closing while playing kills every ffmpeg it started.
        quiet.Close();
        for (var i = 0; i < 100 && (Nashi.Linux.Editors.Media.MediaTools.Live > 0 || FfmpegChildren() > 0); i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        check(Nashi.Linux.Editors.Media.MediaTools.Live == 0 && FfmpegChildren() == 0, "Process hygiene: after a playing editor closes, no ffmpeg child of this process remains");

        // Mute.
        var mute = Open(new MediaEditor("muteVideo", [video], tools));
        outputs = Save(mute, video);
        check(outputs.Count == 1 && !Probe(outputs[0]).HasAudio && Probe(outputs[0]).Video is not null, "Mute Video keeps the picture with no audio stream");

        // Split: equal parts, a removed point, a discarded clip, points by M.
        var split = Open(new MediaEditor("splitVideo", [video], tools));
        Media(split).SetEqual(4);
        check(Media(split).Points.Count == 3, "Equal clips sets 3 points for 4 clips");
        Media(split).Timeline.SelectedPoint = 1;
        split.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Media(split).Discard(0);
        outputs = Save(split, video);
        var durations = outputs.Select(o => Probe(o).Duration).ToArray();
        check(outputs.Count == 2 && Math.Abs(durations[0] - 2) <= .1 && Math.Abs(durations[1] - 1) <= .1 && outputs.All(o => o.Contains(" Part ")), "Split Video: 4 equal clips less a point and a discarded clip give 2 clips of 2 s and 1 s (" + string.Join(", ", durations) + ")");
        var marked = Open(new MediaEditor("splitVideo", [video], tools));
        Media(marked).SetEqual(2);
        Media(marked).Player!.Seek(1);
        marked.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
        check(Media(marked).Points.SequenceEqual([1, 2]), "M adds a split point at the playhead");
        marked.Close();

        // Crop on a frame with the image crop geometry.
        var crop = Open(new VideoCropEditor([video], tools));
        check(crop.Panel.Source is { Width: 320, Height: 240 }, "Crop Video draws on a full-size frame");
        DragOn(crop, new Point(10, 20), new Point(110, 100));
        outputs = Save(crop, video);
        var cropped = Probe(outputs[0]).Video!;
        check(cropped.Width == 100 && cropped.Height == 80, $"Crop Video keeps the dragged 100 × 80 rectangle ({cropped.Width} × {cropped.Height})");

        // Speed.
        var speed = Open(new MediaEditor("changeVideoSpeed", [video], tools));
        Media(speed).Set("speed", 2.0);
        check(Media(speed).Player!.Speed == 2, "The speed previews live");
        outputs = Save(speed, video);
        check(Math.Abs(Probe(outputs[0]).Duration - 2) <= .15, "Change Speed at 2× halves the length (" + Probe(outputs[0]).Duration + " s)");

        // Snapshots.
        var snaps = Open(new MediaEditor("videoSnapshots", [video], tools));
        Media(snaps).Evenly(4);
        outputs = Save(snaps, video);
        check(outputs.Count == 4 && outputs.All(o => o.EndsWith(".png") && new ImageMagick.MagickImageInfo(o).Width == 320), "Snapshots saves 4 evenly spaced full-size frames");
        Media(snaps).Every(1);
        check(Media(snaps).Points.Count == 4, "Every 1 s on a 4 s video is 4 frames");

        // Redact: an area over 1–2 s changes the frames only there, only then.
        var redact = Open(new MediaEditor("redactVideo", [video], tools));
        Media(redact).SetRange(1, 2);
        DragOn(redact, new Point(0, 0), new Point(160, 120));
        check(Media(redact).AreaCount == 1 && redact.SaveButton.IsEnabled, "Dragging on the frame adds a redaction area");
        outputs = Save(redact, video);
        double Diff(string a, string b, double t, bool inside)
        {
            var x = Wait(tools.FrameAsync(a, t, 320, 240, default));
            var y = Wait(tools.FrameAsync(b, t, 320, 240, default));
            double sum = 0; var n = 0;
            for (var py = 0; py < 240; py++)
                for (var px = 0; px < 320; px++)
                {
                    // Inside: clear of the area's edges; outside: clear of them too (the encoder blurs a few pixels).
                    if (inside ? px >= 156 || py >= 116 : px < 164 && py < 124) continue;
                    var i = (py * 320 + px) * 4;
                    sum += Math.Abs(x[i] - y[i]) + Math.Abs(x[i + 1] - y[i + 1]) + Math.Abs(x[i + 2] - y[i + 2]);
                    n++;
                }
            return sum / n / 3;
        }
        var during = Diff(video, outputs[0], 1.5, true);
        var beforeRange = Diff(video, outputs[0], .5, true);
        var outside = Diff(video, outputs[0], 1.5, false);
        check(during > 40 && beforeRange < 8 && outside < 8, $"Redact Video changes frames only inside the area and the range (inside {during:0.0}, before {beforeRange:0.0}, outside {outside:0.0})");

        // Join, ordered by moving the second clip first; a clip without sound gets silence.
        var join = Open(new OrderEditor("joinVideos", [video, silentClip], tools));
        ((OrderEditor)join.Panel).Move(1, 0);
        check(join.Panel.Inputs[0] == silentClip && Texts(join).Contains("Total: 0:06.00"), "Join Videos reorders and totals the lengths");
        outputs = Save(join, join.Panel.Inputs.ToArray());
        var joined = Probe(outputs[0]);
        check(Math.Abs(joined.Duration - 6) <= .2 && joined.Video is { Width: 640, Height: 360 } && joined.HasAudio, $"Join Videos is the sum of the clips at the first one's size with sound ({joined.Duration} s)");

        // Normalize toward -16 LUFS.
        var normalize = Open(new MediaEditor("normalizeAudio", [tone], tools));
        check(!normalize.Canvas.IsVisible && Media(normalize).Player!.Frame is null, "Audio editors show the waveform, not a canvas");
        outputs = Save(normalize, tone);
        var loud = Loudness(outputs[0]);
        check(Math.Abs(loud - -16) <= 1.5, $"Normalize brings a quiet tone near -16 LUFS ({loud} LUFS)");

        // Channels: mono, then stereo swapped.
        var channels = Open(new MediaEditor("audioChannels", [tone], tools));
        Media(channels).Set("channels", 1);
        outputs = Save(channels, tone);
        check(Channels(outputs[0]) == 1, "Channels: Mono saves one channel");
        var swapped = Open(new MediaEditor("audioChannels", [tone], tools));
        Media(swapped).Set("swap", true);
        outputs = Save(swapped, tone);
        var leftPeaks = Wait(tools.PeaksAsync(outputs[0], 3, 1, default, 0));
        var sourceRight = Wait(tools.PeaksAsync(tone, 3, 1, default, 1));
        check(Channels(outputs[0]) == 2 && Media(swapped).Settings.SwapChannels && Math.Abs(leftPeaks[0] - sourceRight[0]) < .02, "Channels: Swap keeps two channels with the right one on the left");

        // Bleep: a silenced range is silent; outside it the tone stays.
        var bleep = Open(new MediaEditor("redactAudio", [tone], tools));
        Media(bleep).Set("bleepMode", "mute");
        Media(bleep).SetRange(1, 2);
        Media(bleep).AddBleep();
        check(Media(bleep).Bleeps.Count == 1 && Media(bleep).Player!.AudioFilter?.Contains("between(t,1,2)") == true, "Add Redaction adds the range and previews it");
        outputs = Save(bleep, tone);
        var peaks = Wait(tools.PeaksAsync(outputs[0], 3, 30, default));
        check(peaks[15] < .001 && peaks[5] > .003 && peaks[25] > .003, $"Bleep: the muted second is silent, the rest keeps its sound ({peaks[5]:0.000}, {peaks[15]:0.000}, {peaks[25]:0.000})");

        // Visualizer.
        var visual = Open(new MediaEditor("audioToVideo", [tone], tools));
        Media(visual).Set("size", (320, 240));
        for (var i = 0; i < 200 && visual.Panel.Frame is null; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        check(visual.Panel.Frame is not null, "Audio to Video previews a frame of the visualization");
        outputs = Save(visual, tone);
        var made = Probe(outputs[0]);
        check(made.Video is { Width: 320, Height: 240 } && made.HasAudio && Path.GetFileName(outputs[0]) == Name + " tone Visualization.mp4", "Audio to Video makes a 320 × 240 video with the sound");

        // Trim Audio.
        var trimAudio = Open(new MediaEditor("trimAudio", [tone], tools));
        Media(trimAudio).SetRange(.5, 2);
        outputs = Save(trimAudio, tone);
        check(Math.Abs(Probe(outputs[0]).Duration - 1.5) <= .1, "Trim Audio keeps In to Out within 0.1 s");

        // Compress and metadata for video.
        var compress = Open(ToolEditorWindow.For("compress", [video], toolchain)!);
        ((FileCompressEditor)compress.Panel).Target(40, "percent");
        outputs = Save(compress, video);
        check(new FileInfo(outputs[0]).Length <= new FileInfo(video).Length * .6 && Path.GetFileName(outputs[0]) == Name + " Compressed.mp4", "Compress (video) reaches a 40% reduction");
        var tags = Open(ToolEditorWindow.For("removeMetadata", [video], toolchain)!);
        check(((MediaMetadataEditor)tags.Panel).Values.GetValueOrDefault("title") == "Original title", "Media metadata lists the container's tags");
        ((MediaMetadataEditor)tags.Panel).Set("title", "Nashi");
        outputs = Save(tags, video);
        var written = Wait(ProcessRunner.RunAsync(ffprobe, ["-v", "error", "-show_entries", "format_tags", "-of", "json", outputs[0]])).Output;
        check(written.Contains("\"Nashi\"") && !written.Contains("secret place"), "Media metadata: Remove all drops the tags and writes the typed title");

        check(hashes.All(p => Hash(p.Key).SequenceEqual(p.Value)), "The media editors never modify the originals");
        check(Nashi.Linux.Editors.Media.MediaTools.Live == 0, "No preview ffmpeg is left running");

        // Renders: each media editor in light, dark and Turkish.
        void Ready(ToolEditorWindow w)
        {
            for (var i = 0; i < 300 && w.Panel is MediaEditor m && m.Timeline.Thumbnails is null && m.Timeline.Peaks is null; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            for (var i = 0; i < 50; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
        }
        Renders("trim-video", () => new MediaEditor("trimVideo", [video], tools), w => { Ready(w); Media(w).SetRange(.8, 3.1); Media(w).Player!.Seek(1.6); Ready(w); });
        Renders("split-video", () => new MediaEditor("splitVideo", [video], tools), Ready);
        Renders("speed", () => new MediaEditor("changeVideoSpeed", [video], tools), Ready);
        Renders("snapshots", () => new MediaEditor("videoSnapshots", [video], tools), w => { Ready(w); Media(w).Evenly(3); });
        Renders("redact-video", () => new MediaEditor("redactVideo", [video], tools), w => { Ready(w); DragOn(w, new Point(40, 40), new Point(160, 120)); });
        Renders("crop-video", () => new VideoCropEditor([video], tools), w => DragOn(w, new Point(40, 30), new Point(240, 200)));
        Renders("mute", () => new MediaEditor("muteVideo", [video], tools), Ready);
        Renders("join", () => new OrderEditor("joinVideos", [video, silentClip], tools));
        Renders("trim-audio", () => new MediaEditor("trimAudio", [tone], tools), w => { Ready(w); Media(w).SetRange(.5, 2.2); });
        Renders("normalize", () => new MediaEditor("normalizeAudio", [tone], tools), Ready);
        Renders("channels", () => new MediaEditor("audioChannels", [tone], tools), Ready);
        Renders("bleep", () => new MediaEditor("redactAudio", [tone], tools), w => { Ready(w); Media(w).SetRange(1, 1.6); Media(w).AddBleep(); });
        Renders("visualizer", () => new MediaEditor("audioToVideo", [tone], tools), w => { Ready(w); for (var i = 0; i < 200 && w.Panel.Frame is null; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); } });
        Renders("compress-video", () => new FileCompressEditor([video], tools));
        Renders("metadata-video", () => new MediaMetadataEditor([video], tools));
        for (var i = 0; i < 100 && (Nashi.Linux.Editors.Media.MediaTools.Live > 0 || FfmpegChildren() > 0); i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        check(Nashi.Linux.Editors.Media.MediaTools.Live == 0 && FfmpegChildren() == 0, "After every media editor closed, no ffmpeg child remains");
        Console.WriteLine("Media editor checks: ran on " + ffmpeg);
    }
}
