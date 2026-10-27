using System.Globalization;
using Nashi.Core;
using Nashi.Linux.Engine;
namespace Nashi.Linux;

/// Terminal entry points. They call the same Operations dispatcher as the
/// window, so scripts, file-manager actions and the GUI produce identical files.
internal static class Cli
{
    internal const string Usage="""
        Nashi for Linux

          nashi [FILE…]                      Open the app (optionally with files selected)
          nashi doctor                       Show which system tools Nashi found
          nashi list FILE…                   Show conversions and tools for these files
          nashi convert FORMAT FILE… [--out DIR] [--quality 1-100] [--dpi N] [--password P]
          nashi tool TOOL FILE… [--out DIR] [--width N] [--height N] [--rotation DEG] [--horizontal true|false]
                [--quality 1-100] [--reduce PERCENT] [--start S] [--end S] [--speed X] [--pages 1,3,5]
                [--pages-per-file N] [--password P] [--style solid|pixelate|blur] [--region X,Y,W,H] [--text CAPTION]
          nashi qr FILE…                     Read QR codes from images or PDFs
          nashi download URL [--kind video|audio|thumbnail|image] [--out DIR]
          nashi --version

        Outputs are new files; originals are never overwritten.
        """;
    internal static bool Handles(string[] args)=>args.Length>0&&args[0] is "doctor" or "list" or "convert" or "tool" or "qr" or "download" or "--version" or "-v" or "--help" or "-h";
    internal static async Task<int> RunAsync(string[] args)
    {
        using var cancellation=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
        try
        {
            var operations=new Operations(Paths.Data);
            switch(args[0])
            {
                case "--version" or "-v":Console.WriteLine("Nashi "+typeof(Cli).Assembly.GetName().Version?.ToString(3)+" (Linux)");return 0;
                case "--help" or "-h":Console.WriteLine(Usage);return 0;
                case "doctor":
                    foreach(var tool in operations.Status())Console.WriteLine($"{(tool.Path is null?"✗":"✓")} {tool.Name,-15} {tool.Path??"missing"}  · {tool.Purpose}");
                    var command=operations.InstallCommand();Console.WriteLine(command.Length==0?"\nAll required tools are available.":"\nInstall the missing tools:\n  "+command);
                    if(LinuxPdf.Tesseract is null)Console.WriteLine("Optional OCR for scanned PDFs:\n  "+LinuxPdf.InstallTesseract);
                    return 0;
            }
            var (positional,options)=Parse(args.Skip(1));
            switch(args[0])
            {
                case "list":
                {
                    var files=Files(positional);Console.WriteLine("Convert to: "+string.Join(", ",FeatureCatalog.Formats(files)));
                    Console.WriteLine("Tools: "+string.Join(", ",Operations.Choices(files).Select(c=>c.Id+" ("+c.Title+")")));return 0;
                }
                case "convert":
                {
                    if(positional.Count<2)throw new ArgumentException("Usage: nashi convert FORMAT FILE…");
                    var format=positional[0].ToLowerInvariant().TrimStart('.');var files=Files(positional.Skip(1));
                    return Print(await operations.RunAsync("convert",format,files,Destination(options,files),Settings(options,format),Progress(),cancellation.Token));
                }
                case "tool":
                {
                    if(positional.Count<2)throw new ArgumentException("Usage: nashi tool TOOL FILE…");
                    var files=Files(positional.Skip(1));
                    return Print(await operations.RunAsync(positional[0],null,files,Destination(options,files),Settings(options,null,positional[0]),Progress(),cancellation.Token));
                }
                case "qr":
                {
                    var values=await LinuxPdf.ReadQrAsync(Files(positional),cancellation.Token);foreach(var value in values)Console.WriteLine(value);
                    if(values.Length==0){Console.Error.WriteLine("No QR codes found.");return 2;}return 0;
                }
                case "download":
                {
                    if(positional.Count!=1)throw new ArgumentException("Usage: nashi download URL");
                    var preferences=new PreferencesStore(Paths.Data).Value;var destination=options.GetValueOrDefault("out")??preferences.DownloadFolder;
                    return Print(await new WebDownload(operations.Tools).DownloadAsync(positional[0],options.GetValueOrDefault("kind")??"video",destination,Progress(),cancellation.Token));
                }
            }
            Console.Error.WriteLine(Usage);return 64;
        }
        catch(OperationCanceledException){Console.Error.WriteLine("Cancelled. Originals kept.");return 130;}
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or PlatformNotSupportedException or FormatException or NotSupportedException)
        {Console.Error.WriteLine("nashi: "+error.Message);return 1;}
    }
    private static int Print(IReadOnlyList<string> outputs){Console.Error.WriteLine();foreach(var output in outputs)Console.WriteLine(output);return 0;}
    private static IProgress<OperationProgress> Progress()=>new Inline(p=>{if(!Console.IsErrorRedirected)Console.Error.Write("\r"+(p.Fraction is {} f?$"{f:P0} ":"")+p.Label.PadRight(60));});
    private sealed class Inline(Action<OperationProgress> report):IProgress<OperationProgress>{public void Report(OperationProgress value)=>report(value);}
    private static (List<string>,Dictionary<string,string>) Parse(IEnumerable<string> args)
    {
        var positional=new List<string>();var options=new Dictionary<string,string>(StringComparer.Ordinal);var list=args.ToList();
        for(var i=0;i<list.Count;i++)
        {
            if(list[i]=="--"){positional.AddRange(list.Skip(i+1));break;}
            if(list[i].StartsWith("--",StringComparison.Ordinal)){if(i+1>=list.Count)throw new ArgumentException("Missing value for "+list[i]);var key=list[i][2..];options[key]=options.TryGetValue(key,out var existing)?existing+";"+list[++i]:list[++i];}
            else positional.Add(list[i]);
        }
        return(positional,options);
    }
    private static List<string> Files(IEnumerable<string> paths)
    {
        var files=paths.Select(Path.GetFullPath).ToList();if(files.Count==0)throw new ArgumentException("Choose at least one file.");
        foreach(var file in files)if(!File.Exists(file)&&!Directory.Exists(file))throw new FileNotFoundException("File not found: "+file);return files;
    }
    private static string Destination(Dictionary<string,string> options,IReadOnlyList<string> files)
    {
        if(options.TryGetValue("out",out var folder))return Path.GetFullPath(folder);
        var preferences=new PreferencesStore(Paths.Data).Value;
        return preferences.OutputMode=="folder"&&!string.IsNullOrWhiteSpace(preferences.OutputFolder)?preferences.OutputFolder:Path.GetDirectoryName(files[0])!;
    }
    private static double Number(string value)=>double.Parse(value,NumberStyles.Float,CultureInfo.InvariantCulture);
    internal static JobSettings Settings(Dictionary<string,string> o,string? format,string? tool=null)
    {
        var preferences=new PreferencesStore(Paths.Data);var profile=format is null?new OutputProfile():preferences.Profile(format);
        int? Int(string key)=>o.TryGetValue(key,out var v)?(int)Number(v):null;double? Double(string key)=>o.TryGetValue(key,out var v)?Number(v):null;
        var quality=Int("quality")??(int)Math.Round(profile.Quality*100);
        var regions=o.TryGetValue("region",out var raw)?raw.Split(';').Select(r=>{var n=r.Split(',').Select(s=>(int)Number(s)).ToArray();if(n.Length!=4)throw new ArgumentException("Use --region X,Y,WIDTH,HEIGHT.");return new PixelRect(n[0],n[1],n[2],n[3]);}).ToArray():[];
        var image=new ImageOptions{Width=Int("width"),Height=Int("height"),Rotation=Int("rotation")??(tool=="rotate"?90:0),Horizontal=!o.TryGetValue("horizontal",out var h)||h!="false",Quality=quality,ReductionPercent=Double("reduce"),TargetBytes=o.TryGetValue("target-kb",out var kb)?(long)(Number(kb)*1024):null,
            Crop=regions.Length>0?regions[0]:null,Regions=regions,RedactionStyle=o.GetValueOrDefault("style")??"solid",AnnotationText=o.GetValueOrDefault("text")??""};
        var media=new MediaOptions{Quality=quality/100d,AudioBitrate=profile.AudioBitrate,SampleDepth=profile.SampleDepth,GifFrameRate=profile.GifFrameRate,GifWidth=profile.GifWidth,PreserveMetadata=profile.PreserveMetadata,
            Start=Double("start")??0,End=Double("end"),Speed=Double("speed")??1,Width=Int("width"),Height=Int("height")??(profile.MaxVideoHeight>0?profile.MaxVideoHeight:null),Channels=Int("channels")??2,ReductionPercent=Double("reduce"),
            SnapshotTimes=o.TryGetValue("times",out var times)?times.Split(',').Select(Number).ToArray():[0],SplitInterval=Double("interval")??30,RedactionMode=o.GetValueOrDefault("style")??"solid",
            Regions=regions.Select(r=>new MediaRegion(r.X,r.Y,r.Width,r.Height)).ToArray()};
        var document=new DocumentOptions(Int("dpi")??profile.Dpi,o.GetValueOrDefault("layout")??profile.Layout,o.GetValueOrDefault("epub-layout")??profile.EpubLayout,o.GetValueOrDefault("password")??"");
        var pages=o.TryGetValue("pages",out var p)?p.Split(',').Select(s=>(int)Number(s)).ToArray():null;
        return new(){Image=image,Media=media,Document=document,Pages=pages,PagesPerFile=Int("pages-per-file")??1,PreserveImageMetadata=profile.PreserveMetadata};
    }
}
