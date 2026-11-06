using Nashi.Core;
namespace Nashi.Linux.Engine;

/// Nashi from a terminal or a script, with the same engines and saved export defaults as the app.
///   nashi convert --to jpg photo.heic scan.pdf      convert (prints each new file)
///   nashi tool compress video.mp4 --out ~/Out       run a tool that works with its defaults
///   nashi formats file…                             what the files can become, and which tools fit
/// Exit codes: 0 done, 1 failed, 2 usage error.
public static class Cli
{
    public static readonly string[] Commands=["convert","tool","formats","help","--help","-h"];
    private static readonly string[] QuickTools=["compress","muteVideo","normalizeAudio","videoSnapshots","extractArchive","createPDF","mergePDF","splitPDF","createCollage","joinVideos","frameImage"];
    public static async Task<int> RunAsync(string[] args)
    {
        var command=args[0];var rest=args.Skip(1).ToList();
        if(command is "help" or "--help" or "-h"){Help();return 0;}
        string? Take(string flag){var at=rest.IndexOf(flag);if(at<0||at+1>=rest.Count)return null;var value=rest[at+1];rest.RemoveRange(at,2);return value;}
        var to=Take("--to")?.TrimStart('.').ToLowerInvariant();var output=Take("--out");var tool=command=="tool"&&rest.Count>0?rest[0]:null;if(tool is not null)rest.RemoveAt(0);
        var inputs=rest.Where(a=>!a.StartsWith("--",StringComparison.Ordinal)).Select(Path.GetFullPath).ToList();
        var missing=inputs.Where(p=>!File.Exists(p)&&!Directory.Exists(p)).ToList();
        if(inputs.Count==0||missing.Count>0){Console.Error.WriteLine(missing.Count>0?"Not found: "+string.Join(", ",missing):"Name at least one file. Try: nashi help");return 2;}
        if(command=="formats")
        {
            Console.WriteLine("Convert to: "+string.Join(", ",FeatureCatalog.Formats(inputs)));
            Console.WriteLine("Tools:      "+string.Join(", ",FeatureCatalog.Tools(inputs)));return 0;
        }
        var destination=output is null?Path.GetDirectoryName(inputs[0])!:Path.GetFullPath(output.StartsWith('~')?AppPaths.Home+output[1..]:output);
        var library=new LibraryStore();var preferences=new PreferencesStore(library.DirectoryPath);var tools=SelfTest.Tools();
        using var cancellation=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
        var progress=new Progress<OperationProgress>(p=>{if(!Console.IsErrorRedirected)Console.Error.Write($"\r\x1b[2K{(p.Fraction is {} f?$"{f*100,3:0}%  ":"")}{p.Label}");});
        try
        {
            IReadOnlyList<string> outputs;
            if(command=="convert")
            {
                if(to is null){Console.Error.WriteLine("Choose a format with --to, e.g. --to jpg. Possible: "+string.Join(", ",FeatureCatalog.Formats(inputs)));return 2;}
                if(!FeatureCatalog.Formats(inputs).Contains(to)){Console.Error.WriteLine($"These files can't become {to}. Possible: "+string.Join(", ",FeatureCatalog.Formats(inputs)));return 2;}
                outputs=await ConvertAsync(inputs,to,destination,preferences,tools,progress,cancellation.Token);
            }
            else if(command=="tool")
            {
                if(tool is null||!QuickTools.Contains(tool)){Console.Error.WriteLine("Tools that run from the terminal: "+string.Join(", ",QuickTools)+". Others need their options window; open them in Nashi.");return 2;}
                if(!FeatureCatalog.Tools(inputs).Contains(tool)){Console.Error.WriteLine($"{tool} doesn't fit these files. Fitting tools: "+string.Join(", ",FeatureCatalog.Tools(inputs)));return 2;}
                outputs=await ToolAsync(tool,inputs,destination,tools,progress,cancellation.Token);
            }
            else{Help();return 2;}
            if(!Console.IsErrorRedirected)Console.Error.Write("\r\x1b[2K");
            library.Record(command=="convert"?"Convert to "+to!.ToUpperInvariant():FeatureCatalog.ToolNames.GetValueOrDefault(tool!,tool!),outputs);
            foreach(var path in outputs)Console.WriteLine(path);return 0;
        }
        catch(OperationCanceledException){Console.Error.WriteLine("\nCancelled. Originals kept.");return 1;}
        catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or FileNotFoundException)
        {Console.Error.WriteLine("\n"+error.Message);return 1;}
    }
    internal static async Task<IReadOnlyList<string>> ConvertAsync(IReadOnlyList<string> inputs,string format,string destination,PreferencesStore preferences,Toolchain tools,IProgress<OperationProgress> progress,CancellationToken token)
    {
        var profile=preferences.Profile(format);
        if(inputs.All(FeatureCatalog.IsImage))
            return await Task.Run(()=>ImageEngine.Apply("convert",inputs,destination,new ImageOptions(Quality:(int)Math.Round(profile.Quality*100)){EncoderPath=Encoder(tools)},format,token,progress),token);
        if(inputs.All(p=>FeatureCatalog.IsVideo(p)||FeatureCatalog.IsAudio(p)))
            return await Media(tools).ConvertAsync(inputs,format,destination,new MediaOptions{Quality=profile.Quality,AudioBitrate=profile.AudioBitrate,SampleDepth=profile.SampleDepth,GifFrameRate=profile.GifFrameRate,GifWidth=profile.GifWidth,Height=profile.MaxVideoHeight>0?profile.MaxVideoHeight:null,PreserveMetadata=profile.PreserveMetadata},progress,token);
        return await new DocumentConversion(new LinuxOffice(),tools).ConvertAsync(inputs,format,destination,new DocumentOptions(profile.Dpi,profile.Layout,profile.EpubLayout),progress,token);
    }
    private static async Task<IReadOnlyList<string>> ToolAsync(string tool,IReadOnlyList<string> inputs,string destination,Toolchain tools,IProgress<OperationProgress> progress,CancellationToken token)
    {
        if(tool=="extractArchive")return await Task.Run<IReadOnlyList<string>>(()=>{using var tx=new ExportTransaction(destination);var outputs=inputs.SelectMany(f=>ArchiveEngine.Extract(f,tx.StagingDirectory,null,progress,token)).ToList();return tx.Publish(outputs,token);},token);
        if(inputs.All(FeatureCatalog.IsImage))return await Task.Run(()=>ImageEngine.Apply(tool,inputs,destination,new ImageOptions(Quality:80){EncoderPath=Encoder(tools)},null,token,progress),token);
        if(inputs.All(p=>FeatureCatalog.IsVideo(p)||FeatureCatalog.IsAudio(p)))return await Media(tools).ToolAsync(tool,inputs,destination,new MediaOptions{Quality=.8,SnapshotTimes=[0]},progress,token);
        if(tool=="compress")return await PdfCompression.RunAsync(inputs,destination,new ImageOptions(Quality:75),"",progress,token);
        return await Task.Run(()=>DocumentEngine.Pdf(tool=="mergePDF"?"mergePDFs":tool,inputs,destination,null,token),token);
    }
    private static string? Encoder(Toolchain tools){try{return tools.Executable("ffmpeg");}catch(FileNotFoundException){return null;}}
    private static MediaEngine Media(Toolchain tools)=>new(tools.Executable("ffmpeg"),tools.Executable("ffprobe"));
    private static void Help()=>Console.WriteLine("""
        Nashi for Linux, from the terminal

          nashi                                 open the app (files given here open in it)
          nashi convert --to FORMAT FILES…      convert, e.g. nashi convert --to jpg *.heic
          nashi tool TOOL FILES…                run a tool with its defaults, e.g. nashi tool compress clip.mp4
          nashi formats FILES…                  list the formats and tools that fit
          --out FOLDER                          where new files go (default: beside the first file)

        Tools from the terminal: compress, muteVideo, normalizeAudio, videoSnapshots, extractArchive,
        createPDF, mergePDF, splitPDF, createCollage, joinVideos, frameImage.
        Exports use the defaults saved in Nashi. Originals are never changed; each new file is printed on its own line.

          nashi --self-test [FOLDER]            check every engine with real exports
          nashi --shortcuts-check               check KDE global shortcuts
          nashi --notify-check                  send a test notification
          nashi --version
        """);
}
