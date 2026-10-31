using System.IO;
using System.Text.Json;
using Nashi.Core;
using Nashi.Windows;
using PdfSharp.Pdf.IO;

namespace Nashi.Cli;

public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                Console.Error.WriteLine("Usage: nashi-cli convert FORMAT FILE... [--options JSON] [--json]");
                Console.Error.WriteLine("       nashi-cli tool TOOL_ID FILE... [--options JSON] [--json]");
                Console.Error.WriteLine("       nashi-cli inspect FILE");
                Console.Error.WriteLine("       nashi-cli catalog");
                Console.Error.WriteLine("       nashi-cli dependencies");
                return args.Length == 0 ? 64 : 0;
            }

            var toolsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nashi", "Tools");
            var toolchain = new Toolchain(toolsDir, Environment.GetEnvironmentVariable("PATH"));

            if (args.Length == 1 && args[0] == "catalog")
            {
                var catalogData = new
                {
                    conversionMatrix = FeatureCatalog.Conversions,
                    tools = FeatureCatalog.ToolNames.Keys
                };
                Console.WriteLine(JsonSerializer.Serialize(catalogData, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            if (args.Length == 2 && args[0] == "inspect")
            {
                var file = Path.GetFullPath(args[1]);
                if (!File.Exists(file))
                {
                    Console.Error.WriteLine($"File not found: {Path.GetFileName(file)}");
                    return 1;
                }
                var ext = FeatureCatalog.Extension(file);
                var result = new Dictionary<string, object?>();

                if (FeatureCatalog.IsImage(file) || ext is "gif")
                {
                    result["category"] = "image";
                    try
                    {
                        var bmp = ImageEngine.Load(file);
                        result["width"] = bmp.PixelWidth;
                        result["height"] = bmp.PixelHeight;
                    }
                    catch
                    {
                        using var img = PhotoCodec.Read(file);
                        result["width"] = (int)img.Width;
                        result["height"] = (int)img.Height;
                    }
                }
                else if (ext is "pdf")
                {
                    result["category"] = "pdf";
                    using var pdf = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                    result["pages"] = pdf.PageCount;
                }
                else if (FeatureCatalog.IsVideo(file) || FeatureCatalog.IsAudio(file))
                {
                    result["category"] = FeatureCatalog.IsVideo(file) ? "video" : "audio";
                    try
                    {
                        var ffmpeg = toolchain.Executable("ffmpeg");
                        var ffprobe = toolchain.Executable("ffprobe");
                        var engine = new MediaEngine(ffmpeg, ffprobe);
                        var info = await engine.InspectAsync(file);
                        result["duration"] = info.Duration;
                        result["hasAudio"] = info.HasAudio;
                        result["hasVideo"] = info.Video is not null;
                        result["width"] = info.Video?.Width;
                        result["height"] = info.Video?.Height;
                    }
                    catch
                    {
                    }
                }
                else
                {
                    result["category"] = ext;
                }

                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            if (args.Length == 1 && args[0] == "dependencies")
            {
                var deps = new Dictionary<string, string>();
                foreach (var name in new[] { "ffmpeg", "ffprobe", "yt-dlp", "deno" })
                {
                    try { deps[name] = toolchain.Executable(name); } catch { }
                }
                Console.WriteLine(JsonSerializer.Serialize(deps, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            if (args.Length >= 3 && args[0] is "convert" or "tool")
            {
                var action = args[0];
                var target = args[1];
                var paths = args.Skip(2).ToList();
                var jsonOutput = paths.Remove("--json");

                var optIndex = paths.IndexOf("--options");
                if (optIndex >= 0 && optIndex + 1 < paths.Count)
                {
                    paths.RemoveAt(optIndex + 1);
                    paths.RemoveAt(optIndex);
                }

                if (paths.Count == 0)
                {
                    Console.Error.WriteLine("No input files specified.");
                    return 1;
                }

                foreach (var p in paths)
                {
                    if (!File.Exists(p))
                    {
                        Console.Error.WriteLine($"Input file not found: {p}");
                        return 1;
                    }
                }

                var outputPaths = new List<string>();

                if (action == "convert")
                {
                    var destFormat = target.ToLowerInvariant();
                    foreach (var input in paths)
                    {
                        var inExt = FeatureCatalog.Extension(input);
                        var outDir = Path.GetDirectoryName(Path.GetFullPath(input)) ?? Environment.CurrentDirectory;

                        if (FeatureCatalog.IsImage(input) || inExt is "gif" || destFormat is "ico" || ImageEngine.Formats.Contains(destFormat))
                        {
                            var imgOpts = new ImageOptions();
                            var produced = ImageEngine.Apply("convert", [input], outDir, imgOpts, format: destFormat);
                            outputPaths.AddRange(produced);
                        }
                        else if (FeatureCatalog.IsVideo(input) || FeatureCatalog.IsAudio(input))
                        {
                            var ffmpeg = toolchain.Executable("ffmpeg");
                            var ffprobe = toolchain.Executable("ffprobe");
                            var engine = new MediaEngine(ffmpeg, ffprobe);
                            var mediaOpts = new MediaOptions();
                            var produced = await engine.ConvertAsync([input], destFormat, outDir, mediaOpts);
                            outputPaths.AddRange(produced);
                        }
                        else if (inExt is "zip" or "tar" or "gz" or "rar")
                        {
                            var produced = ArchiveEngine.Convert(input, destFormat, outDir);
                            outputPaths.AddRange(produced);
                        }
                        else
                        {
                            Console.Error.WriteLine($"Unsupported conversion: {inExt} -> {destFormat}");
                            return 1;
                        }
                    }
                }
                else if (action == "tool")
                {
                    var toolId = target;
                    var firstInput = paths[0];
                    var inExt = FeatureCatalog.Extension(firstInput);
                    var outDir = Path.GetDirectoryName(Path.GetFullPath(firstInput)) ?? Environment.CurrentDirectory;

                    if (toolId is "createPDF" or "createCollage" || (FeatureCatalog.IsImage(firstInput) && toolId is "rotate" or "crop" or "compress" or "editImage" or "redactImage" or "frameImage"))
                    {
                        var imgOpts = new ImageOptions();
                        var produced = ImageEngine.Apply(toolId, paths, outDir, imgOpts);
                        outputPaths.AddRange(produced);
                    }
                    else if (inExt is "pdf" || toolId is "mergePDF" or "splitPDF" or "organizePDF")
                    {
                        var produced = DocumentEngine.Pdf(toolId, paths, outDir);
                        outputPaths.AddRange(produced);
                    }
                    else if (MediaEngine.Tools.Contains(toolId))
                    {
                        var ffmpeg = toolchain.Executable("ffmpeg");
                        var ffprobe = toolchain.Executable("ffprobe");
                        var engine = new MediaEngine(ffmpeg, ffprobe);
                        var mediaOpts = new MediaOptions();
                        var produced = await engine.ToolAsync(toolId, paths, outDir, mediaOpts);
                        outputPaths.AddRange(produced);
                    }
                    else if (toolId is "extractArchive")
                    {
                        foreach (var archive in paths)
                        {
                            var extracted = ArchiveEngine.Extract(archive, outDir);
                            outputPaths.AddRange(extracted);
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine($"Unsupported or unknown tool: {toolId}");
                        return 1;
                    }
                }

                if (jsonOutput)
                {
                    Console.WriteLine(JsonSerializer.Serialize(outputPaths, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    foreach (var p in outputPaths)
                    {
                        Console.WriteLine(p);
                    }
                }
                return 0;
            }

            Console.Error.WriteLine("Unknown command. Run 'nashi-cli --help' for usage.");
            return 64;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
