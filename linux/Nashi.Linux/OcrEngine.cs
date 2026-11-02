using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ImageMagick;
using Nashi.Core;

namespace Nashi.Linux;

public sealed record OcrOptions(string Languages = "eng", int Segmentation = 3, string Pages = "", string Password = "", bool KeepExistingText = true);
public sealed record OcrResult(string Input, string Text, int Pages)
{
    public IReadOnlyDictionary<int, string> PageText { get; init; } = new Dictionary<int, string>();
}
public sealed record OcrStatus(string Version, IReadOnlyList<string> Languages);
public sealed record OcrWord(string Text, double Confidence, int Left, int Top, int Width, int Height);

/// Local Tesseract CLI, one bounded page at a time. No cloud service, shell interpolation or source modification.
/// OCR cannot run here: Tesseract or the chosen language pack is not installed. Only this lets a conversion skip OCR.
public sealed class OcrUnavailableException(string message) : InvalidOperationException(message);

public sealed class OcrEngine(string? executable, string? tessdata = null)
{
    const int TextLimit = 1_000_000, PageLimit = 200;
    public static bool Supports(string file) => !Directory.Exists(file) && (FeatureCatalog.IsImage(file) || FeatureCatalog.Extension(file) == "pdf");
    public static OcrEngine System() => new(FindExecutable(Environment.GetEnvironmentVariable("PATH")));
    internal static string? FindExecutable(string? searchPath)
    {
        foreach (var folder in (searchPath ?? "").Split(Path.PathSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Path.IsPathFullyQualified(folder)) continue;
            var candidate = Path.Combine(folder, "tesseract");
            if (ToolInstallation.UsableFile(candidate) && ToolInstallation.CanExecute(candidate)) return candidate;
        }
        return null;
    }
    IEnumerable<string> DataArguments => tessdata is null ? [] : new[] { "--tessdata-dir", tessdata };

    async Task<ProcessResult> RunAsync(IEnumerable<string> arguments, CancellationToken token)
    {
        if (executable is null) throw new OcrUnavailableException("Install Tesseract and its English/Turkish language packs, then check again.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try { return await ProcessRunner.RunAsync(executable, arguments, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("OCR timed out. Try fewer pages or a smaller image."); }
    }

    public async Task<OcrStatus> ProbeAsync(CancellationToken token = default)
    {
        var result = await RunAsync(["--version"], token).ConfigureAwait(false);
        var version = (result.Output + "\n" + result.Error).Split('\n').FirstOrDefault(line => line.StartsWith("tesseract ", StringComparison.OrdinalIgnoreCase)) ?? "";
        var match = Regex.Match(version, @"tesseract (\d+)", RegexOptions.IgnoreCase);
        if (!match.Success || int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) < 4)
            throw new NotSupportedException("Use Tesseract 5 or a compatible Tesseract 4 LSTM installation.");
        var languages = await RunAsync(new[] { "--list-langs" }.Concat(DataArguments), token).ConfigureAwait(false);
        return new(version.Trim(), languages.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => Regex.IsMatch(line, @"^[a-zA-Z0-9_/]+$")).Distinct().ToArray());
    }

    internal static int[] Pages(string value, int count)
    {
        if (string.IsNullOrWhiteSpace(value)) return Enumerable.Range(1, count).ToArray();
        var pages = new SortedSet<int>();
        foreach (var part in value.Split(','))
        {
            var range = part.Trim().Split('-');
            if (range.Length is < 1 or > 2 || !int.TryParse(range[0], out var first)
                || !int.TryParse(range[^1], out var last) || first < 1 || last < first || last > count)
                throw new ArgumentException("Enter valid PDF pages, for example 1-3, 5.");
            for (var page = first; page <= last; page++) pages.Add(page);
        }
        return pages.ToArray();
    }

    public async Task<IReadOnlyList<OcrResult>> RecognizeAsync(IReadOnlyList<string> inputs, OcrOptions options,
        IProgress<OperationProgress>? progress = null, CancellationToken token = default)
    {
        if (inputs.Count is < 1 or > 100 || inputs.Any(f => !Supports(f))) throw new ArgumentException("Choose images or PDFs for text recognition.");
        if (!ValidLanguages(options.Languages) || options.Segmentation is not (3 or 6 or 11))
            throw new ArgumentException("Choose a supported OCR language and layout.");
        if (inputs.Any(file => new FileInfo(file).Length > 200_000_000)) throw new InvalidDataException("Choose OCR files smaller than 200 MB.");
        var status = await ProbeAsync(token).ConfigureAwait(false);
        if (options.Languages.Split('+').Any(language => !status.Languages.Contains(language)))
            throw new OcrUnavailableException("The selected OCR language pack is missing. Install it and check again.");
        var temp = Directory.CreateTempSubdirectory("nashi-ocr-");
        try
        {
            var results = new List<OcrResult>();
            var pageTotal = 0;
            foreach (var file in inputs)
            {
                token.ThrowIfCancellationRequested();
                var text = new StringBuilder();
                var pageText = new Dictionary<int, string>();
                var currentPage = 1;
                var count = 0;
                void Append(string value)
                {
                    if (text.Length + value.Length > TextLimit) throw new InvalidDataException("OCR text exceeds the preview limit. Choose fewer pages.");
                    if (text.Length > 0) text.Append("\n\n");
                    text.Append(value.Trim());
                    pageText[currentPage] = value.Trim();
                }
                async Task RecognizePage(byte[] image)
                {
                    var source = Path.Combine(temp.FullName, "page.png");
                    var output = Path.Combine(temp.FullName, "recognized");
                    await File.WriteAllBytesAsync(source, image, token).ConfigureAwait(false);
                    File.Delete(output + ".txt");
                    await RunAsync(new[] { source, output, "--oem", "1", "-l", options.Languages, "--psm",
                        options.Segmentation.ToString(CultureInfo.InvariantCulture), "--dpi", "300" }.Concat(DataArguments).Append("txt"), token).ConfigureAwait(false);
                    if (!File.Exists(output + ".txt") || new FileInfo(output + ".txt").Length > 4_000_000)
                        throw new InvalidDataException("OCR did not return a supported text result.");
                    Append(await File.ReadAllTextAsync(output + ".txt", token).ConfigureAwait(false));
                }
                if (FeatureCatalog.Extension(file) == "pdf")
                {
                    using var pdf = UglyToad.PdfPig.PdfDocument.Open(file, new UglyToad.PdfPig.ParsingOptions { Password = options.Password });
                    var pages = Pages(options.Pages, pdf.NumberOfPages);
                    if (pageTotal + pages.Length > PageLimit) throw new InvalidDataException("Choose at most 200 pages per OCR run.");
                    PdfRasterDocument? raster = null;
                    try
                    {
                        foreach (var index in pages)
                        {
                            currentPage = index;
                            token.ThrowIfCancellationRequested();
                            progress?.Report(new(null, $"{Path.GetFileName(file)} · {index}/{pdf.NumberOfPages}"));
                            var page = pdf.GetPage(index);
                            if (options.KeepExistingText && !string.IsNullOrWhiteSpace(page.Text)) Append(page.Text);
                            else
                            {
                                if (page.Width * page.Height * Math.Pow(300 / 72d, 2) > 25_000_000)
                                    throw new InvalidDataException("OCR pages must be smaller than 25 MP at 300 DPI.");
                                raster ??= PdfRasterDocument.Open(file, options.Password);
                                await RecognizePage(raster.RenderPage(index - 1, 300, token)).ConfigureAwait(false);
                            }
                            count++;
                        }
                    }
                    finally { raster?.Dispose(); }
                }
                else
                {
                    if (pageTotal >= PageLimit) throw new InvalidDataException("Choose at most 200 pages per OCR run.");
                    using var image = PhotoCodec.Read(file);
                    if ((ulong)image.Width * image.Height > 25_000_000) throw new InvalidDataException("Choose OCR images smaller than 25 MP.");
                    image.BackgroundColor = MagickColors.White;
                    image.Alpha(AlphaAction.Remove);
                    progress?.Report(new(null, Path.GetFileName(file)));
                    await RecognizePage(image.ToByteArray(MagickFormat.Png24)).ConfigureAwait(false);
                    count = 1;
                }
                pageTotal += count;
                results.Add(new(file, text.ToString(), count) { PageText = pageText });
            }
            return results;
        }
        finally { temp.Delete(recursive: true); }
    }

    static bool ValidLanguages(string value) => value.Length <= 200 && value.Split('+').Length <= 6
        && Regex.IsMatch(value, @"^[A-Za-z0-9_]+(?:/[A-Za-z0-9_]+)?(?:\+[A-Za-z0-9_]+(?:/[A-Za-z0-9_]+)?)*$");

    public async Task<IReadOnlyList<OcrWord>> FindWordsAsync(string file, string languages, CancellationToken token = default)
    {
        if (!ValidLanguages(languages) || !FeatureCatalog.IsImage(file)) throw new ArgumentException("Choose an image and installed OCR languages.");
        var status = await ProbeAsync(token).ConfigureAwait(false);
        if (languages.Split('+').Any(l => !status.Languages.Contains(l))) throw new OcrUnavailableException("The selected OCR language pack is missing.");
        var temp = Directory.CreateTempSubdirectory("nashi-ocr-words-");
        try
        {
            using var image = PhotoCodec.Read(file);
            if ((ulong)image.Width * image.Height > 25_000_000) throw new InvalidDataException("Find text in images smaller than 25 MP.");
            image.BackgroundColor = MagickColors.White; image.Alpha(AlphaAction.Remove);
            var input = Path.Combine(temp.FullName, "image.png"); image.Write(input, MagickFormat.Png24);
            var result = await RunAsync(new[] { input, "stdout", "--oem", "1", "-l", languages, "--psm", "11" }.Concat(DataArguments).Append("tsv"), token).ConfigureAwait(false);
            return ParseWords(result.Output, (int)image.Width, (int)image.Height, token);
        }
        finally { temp.Delete(true); }
    }

    internal static IReadOnlyList<OcrWord> ParseWords(string tsv, int width, int height, CancellationToken token = default)
    {
        if (tsv.Length > 8_000_000) throw new InvalidDataException("Too many detected words. Use a smaller image.");
        var words = new List<OcrWord>();
        foreach (var line in tsv.Split('\n'))
        {
            token.ThrowIfCancellationRequested();
            var columns = line.TrimEnd('\r').Split('\t', 12);
            if (columns.Length != 12 || columns[0] != "5" || string.IsNullOrWhiteSpace(columns[11])) continue;
            if (!double.TryParse(columns[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence) || !double.IsFinite(confidence) || confidence is < 30 or > 100) continue;
            if (!int.TryParse(columns[6], NumberStyles.None, CultureInfo.InvariantCulture, out var left) || !int.TryParse(columns[7], NumberStyles.None, CultureInfo.InvariantCulture, out var top)
                || !int.TryParse(columns[8], NumberStyles.None, CultureInfo.InvariantCulture, out var w) || !int.TryParse(columns[9], NumberStyles.None, CultureInfo.InvariantCulture, out var h) || w < 1 || h < 1 || left >= width || top >= height) continue;
            var right = (int)Math.Min(width, (long)left + w + 2); var bottom = (int)Math.Min(height, (long)top + h + 2);
            left = Math.Max(0, left - 2); top = Math.Max(0, top - 2);
            words.Add(new(columns[11], confidence, left, top, right - left, bottom - top));
            if (words.Count > 10000) throw new InvalidDataException("Too many detected words. Use a smaller image.");
        }
        return words;
    }

    /// Existing text never needs Tesseract. Only pages without a text layer are sent to local OCR.
    public async Task<IReadOnlyDictionary<int, string>> PdfTextAsync(string input, string password, string languages,
        IProgress<OperationProgress>? progress, CancellationToken token)
    {
        var pages = new Dictionary<int, string>();
        using (var pdf = UglyToad.PdfPig.PdfDocument.Open(input, new() { Password = password }))
        {
            foreach (var page in pdf.GetPages())
            {
                token.ThrowIfCancellationRequested();
                pages[page.Number] = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);
            }
        }
        var missing = pages.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToArray();
        if (missing.Length > 0)
        {
            try
            {
                var recognized = await RecognizeAsync([input], new(languages, Pages: string.Join(',', missing), Password: password), progress, token).ConfigureAwait(false);
                foreach (var page in recognized.Single().PageText) pages[page.Key] = page.Value;
            }
            // Without Tesseract or the language pack, OCR only fills gaps: keep the digital text and leave these pages blank.
            // A PDF with no text at all still reports it; any other OCR failure (timeouts, limits, bad output) reaches the caller.
            catch (OcrUnavailableException) when (missing.Length < pages.Count) { }
        }
        return pages;
    }

    /// Export reviewed text, one UTF-8 copy per source, in a single transaction.
    public static IReadOnlyList<string> Save(IReadOnlyList<OcrResult> results, IReadOnlyList<string> files, string destination, CancellationToken token)
    {
        using var tx = new ExportTransaction(destination);
        var outputs = new List<string>();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var result = results.Single(r => r.Input == file);
            var output = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(file) + " - Text.txt");
            File.WriteAllText(output, result.Text, new UTF8Encoding(false));
            outputs.Add(output);
        }
        return tx.Publish(outputs, token);
    }
}
