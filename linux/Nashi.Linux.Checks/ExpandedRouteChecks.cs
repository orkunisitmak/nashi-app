using System.Security.Cryptography;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;

static class ExpandedRouteChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var work = Directory.CreateDirectory(Path.Combine(root, "expanded routes")).FullName;
        var png = Path.Combine(work, "photo.png");
        var jpg = Path.Combine(work, "already.jpg");
        using (var image = new MagickImage(MagickColors.Blue, 40, 30)) { image.Write(png); image.Write(jpg); }
        var original = SHA256.HashData(File.ReadAllBytes(jpg));
        var runner = new OperationRunner(new Toolchain(work, Environment.GetEnvironmentVariable("PATH")));
        var progress = new Progress<OperationProgress>();
        var batch = await runner.RunBesideSourcesAsync("convert:jpg", [png, jpg], folder => Task.FromResult<string?>(folder), progress, default);
        check(Capabilities.Formats([png, jpg]).Contains("jpg") && Capabilities.Formats([png, jpg]).Contains("png")
            && batch is { Count: 2 } && batch.All(f => Path.GetDirectoryName(f) == work && f != jpg)
            && SHA256.HashData(File.ReadAllBytes(jpg)).SequenceEqual(original), "Mixed JPG/PNG batches offer both targets and save fresh copies without overwriting originals");
        check(WheelFlow.Sides(["x.mp4"])[0].Items.Select(i => i.Title).SequenceEqual(["Video", "Audio", "Images"])
            && WheelFlow.Sides(["x.mp4"])[1].Items.Select(i => i.Title).SequenceEqual(["Edit", "Audio", "Frames", "Create", "Compress", "Metadata", "File Actions"]),
            "Video has the proposed output families and seven task groups, without More Tools");
        check(!Capabilities.Formats(["x.gif"]).Any(Capabilities.AudioOutputs.Contains)
            && Capabilities.Formats(["x.aac"]).Contains("flac") && Capabilities.Tools(["x.m4v"]).Contains("trimVideo"),
            "GIF offers no audio extraction; AAC and M4V inputs have working format and tool families");

        var ffmpeg = Environment.GetEnvironmentVariable("NASHI_VERIFY_FFMPEG");
        var ffprobe = Environment.GetEnvironmentVariable("NASHI_VERIFY_FFPROBE");
        if (string.IsNullOrEmpty(ffmpeg) || string.IsNullOrEmpty(ffprobe))
        {
            Console.WriteLine("Expanded media routes: skipped (set NASHI_VERIFY_FFMPEG and NASHI_VERIFY_FFPROBE)");
            return;
        }
        var tools = new Toolchain(Path.Combine(work, "tools"), Path.GetDirectoryName(ffmpeg) + ":" + Path.GetDirectoryName(ffprobe));
        runner = new OperationRunner(tools);
        var engine = new MediaEngine(ffmpeg, ffprobe);
        var clip = Path.Combine(work, "clip.mp4");
        var wav = Path.Combine(work, "tone.wav");
        await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=160x120:rate=10", "-f", "lavfi", "-i", "sine=duration=1", "-shortest", "-pix_fmt", "yuv420p", clip]);
        await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=duration=1", wav]);
        var hashes = new[] { clip, wav }.ToDictionary(f => f, f => SHA256.HashData(File.ReadAllBytes(f)));
        foreach (var source in new[] { clip, wav })
        foreach (var format in Capabilities.Formats([source]))
        {
            try
            {
                var output = await runner.RunAsync("convert:" + format, [source], Path.Combine(work, Path.GetExtension(source)[1..] + "-" + format), progress, default);
                var valid = output.Count == 1 && Path.GetExtension(output[0]) == "." + format;
                if (valid && format is "jpg" or "png")
                {
                    var image = new MagickImageInfo(output[0]);
                    valid = image.Width == 160 && image.Height == 120 && image.Format == PhotoCodec.Format(format);
                }
                else if (valid)
                {
                    var info = await engine.InspectAsync(output[0]);
                    valid = info.Duration > 0 && (Capabilities.AudioOutputs.Contains(format) ? info.Audio is not null && info.Video is null : info.Video is not null);
                }
                check(valid, "Expanded route " + Path.GetExtension(source) + " → " + format + " produces a valid output");
            }
            catch (Exception error) { check(false, "Expanded route " + Path.GetExtension(source) + " → " + format + ": " + error.Message); }
        }
        var aac = (await runner.RunAsync("convert:aac", [wav], Path.Combine(work, "aac-input"), progress, default))[0];
        var m4v = Path.Combine(work, "input.m4v");
        File.Copy(clip, m4v);
        check((await runner.RunAsync("convert:flac", [aac], Path.Combine(work, "aac-output"), progress, default)).Count == 1
            && (await runner.RunAsync("convert:wav", [m4v], Path.Combine(work, "m4v-output"), progress, default)).Count == 1,
            "AAC and M4V inputs execute their new routes");
        var framesFolder = Path.Combine(work, "selected frames");
        var frames = await MediaFrames.ExportAsync(engine, [clip], "jpg", framesFolder, new MediaOptions { SnapshotTimes = [0, .5] }, progress, default);
        check(frames.Count == 2 && frames.All(f => new MagickImageInfo(f).Format == MagickFormat.Jpeg)
            && Directory.GetFiles(framesFolder).Length == 2 && !Directory.GetDirectories(framesFolder).Any(),
            "Selected JPG frames publish together and leave no intermediate PNGs or staging directories");
        check(hashes.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)), "Expanded media conversions preserve both original inputs");
    }
}
