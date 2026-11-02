using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageMagick;
using ImageMagick.Drawing;
using Nashi.Core;
using Nashi.Linux;
using Nashi.Linux.Editors;

static class OcrChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Avalonia.Media.Imaging.WriteableBitmap?, string> smoke)
    {
        var work = Directory.CreateDirectory(Path.Combine(root, "ocr")).FullName;
        var fake = Path.Combine(work, "tesseract");
        var log = Path.Combine(work, "calls");
        File.WriteAllText(fake, "#!/bin/sh\ncase \"$1\" in\n--version) echo 'tesseract 5.3.0'; exit 0;;\n--list-langs) printf 'List of available languages (2):\\neng\\ntur\\n'; exit 0;;\nesac\nprintf '%s\\n' \"$@\" >> " + Integration.Quote(log) + "\nprintf 'Nashi OCR 2026\\nİstanbul güzel şehir' > \"$2.txt\"\n");
        ToolInstallation.MakeExecutable(fake);
        var png = Path.Combine(work, "-image ğ ' $.png");
        using (var image = new MagickImage(MagickColors.White, 1400, 350))
        {
            new Drawables().Font("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf").FontPointSize(60).FillColor(MagickColors.Black)
                .Text(40, 110, "NASHI OCR 2026").Text(40, 230, "Clear printed text").Draw(image);
            image.Write(png);
        }
        var turkish = Path.Combine(work, "Türkçe.png");
        using (var image = new MagickImage(MagickColors.White, 1500, 300))
        {
            new Drawables().Font("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf").FontPointSize(64).FillColor(MagickColors.Black)
                .Text(40, 150, "İstanbul güzel şehir 2026").Draw(image);
            image.Write(turkish);
        }
        var pdf = ImageOperations.Convert([png], "pdf", work, null, default).Single();
        var digital = Path.Combine(work, "existing.pdf");
        File.WriteAllBytes(digital, DocumentChecks.Pdf("Existing PDF text", "Second page text"));
        var original = new[] { png, turkish, pdf, digital }.ToDictionary(f => f, f => SHA256.HashData(File.ReadAllBytes(f)));
        var engine = new OcrEngine(fake);
        Task.Run(async () =>
        {
            var status = await engine.ProbeAsync();
            check(status.Version == "tesseract 5.3.0" && status.Languages.SequenceEqual(["eng", "tur"]), "OCR checks version and installed language data");
            check(OcrEngine.FindExecutable(work) == fake && OcrEngine.FindExecutable("relative") is null,
                "OCR executable lookup accepts only absolute executable locations");
            check(OcrEngine.Pages("1-2, 2", 2).SequenceEqual([1, 2]) && OcrEngine.Pages("", 2).Length == 2,
                "OCR page ranges are deduplicated, ordered and bounded");
            var rejected = false;
            try { OcrEngine.Pages("0, 4", 2); } catch (ArgumentException) { rejected = true; }
            check(rejected, "OCR rejects out-of-range PDF pages");
            rejected = false;
            try { await engine.RecognizeAsync([png], new("eng;echo bad")); } catch (ArgumentException) { rejected = true; }
            check(rejected && !File.Exists(log), "Unrecognized OCR language arguments fail before any recognition process");
            var results = await engine.RecognizeAsync([png, pdf], new("eng+tur"));
            check(results.Count == 2 && results.All(r => r.Text.Contains("İstanbul") && r.Pages == 1),
                "OCR image and scanned-PDF paths return UTF-8 text and source-page counts");
            check(File.ReadAllText(log).Contains("eng+tur") && File.ReadAllText(log).Contains("--oem\n1"),
                "OCR passes language and LSTM arguments through the CLI");
            File.Delete(log);
            results = await engine.RecognizeAsync([digital], new(Pages: "2"));
            check(results.Single().Text.Contains("Second page text") && !File.Exists(log),
                "Existing PDF text and selected page ranges avoid redundant OCR");
            var output = OcrEngine.Save([new(png, "Reviewed text ğ", 1), new(pdf, "PDF reviewed", 1)], [png, pdf], work, default);
            check(output.Count == 2 && output.Distinct().Count() == 2 && File.ReadAllText(output[0]) == "Reviewed text ğ",
                "Reviewed OCR exports create unique UTF-8 text copies");
            rejected = false;
            try { await new OcrEngine(null).ProbeAsync(); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Missing OCR tools produce an actionable dependency error");
            var gap = Path.Combine(work, "gap.pdf");
            File.WriteAllBytes(gap, DocumentChecks.Pdf("Digital page", ""));
            var kept = await new OcrEngine(null).PdfTextAsync(gap, "", "eng", null, default);
            check(kept[1].Contains("Digital page") && string.IsNullOrWhiteSpace(kept[2]),
                "Without Tesseract, a PDF with one textless page still converts and keeps its digital text");
            var blank = Path.Combine(work, "blank.pdf");
            File.WriteAllBytes(blank, DocumentChecks.Pdf(""));
            rejected = false;
            try { await new OcrEngine(null).PdfTextAsync(blank, "", "eng", null, default); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Without Tesseract, a PDF with no text at all still asks for OCR");

            var slow = Path.Combine(work, "slow-tesseract");
            var marker = Path.Combine(work, "running");
            File.WriteAllText(slow, File.ReadAllText(fake).Replace("printf '%s\\n'", "touch " + Integration.Quote(marker) + "\nsleep 10\nprintf '%s\\n'"));
            ToolInstallation.MakeExecutable(slow);
            using var cancellation = new CancellationTokenSource();
            var running = new OcrEngine(slow).RecognizeAsync([png], new(), token: cancellation.Token);
            for (var i = 0; i < 200 && !File.Exists(marker); i++) await Task.Delay(10);
            var started = File.Exists(marker);
            cancellation.Cancel();
            rejected = false;
            try { await running; } catch (OperationCanceledException) { rejected = true; }
            check(started && rejected, "Cancelling an active OCR process stops recognition");

            var real = Environment.GetEnvironmentVariable("NASHI_VERIFY_TESSERACT");
            if (!string.IsNullOrEmpty(real))
            {
                var actual = new OcrEngine(real, Environment.GetEnvironmentVariable("NASHI_VERIFY_TESSDATA"));
                var text = await actual.RecognizeAsync([png, pdf], new(Segmentation: 6));
                check(text.Count == 2 && text.All(r => r.Text.Contains("NASHI") && r.Text.Contains("2026")),
                    "Real Tesseract reads a printed image and scanned PDF");
                var tr = await actual.RecognizeAsync([turkish], new("tur", 6));
                check(tr.Single().Text.Contains("güzel") && tr.Single().Text.Contains("şehir"),
                    "Real Turkish OCR preserves Turkish letters");
                var words = await actual.FindWordsAsync(png, "eng");
                check(words.Any(w => w.Text.Contains("NASHI")) && words.All(w => w.Left >= 0 && w.Top >= 0 && w.Width > 0 && w.Height > 0 && w.Left + w.Width <= 1400 && w.Top + w.Height <= 350),
                    "Real Tesseract supplies bounded word regions for redaction");
                var automatic = await new DocumentOperations(null, null) { Ocr = actual }.ConvertAsync([pdf], "txt", work, null, default);
                check(File.ReadAllText(automatic.Single()).Contains("NASHI"), "Real scanned-PDF conversion uses automatic local OCR");
                Console.WriteLine("OCR checks: real " + (await actual.ProbeAsync()).Version);
            }
            else Console.WriteLine("OCR checks: real engine skipped (set NASHI_VERIFY_TESSERACT)");
            check(original.All(p => SHA256.HashData(File.ReadAllBytes(p.Key)).SequenceEqual(p.Value)), "OCR and text exports leave every source unchanged");
        }).GetAwaiter().GetResult();

        check(WheelMenu.Tools([png, pdf]).SelectMany(i => i.Group ?? [i]).Any(i => i.Id == "recognizeText")
            && WheelMenu.Tools(["x.mp3"]).SelectMany(i => i.Group ?? [i]).All(i => i.Id != "recognizeText"),
            "OCR appears for images/PDFs in the contextual wheel, without adding media-only actions");
        var missing = new OcrEditor([png], new OcrEngine(null));
        var missingWindow = new ToolEditorWindow(missing);
        missingWindow.Show(); Wait(missingWindow.Loading);
        check(!missing.CanSave && missingWindow.GetVisualDescendants().OfType<TextBox>().Any(t => t.Text?.Contains("sudo apt install tesseract") == true),
            "Missing OCR dependencies show install instructions and disable Save");
        missingWindow.Close(); Dispatcher.UIThread.RunJobs();

        var flow = new WheelFlow(new Toolchain(Path.Combine(work, "none"), ""), null, _ => { }, () => Task.CompletedTask);
        flow.ShowTools([png]);
        Dispatcher.UIThread.RunJobs();
        EditorChecks.Choose(flow.LastWheel!, "recognizeText");
        Dispatcher.UIThread.RunJobs();
        check(flow.LastEditor is { Panel: OcrEditor, Title: { } title } && title.Contains("Recognize Text"),
            "Choosing Read / OCR opens the text-recognition editor through the actual wheel");
        flow.LastEditor?.Close(); Dispatcher.UIThread.RunJobs();

        var panel = new OcrEditor([png, pdf], engine);
        var window = new ToolEditorWindow(panel);
        window.Show(); Wait(window.Loading);
        Wait(panel.RecognizeAsync(new()));
        var transcript = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.AcceptsReturn);
        transcript.Text = "Corrected OCR text ğ";
        Dispatcher.UIThread.RunJobs();
        var resultPicker = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Items.Count == 2);
        resultPicker.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        resultPicker.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        check(transcript.Text == "Corrected OCR text ğ", "Switching result files preserves the user's text edits");
        var copyButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Copy text");
        copyButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var copied = window.Clipboard!.TryGetTextAsync(); Wait(copied);
        check(copied.Result == "Corrected OCR text ğ", "Copy text writes the reviewed result to the isolated headless clipboard");
        var job = panel.Job();
        var saved = Task.Run(() => job([png], Path.Combine(work, "reviewed"), new Progress<OperationProgress>(), default)).GetAwaiter().GetResult();
        check(panel.CanSave && File.ReadAllText(saved.Single()) == "Corrected OCR text ğ",
            "OCR editor exports the user's reviewed text rather than re-running recognition");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
        smoke(window.CaptureRenderedFrame(), "ocr-editor");
        panel.Reset(); Dispatcher.UIThread.RunJobs();
        check(!panel.CanSave && panel.Results.Count == 0, "Reset clears reviewed OCR results");
        window.Close(); Dispatcher.UIThread.RunJobs();
    }

    static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException("OCR UI fixture did not finish.");
        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }
}
