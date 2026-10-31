using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;
using Nashi.Linux.Editors;

static partial class EditorChecks
{
    // ---- Metadata

    static void Metadata(string work, string photo)
    {
        var saved = AppPaths.MetadataRoot;
        try
        {
            // Without ExifTool the wheel opens setup instead of the editor.
            check(AppPaths.Metadata().Executable is null && ThrowsMissing(() => ToolEditorWindow.For("removeMetadata", [photo])), "Metadata without ExifTool asks for the metadata tools");
            var setups = 0;
            var flow = new WheelFlow(new Toolchain(Directory.CreateDirectory(Path.Combine(work, "no-tools")).FullName, ""), null, _ => { }, () => { setups++; return Task.CompletedTask; });
            flow.ShowTools([photo]);
            Dispatcher.UIThread.RunJobs();
            var wheel = flow.LastWheel!;
            Choose(wheel, "removeMetadata");
            Dispatcher.UIThread.RunJobs();
            check(setups == 1 && flow.LastEditor is null, "Choosing Metadata without ExifTool opens setup, not an editor");

            // A stand-in ExifTool that answers a read with fixed tags and records every call.
            var fake = Directory.CreateDirectory(Path.Combine(work, "fake-exiftool", "metadata-13.59")).FullName;
            var script = Path.Combine(fake, "exiftool");
            var log = Path.Combine(fake, "calls.log");
            File.WriteAllText(script, "#!/bin/sh\nprintf '%s\\n' \"$*\" >> \"" + log + "\"\n[ \"$1\" = -json ] && printf '[{\"SourceFile\":\"x\",\"EXIF:Artist\":\"Ece\",\"EXIF:Make\":\"Nashi Cam\",\"File:FileSize\":\"12 kB\",\"XMP:Title\":\"Hello\"}]'\nexit 0\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            AppPaths.MetadataRoot = Path.GetDirectoryName(fake);
            var window = Open(new MetadataEditor([photo]));
            var editor = (MetadataEditor)window.Panel;
            var texts = Texts(window);
            check(window.Canvas.GetVisualRoot() is null && texts.Contains("EXIF") && texts.Contains("XMP") && texts.Contains("File") && texts.Contains("Artist") && texts.Contains("12 kB") && editor.Values["EXIF:Artist"] == "Ece", "Metadata lists the tags by group, the editable ones as fields with their values");
            check(Field(window, 0).Watermark == "Search metadata" && MetadataEditor.Editable.All(editor.Values.ContainsKey), "Metadata has a search and a field for every editable EXIF and XMP tag");
            editor.Search("cam");
            check(Texts(window).Contains("Make") && !Texts(window).Contains("Artist"), "Search keeps the matching tags");
            editor.Search("");
            editor.Set("EXIF:Copyright", "Nashi");
            editor.Set("EXIF:Artist", "");
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var dest = Directory.CreateDirectory(Path.Combine(work, "metadata")).FullName;
            var outputs = Run(window.Job!, [photo], dest);
            var call = File.ReadAllLines(log).Last();
            check(outputs.Count == 1 && Path.GetFileName(outputs[0]) == Name + " Metadata.png" && call.Contains("-EXIF:Copyright=Nashi") && call.Contains("-EXIF:Artist=") && !call.Contains("-all="), "Saving writes the edited and cleared tags to a copy beside the source");
            var clean = Open(new MetadataEditor([photo]));
            ((MetadataEditor)clean.Panel).RemoveAll();
            check(Texts(clean).Contains("Every tag will be removed. Encoded pixels, color profile and display orientation are preserved.") && !Texts(clean).Contains("12 kB"), "Remove All clears the list and says what stays");
            clean.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Run(clean.Job!, [photo], Directory.CreateDirectory(Path.Combine(work, "metadata-clean")).FullName);
            check(File.ReadAllLines(log).Last().Contains("-all="), "Remove All writes -all=");
            Renders("metadata", () => new MetadataEditor([photo]));

            // The real ExifTool, when NASHI_VERIFY_METADATA names a tools root that holds it.
            AppPaths.MetadataRoot = Environment.GetEnvironmentVariable("NASHI_VERIFY_METADATA");
            if (string.IsNullOrEmpty(AppPaths.MetadataRoot) || AppPaths.Metadata().Executable is null)
            {
                Console.WriteLine("Metadata checks: ExifTool absent; 1 round trip skipped (set NASHI_VERIFY_METADATA to a tools root with metadata-13.59/exiftool)");
                return;
            }
            var jpg = Quadrants(Path.Combine(work, Name + " exif.jpg"), 64, 48);
            var service = AppPaths.Metadata();
            var tagged = Wait(service.WriteAsync([jpg], Directory.CreateDirectory(Path.Combine(work, "exif-tagged")).FullName, false, new Dictionary<string, string> { ["EXIF:Artist"] = "Ece Tosun" }));
            var back = Wait(service.ReadAsync(tagged[0]));
            var stripped = Wait(service.WriteAsync(tagged, Directory.CreateDirectory(Path.Combine(work, "exif-clean")).FullName, true));
            var gone = Wait(service.ReadAsync(stripped[0]));
            check(back.GetValueOrDefault("EXIF:Artist") == "Ece Tosun" && !gone.ContainsKey("EXIF:Artist") && !Wait(service.ReadAsync(jpg)).ContainsKey("EXIF:Artist"), "ExifTool round trip: a tag is written, read back and removed; the original stays as it was");
            Console.WriteLine("Metadata checks: ExifTool round trip ran");
        }
        finally { AppPaths.MetadataRoot = saved; }
    }

    static bool ThrowsMissing(Action action)
    {
        try { action(); return false; }
        catch (MissingToolsException) { return true; }
    }
}
