using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;

static class OutputLocationChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var downloads = Directory.CreateDirectory(Path.Combine(root, "output-location", "Downloads")).FullName;
        var desktop = Directory.CreateDirectory(Path.Combine(root, "output-location", "Desktop")).FullName;
        var inputs = new[] { Path.Combine(downloads, "photo.png"), Path.Combine(desktop, "photo.png") };
        foreach (var file in inputs)
        {
            using var image = new MagickImage(MagickColors.Blue, 16, 16);
            image.Write(file);
        }
        var notes = new List<string>();
        var flow = new WheelFlow(new Toolchain(Path.Combine(root, "no-output-location-tools"), ""), null, notes.Add, () => Task.CompletedTask);
        var pdfs = inputs.Select(file => ImageOperations.Convert([file], "pdf", Path.GetDirectoryName(file)!, null, default)[0]).ToArray();
        foreach (var (choice, output, selection) in new[] { ("convert:jpg", "photo.jpg", inputs), ("quick:grayscale", "photo - Edited.png", inputs), ("convert:jpg", "photo Page 1.jpg", pdfs) })
        {
            flow.Show(selection, choice.StartsWith("quick:") ? "tools" : "convert");
            var window = flow.LastWheel!;
            Dispatcher.UIThread.RunJobs();
            window.Wheel.HighlightedIndex = window.Wheel.Segments.ToList().FindIndex(i => i.Id == choice);
            window.Wheel.Focus();
            if (window.Wheel.HighlightedIndex < 0)
            {
                var group = window.Wheel.Segments.ToList().FindIndex(i => i.Group?.Any(child => child.Id == choice) == true);
                window.Wheel.HighlightedIndex = group;
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                var sub = window.Wheel.SubItems.ToList().FindIndex(i => i.Id == choice);
                for (var i = 0; i <= sub; i++) window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            }
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!flow.Current.IsCompleted && DateTime.UtcNow < until) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
            Dispatcher.UIThread.RunJobs();
            check(flow.Current.IsCompleted && notes.Count == 0 && File.Exists(Path.Combine(downloads, output)) && File.Exists(Path.Combine(desktop, output)),
                choice + ": Home/background wheel writes each output in its source folder (Downloads and Desktop)");
            var toast = SuccessToast.Current;
            var detail = toast?.FindControl<TextBlock>("DetailText");
            check(detail?.Text == Path.Combine(downloads, output), choice + ": completion shows the actual output path");
            check(detail is not null && ToolTip.GetTip(detail)?.ToString() == detail.Text, choice + ": full output path remains readable in the tooltip");
            toast?.Close();
        }
        check(inputs.All(File.Exists) && !Directory.Exists(Path.Combine(downloads, "Nashi")) && !Directory.Exists(Path.Combine(desktop, "Nashi")),
            "Source files are kept and no Nashi output subfolder is created");
    }
}
