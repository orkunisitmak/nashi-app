using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;
using Nashi.Linux.Editors;

static partial class EditorChecks
{
    // ---- Review round 1: saves are never lost, metadata never leaks, colours and the palette stay right.

    static void Review(string work, string photo)
    {
        var folder = Directory.CreateDirectory(Path.Combine(work, "review")).FullName;

        // One editor at a time, and a Save while a run is busy waits for it instead of vanishing.
        var flow = new WheelFlow(new Toolchain(Directory.CreateDirectory(Path.Combine(folder, "tools")).FullName, ""), null, _ => { }, () => Task.CompletedTask);
        var source = Quadrants(Path.Combine(folder, Name + ".png"), 400, 300);
        flow.ShowTools([source]);
        Dispatcher.UIThread.RunJobs();
        Choose(flow.LastWheel!, "cropImage");
        var first = flow.LastEditor!;
        Wait(first.Loading);
        flow.ShowTools([source]);
        Dispatcher.UIThread.RunJobs();
        Choose(flow.LastWheel!, "editImage");
        check(flow.LastEditor == first && first.IsVisible, "Choosing another editor while one is open brings the open one back");
        var busy = new TaskCompletionSource();
        flow.Current = busy.Task;
        ((CropEditor)first.Panel).Set(new Rect(0, 0, 100, 80));
        first.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        for (var i = 0; i < 20; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        var output = Path.Combine(folder, Name + " - Cropped.png");
        var waiting = flow.LastToast?.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Waiting for the current task to finish…") == true;
        check(!first.IsVisible && !File.Exists(output) && waiting, "A Save while a run is busy waits, with its progress card up");
        busy.SetResult();
        for (var i = 0; i < 300 && !File.Exists(output); i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
        check(File.Exists(output), "When the busy run ends, the waiting Save runs");
        SuccessToast.Current?.Close();

        // Nothing is silent while busy: the wheel still opens, a direct run and an action wait their turn with a card.
        bool Waiting() => flow.LastToast is { IsVisible: true } t && t.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "Waiting for the current task to finish…");
        void Pump(Func<bool> until) { for (var i = 0; i < 300 && !until(); i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); } }
        var hold = new TaskCompletionSource();
        flow.Current = hold.Task;
        var wheelBefore = flow.LastWheel;
        flow.ShowTools([source]);
        Dispatcher.UIThread.RunJobs();
        check(flow.LastWheel != wheelBefore && flow.LastWheel!.IsVisible, "While a run is busy the tools wheel still opens");
        Choose(flow.LastWheel!, "convert");
        Choose(flow.LastWheel!, "convert:jpg");
        Pump(Waiting);
        var jpg = Path.Combine(folder, Name + ".jpg");
        check(Waiting() && !File.Exists(jpg), "A conversion chosen while busy waits, saying so");
        hold.SetResult();
        Pump(() => File.Exists(jpg) && !flow.Busy);
        check(File.Exists(jpg) && !flow.Busy && flow.Current.IsCompleted, "Then it runs, and Busy ends with the queue");
        SuccessToast.Current?.Close();
        var holdAgain = new TaskCompletionSource();
        flow.Current = holdAgain.Task;
        flow.ShowActions([source]);
        Dispatcher.UIThread.RunJobs();
        Choose(flow.LastWheel!, "action:copyPath");
        Pump(Waiting);
        check(Waiting() && SuccessToast.Current is null, "An action chosen while busy waits, saying so");
        holdAgain.SetResult();
        string? Title() => SuccessToast.Current?.FindControl<TextBlock>("TitleText")?.Text;
        Pump(() => Title() == "Path copied.");
        check(Title() == "Path copied." && !flow.Busy, "Then the action runs");
        SuccessToast.Current?.Close();

        // A wait cancelled from its card says so at once but keeps its place: what is queued behind it still waits
        // for the run in front. A is a real Copy held at its folder picker; B a Copy cancelled while waiting; C a conversion.
        var notes = new List<string>();
        var queue = new WheelFlow(new Toolchain(Directory.CreateDirectory(Path.Combine(folder, "queue-tools")).FullName, ""), null, notes.Add, () => Task.CompletedTask);
        var queued = Directory.CreateDirectory(Path.Combine(folder, "queue")).FullName;
        var picked = Directory.CreateDirectory(Path.Combine(folder, "queue-picked")).FullName;
        var queueSource = Quadrants(Path.Combine(queued, Name + ".png"), 40, 30);
        var gate = new TaskCompletionSource<string?>();
        var picks = 0;
        queue.PickFolder = (_, _) => { picks++; return gate.Task; };
        queue.ShowActions([queueSource]);
        Dispatcher.UIThread.RunJobs();
        Choose(queue.LastWheel!, "action:copy");
        Pump(() => picks == 1);
        queue.ShowActions([queueSource]);
        Dispatcher.UIThread.RunJobs();
        Choose(queue.LastWheel!, "action:copy");
        Pump(() => queue.LastToast is { IsVisible: true });
        var cancelB = queue.LastToast!;
        queue.ShowTools([queueSource]);
        Dispatcher.UIThread.RunJobs();
        Choose(queue.LastWheel!, "convert");
        Choose(queue.LastWheel!, "convert:jpg");
        cancelB.FindControl<Button>("ActionButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var queuedJpg = Path.Combine(queued, Name + ".jpg");
        Pump(() => notes.Count > 0);
        for (var i = 0; i < 20; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        check(notes.SequenceEqual(["Cancelled. Originals kept."]) && queue.Busy && !File.Exists(queuedJpg) && picks == 1, "Cancelling a waiting action says so at once; Busy stays on and the conversion behind it still waits for the held run");
        gate.SetResult(picked);
        Pump(() => File.Exists(queuedJpg) && !queue.Busy);
        check(File.Exists(Path.Combine(picked, Name + ".png")) && File.Exists(queuedJpg) && !queue.Busy && picks == 1 && Directory.GetFiles(picked).Length == 1, "Released, the held Copy finishes, the conversion runs after it, and the cancelled Copy wrote nothing");
        SuccessToast.Current?.Close();

        // The colour profile stays on edited copies; their metadata goes.
        var tagged = Path.Combine(folder, Name + " tagged.jpg");
        using (var image = new MagickImage("gradient:red-blue", 64, 48))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(41, 1), new Rational(0, 1), new Rational(0, 1) });
            image.SetProfile(exif);
            image.SetProfile(ColorProfiles.AppleRGB);
            using var file = File.Create(tagged);
            image.Write(file, MagickFormat.Jpeg);
        }
        var cropped = ImageTools.Each([tagged], Directory.CreateDirectory(Path.Combine(folder, "icc")).FullName, " - Cropped", (_, image) => ImageTools.Crop(image, new Rect(0, 0, 32, 24)), null, default);
        using (var back = new MagickImage(cropped[0])) check(back.GetColorProfile() is not null && back.GetExifProfile()?.GetValue(ExifTag.GPSLatitude) is null, "Crop keeps the colour profile and drops GPS");

        // Compress's "the original is smaller" copy is the original's bytes without its metadata.
        var tiny = Path.Combine(folder, "tiny gps.jpg");
        using (var image = new MagickImage("gradient:red-blue", 8, 8))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(41, 1), new Rational(0, 1), new Rational(0, 1) });
            image.SetProfile(exif);
            image.Quality = 5;
            using var file = File.Create(tiny);
            image.Write(file, MagickFormat.Jpeg);
        }
        var small = ImageOperations.Compress([tiny], Directory.CreateDirectory(Path.Combine(folder, "tiny")).FullName, null, default);
        using (var back = new MagickImage(small[0])) check(new FileInfo(small[0]).Length < new FileInfo(tiny).Length && back.GetExifProfile() is null && back.Width == 8, "When the original is smaller, Compress keeps its pixels but not its EXIF or GPS");
        var png = Path.Combine(folder, "text.png");
        using (var image = new MagickImage(MagickColors.Red, 16, 16)) { image.SetAttribute("comment", "secret place"); using var file = File.Create(png); image.Write(file, MagickFormat.Png); }
        var bytes = File.ReadAllBytes(png);
        var clean = ImageTools.StrippedBytes(bytes, "png")!;
        check(System.Text.Encoding.Latin1.GetString(bytes).Contains("secret place") && !System.Text.Encoding.Latin1.GetString(clean).Contains("secret place") && new MagickImage(clean).Width == 16, "A PNG loses its text chunks losslessly");

        // The palette: Fluent's accent is black (light) or white (dark), warnings are plain text, gold is for the
        // primary action and the selection.
        var window = Open(new CompressEditor([source]));
        Color Accent(ThemeVariant theme) => window.TryFindResource("SystemAccentColor", theme, out var v) && v is Color c ? c : default;
        check(Accent(ThemeVariant.Light) == Colors.Black && Accent(ThemeVariant.Dark) == Colors.White, "Fluent's accent is black in light and white in dark, not gold");
        ((CompressEditor)window.Panel).Target(150, "percent");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var warning = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Enter a reduction greater than 0% and less than 100%.");
        check(warning.Foreground is ISolidColorBrush { Color: { R: 0, G: 0, B: 0 } }, "A warning is plain black text in light, not gold");
        window.Close();
        var redact = Open(new RedactEditor([source]));
        check(Texts(redact).Contains("Strength") && ((RedactEditor)redact.Panel).Settings.Strength == 1, "Redact Photo has a Strength slider, strong by default");
        var preview = redact.Panel.Source!.Preview;
        redact.Close();
        for (var i = 0; i < 20; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
        check(Disposed(preview), "Closing an editor frees its preview copy once no render uses it");
    }

    static bool Disposed(MagickImage image)
    {
        try { _ = image.Width; return false; }
        catch (ObjectDisposedException) { return true; }
    }
}
