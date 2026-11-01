using System.Text.Json.Serialization;
using System.IO;
using System.Text.Json;
using Nashi.Core;
using Nashi.Windows;
using PdfSharp.Pdf.IO;

namespace Nashi.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions StrictOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        var isJson = args.Contains("--json");

        int Fail(string message)
        {
            if (isJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { error = message }, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.Error.WriteLine(message);
            }
            return 1;
        }

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
                    return Fail($"File not found: {Path.GetFileName(file)}");
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
                    catch (Exception ex)
                    {
                        return Fail($"Media inspection failed: {ex.Message}");
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

                string? optionsJson = null;
                var optIndex = paths.IndexOf("--options");
                if (optIndex >= 0 && optIndex + 1 < paths.Count)
                {
                    optionsJson = paths[optIndex + 1];
                    try
                    {
                        using var doc = JsonDocument.Parse(optionsJson);
                    }
                    catch (Exception ex)
                    {
                        return Fail($"Invalid --options JSON: {ex.Message}");
                    }
                    paths.RemoveAt(optIndex + 1);
                    paths.RemoveAt(optIndex);
                }

                if (paths.Count == 0)
                {
                    return Fail("No input files specified.");
                }

                foreach (var p in paths)
                {
                    if (!File.Exists(p))
                    {
                        return Fail($"Input file not found: {p}");
                    }
                }

                var stageDir = Path.Combine(Path.GetTempPath(), "nashi-stage-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stageDir);
                var stagedOutputs = new List<(string stageFile, string finalFile)>();

                try
                {
                    if (action == "convert")
                    {
                        var destFormat = target.ToLowerInvariant();
                        foreach (var input in paths)
                        {
                            var inExt = FeatureCatalog.Extension(input);
                            var outDir = Path.GetDirectoryName(Path.GetFullPath(input)) ?? Environment.CurrentDirectory;

                            // Validate source -> dest in feature matrix
                            if (!FeatureCatalog.Conversions.TryGetValue(inExt, out var targets) || !targets.Contains(destFormat))
                            {
                                return Fail($"Unsupported conversion: {inExt} -> {destFormat}");
                            }

                            if (FeatureCatalog.IsImage(input) || inExt is "gif" || destFormat is "ico" || ImageEngine.Formats.Contains(destFormat))
                            {
                                var imgOpts = optionsJson is not null
                                    ? JsonSerializer.Deserialize<ImageOptions>(optionsJson, StrictOptions) ?? new ImageOptions()
                                    : new ImageOptions();
                                var produced = ImageEngine.Apply("convert", [input], stageDir, imgOpts, format: destFormat);
                                foreach (var f in produced)
                                {
                                    stagedOutputs.Add((f, Path.Combine(outDir, Path.GetFileName(f))));
                                }
                            }
                            else if (FeatureCatalog.IsVideo(input) || FeatureCatalog.IsAudio(input))
                            {
                                var ffmpeg = toolchain.Executable("ffmpeg");
                                var ffprobe = toolchain.Executable("ffprobe");
                                var engine = new MediaEngine(ffmpeg, ffprobe);
                                var mediaOpts = optionsJson is not null
                                    ? JsonSerializer.Deserialize<MediaOptions>(optionsJson, StrictOptions) ?? new MediaOptions()
                                    : new MediaOptions();
                                var produced = await engine.ConvertAsync([input], destFormat, stageDir, mediaOpts);
                                foreach (var f in produced)
                                {
                                    stagedOutputs.Add((f, Path.Combine(outDir, Path.GetFileName(f))));
                                }
                            }
                            else if (inExt is "zip" or "tar" or "gz" or "rar")
                            {
                                var produced = ArchiveEngine.Convert(input, destFormat, stageDir);
                                foreach (var f in produced)
                                {
                                    stagedOutputs.Add((f, Path.Combine(outDir, Path.GetFileName(f))));
                                }
                            }
                            else
                            {
                                return Fail($"Unsupported conversion: {inExt} -> {destFormat}");
                            }
                        }
                    }
                    else if (action == "tool")
                    {
                        var toolId = target switch
                        {
                            "annotateImage" => "annotate",
                            "cropImage" => "crop",
                            _ => target
                        };
                        var firstInput = paths[0];
                        var inExt = FeatureCatalog.Extension(firstInput);
                        var outDir = Path.GetDirectoryName(Path.GetFullPath(firstInput)) ?? Environment.CurrentDirectory;

                        if (toolId is "createPDF" or "createCollage" || (FeatureCatalog.IsImage(firstInput) && toolId is "rotate" or "crop" or "compress" or "removeMetadata" or "annotate" or "editImage" or "redactImage" or "frameImage"))
                        {
                            var imgOpts = optionsJson is not null
                                ? JsonSerializer.Deserialize<ImageOptions>(optionsJson, StrictOptions) ?? new ImageOptions()
                                : new ImageOptions();
                            var produced = ImageEngine.Apply(toolId, paths, stageDir, imgOpts);
                            foreach (var f in produced)
                            {
                                stagedOutputs.Add((f, Path.Combine(outDir, Path.GetFileName(f))));
                            }
                        }
                        else if (inExt is "pdf" || toolId is "mergePDF" or "splitPDF" or "organizePDF")
                        {
                            var produced = DocumentEngine.Pdf(toolId, paths, stageDir);
                            foreach (var f in produced)
                            {
                                stagedOutputs.Add((f, Path.Combine(outDir, Path.GetFileName(f))));
                            }
                        }
                        else if (MediaEngine.Tools.Contains(toolId))
                        {
                            var ffmpeg = toolchain.Executable("ffmpeg");
                            var ffprobe = toolchain.Executable("ffprobe");
                            var engine = new MediaEngine(ffmpeg, ffprobe);
                            var mediaOpts = optionsJson is not null
                                ? JsonSerializer.Deserialize<MediaOptions>(optionsJson, StrictOptions) ?? new MediaOptions()
                                : new MediaOptions();
                            var produced = await engine.ToolAsync(toolId, paths, stageDir, mediaOpts);
                            foreach (var f in produced)
                            {
                                stagedOutputs.Add((f, Path.Combine(outDir, Path.GetFileName(f))));
                            }
                        }
                        else if (toolId is "extractArchive")
                        {
                            foreach (var archive in paths)
                            {
                                var extracted = ArchiveEngine.Extract(archive, stageDir);
                                foreach (var f in extracted)
                                {
                                    stagedOutputs.Add((f, Path.Combine(outDir, Path.GetFileName(f))));
                                }
                            }
                        }
                        else
                        {
                            return Fail($"Unsupported or unknown tool: {toolId}");
                        }
                    }

                    // Publish all staged outputs atomically
                    var published = new List<string>();
                    try
                    {
                        foreach (var (stageFile, finalFile) in stagedOutputs)
                        {
                            var destFile = SafePaths.Unique(Path.GetDirectoryName(finalFile)!, Path.GetFileName(finalFile));
                            File.Move(stageFile, destFile);
                            published.Add(destFile);
                        }
                    }
                    catch
                    {
                        foreach (var p in published)
                        {
                            try { File.Delete(p); } catch { }
                        }
                        throw;
                    }

                    if (jsonOutput)
                    {
                        Console.WriteLine(JsonSerializer.Serialize(published, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    else
                    {
                        foreach (var p in published)
                        {
                            Console.WriteLine(p);
                        }
                    }
                    return 0;
                }
                finally
                {
                    if (Directory.Exists(stageDir))
                    {
                        try { Directory.Delete(stageDir, true); } catch { }
                    }
                }
            }

            return Fail("Unknown command. Run 'nashi-cli --help' for usage.");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }
}
