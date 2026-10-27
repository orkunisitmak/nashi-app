using Nashi.Core;
namespace Nashi.Linux.Engine;

/// LibreOffice from the distribution or the official /opt tarball.
public sealed class OfficeRuntime{public string? Executable=>SystemTools.Office();}

public sealed record ToolChoice(string Id,string Title,string Symbol);
public sealed record JobSettings
{
    public ImageOptions Image{get;init;}=new();
    public MediaOptions Media{get;init;}=new();
    public DocumentOptions Document{get;init;}=new();
    public int[]? Pages{get;init;}
    public int PagesPerFile{get;init;}=1;
    public bool PreserveImageMetadata{get;init;}=true;
}

/// One dispatcher for the window and the command line, mirroring the Windows
/// MainWindow.EditAsync routing: image, media, archive, PDF and document jobs
/// each run inside an ExportTransaction and publish beside the destination.
public sealed class Operations
{
    public Toolchain Tools{get;}public MetadataService Metadata{get;}public OfficeRuntime Office{get;}=new();
    public Operations(string dataDirectory){var root=Path.Combine(dataDirectory,"Tools");Tools=new(Path.Combine(root,"v1"));Metadata=new(root);}

    public static bool Images(IReadOnlyList<string> files)=>files.Count>0&&files.All(FeatureCatalog.IsImage);
    public static bool Media(IReadOnlyList<string> files)=>files.Count>0&&files.All(p=>FeatureCatalog.IsVideo(p)||FeatureCatalog.IsAudio(p));
    public static string Symbol(string id)=>id switch{"compress"=>"↘","removeMetadata"=>"◇","editImage"=>"☷","annotateImage"=>"✎","frameImage"=>"▣","cropImage" or "cropVideo"=>"⌗","trimAudio" or "trimVideo"=>"✂","muteVideo"=>"♫","normalizeAudio"=>"≋","audioChannels"=>"⇆","audioToVideo"=>"▷","createCollage"=>"▦","redactImage" or "redactVideo" or "redactAudio"=>"▨","readQRCode"=>"▧","mergePDF"=>"▣","splitPDF" or "splitVideo"=>"◫","organizePDF"=>"▤","extractArchive"=>"↗","createPDF"=>"▱","joinVideos"=>"⇥","videoSnapshots"=>"▣","changeVideoSpeed"=>"◴","createZip"=>"▤",_=>"↗"};
    public static IReadOnlyList<ToolChoice> Choices(IReadOnlyList<string> files)
    {
        var tools=FeatureCatalog.Tools(files).Select(id=>new ToolChoice(id,FeatureCatalog.ToolNames[id],Symbol(id))).ToList();
        // Images also offer the quick Windows transforms that live outside the catalog.
        if(Images(files))tools.InsertRange(0,[new("resize","Resize","⤢"),new("rotate","Rotate","↻"),new("flip","Flip","⇋")]);
        if(files.Count>0)tools.Add(new("createZip","Create ZIP","▤"));
        return tools;
    }
    public static string NormalizedId(string id)=>id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>id};

    /// format is set only for "convert". Returns the published output paths.
    public async Task<IReadOnlyList<string>> RunAsync(string id,string? format,IReadOnlyList<string> inputs,string destination,JobSettings settings,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        if(inputs.Count==0)throw new ArgumentException("Choose files first.");
        id=NormalizedId(id);var input=inputs[0];
        if(id=="convert"&&(format is null||!FeatureCatalog.Formats(inputs).Contains(format)))throw new ArgumentException("This conversion is not available for the selected files.");
        if(id=="createZip")return await Task.Run(()=>(IReadOnlyList<string>)[DocumentEngine.Zip(inputs,destination,progress,token)],token);
        if(id=="removeMetadata")return await RemoveMetadataAsync(inputs,destination,progress,token);
        if(Images(inputs))
        {
            var image=settings.Image with{EncoderPath=Tools.MediaReady?Tools.Executable("ffmpeg"):null};
            if(id=="convert"&&format is "heic"&&image.EncoderPath is null)throw new InvalidOperationException("HEIC export uses FFmpeg's HEVC encoder. "+SystemTools.InstallCommand(["ffmpeg"]));
            using var tx=new ExportTransaction(destination);
            var exported=await Task.Run(()=>LinuxImages.Apply(id,inputs,tx.StagingDirectory,image,id=="convert"?format:null,token,progress),token);
            if(id=="convert"&&settings.PreserveImageMetadata&&format is not ("pdf" or "bmp" or "gif")&&Metadata.Executable is not null)
                for(var n=0;n<exported.Count;n++)await Metadata.CopyImageTagsAsync(inputs[n],exported[n],token);
            return tx.Publish(exported,token);
        }
        if(Media(inputs))
        {
            if(!Tools.MediaReady)throw new InvalidOperationException("Media editing needs FFmpeg. "+SystemTools.InstallCommand(["ffmpeg"]));
            var engine=new MediaEngine(Tools.Executable("ffmpeg"),Tools.Executable("ffprobe"));
            return id=="convert"?await engine.ConvertAsync(inputs,format!,destination,settings.Media,progress,token):await engine.ToolAsync(id,inputs,destination,settings.Media,progress,token);
        }
        if(id is "extractArchive" or "unzip")return await Task.Run<IReadOnlyList<string>>(()=>{using var tx=new ExportTransaction(destination);var outputs=inputs.SelectMany(file=>ArchiveEngine.Extract(file,tx.StagingDirectory,settings.Document.Password,progress,token)).ToList();return tx.Publish(outputs,token);},token);
        if(id=="convert")return await new DocumentConversion(Office,Tools).ConvertAsync(inputs,format!,destination,settings.Document,progress,token);
        if(id=="compress"&&inputs.All(p=>FeatureCatalog.Extension(p)=="pdf"))return await PdfCompression.RunAsync(inputs,destination,settings.Image,settings.Document.Password,progress,token);
        if(id is "mergePDFs" or "splitPDF" or "organizePDF")
            return await Task.Run(()=>DocumentEngine.Pdf(id,inputs,destination,settings.Pages,token,pagesPerFile:settings.PagesPerFile,password:settings.Document.Password),token);
        throw new ArgumentException("This tool is not available for the selected files.");
    }
    /// ExifTool when the distribution has it (same as Windows); otherwise each
    /// engine writes a fresh copy without metadata.
    private async Task<IReadOnlyList<string>> RemoveMetadataAsync(IReadOnlyList<string> inputs,string destination,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        if(Media(inputs)&&Tools.MediaReady)return await new MediaEngine(Tools.Executable("ffmpeg"),Tools.Executable("ffprobe")).ToolAsync("removeMetadata",inputs,destination,new(){RemoveAllMetadata=true},progress,token);
        if(Metadata.Executable is not null)return await Metadata.WriteAsync(inputs,destination,true,progress:progress,token:token);
        if(Images(inputs))return await Task.Run(()=>LinuxImages.Apply("removeMetadata",inputs,destination,new(),null,token,progress),token);
        if(inputs.All(p=>FeatureCatalog.Extension(p)=="pdf"))return await Task.Run(()=>DocumentEngine.Pdf("removeMetadata",inputs,destination,token:token),token);
        throw new InvalidOperationException("Install ExifTool to remove metadata from these files: "+SystemTools.InstallCommand(["exiftool"]));
    }

    public sealed record ToolStatus(string Name,string Purpose,string? Path,string InstallKey);
    public IReadOnlyList<ToolStatus> Status()
    {
        string? Find(Func<string?> lookup){try{return lookup();}catch(FileNotFoundException){return null;}}
        return
        [
            new("FFmpeg","Video and audio editing, HEIC export",Find(()=>Tools.Executable("ffmpeg")),"ffmpeg"),
            new("FFprobe","Media inspection",Find(()=>Tools.Executable("ffprobe")),"ffprobe"),
            new("yt-dlp","Web downloads",Find(()=>Tools.Executable("yt-dlp")),"yt-dlp"),
            new("Deno / Node.js","Web downloads and Pages files",Find(()=>Tools.Executable("deno")),"deno"),
            new("LibreOffice","Documents, slides and spreadsheets",Office.Executable,"office"),
            new("ExifTool","Metadata editing",Metadata.Executable,"exiftool"),
            new("Tesseract","Text from scanned PDFs",LinuxPdf.Tesseract,"tesseract")
        ];
    }
    public string InstallCommand()
    {
        var missing=Status().Where(s=>s.Path is null&&s.InstallKey!="tesseract").Select(s=>s.InstallKey).ToArray();
        return SystemTools.InstallCommand(missing);
    }
}
