using System.Text.Json;
using ImageMagick;
using Nashi.Core;

namespace Nashi.Cli;

/// <summary>
/// Entry point and command processor for the Nashi Windows command-line interface.
/// </summary>
public static class Program
{
    private static readonly string ToolsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nashi",
        "Tools"
    );

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
                "convert" => args.Length < 3 ? MissingArgument("convert <format> <file...> [--output <dir>]") : await RunConvertAsync(args[1], args[2..]),
                "tool" => args.Length < 3 ? MissingArgument("tool <tool-id> <file...> [--output <dir>]") : await RunToolAsync(args[1], args[2..]),
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
        Console.WriteLine("  nashi convert <format> <file...> [--output <dir>]");
        Console.WriteLine("  nashi tool <tool-id> <file...> [--output <dir>]");
        Console.WriteLine("  nashi inspect <file>");
        Console.WriteLine("  nashi catalog");
        Console.WriteLine("  nashi dependencies\n");
        Console.WriteLine("Examples:");
        Console.WriteLine("  nashi convert pdf document.docx");
        Console.WriteLine("  nashi convert webp image.png");
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
        var output = new
        {
            conversions = FeatureCatalog.Conversions,
            tools = FeatureCatalog.ToolNames
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
            return 2;
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
        var toolchain = new Toolchain(ToolsDirectory);
        var status = new
        {
            toolsReady = toolchain.Ready,
            ffmpeg = toolchain.Executable is not null,
            metadata = new MetadataService(ToolsDirectory).Executable is not null,
            libreOffice = File.Exists(GetLibreOfficeExecutable())
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
        var (inputs, outputDir) = ParseFilesAndOutput(filesAndOptions);
        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("Error: No valid input files specified.");
            return 64;
        }

        outputDir ??= Path.GetDirectoryName(Path.GetFullPath(inputs[0])) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(outputDir);
        format = NormalizeFormat(format.TrimStart('.').ToLowerInvariant());

        foreach (var file in inputs)
        {
            var fullPath = Path.GetFullPath(file);
            var inExt = NormalizeFormat(Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant());

            if (IsImageFormat(inExt) && IsImageFormat(format))
            {
                var destName = Path.GetFileNameWithoutExtension(fullPath) + "." + format;
                var destPath = SafePaths.UniquePath(Path.Combine(outputDir, destName));
                using var image = PhotoCodec.Read(fullPath);
                var png = image.ToByteArray(MagickFormat.Png);
                PhotoCodec.EncodePng(png, destPath, format, token: CancellationToken.None);
                Console.WriteLine($"Created: {destPath}");
            }
            else if (inExt is "docx" or "doc" or "xlsx" or "xls" or "pptx" or "ppt" or "odt" or "ods" or "odp")
            {
                var soffice = GetLibreOfficeExecutable();
                var deno = GetExecutable("deno.exe");
                if (File.Exists(soffice))
                {
                    var office = new OfficeDocuments(soffice, deno, ToolsDirectory);
                    var dest = await office.OfficeAsync(fullPath, format, outputDir, CancellationToken.None);
                    Console.WriteLine($"Created: {dest}");
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

        Console.WriteLine("Done.");
        return 0;
    }

    /// <summary>
    /// Runs a standalone tool such as PDF split/merge or archive extraction.
    /// </summary>
    /// <param name="toolId">Tool identifier (splitPDF, mergePDF, extractArchive).</param>
    /// <param name="filesAndOptions">Input file paths and optional flags.</param>
    /// <returns>Process exit code (0 on success).</returns>
    private static async Task<int> RunToolAsync(string toolId, string[] filesAndOptions)
    {
        var (inputs, outputDir) = ParseFilesAndOutput(filesAndOptions);
        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("Error: No valid input files specified.");
            return 64;
        }

        outputDir ??= Path.GetDirectoryName(Path.GetFullPath(inputs[0])) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(outputDir);

        switch (toolId.ToLowerInvariant())
        {
            case "readqrcode":
                foreach (var file in inputs)
                {
                    var codes = ReadQrCodes(file);
                    Console.WriteLine($"QR codes in {Path.GetFileName(file)}: {codes.Length}");
                    foreach (var code in codes) Console.WriteLine($" - {code}");
                }
                return 0;

            case "createpdf":
                var imageInputs = inputs.Where(f => IsImageFormat(Path.GetExtension(f))).ToList();
                if (imageInputs.Count > 0)
                {
                    Console.WriteLine($"Creating PDF from {imageInputs.Count} image(s)...");
                    var pdfPath = SafePaths.UniquePath(Path.Combine(outputDir, "Images.pdf"));
                    using var collection = new MagickImageCollection();
                    foreach (var img in imageInputs)
                    {
                        collection.Add(PhotoCodec.Read(img));
                    }
                    collection.Write(pdfPath, MagickFormat.Pdf);
                    Console.WriteLine($"Created: {pdfPath}");
                    return 0;
                }
                Console.Error.WriteLine("Error: No valid images found to create PDF.");
                return 1;

            case "splitpdf":
                foreach (var file in inputs)
                {
                    Console.WriteLine($"Splitting PDF: {file}");
                    var outputs = DocumentEngine.Pdf("splitPDF", new[] { file }, outputDir, null, CancellationToken.None, pagesPerFile: 1);
                    foreach (var outf in outputs) Console.WriteLine($"Created: {outf}");
                }
                return 0;

            case "mergepdf":
            case "mergepdfs":
                Console.WriteLine($"Merging {inputs.Count} PDFs...");
                var merged = DocumentEngine.Pdf("mergePDFs", inputs.ToArray(), outputDir, null, CancellationToken.None);
                foreach (var outf in merged) Console.WriteLine($"Created: {outf}");
                return 0;

            case "extractarchive":
            case "unzip":
                foreach (var file in inputs)
                {
                    Console.WriteLine($"Extracting {Path.GetFileName(file)}...");
                    var outputs = ArchiveEngine.Extract(file, outputDir, null, new Progress<OperationProgress>(), CancellationToken.None);
                    foreach (var outf in outputs) Console.WriteLine($"Extracted: {outf}");
                }
                return 0;

            default:
                Console.Error.WriteLine($"Tool '{toolId}' is not yet supported in CLI mode.");
                return 1;
        }
    }

    /// <summary>
    /// Determines whether the given file extension represents a supported raster image format.
    /// </summary>
    /// <param name="ext">Extension without leading dot.</param>
    /// <returns>True if recognized as an image format.</returns>
    private static bool IsImageFormat(string ext) => ext is "jpg" or "jpeg" or "png" or "webp" or "gif" or "avif" or "heic" or "bmp" or "tiff" or "tif";

    /// <summary>
    /// Reads and decodes QR codes from an image file using ZXing luminance source.
    /// </summary>
    /// <param name="filePath">Target image path containing QR codes.</param>
    /// <returns>Array of detected text strings.</returns>
    private static string[] ReadQrCodes(string filePath)
    {
        try
        {
            using var image = new MagickImage(filePath);
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
            return reader.DecodeMultiple(pixels, (int)image.Width, (int)image.Height, ZXing.RGBLuminanceSource.BitmapFormat.BGRA32)?
                .Select(r => r.Text).Distinct().ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Parses input file paths and an optional custom output directory from command-line arguments.
    /// </summary>
    /// <param name="args">Array of file path and flag arguments.</param>
    /// <returns>Tuple containing valid input files and optional output directory.</returns>
    private static (List<string> Inputs, string? OutputDir) ParseFilesAndOutput(string[] args)
    {
        var inputs = new List<string>();
        string? outputDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "-o" or "--output" or "--outdir" && i + 1 < args.Length)
            {
                outputDir = args[++i];
            }
            else if (File.Exists(args[i]) || Directory.Exists(args[i]))
            {
                inputs.Add(args[i]);
            }
        }

        return (inputs, outputDir);
    }

    /// <summary>
    /// Resolves the absolute path to a toolchain binary executable.
    /// </summary>
    /// <param name="name">Executable file name.</param>
    /// <returns>Absolute path if found locally, otherwise binary name for system PATH lookup.</returns>
    private static string GetExecutable(string name)
    {
        var localTool = Path.Combine(ToolsDirectory, name);
        if (File.Exists(localTool)) return localTool;
        var v1Tool = Path.Combine(ToolsDirectory, "v1", name);
        if (File.Exists(v1Tool)) return v1Tool;
        return name;
    }

    /// <summary>
    /// Resolves the path to the installed or bundled LibreOffice soffice.exe executable.
    /// </summary>
    /// <returns>Executable path or generic soffice.exe name.</returns>
    private static string GetLibreOfficeExecutable()
    {
        var localOffice = Path.Combine(ToolsDirectory, "office-26.8.1", "program", "soffice.exe");
        if (File.Exists(localOffice)) return localOffice;
        var pfOffice = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe");
        if (File.Exists(pfOffice)) return pfOffice;
        return "soffice.exe";
    }
}
