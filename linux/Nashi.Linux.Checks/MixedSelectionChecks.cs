using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;

static class MixedSelectionChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Avalonia.Media.Imaging.WriteableBitmap?, string> smoke)
    {
        var inputs = Task.Run(async () =>
        {
            var work = Directory.CreateDirectory(Path.Combine(root, "mixed-selections")).FullName;
            var pictures = Directory.CreateDirectory(Path.Combine(work, "Pictures")).FullName;
            var documents = Directory.CreateDirectory(Path.Combine(work, "Documents")).FullName;
            var unrelated = Directory.CreateDirectory(Path.Combine(work, "Other")).FullName;
            var png = Path.Combine(pictures, "photo.png");
            using (var image = new MagickImage(MagickColors.Coral, 40, 30)) image.Write(png);
            var pdf = ImageOperations.Convert([png], "pdf", documents, null, default).Single();
            var binary = Path.Combine(unrelated, "data.bin");
            File.WriteAllText(binary, "Unrelated data must remain unchanged.");
            var inputs = new[] { png, pdf, binary };
            var hashes = inputs.ToDictionary(f => f, f => SHA256.HashData(File.ReadAllBytes(f)));
            check(Capabilities.ConversionInputs([png, pdf], "jpg").Count == 2
                && Capabilities.ConversionInputs([png, pdf], "pdf").Count == 2,
                "PDF plus image has common JPG/PNG/PDF paths, including an already-PDF copy");
            check(Capabilities.ConversionInputs(inputs, "webp").SequenceEqual([png])
                && Capabilities.Formats(inputs).Take(3).All(f => Capabilities.ConversionInputs(inputs, f).Count == 2),
                "Mixed targets put broadly applicable choices first and scope unsupported files explicitly");
            check(!Capabilities.Formats(["same.pdf"]).Contains("pdf") && !Capabilities.Formats([binary]).Any(),
                "Mixed selection support does not expose meaningless single-file identity conversions");

            var runner = new OperationRunner(new Toolchain(Path.Combine(work, "no-tools"), ""));
            var progress = new Progress<OperationProgress>();
            var output = await runner.RunAsync("convert:jpg", [png, pdf], Path.Combine(work, "common-jpg"), progress, default);
            check(output.Count == 2 && output.All(f => new MagickImageInfo(f).Format == MagickFormat.Jpeg),
                "A real mixed PDF/image batch creates JPEG outputs through both engines");
            var asked = new List<string>();
            output = (await runner.RunBesideSourcesAsync("convert:pdf", inputs, folder =>
            { asked.Add(folder); return Task.FromResult<string?>(folder); }, progress, default))!;
            check(output.Count == 2 && asked.SequenceEqual([pictures, documents])
                && Path.GetDirectoryName(output[0]) == pictures && Path.GetDirectoryName(output[1]) == documents
                && output[1] != pdf && SHA256.HashData(File.ReadAllBytes(output[1])).SequenceEqual(hashes[pdf]),
                "Mixed outputs stay beside their sources; already-PDF input gets a fresh copy; unrelated folders are not prompted");
            check(runner.Note?.Contains("2 of 3") == true, "Partial batch completion reports the actual source-file count");
            output = await runner.RunAsync("convert:webp", inputs, Path.Combine(work, "partial-webp"), progress, default);
            check(output.Count == 1 && new MagickImageInfo(output[0]).Format == MagickFormat.WebP && runner.Note?.Contains("1 of 3") == true,
                "A partial image target handles only the compatible file and reports the skipped scope");

            var broken = Path.Combine(documents, "broken.pdf");
            File.WriteAllText(broken, "This is not a PDF.");
            var failedDirectory = Directory.CreateDirectory(Path.Combine(work, "failed")).FullName;
            var failed = false;
            try { await runner.RunAsync("convert:jpg", [png, broken], failedDirectory, progress, default); }
            catch (Exception) { failed = true; }
            check(failed && !Directory.EnumerateFileSystemEntries(failedDirectory).Any(),
                "Failure in the later PDF engine rolls back outputs from the earlier image engine");
            check(hashes.All(p => SHA256.HashData(File.ReadAllBytes(p.Key)).SequenceEqual(p.Value)),
                "Common and partial mixed batches preserve every original, including unsupported inputs");

        return inputs;
        }).GetAwaiter().GetResult();

        var window = new WheelWindow("", []);
        window.Wheel.Sides = WheelFlow.Sides(inputs);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var wheel = window.Wheel;
        var group = wheel.Items.ToList().FindIndex(i => i.Group?.Any(c => c.Id == "convert:webp") == true);
        wheel.OpenGroup(group);
        var child = wheel.SubItems.ToList().FindIndex(i => i.Id == "convert:webp");
        wheel.Focus();
        for (var i = 0; i <= child; i++) window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        check(wheel.Description == "WEBP · 1 of 3 files" && wheel.SubItems[child].Title == "WEBP",
            "The mixed-selection wheel keeps a short caption and displays the source-file scope before choosing");
        smoke(window.CaptureRenderedFrame(), "wheel-mixed-partial-scope");
        window.Close();

        var notifications = new List<string>();
        var flow = new WheelFlow(new Toolchain(Path.Combine(root, "mixed-ui-no-tools"), ""), null, notifications.Add, () => Task.CompletedTask);
        flow.ShowFormats(inputs);
        Dispatcher.UIThread.RunJobs();
        EditorChecks.Choose(flow.LastWheel!, "convert:webp");
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!flow.Current.IsCompleted && DateTime.UtcNow < until)
        { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
        Dispatcher.UIThread.RunJobs();
        check(flow.Current.IsCompleted && notifications.Count == 0
            && File.Exists(Path.ChangeExtension(inputs[0], "webp")),
            "Choosing a partial target through the actual wheel exports beside the compatible source");
        check(SuccessToast.Current?.FindControl<TextBlock>("DetailText")?.Text?.Contains("WEBP · 1 of 3 files") == true,
            "Partial wheel completion retains the visible source-file scope");
        SuccessToast.Current?.Close();
    }
}
