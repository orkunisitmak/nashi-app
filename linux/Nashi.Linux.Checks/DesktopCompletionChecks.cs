using System.IO.Compression;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;

static class DesktopCompletionChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Avalonia.Media.Imaging.WriteableBitmap?, string> smoke)
    {
        var work = Directory.CreateDirectory(Path.Combine(root, "desktop completion")).FullName;
        var data = AppPaths.DataOverride;
        AppPaths.DataOverride = Path.Combine(work, "data"); Settings.Reload();
        try
        {
            var image = Path.Combine(work, "source.png");
            using (var pixels = new MagickImage(MagickColors.SkyBlue, 40, 30)) pixels.Write(image);
            var folder = Directory.CreateDirectory(Path.Combine(work, "folder.png")).FullName;
            File.WriteAllText(Path.Combine(folder, "note.txt"), "Folder contents");
            Directory.CreateDirectory(Path.Combine(folder, "empty"));
            var destination = Directory.CreateDirectory(Path.Combine(work, "target")).FullName;
            var selection = new Selection(); selection.Add([folder + "/", image]);
            check(selection.Files.Count == 2 && !Capabilities.Formats([folder]).Any() && !Capabilities.Tools([folder]).Any()
                && Capabilities.ConversionInputs([folder, image], "jpg").SequenceEqual([image]), "Folders are accepted without being mistaken for image formats");
            var runner = new OperationRunner(new Toolchain(work, ""));
            var zip = Task.Run(() => runner.RunAsync("quick:zip", [folder, image], destination, new Progress<OperationProgress>(), default)).GetAwaiter().GetResult().Single();
            using (var archive = ZipFile.OpenRead(zip)) check(archive.GetEntry("folder.png/note.txt") is not null && archive.GetEntry("folder.png/empty/") is not null && archive.GetEntry("source.png") is not null, "ZIP includes selected folders, empty subfolders and files");
            var copied = FileActions.CopyTo([folder], destination).Single();
            check(File.ReadAllText(Path.Combine(copied, "note.txt")) == "Folder contents" && Directory.Exists(Path.Combine(copied, "empty")), "Copy recursively preserves folder contents");
            var refused = false;
            try { FileActions.CopyTo([folder], folder); } catch (IOException) { refused = true; }
            check(refused && Directory.GetDirectories(folder, ".nashi-copy-*").Length == 0, "Copy into the source folder is rejected before staging");
            var linked = Directory.CreateDirectory(Path.Combine(work, "linked")).FullName;
            File.CreateSymbolicLink(Path.Combine(linked, "link.txt"), image);
            refused = false;
            try { FileActions.CopyTo([linked], destination); } catch (InvalidDataException) { refused = true; }
            check(refused && !Directory.Exists(Path.Combine(destination, "linked")), "Folder copy rejects links without publishing a partial copy");
            var readOnly = Directory.CreateDirectory(Path.Combine(work, "read-only")).FullName;
            File.WriteAllText(Path.Combine(readOnly, "kept.txt"), "safe");
            File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            refused = false;
            try { FileActions.CopyTo([readOnly, Path.Combine(work, "missing.txt")], destination); } catch (IOException) { refused = true; }
            finally { File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            check(refused && !Directory.Exists(Path.Combine(destination, "read-only")) && File.ReadAllText(Path.Combine(readOnly, "kept.txt")) == "safe", "Copy failure rolls back read-only copied folders without changing the source");
            var moved = FileActions.MoveTo([copied], work);
            check(moved.Files.Count == 1 && Directory.Exists(moved.Files[0]) && !Directory.Exists(copied), "Move supports folders without replacing existing names");

            // Case-sensitive Library paths come with the same shared-engine Linux fix as ':' names; until then use two distinct names.
            var upper = Path.Combine(work, "Case.txt"); var lower = Path.Combine(work, EditorChecks.PosixNames ? "case.txt" : "case 2.txt");
            File.WriteAllText(upper, "Upper"); File.WriteAllText(lower, "Lower"); History.Record("Case-sensitive task", [upper, lower]);
            check(History.Open().Entries[0].Paths.Length == 2, "Linux history keeps differently cased file names");
            var onUi = false;
            Action historyChanged = () => onUi = Dispatcher.UIThread.CheckAccess();
            History.Changed += historyChanged;
            Task.Run(() => History.Record("Background task", [image])).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs(); History.Changed -= historyChanged;
            check(onUi, "Background history updates are delivered on the UI thread");

            var imports = ClipboardImport.SaveText("İstanbul\nPasted text");
            check(File.ReadAllText(imports) == "İstanbul\nPasted text" && File.GetUnixFileMode(imports) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "Clipboard text imports persist as private UTF-8 files");
            var cliOut = new StringWriter(); var cliError = new StringWriter();
            var exit = Task.Run(() => Cli.RunAsync(["convert", image, "--to", "jpg", "--output", destination], cliOut, cliError)).GetAwaiter().GetResult();
            var jpg = cliOut.ToString().Trim();
            check(exit == 0 && File.Exists(jpg) && new MagickImageInfo(jpg).Width == 40 && File.Exists(image), "Headless CLI performs a real conversion and keeps the source");
            cliOut = new(); cliError = new();
            exit = Task.Run(() => Cli.RunAsync(["convert", image, "--to", "jpg", "--quality", "NaN"], cliOut, cliError)).GetAwaiter().GetResult();
            check(exit == 1 && cliError.ToString().Contains("integer"), "CLI rejects invalid numeric defaults");
            var invalidPdf = Path.Combine(work, "invalid.pdf"); File.WriteAllText(invalidPdf, "Not a PDF");
            cliOut = new(); cliError = new();
            exit = Task.Run(() => Cli.RunAsync(["convert", invalidPdf, "--to", "txt", "--output", destination], cliOut, cliError)).GetAwaiter().GetResult();
            check(exit == 1 && cliError.ToString().Length > 0 && File.ReadAllText(invalidPdf) == "Not a PDF", "Malformed inputs produce a CLI error without a crash or source changes");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                exit = Task.Run(() => Cli.RunAsync(["convert", image, "--to", "webp", "--output", destination], new StringWriter(), new StringWriter(), cancellation.Token)).GetAwaiter().GetResult();
                check(exit == 130 && !File.Exists(Path.Combine(destination, "source.webp")), "CLI cancellation has exit 130 and publishes no output");
            }
            var pdf = Path.Combine(work, "digital.pdf"); File.WriteAllBytes(pdf, DocumentChecks.Pdf("Digital PDF text"));
            var noOcr = new OcrEngine(null);
            var digital = Task.Run(() => noOcr.PdfTextAsync(pdf, "", "eng", null, default)).GetAwaiter().GetResult();
            check(digital[1].Contains("Digital PDF text"), "Digital PDF conversion needs no OCR executable");
            var profileRunner = new OperationRunner(new Toolchain(work, "")) { Profile = _ => new(Dpi: 72) };
            var rendered = Task.Run(() => profileRunner.RunAsync("convert:png", [pdf], destination, new Progress<OperationProgress>(), default)).GetAwaiter().GetResult();
            check(new MagickImageInfo(rendered.Single()).Width == 612, "Saved PDF DPI defaults reach the real renderer");
            profileRunner.Profile = _ => new(Quality: .2, Dpi: 72);
            var jpeg = Task.Run(() => profileRunner.RunAsync("convert:jpg", [pdf], destination, new Progress<OperationProgress>(), default)).GetAwaiter().GetResult();
            using (var encoded = new MagickImage(jpeg.Single())) check(encoded.Quality is > 0 and <= 25, "PDF to JPG honors the format's image quality setting");

            var fake = Path.Combine(work, "tesseract");
            File.WriteAllText(fake, "#!/bin/sh\ncase \"$1\" in\n--version) echo 'tesseract 5.3.0'; exit 0;;\n--list-langs) printf 'eng\\ntur\\n'; exit 0;;\nesac\ncase \"$*\" in\n*tsv*) printf '5\\t1\\t1\\t1\\t1\\t1\\t2\\t4\\t20\\t12\\t95\\tSecret\\n';;\n*) printf 'Recognized scanned page' > \"$2.txt\";;\nesac\n");
            ToolInstallation.MakeExecutable(fake);
            var scanned = ImageOperations.Convert([image], "pdf", work, null, default).Single();
            var docs = new DocumentOperations(null, null) { Ocr = new(fake) };
            var extracted = Task.Run(() => docs.ConvertAsync([scanned], "txt", destination, null, default)).GetAwaiter().GetResult();
            check(File.ReadAllText(extracted.Single()) == "Recognized scanned page", "Scanned PDF to TXT automatically invokes local OCR");
            if (AppPaths.Office() is { } office)
            {
                var docx = Task.Run(() => new DocumentOperations(office, null) { Ocr = new(fake) }.ConvertAsync([scanned], "docx", destination, null, default)).GetAwaiter().GetResult().Single();
                using var archive = ZipFile.OpenRead(docx);
                using var text = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
                check(text.ReadToEnd().Contains("Recognized scanned page"), "Scanned PDF to DOCX contains editable recognized text");
                var tableRefused = false;
                try { Task.Run(() => new DocumentOperations(office, null).ConvertAsync([scanned], "xlsx", destination, null, default)).GetAwaiter().GetResult(); }
                catch (InvalidDataException error) { tableRefused = error.Message.Contains("table"); }
                check(tableRefused, "Scanned PDF to spreadsheet reports its limitation instead of saving an empty workbook");
            }
            var words = Task.Run(() => new OcrEngine(fake).FindWordsAsync(image, "eng")).GetAwaiter().GetResult();
            check(words.Count == 1 && words[0].Text == "Secret" && words[0].Left == 0 && words[0].Width == 24, "Tesseract TSV returns padded, bounded word regions");
            var invalid = "5\t1\t1\t1\t1\t1\t2\t4\t20\t12\tNaN\tbad\n5\t1\t1\t1\t1\t1\t2\t4\t20\t12\t5\tuncertain";
            check(OcrEngine.ParseWords(invalid, 40, 30).Count == 0, "Text detection rejects invalid and low-confidence boxes");
            using (var preview = PhotoCodec.Read(image))
            {
                var redact = new Nashi.Linux.Editors.RedactEditor([image]) { Source = new(image, preview, 1, 40, 30) };
                var pending = redact.FindTextAsync(new(fake), "secret", "eng");
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!pending.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
                if (!pending.IsCompleted) throw new TimeoutException("Redaction detection did not complete.");
                pending.GetAwaiter().GetResult();
                check(redact.Areas.Count == 1 && redact.CanSave, "Find text adds reviewable regions to the redaction editor");
                var redacted = Task.Run(() => redact.Job()([image], destination, new Progress<OperationProgress>(), default)).GetAwaiter().GetResult().Single();
                using var result = PhotoCodec.Read(redacted); using var pixels = result.GetPixels();
                check(pixels.GetPixel(6, 8).ToColor()!.Equals(MagickColors.Black) && new MagickImageInfo(image).Width == 40, "Detected text regions are covered in the exported copy");
                redact.Dispose();
            }

            var language = Settings.Current.Language;
            Settings.Save(Settings.Current with { OutputMode = "folder", OutputFolder = destination, Appearance = "light", Language = "tr" });
            new PreferencesStore(AppPaths.Data).SetProfile("jpg", new(Quality: .42, Dpi: 200));
            var settings = new SettingsWindow(); settings.Show(); Dispatcher.UIThread.RunJobs();
            smoke(settings.CaptureRenderedFrame(), "settings-general");
            var tabs = settings.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            var quality = settings.GetVisualDescendants().OfType<NumericUpDown>().Single(n => AutomationProperties.GetName(n) == "Image / media quality (%)");
            quality.Value = 55;
            smoke(settings.CaptureRenderedFrame(), "settings-conversions");
            tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            settings.GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem = "ask";
            smoke(settings.CaptureRenderedFrame(), "settings-output");
            Settings.Update(s => s with { Shelf = [image] });
            check(settings.SaveChanges() && Settings.Current.OutputMode == "ask" && Settings.Current.Shelf.SequenceEqual([image]) && new PreferencesStore(AppPaths.Data).Profile("jpg") is { Dpi: 200, Quality: .55 }, "Settings saves actual UI changes and defaults while preserving concurrent Float changes");
            check(Settings.Current.Language == "tr", "Saving Settings keeps the chosen interface language");
            Settings.Update(s => s with { Language = language });
            settings.Close();
            IReadOnlyList<string> reused = [];
            var library = new LibraryWindow(paths => reused = paths); library.Show(); Dispatcher.UIThread.RunJobs();
            library.SearchBox.Text = "Case-sensitive task"; Dispatcher.UIThread.RunJobs();
            smoke(library.CaptureRenderedFrame(), "library-search");
            var use = library.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Use files"));
            use.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(reused.SequenceEqual([upper, lower]), "Library search and Use files restore the exact output selection");
            library.Close();
            File.Delete(upper);
            check(History.Open().Search("Case-sensitive task").Single().AvailablePaths.SequenceEqual([lower]), "Library retains history while excluding missing files from reuse");
            IReadOnlyList<string> downloaded = [];
            var web = new WebWindow(new Toolchain(work, ""), paths => downloaded = paths, "https://example.com/photo.png"); web.Show(); Dispatcher.UIThread.RunJobs();
            smoke(web.CaptureRenderedFrame(), "download-link");
            check(web.Link.Text == "https://example.com/photo.png" && !web.Busy, "Pasted links populate the download screen without fetching automatically");
            Button WebButton(string text) => web.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, text));
            web.Fetch = (_, _, _, _, _) => Task.FromResult<IReadOnlyList<string>>([image]);
            WebButton("Download").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            check(!web.Busy && History.Open().Entries[0] is { Action: "Download" } && WebButton("Use downloaded files").IsEnabled, "Download success records Library history and enables reuse");
            WebButton("Use downloaded files").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(downloaded.SequenceEqual([image]) && !web.IsVisible, "Downloaded files return to Home selection");
            web = new WebWindow(new Toolchain(work, ""), _ => { }, "https://example.com/photo.png"); web.Show(); Dispatcher.UIThread.RunJobs();
            web.Fetch = async (_, _, _, _, token) => { await Task.Delay(Timeout.Infinite, token); return []; };
            WebButton("Download").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(web.Busy && !WebButton("Download").IsEnabled, "A running download prevents duplicate starts");
            WebButton("Cancel download").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var wait = DateTime.UtcNow.AddSeconds(5);
            while (web.Busy && DateTime.UtcNow < wait) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            check(!web.Busy && WebButton("Download").IsEnabled && !WebButton("Use downloaded files").IsEnabled, "Cancellation restores download controls without exposing partial results"); web.Close();
        }
        finally { AppPaths.DataOverride = data; Settings.Reload(); SettingsWindow.ApplyAppearance("light"); }
    }
}
