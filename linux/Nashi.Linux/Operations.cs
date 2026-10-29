using Nashi.Core;
using PdfSharp.Pdf.IO;
namespace Nashi.Linux;

public sealed record ActionChoice(string Id,string Label)
{
    public override string ToString()=>Label;
}
public sealed record ExportOptions
{
    public string Format{get;init;}="png";
    public int? Width{get;init;}public int? Height{get;init;}
    public int X{get;init;}public int Y{get;init;}public int Rotation{get;init;}=90;
    public int Quality{get;init;}=85;public bool PreserveMetadata{get;init;}=true;
    public double Start{get;init;}public double? End{get;init;}
    public double Speed{get;init;}=1;public int Channels{get;init;}=2;
    public string Pages{get;init;}="";public int PagesPerFile{get;init;}=1;
    public string Password{get;init;}="";
    public PhotoAdjustments Adjustments{get;init;}=new();
}
public static class Operations
{
    private static readonly HashSet<string> MediaTools=["compress","removeMetadata","normalizeAudio","muteVideo","trimVideo","trimAudio","changeVideoSpeed","joinVideos","videoSnapshots","splitVideo","audioToVideo","audioChannels"];
    private static readonly string[] OfficeInputs=["doc","docx","rtf","txt","md","odt","xls","xlsx","ods","csv","tsv","ppt","pptx"];
    private static readonly string[] OfficeOutputs=["pdf","txt","docx","xlsx","csv","tsv","pptx","epub","rtf"];
    public static string DataDirectory
    {
        get
        {
            var xdg=Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return Path.Combine(xdg is not null&&Path.IsPathFullyQualified(xdg)?xdg:
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".local","share"),"nashi");
        }
    }
    public static string[] Formats(IReadOnlyList<string> files)
    {
        if(files.Count==0)return [];
        var common=FeatureCatalog.Formats(files);
        if(files.All(FeatureCatalog.IsImage))return common.Intersect(RasterEngine.Formats).ToArray();
        if(files.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p)))return common.Where(f=>MediaEngine.AudioFormats.Contains(f)||MediaEngine.VideoFormats.Contains(f)).ToArray();
        if(files.Count==1&&FeatureCatalog.IsArchive(files[0]))return common.Intersect(["zip","tar","gz"]).ToArray();
        if(files.All(p=>FeatureCatalog.Extension(p) is "srt" or "vtt"))return common.Intersect(["srt","vtt","txt"]).ToArray();
        if(files.All(p=>OfficeInputs.Contains(FeatureCatalog.Extension(p)))&&SystemTools.Find("libreoffice") is not null)return common.Intersect(OfficeOutputs).ToArray();
        return [];
    }
    public static ActionChoice[] Choices(IReadOnlyList<string> files)
    {
        if(files.Count==0)return [];
        var choices=new List<ActionChoice>();
        if(Formats(files).Length>0)choices.Add(new("convert","Convert"));
        var catalog=FeatureCatalog.Tools(files);
        if(files.All(FeatureCatalog.IsImage))
        {
            choices.AddRange(new[]{new ActionChoice("resize","Resize image"),new("crop","Crop image"),new("rotate","Rotate image"),new("flip","Flip horizontally"),new("editImage","Adjust image"),new("removeMetadata","Remove metadata"),new("createPDF","Create PDF")});
        }
        else if(files.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p)))
        {
            if(SystemTools.Find("ffmpeg") is not null&&SystemTools.Find("ffprobe") is not null)
                choices.AddRange(catalog.Where(MediaTools.Contains).Select(t=>new ActionChoice(t,t=="removeMetadata"?"Remove metadata":FeatureCatalog.ToolNames[t])));
            else choices.RemoveAll(c=>c.Id=="convert");
        }
        else if(files.All(p=>FeatureCatalog.Extension(p)=="pdf"))
            choices.AddRange(catalog.Where(t=>t is "mergePDF" or "splitPDF" or "organizePDF").Select(t=>new ActionChoice(t=="mergePDF"?"mergePDFs":t,FeatureCatalog.ToolNames[t])));
        if(files.Count==1&&FeatureCatalog.IsArchive(files[0]))choices.Add(new("extract","Extract archive"));
        choices.Add(new("zip","Create ZIP"));return choices.ToArray();
    }
    public static async Task<IReadOnlyList<string>> RunAsync(string action,IReadOnlyList<string> files,string destination,
        ExportOptions options,IProgress<OperationProgress>? progress=null,CancellationToken token=default)
    {
        if(!Choices(files).Any(c=>c.Id==action))throw new ArgumentException("Choose a supported action for these files.");
        if(action=="convert"&&!Formats(files).Contains(options.Format))throw new ArgumentException("Choose a supported output format.");
        // One outer transaction makes multi-input subtitles/archives/Office jobs atomic too.
        using var tx=new ExportTransaction(destination);IReadOnlyList<string> results;
        if(action=="zip")results=[DocumentEngine.Zip(files,tx.StagingDirectory,progress,token)];
        else if(action=="extract")results=ArchiveEngine.Extract(files.Single(),tx.StagingDirectory,options.Password,progress,token);
        else if(files.All(FeatureCatalog.IsImage))
            results=RasterEngine.Export(action,files,action=="createPDF"?"pdf":options.Format,tx.StagingDirectory,
                new(options.Width,options.Height,options.X,options.Y,options.Rotation,options.Quality,options.PreserveMetadata,options.Adjustments),progress,token);
        else if(files.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p)))
        {
            var engine=new MediaEngine(SystemTools.Find("ffmpeg")??throw new FileNotFoundException("Install FFmpeg."),SystemTools.Find("ffprobe")??throw new FileNotFoundException("Install FFmpeg."));
            var media=new MediaOptions{Quality=options.Quality/100d,PreserveMetadata=options.PreserveMetadata,Start=options.Start,End=options.End,Speed=options.Speed,Channels=options.Channels,SnapshotTimes=[options.Start],SplitInterval=options.Speed};
            results=action=="convert"?await engine.ConvertAsync(files,options.Format,tx.StagingDirectory,media,progress,token):await engine.ToolAsync(action,files,tx.StagingDirectory,media,progress,token);
        }
        else if(files.All(p=>FeatureCatalog.Extension(p)=="pdf"))
        {
            int[]? pages=null;
            if(action=="organizePDF")
            {
                using var pdf=PdfReader.Open(files[0],options.Password,PdfDocumentOpenMode.Import);
                pages=DocumentEngine.PageRange(options.Pages,pdf.PageCount);
            }
            results=DocumentEngine.Pdf(action,files,tx.StagingDirectory,pages,token,pagesPerFile:options.PagesPerFile,password:options.Password,pageRanges:options.Pages);
        }
        else if(FeatureCatalog.IsArchive(files[0]))results=ArchiveEngine.Convert(files.Single(),options.Format,tx.StagingDirectory,options.Password,progress,token);
        else
        {
            var outputs=new List<string>();
            foreach(var input in files)
            {
                token.ThrowIfCancellationRequested();
                if(FeatureCatalog.Extension(input) is "srt" or "vtt")
                {
                    if(new FileInfo(input).Length>20_000_000)throw new InvalidDataException("Subtitle exceeds 20 MB.");
                    var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+"."+options.Format);
                    await File.WriteAllTextAsync(output,SubtitleCodec.Convert(await File.ReadAllTextAsync(input,token),options.Format),token);outputs.Add(output);
                }
                else
                {
                    var office=new OfficeDocuments(SystemTools.Find("libreoffice")??throw new FileNotFoundException("Install LibreOffice."),"","");
                    // Isolate identical stems: LibreOffice chooses its own output name.
                    var job=Path.Combine(tx.StagingDirectory,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
                    var rendered=await office.OfficeAsync(input,options.Format,job,token);
                    var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileName(rendered));File.Move(rendered,output);outputs.Add(output);
                }
            }
            results=outputs;
        }
        return tx.Publish(results,token);
    }
}
