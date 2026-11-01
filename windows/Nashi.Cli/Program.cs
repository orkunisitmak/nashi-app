using System.Globalization;
using System.Text.Json;
using ImageMagick;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Nashi.Cli;

/// <summary>
/// Entry point and command processor for the Nashi Windows command-line interface.
/// </summary>
public static class Program
{
    private static readonly string ToolsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nashi",
        "Tools",
        "v1"
    );

    private static readonly string OfficeToolsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nashi",
        "Tools"
    );

    private static readonly string[] CliSupportedTools =
    [
        "readQRCode", "createPDF", "splitPDF", "mergePDF", "extractArchive"
    ];

    /// <summary>
    /// Executes the Nashi command-line application.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the application.</param>
    /// <returns>Exit code representing operation result (0 for success).</returns>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            return command switch
            {
                "catalog" => RunCatalog(),
                "inspect" => args.Length < 2 ? MissingArgument("inspect <file>") : await RunInspectAsync(args[1]),
                "dependencies" => RunDependencies(),
                "convert" => args.Length < 3 ? MissingArgument("convert <format> <file...> [--output <dir>] [--json]") : await RunConvertAsync(args[1], args[2..]),
                "resize" => args.Length < 3 ? MissingArgument("resize <dimensions> <file...> [--fit crop|contain|stretch] [--quality 100] [--output <dir>] [--options <json>] [--json]") : await RunResizeAsync(args[1], args[2..]),
                "tool" => args.Length < 3 ? MissingArgument("tool <tool-id> <file...> [--output <dir>] [--json]") : await RunToolAsync(args[1], args[2..]),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Nashi CLI Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Prints CLI usage information and available commands to stdout.
    /// </summary>
    private static void PrintUsage()
    {
        Console.WriteLine("Nashi CLI - File toolkit for Windows");
        Console.WriteLine("Parity interface for Nashi.Core\n");
        Console.WriteLine("Usage:");
        Console.WriteLine("  nashi convert <format> <file...> [--output <dir>] [--json]");
        Console.WriteLine("  nashi resize <dimensions> <file...> [--fit crop|contain|stretch] [--quality 100] [--output <dir>] [--options <json>] [--json]");
        Console.WriteLine("  nashi tool <tool-id> <file...> [--output <dir>] [--json]");
        Console.WriteLine("  nashi inspect <file>");
        Console.WriteLine("  nashi catalog");
        Console.WriteLine("  nashi dependencies\n");
        Console.WriteLine("Examples:");
        Console.WriteLine("  nashi convert pdf document.docx");
        Console.WriteLine("  nashi convert webp image.png --json");
        Console.WriteLine("  nashi resize 1900x1200 photo.jpg --fit crop");
        Console.WriteLine("  nashi resize 1920x1080 wallpaper.png --fit contain --quality 100 --json");
        Console.WriteLine("  nashi tool readQRCode ticket.png");
        Console.WriteLine("  nashi tool splitPDF doc.pdf");
        Console.WriteLine("  nashi tool mergePDF page1.pdf page2.pdf");
        Console.WriteLine("  nashi inspect photo.jpg");
    }

    /// <summary>
    /// Reports a missing argument error message to stderr.
    /// </summary>
    /// <param name="syntax">Expected usage syntax.</param>
    /// <returns>Process exit code 64 (command line usage error).</returns>
    private static int MissingArgument(string syntax)
    {
        Console.Error.WriteLine($"Error: Missing required arguments. Expected: nashi {syntax}");
        return 64;
    }

    /// <summary>
    /// Reports an unrecognized command to stderr and prints usage instructions.
    /// </summary>
    /// <param name="command">Unrecognized command string.</param>
    /// <returns>Process exit code 64.</returns>
    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}\n");
        PrintUsage();
        return 64;
    }

    /// <summary>
    /// Outputs the supported formats, conversions, and tool catalog in JSON format.
    /// </summary>
    /// <returns>Process exit code 0.</returns>
    private static int RunCatalog()
    {
        var supportedConversions = new Dictionary<string, string[]>();
        foreach (var (source, targets) in FeatureCatalog.Conversions)
        {
            var s = NormalizeFormat(source);
            if (IsImageFormat(s))
            {
                var validTargets = targets.Select(NormalizeFormat).Where(IsImageFormat).Distinct().ToArray();
                if (validTargets.Length > 0) supportedConversions[source] = validTargets;
            }
            else if (s is "docx" or "doc" or "xlsx" or "xls" or "pptx" or "ppt" or "odt" or "ods" or "odp")
            {
                supportedConversions[source] = targets;
            }
        }

        var output = new Dictionary<string, object>
        {
            ["conversionMatrix"] = FeatureCatalog.Conversions,
            ["conversions"] = supportedConversions,
            ["tools"] = FeatureCatalog.ToolNames,
            ["supportedTools"] = CliSupportedTools
        };
        Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>
    /// Inspects file properties such as resolution, metadata, and container information.
    /// </summary>
    /// <param name="filePath">Target file path to inspect.</param>
    /// <returns>Process exit code (0 on success).</returns>
    private static async Task<int> RunInspectAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: File '{filePath}' not found.");
            return 1;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var info = new FileInfo(filePath);

        var result = new Dictionary<string, object>
        {
            ["path"] = filePath,
            ["sizeBytes"] = info.Length,
            ["extension"] = ext,
            ["created"] = info.CreationTimeUtc,
            ["modified"] = info.LastWriteTimeUtc
        };

        if (IsImageFormat(ext))
        {
            try
            {
                using var image = PhotoCodec.Read(filePath);
                result["imageWidth"] = image.Width;
                result["imageHeight"] = image.Height;
                result["imageFormat"] = image.Format.ToString();
                result["colorSpace"] = image.ColorSpace.ToString();
            }
            catch (Exception ex)
            {
                result["imageError"] = ex.Message;
            }
        }
        else if (ext == ".pdf")
        {
            try
            {
                using var document = UglyToad.PdfPig.PdfDocument.Open(filePath);
                result["pages"] = document.NumberOfPages;
            }
            catch (Exception ex)
            {
                result["pdfError"] = ex.Message;
            }
        }
        else if (FeatureCatalog.IsVideo(filePath) || FeatureCatalog.IsAudio(filePath))
        {
            var toolchain = new Toolchain(ToolsDirectory, Environment.GetEnvironmentVariable("PATH"));
            string? ffprobe = null;
            try { ffprobe = toolchain.Executable("ffprobe"); } catch { }
            if (ffprobe is not null)
            {
                try
                {
                    var engine = new MediaEngine("", ffprobe);
                    var mediaInfo = await engine.InspectAsync(filePath);
                    result["duration"] = mediaInfo.Duration;
                    result["hasAudio"] = mediaInfo.HasAudio;
                    result["hasVideo"] = mediaInfo.Video is not null;
                    if (mediaInfo.Video is not null)
                    {
                        if (mediaInfo.Video.Width is not null) result["videoWidth"] = mediaInfo.Video.Width.Value;
                        if (mediaInfo.Video.Height is not null) result["videoHeight"] = mediaInfo.Video.Height.Value;
                        result["videoCodec"] = mediaInfo.Video.Codec;
                    }
                    if (mediaInfo.Audio is not null)
                    {
                        result["audioCodec"] = mediaInfo.Audio.Codec;
                    }
                }
                catch (Exception ex)
                {
                    result["mediaError"] = ex.Message;
                }
            }
        }

        var metadataService = new MetadataService(ToolsDirectory);
        if (metadataService.Executable is not null)
        {
            try
            {
                var tags = await metadataService.ReadAsync(filePath);
                result["metadata"] = tags;
            }
            catch (Exception ex)
            {
                result["metadataError"] = ex.Message;
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>
    /// Checks availability of backend binary toolchain dependencies (FFmpeg, LibreOffice, ExifTool, Deno).
    /// </summary>
    /// <returns>Process exit code 0.</returns>
    private static int RunDependencies()
    {
        var toolchain = new Toolchain(ToolsDirectory, Environment.GetEnvironmentVariable("PATH"));
        string? ffmpegPath = null;
        try { ffmpegPath = toolchain.Executable("ffmpeg"); } catch { }
        string? ffprobePath = null;
        try { ffprobePath = toolchain.Executable("ffprobe"); } catch { }
        string? denoPath = null;
        try { denoPath = toolchain.Executable("deno"); } catch { }

        var metadataService = new MetadataService(ToolsDirectory);
        string? exiftoolPath = metadataService.Executable;

        var soffice = GetLibreOfficeExecutable(applyWorkarounds: false);
        bool hasSoffice = soffice is not null && File.Exists(soffice);

        var status = new
        {
            toolsReady = toolchain.Ready,
            ffmpeg = ffmpegPath is not null,
            ffprobe = ffprobePath is not null,
            deno = denoPath is not null,
            metadata = exiftoolPath is not null,
            libreOffice = hasSoffice,
            paths = new Dictionary<string, string?>
            {
                ["ffmpeg"] = ffmpegPath,
                ["ffprobe"] = ffprobePath,
                ["deno"] = denoPath,
                ["exiftool"] = exiftoolPath,
                ["soffice"] = hasSoffice ? soffice : null
            }
        };
        Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>
    /// Converts input files to the specified output format using Nashi.Core converters.
    /// </summary>
    /// <param name="format">Target format extension (e.g. webp, png, pdf, mp4).</param>
    /// <param name="filesAndOptions">Input file paths and optional flags.</param>
    /// <returns>Process exit code (0 on success).</returns>
    private static async Task<int> RunConvertAsync(string format, string[] filesAndOptions)
    {
        var (inputs, outputDir, jsonOutput, parseError) = ParseFilesAndOutput(filesAndOptions);
        if (parseError is not null)
        {
            Console.Error.WriteLine($"Error: {parseError}");
            bool isFileMissing = parseError.StartsWith("File not found") || parseError.StartsWith("File or directory not found");
            return isFileMissing ? 1 : 64;
        }

        outputDir ??= Path.GetDirectoryName(Path.GetFullPath(inputs[0])) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(outputDir);
        format = NormalizeFormat(format.TrimStart('.').ToLowerInvariant());

        var createdFiles = new List<string>();

        foreach (var file in inputs)
        {
            var fullPath = Path.GetFullPath(file);
            var inExt = NormalizeFormat(Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant());

            if (IsImageFormat(inExt) && IsImageFormat(format))
            {
                var toolchain = new Toolchain(ToolsDirectory, Environment.GetEnvironmentVariable("PATH"));
                string? ffmpegPath = null;
                try { ffmpegPath = toolchain.Executable("ffmpeg"); } catch { }
                if (format == "heic" && ffmpegPath is null)
                {
                    Console.Error.WriteLine("Error: Converting to HEIC requires FFmpeg. Ensure FFmpeg is available on PATH or installed via Nashi tools setup.");
                    return 1;
                }
                var destName = Path.GetFileNameWithoutExtension(fullPath) + "." + format;
                var destPath = SafePaths.Unique(outputDir, destName);
                using var image = PhotoCodec.Read(fullPath);
                var png = image.ToByteArray(MagickFormat.Png);
                PhotoCodec.EncodePng(png, destPath, format, encoder: ffmpegPath, token: CancellationToken.None);
                createdFiles.Add(destPath);
                if (!jsonOutput) Console.WriteLine($"Created: {destPath}");
            }
            else if (inExt is "docx" or "doc" or "xlsx" or "xls" or "pptx" or "ppt" or "odt" or "ods" or "odp")
            {
                var soffice = GetLibreOfficeExecutable(applyWorkarounds: true);
                var deno = GetExecutable("deno.exe");
                if (soffice is not null && File.Exists(soffice))
                {
                    var office = new OfficeDocuments(soffice, deno, AppContext.BaseDirectory);
                    var dest = await office.OfficeAsync(fullPath, format, outputDir, CancellationToken.None);
                    createdFiles.Add(dest);
                    if (!jsonOutput) Console.WriteLine($"Created: {dest}");
                }
                else
                {
                    Console.Error.WriteLine("Error: LibreOffice is required for document conversion but was not found.");
                    return 1;
                }
            }
            else
            {
                Console.Error.WriteLine($"Unsupported conversion: {inExt} -> {format}");
                return 1;
            }
        }

        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(createdFiles, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine("Done.");
        }
        return 0;
    }

    /// <summary>
    /// Resizes images with studio-fidelity Lanczos resampling and configurable fit modes (crop, contain, stretch).
    /// </summary>
    /// <param name="dimensionsArg">Dimension specification: e.g. 1900x1200, 1900x, x1200, or 50%.</param>
    /// <param name="filesAndOptions">Input image paths and flags (--fit, --quality, --format, --output, --json).</param>
    /// <returns>Process exit code (0 on success).</returns>
    private static async Task<int> RunResizeAsync(string dimensionsArg, string[] filesAndOptions)
    {
        var (inputs, outputDir, fit, quality, formatOverride, jsonOutput, parseError) = ParseResizeArguments(filesAndOptions);
        if (parseError is not null)
        {
            Console.Error.WriteLine($"Error: {parseError}");
            bool isFileMissing = parseError.StartsWith("File not found") || parseError.StartsWith("File or directory not found");
            return isFileMissing ? 1 : 64;
        }

        outputDir ??= Path.GetDirectoryName(Path.GetFullPath(inputs[0])) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(outputDir);

        var (reqW, reqH, scalePercent) = ParseDimensions(dimensionsArg);
        if (reqW is null && reqH is null && scalePercent is null)
        {
            Console.Error.WriteLine($"Error: Invalid dimensions '{dimensionsArg}'. Expected format: WxH (e.g. 1900x1200), Wx (e.g. 1900x), xH (e.g. x1200), or P% (e.g. 50%).");
            return 64;
        }

        var resizedOutputs = new List<string>();
        int processedCount = 0;
        foreach (var file in inputs)
        {
            var fullPath = Path.GetFullPath(file);
            var inExt = NormalizeFormat(Path.GetExtension(fullPath));
            if (!IsImageFormat(inExt))
            {
                Console.Error.WriteLine($"Error: File '{Path.GetFileName(file)}' is not a supported image format for resize.");
                return 1;
            }

            var targetFormat = formatOverride ?? inExt;
            targetFormat = NormalizeFormat(targetFormat);

            using var image = PhotoCodec.Read(fullPath);
            int origW = (int)image.Width;
            int origH = (int)image.Height;

            int targetW, targetH;
            if (scalePercent is not null)
            {
                double factor = scalePercent.Value / 100.0;
                targetW = Math.Max(1, (int)Math.Round(origW * factor));
                targetH = Math.Max(1, (int)Math.Round(origH * factor));
            }
            else if (reqW is not null && reqH is not null)
            {
                targetW = reqW.Value;
                targetH = reqH.Value;
            }
            else if (reqW is not null)
            {
                targetW = reqW.Value;
                targetH = Math.Max(1, (int)Math.Round(origH * ((double)targetW / origW)));
            }
            else
            {
                targetH = reqH!.Value;
                targetW = Math.Max(1, (int)Math.Round(origW * ((double)targetH / origH)));
            }

            if (targetW < 1 || targetH < 1 || targetW > 30000 || targetH > 30000 || (long)targetW * targetH > 100_000_000)
            {
                Console.Error.WriteLine($"Error: Target dimensions {targetW}x{targetH} exceed limits (1 to 30,000 px, 100 MP limit).");
                return 1;
            }

            image.FilterType = FilterType.Lanczos;

            if (fit == "crop" && reqW is not null && reqH is not null)
            {
                // Crop-first optimization: Crop source image to target aspect ratio before resizing,
                // preventing huge intermediate memory allocations for extreme aspect ratio inputs.
                double targetAspect = (double)targetW / targetH;
                double sourceAspect = (double)origW / origH;
                int cropX = 0, cropY = 0;
                int croppedW = origW, croppedH = origH;

                if (sourceAspect > targetAspect)
                {
                    croppedW = Math.Clamp((int)Math.Round(origH * targetAspect), 1, origW);
                    cropX = Math.Max(0, (origW - croppedW) / 2);
                }
                else if (sourceAspect < targetAspect)
                {
                    croppedH = Math.Clamp((int)Math.Round(origW / targetAspect), 1, origH);
                    cropY = Math.Max(0, (origH - croppedH) / 2);
                }

                if (croppedW != origW || croppedH != origH)
                {
                    image.Crop(new MagickGeometry(cropX, cropY, (uint)croppedW, (uint)croppedH));
                    image.ResetPage();
                }

                image.Resize(new MagickGeometry((uint)targetW, (uint)targetH) { IgnoreAspectRatio = true });
            }
            else if (fit == "stretch")
            {
                image.Resize(new MagickGeometry((uint)targetW, (uint)targetH) { IgnoreAspectRatio = true });
            }
            else
            {
                double scale = Math.Min((double)targetW / origW, (double)targetH / origH);
                int scaledW = Math.Max(1, (int)Math.Round(origW * scale));
                int scaledH = Math.Max(1, (int)Math.Round(origH * scale));
                image.Resize((uint)scaledW, (uint)scaledH);
                targetW = scaledW;
                targetH = scaledH;
            }

            image.Quality = (uint)Math.Clamp(quality, 1, 100);

            if (targetFormat is "jpg" or "jpeg")
            {
                image.Format = MagickFormat.Jpeg;
                if (quality >= 95)
                {
                    image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:4:4");
                }
            }
            else if (targetFormat == "png")
            {
                image.Format = MagickFormat.Png;
            }
            else if (targetFormat == "webp")
            {
                image.Format = MagickFormat.WebP;
                if (quality >= 100) image.Settings.SetDefine(MagickFormat.WebP, "lossless", "true");
            }

            var baseName = Path.GetFileNameWithoutExtension(fullPath);
            var destName = $"{baseName}-{targetW}x{targetH}.{targetFormat}";
            var destPath = SafePaths.Unique(outputDir, destName);

            var pngBytes = image.ToByteArray(MagickFormat.Png);
            PhotoCodec.EncodePng(pngBytes, destPath, targetFormat, quality, token: CancellationToken.None);
            processedCount++;
            resizedOutputs.Add(destPath);
            if (!jsonOutput)
            {
                Console.WriteLine($"Resized: {destPath} ({image.Width}x{image.Height}, quality {quality}, fit: {fit})");
            }
        }

        if (processedCount == 0)
        {
            Console.Error.WriteLine("Error: No valid images were processed.");
            return 1;
        }

        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(resizedOutputs, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Done. Processed {processedCount} image(s).");
        }
        return 0;
    }

    private static (int? Width, int? Height, double? Percent) ParseDimensions(string spec)
    {
        spec = spec.Trim().ToLowerInvariant();
        if (spec.EndsWith('%') && double.TryParse(spec[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) && pct > 0)
        {
            return (null, null, pct);
        }

        var parts = spec.Split('x');
        if (parts.Length == 2)
        {
            bool hasW = parts[0].Length > 0;
            bool hasH = parts[1].Length > 0;
            if (hasW && hasH)
            {
                if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0 &&
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) && h > 0)
                {
                    return (w, h, null);
                }
                return (null, null, null);
            }
            if (hasW && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var singleW) && singleW > 0)
            {
                return (singleW, null, null);
            }
            if (hasH && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var singleH) && singleH > 0)
            {
                return (null, singleH, null);
            }
        }
        else if (parts.Length == 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var singleW) && singleW > 0)
        {
            return (singleW, null, null);
        }

        return (null, null, null);
    }

    private static (List<string> Inputs, string? OutputDir, string Fit, int Quality, string? Format, bool JsonOutput, string? Error) ParseResizeArguments(string[] args)
    {
        var inputs = new List<string>();
        string? outputDir = null;
        string fit = "crop";
        int quality = 100;
        string? format = null;
        bool jsonOutput = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-o" or "--output" or "--outdir" && i + 1 < args.Length)
            {
                outputDir = args[++i];
            }
            else if (arg is "--json")
            {
                jsonOutput = true;
            }
            else if (arg is "--options" && i + 1 < args.Length)
            {
                var optionsJson = args[++i];
                try
                {
                    using var doc = JsonDocument.Parse(optionsJson);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return (inputs, outputDir, fit, quality, format, jsonOutput, "The --options argument must be a JSON object.");
                    }
                    var root = doc.RootElement;
                    if (root.TryGetProperty("fit", out var fitProp))
                    {
                        if (fitProp.ValueKind != JsonValueKind.String)
                        {
                            return (inputs, outputDir, fit, quality, format, jsonOutput, "The 'fit' option must be a string (crop, contain, stretch).");
                        }
                        var val = fitProp.GetString()?.ToLowerInvariant();
                        fit = val switch
                        {
                            "crop" => "crop",
                            "contain" or "fit" => "contain",
                            "stretch" or "fill" or "exact" => "stretch",
                            _ => null!
                        };
                        if (fit is null)
                        {
                            return (inputs, outputDir, "crop", quality, format, jsonOutput, $"Unknown --fit value '{val}' in --options. Supported options: crop, contain, stretch.");
                        }
                    }
                    if (root.TryGetProperty("quality", out var qualityProp))
                    {
                        if (qualityProp.ValueKind != JsonValueKind.Number || !qualityProp.TryGetInt32(out var qVal))
                        {
                            return (inputs, outputDir, fit, quality, format, jsonOutput, "The 'quality' option must be an integer between 1 and 100.");
                        }
                        quality = Math.Clamp(qVal, 1, 100);
                    }
                    if (root.TryGetProperty("format", out var formatProp))
                    {
                        if (formatProp.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(formatProp.GetString()))
                        {
                            return (inputs, outputDir, fit, quality, format, jsonOutput, "The 'format' option must be a non-empty string.");
                        }
                        format = formatProp.GetString();
                    }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    return (inputs, outputDir, fit, quality, format, jsonOutput, $"Invalid JSON passed to --options: {ex.Message}");
                }
            }
            else if (arg is "--fit" && i + 1 < args.Length)
            {
                var val = args[++i].ToLowerInvariant();
                fit = val switch
                {
                    "crop" => "crop",
                    "contain" or "fit" => "contain",
                    "stretch" or "fill" or "exact" => "stretch",
                    _ => null!
                };
                if (fit is null)
                {
                    return (inputs, outputDir, "crop", quality, format, jsonOutput, $"Unknown --fit value '{val}'. Supported options: crop, contain, stretch.");
                }
            }
            else if (arg is "-q" or "--quality" && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var q))
            {
                quality = Math.Clamp(q, 1, 100);
            }
            else if (arg is "-f" or "--format" && i + 1 < args.Length)
            {
                format = args[++i];
            }
            else if (arg.StartsWith('-'))
            {
                return (inputs, outputDir, fit, quality, format, jsonOutput, $"Unknown option: {arg}");
            }
            else
            {
                if (!File.Exists(arg) && !Directory.Exists(arg))
                {
                    return (inputs, outputDir, fit, quality, format, jsonOutput, $"File not found: '{arg}'");
                }
                inputs.Add(arg);
            }
        }

        if (inputs.Count == 0)
        {
            return (inputs, outputDir, fit, quality, format, jsonOutput, "No valid input files specified.");
        }

        return (inputs, outputDir, fit, quality, format, jsonOutput, null);
    }

    /// <summary>
    /// Runs a standalone tool such as PDF split/merge, QR reading, or archive extraction.
    /// </summary>
    /// <param name="toolId">Tool identifier (splitPDF, mergePDF, readQRCode, extractArchive, createPDF).</param>
    /// <param name="filesAndOptions">Input file paths and optional flags.</param>
    /// <returns>Process exit code (0 on success).</returns>
    private static async Task<int> RunToolAsync(string toolId, string[] filesAndOptions)
    {
        var (inputs, outputDir, jsonOutput, parseError) = ParseFilesAndOutput(filesAndOptions);
        if (parseError is not null)
        {
            Console.Error.WriteLine($"Error: {parseError}");
            bool isFileMissing = parseError.StartsWith("File not found") || parseError.StartsWith("File or directory not found");
            return isFileMissing ? 1 : 64;
        }

        outputDir ??= Path.GetDirectoryName(Path.GetFullPath(inputs[0])) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(outputDir);

        switch (toolId.ToLowerInvariant())
        {
            case "readqrcode":
                bool hadQrError = false;
                var qrResults = new Dictionary<string, string[]>();
                foreach (var file in inputs)
                {
                    var (codes, error) = ReadQrCodes(file);
                    if (error is not null)
                    {
                        Console.Error.WriteLine($"Error reading QR codes from '{Path.GetFileName(file)}': {error}");
                        hadQrError = true;
                        continue;
                    }
                    qrResults[file] = codes;
                    if (!jsonOutput)
                    {
                        Console.WriteLine($"QR codes in {Path.GetFileName(file)}: {codes.Length}");
                        foreach (var code in codes) Console.WriteLine($" - {code}");
                    }
                }
                if (hadQrError) return 1;
                if (jsonOutput)
                {
                    Console.WriteLine(JsonSerializer.Serialize(qrResults, new JsonSerializerOptions { WriteIndented = true }));
                }
                return 0;

            case "createpdf":
                var nonImage = inputs.FirstOrDefault(f => !IsImageFormat(Path.GetExtension(f)));
                if (nonImage is not null)
                {
                    Console.Error.WriteLine($"Error: File '{Path.GetFileName(nonImage)}' is not a supported image format for createPDF.");
                    return 1;
                }
                if (inputs.Count == 0)
                {
                    Console.Error.WriteLine("Error: No valid images found to create PDF.");
                    return 1;
                }

                if (!jsonOutput) Console.WriteLine($"Creating PDF from {inputs.Count} image(s)...");
                var pdfPath = SafePaths.Unique(outputDir, "Images.pdf");
                using (var pdf = new PdfDocument())
                {
                    foreach (var imgPath in inputs)
                    {
                        using var image = PhotoCodec.Read(imgPath);
                        using var pngStream = new MemoryStream();
                        image.Write(pngStream, MagickFormat.Png);
                        pngStream.Position = 0;
                        using var xImage = XImage.FromStream(pngStream);
                        var page = pdf.AddPage();
                        page.Width = XUnit.FromPoint(image.Width * 72.0 / 96.0);
                        page.Height = XUnit.FromPoint(image.Height * 72.0 / 96.0);
                        using var g = XGraphics.FromPdfPage(page);
                        g.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
                    }
                    pdf.Save(pdfPath);
                }
                if (jsonOutput)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new[] { pdfPath }));
                }
                else
                {
                    Console.WriteLine($"Created: {pdfPath}");
                }
                return 0;

            case "splitpdf":
                var splitOutputs = new List<string>();
                foreach (var file in inputs)
                {
                    if (!jsonOutput) Console.WriteLine($"Splitting PDF: {file}");
                    var outputs = DocumentEngine.Pdf("splitPDF", new[] { file }, outputDir, null, CancellationToken.None, pagesPerFile: 1);
                    splitOutputs.AddRange(outputs);
                    if (!jsonOutput)
                    {
                        foreach (var outf in outputs) Console.WriteLine($"Created: {outf}");
                    }
                }
                if (jsonOutput)
                {
                    Console.WriteLine(JsonSerializer.Serialize(splitOutputs, new JsonSerializerOptions { WriteIndented = true }));
                }
                return 0;

            case "mergepdf":
            case "mergepdfs":
                if (!jsonOutput) Console.WriteLine($"Merging {inputs.Count} PDFs...");
                var merged = DocumentEngine.Pdf("mergePDFs", inputs.ToArray(), outputDir, null, CancellationToken.None);
                if (jsonOutput)
                {
                    Console.WriteLine(JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    foreach (var outf in merged) Console.WriteLine($"Created: {outf}");
                }
                return 0;

            case "extractarchive":
            case "unzip":
                var extractedOutputs = new List<string>();
                foreach (var file in inputs)
                {
                    if (!jsonOutput) Console.WriteLine($"Extracting {Path.GetFileName(file)}...");
                    var outputs = ArchiveEngine.Extract(file, outputDir, null, new Progress<OperationProgress>(), CancellationToken.None);
                    extractedOutputs.AddRange(outputs);
                    if (!jsonOutput)
                    {
                        foreach (var outf in outputs) Console.WriteLine($"Extracted: {outf}");
                    }
                }
                if (jsonOutput)
                {
                    Console.WriteLine(JsonSerializer.Serialize(extractedOutputs, new JsonSerializerOptions { WriteIndented = true }));
                }
                return 0;

            default:
                Console.Error.WriteLine($"Error: Tool '{toolId}' is not supported in CLI mode. Supported tools: {string.Join(", ", CliSupportedTools)}");
                return 1;
        }
    }

    /// <summary>
    /// Determines whether the given file extension represents a supported raster image format.
    /// </summary>
    private static string NormalizeFormat(string ext) => ext.TrimStart('.').ToLowerInvariant() switch
    {
        "jpeg" => "jpg",
        "tif" => "tiff",
        "heif" => "heic",
        "aif" => "aiff",
        "markdown" => "md",
        "htm" => "html",
        var other => other
    };

    private static bool IsImageFormat(string ext) => NormalizeFormat(ext) is "jpg" or "png" or "webp" or "gif" or "avif" or "heic" or "bmp" or "tiff";

    /// <summary>
    /// Reads and decodes QR codes from an image file using ZXing luminance source.
    /// </summary>
    /// <param name="filePath">Target image path containing QR codes.</param>
    /// <returns>Decoded QR codes or error message.</returns>
    private static (string[] Codes, string? Error) ReadQrCodes(string filePath)
    {
        try
        {
            using var image = PhotoCodec.Read(filePath);
            var pixels = image.ToByteArray(MagickFormat.Bgra);
            var reader = new ZXing.BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = [ZXing.BarcodeFormat.QR_CODE]
                }
            };
            var results = reader.DecodeMultiple(pixels, (int)image.Width, (int)image.Height, ZXing.RGBLuminanceSource.BitmapFormat.BGRA32);
            var codes = results?.Select(r => r.Text).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToArray() ?? [];
            return (codes, null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }

    /// <summary>
    /// Parses input file paths, options, and an optional custom output directory from command-line arguments.
    /// </summary>
    private static (List<string> Inputs, string? OutputDir, bool JsonOutput, string? Error) ParseFilesAndOutput(string[] args)
    {
        var inputs = new List<string>();
        string? outputDir = null;
        bool jsonOutput = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-o" or "--output" or "--outdir" && i + 1 < args.Length)
            {
                outputDir = args[++i];
            }
            else if (arg is "--json")
            {
                jsonOutput = true;
            }
            else if (arg.StartsWith('-'))
            {
                return (inputs, outputDir, jsonOutput, $"Unknown option: {arg}");
            }
            else
            {
                if (!File.Exists(arg) && !Directory.Exists(arg))
                {
                    return (inputs, outputDir, jsonOutput, $"File not found: '{arg}'");
                }
                inputs.Add(arg);
            }
        }

        if (inputs.Count == 0)
        {
            return (inputs, outputDir, jsonOutput, "No valid input files specified.");
        }

        return (inputs, outputDir, jsonOutput, null);
    }

    /// <summary>
    /// Resolves the path to a toolchain binary executable.
    /// </summary>
    private static string GetExecutable(string name)
    {
        var localTool = Path.Combine(ToolsDirectory, name);
        if (File.Exists(localTool)) return localTool;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var entry in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var folder = entry.Trim('"');
            if (folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(folder)) continue;
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate)) return candidate;
        }

        return name;
    }

    /// <summary>
    /// Resolves the path to the installed or bundled LibreOffice soffice.exe executable.
    /// </summary>
    private static string? GetLibreOfficeExecutable(bool applyWorkarounds = false)
    {
        return OfficeLocator.ResolveExecutable(OfficeToolsDirectory, Environment.GetEnvironmentVariable("PATH"), applyWorkarounds);
    }
}
