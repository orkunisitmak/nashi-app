using System.Globalization;
using System.Text.Json;
using Nashi.Core;

namespace Nashi.Linux;

/// File processing without starting Avalonia, the singleton or desktop integration.
public static class Cli
{
    private static readonly string[] Commands = ["help", "--help", "doctor", "list", "convert", "tool", "ocr", "qr", "download"];
    public static bool Handles(string[] args) => args.Length > 0 && Commands.Contains(args[0]);
    private static readonly string[] DirectTools = ["createPDF", "mergePDF", "extractArchive", "compress", "muteVideo", "normalizeAudio"];
    public const string Help = """
Nashi for Linux

  nashi list FILE...                 List contextual formats and tools
  nashi doctor                       Show local processing dependencies
  nashi convert FILE... --to FORMAT [--output FOLDER] [--quality 10..100] [--dpi 36..600]
  nashi tool ID FILE... [--output FOLDER]
  nashi ocr FILE... [--language LANG[+LANG]] [--pages 1-3,5] [--password SECRET] [--output FOLDER]
  nashi qr FILE...                    Print detected codes as JSON
  nashi download URL --kind video|audio|image|photos|thumbnail [--output FOLDER]

Files are saved beside each source by default. Existing files are never overwritten.
Use -- before filenames starting with '-'. Ctrl+C cancels with exit code 130.
CLI tools: createPDF, mergePDF, extractArchive, compress, muteVideo, normalizeAudio,
  quick:zip, quick:rotate, quick:rotateLeft, quick:flip, quick:flipVertical,
  quick:grayscale, quick:removeBackground. Other tools use the graphical editor.
""";

    internal sealed record Request(string Command, string[] Files, Dictionary<string, string> Options, string? Tool);
    internal static Request Parse(string[] args)
    {
        if (!Handles(args)) throw new ArgumentException("Unknown command. Run nashi --help.");
        var command = args[0];
        var start = command == "tool" ? 2 : 1;
        var tool = command == "tool" ? args.ElementAtOrDefault(1) : null;
        if (command == "tool" && string.IsNullOrWhiteSpace(tool)) throw new ArgumentException("Choose a tool ID. Run nashi --help.");
        var allowed = command switch
        {
            "convert" => new[] { "--to", "--output", "--quality", "--dpi" },
            "ocr" => ["--output", "--language", "--pages", "--password"],
            "tool" => ["--output"], "download" => ["--output", "--kind"], _ => [],
        };
        var options = new Dictionary<string, string>();
        var files = new List<string>();
        var positional = false;
        for (var i = start; i < args.Length; i++)
        {
            var value = args[i];
            if (!positional && value == "--") { positional = true; continue; }
            if (!positional && value.StartsWith('-'))
            {
                if (!allowed.Contains(value) || ++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal) || !options.TryAdd(value, args[i]))
                    throw new ArgumentException("Unknown, repeated or incomplete option: " + value);
            }
            else files.Add(command == "download" ? value : Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)));
        }
        if (command is not ("doctor" or "help" or "--help") && files.Count == 0) throw new ArgumentException("Choose at least one input.");
        if (command is "doctor" or "help" or "--help" && files.Count > 0) throw new ArgumentException("This command takes no files.");
        if (command == "convert" && !options.ContainsKey("--to")) throw new ArgumentException("Choose a target with --to FORMAT.");
        return new(command, files.Distinct(StringComparer.Ordinal).ToArray(), options, tool);
    }

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken token = default)
    {
        try
        {
            var request = Parse(args);
            var command = request.Command;
            if (command is "help" or "--help") { await output.WriteLineAsync(Help); return 0; }
            var tools = AppPaths.CreateToolchain();
            if (command == "doctor")
            {
                string? Find(string name) { try { return tools.Executable(name); } catch (FileNotFoundException) { return null; } }
                await output.WriteLineAsync(JsonSerializer.Serialize(new { ffmpeg = Find("ffmpeg"), ffprobe = Find("ffprobe"), deno = Find("deno"), ytDlp = Find("yt-dlp"), libreOffice = AppPaths.Office(), tesseract = OcrEngine.FindExecutable(Environment.GetEnvironmentVariable("PATH")), exifTool = AppPaths.Metadata().Executable }));
                return 0;
            }
            if (command == "download")
            {
                if (request.Files.Length != 1) throw new ArgumentException("Choose one public URL.");
                var downloads = request.Options.GetValueOrDefault("--output", BrowserDrop.DownloadFolder());
                var saved = await new WebDownload(tools).DownloadAsync(request.Files[0], request.Options.GetValueOrDefault("--kind", "video"), downloads, token: token);
                History.Record("Download", saved);
                foreach (var file in saved) await output.WriteLineAsync(file);
                return 0;
            }
            if (request.Files.Any(f => !File.Exists(f) && !Directory.Exists(f))) throw new FileNotFoundException("An input no longer exists.");
            if (command == "list")
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(new { formats = Capabilities.Formats(request.Files), tools = Capabilities.Tools(request.Files), quickTools = Capabilities.QuickTools(request.Files), cliTools = DirectTools.Where(t => Capabilities.Tools(request.Files).Contains(t)) }));
                return 0;
            }
            if (command == "qr")
            {
                var (codes, failures) = await Task.Run(() => Editors.QrReaderWindow.Scan(request.Files, null, token), token);
                await output.WriteLineAsync(JsonSerializer.Serialize(codes));
                foreach (var failure in failures) await error.WriteLineAsync(failure);
                return failures.Count > 0 ? 1 : 0;
            }
            var destination = request.Options.GetValueOrDefault("--output");
            if (destination is not null) destination = Path.GetFullPath(destination);
            var runner = new OperationRunner(tools) { Profile = format => new PreferencesStore(AppPaths.Data).Profile(format) };
            var choice = command == "convert" ? "convert:" + request.Options["--to"].ToLowerInvariant() : request.Tool ?? "recognizeText";
            Editors.EditorJob? job = null;
            if (command == "convert")
            {
                var profile = runner.Profile(choice[8..]);
                if (request.Options.TryGetValue("--quality", out var q)) profile = profile with { Quality = Integer(q, 10, 100) / 100d };
                if (request.Options.TryGetValue("--dpi", out var d)) profile = profile with { Dpi = Integer(d, 36, 600) };
                runner.Profile = _ => profile;
            }
            if (command == "tool" && !(DirectTools.Contains(choice) || choice.StartsWith("quick:", StringComparison.Ordinal)))
                throw new ArgumentException("This tool needs the graphical editor. Run nashi --help for CLI tools.");
            if (command == "ocr")
            {
                var results = await OcrEngine.System().RecognizeAsync(request.Files, new(request.Options.GetValueOrDefault("--language", Settings.Current.OcrLanguage), Pages: request.Options.GetValueOrDefault("--pages", ""), Password: request.Options.GetValueOrDefault("--password", "")), token: token);
                job = (files, folder, _, cancellation) => Task.Run(() => OcrEngine.Save(results, files, folder, cancellation), cancellation);
            }
            var made = await runner.RunBesideSourcesAsync(choice, request.Files, folder => Task.FromResult<string?>(destination ?? folder), new Progress<OperationProgress>(), token, job) ?? [];
            History.Record(choice, made);
            foreach (var file in made) await output.WriteLineAsync(file);
            if (runner.Note is { } note) await error.WriteLineAsync(note);
            if (History.Warning is { } warning) await error.WriteLineAsync(warning);
            return 0;
        }
        catch (OperationCanceledException) { await error.WriteLineAsync("Cancelled. Originals kept."); return 130; }
        catch (Exception failure)
        { await error.WriteLineAsync(failure.Message); return 1; }
    }

    static int Integer(string value, int min, int max) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max
        ? number : throw new ArgumentException("Choose an integer within the supported range.");
}
