using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Nashi.Core;
using Nashi.Linux.Engine;
namespace Nashi.Linux.UI;

public sealed partial class MainWindow
{
    private bool Images=>files.Count>0&&files.All(FeatureCatalog.IsImage);
    private bool Media=>files.Count>0&&files.All(p=>FeatureCatalog.IsVideo(p)||FeatureCatalog.IsAudio(p));
    private string? Encoder=>HasMedia?tools.Executable("ffmpeg"):null;

    /// Same routing as the Windows EditAsync: images through the Linux image engine, audio and
    /// video through FFmpeg, archives, PDFs and documents through Nashi.Core and LibreOffice.
    internal async Task EditAsync(ToolChoice choice)
    {
        if(busy||files.Count==0)return;
        var requestedFormat=choice.Id.StartsWith("convert:",StringComparison.Ordinal)?choice.Id[8..]:null;
        if(requestedFormat is not null)choice=new("convert","Convert to "+requestedFormat.ToUpperInvariant(),"↗");
        choice=choice with{Id=choice.Id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>choice.Id}};
        var selected=files.ToArray();var input=selected[0];var imageSelection=Images;var mediaSelection=Media;
        try
        {
            if(choice.Id=="removeMetadata"){await EditMetadataAsync(selected);return;}
            if(choice.Id=="readQRCode"){await ReadQrAsync(selected);return;}
            if(!imageSelection&&!mediaSelection&&choice.Id is not ("extractArchive" or "unzip")){await EditDocumentAsync(choice,selected,requestedFormat);return;}
            MediaInfo? info=null;byte[]? preview=null;int sourceWidth=0,sourceHeight=0;
            if(imageSelection)
            {
                (preview,sourceWidth,sourceHeight)=await Task.Run(()=>{using var image=ImageEngine.Load(input);var w=(int)image.Width;var h=(int)image.Height;return(ImageEngine.PreviewPng(input),w,h);});
            }
            else if(mediaSelection)
            {
                if(!HasMedia){await OfferMediaSetup();return;}
                var engine=new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe"));info=await engine.InspectAsync(input);
                if(choice.Id is "cropVideo" or "redactVideo")
                {
                    var frame=Path.Combine(Path.GetTempPath(),"Nashi-preview-"+Guid.NewGuid().ToString("N")+".png");
                    try{await ProcessRunner.RunAsync(tools.Executable("ffmpeg"),["-v","error","-i",input,"-frames:v","1",frame]);preview=await File.ReadAllBytesAsync(frame);}
                    finally{if(File.Exists(frame))File.Delete(frame);}
                    sourceWidth=info.Video?.Width??0;sourceHeight=info.Video?.Height??0;
                }
            }
            var editor=new ToolEditor(choice.Id,choice.Title,selected,choice.Id=="convert"?FeatureCatalog.Formats(selected):null,preferences,requestedFormat,info,preview,sourceWidth,sourceHeight,metadata.Executable is not null);
            if(!await editor.ShowDialog<bool>(this))return;
            var inputs=choice.Id is "cropVideo" or "redactVideo" or "annotate"?new[]{input}:editor.OrderedInputs;
            if(imageSelection)
            {
                var options=editor.ImageOptions with{EncoderPath=Encoder};var format=choice.Id=="convert"?editor.Format:null;var keep=editor.PreserveMetadata&&metadata.Executable is not null;
                await RunAsync(async(destination,p,t)=>
                {
                    using var tx=new ExportTransaction(destination);var exported=await Task.Run(()=>ImageEngine.Apply(choice.Id,inputs,tx.StagingDirectory,options,format,t,p),t);
                    if(choice.Id=="convert"&&keep&&format is not ("pdf" or "bmp" or "gif"))for(var n=0;n<exported.Count;n++)await metadata.CopyImageTagsAsync(inputs[n],exported[n],t);
                    return tx.Publish(exported,t);
                },choice.Title);
            }
            else if(mediaSelection)
            {
                var options=editor.MediaOptions;var format=editor.Format;
                await RunAsync((destination,p,t)=>{var engine=new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe"));return choice.Id=="convert"?engine.ConvertAsync(inputs,format,destination,options,p,t):engine.ToolAsync(choice.Id,inputs,destination,options,p,t);},choice.Title);
            }
            else
            {
                var password=editor.DocumentOptions.Password;
                await RunAsync((destination,p,t)=>Task.Run<IReadOnlyList<string>>(()=>{using var tx=new ExportTransaction(destination);var outputs=inputs.SelectMany(file=>ArchiveEngine.Extract(file,tx.StagingDirectory,password,p,t)).ToList();return tx.Publish(outputs,t);},t),choice.Title);
            }
        }
        catch(Exception error){await Dialogs.Message(this,"Couldn’t open this tool",L10n.T(error.Message));}
    }
    private async Task OfferMediaSetup()
    {
        if(await Dialogs.Confirm(this,"Media setup","Audio and video tools need FFmpeg. Download the verified media tools now, or install ffmpeg from your distribution.","Download"))await new SetupWindow(this).InstallMediaAsync();
    }
    private async Task EditDocumentAsync(ToolChoice choice,string[] selected,string? requestedFormat)
    {
        if(choice.Id=="convert")
        {
            var editor=new ToolEditor("convert","Convert",selected,FeatureCatalog.Formats(selected),preferences,requestedFormat);if(!await editor.ShowDialog<bool>(this))return;
            var format=editor.Format;var options=editor.DocumentOptions;
            if(NeedsOffice(selected,format)&&office.Executable is null)
            {await Dialogs.Command(this,"Set up office tools","Document, slide and spreadsheet conversions use LibreOffice from your distribution. Install it, then try again:",LinuxOffice.InstallHint);return;}
            await RunAsync((destination,p,t)=>new DocumentConversion(office,tools).ConvertAsync(selected,format,destination,options,p,t),"Convert to "+format.ToUpperInvariant());return;
        }
        if(choice.Id=="organizePDF")
        {
            var organizer=new ToolEditor("organizePDF",choice.Title,selected,null,preferences);if(!await organizer.ShowDialog<bool>(this))return;
            var pages=organizer.Pages;var pageRotations=organizer.PageRotations;var uniform=organizer.UniformWidth;
            await RunAsync((destination,p,t)=>Task.Run(()=>DocumentEngine.Pdf("organizePDF",selected,destination,pages,t,pageRotations:pageRotations,uniformWidth:uniform),t),"Organize PDF");return;
        }
        var dialog=new ToolEditor(choice.Id,choice.Title,selected,null,preferences);if(!await dialog.ShowDialog<bool>(this))return;
        if(choice.Id=="compress"){var options=dialog.ImageOptions;var password=dialog.DocumentOptions.Password;await RunAsync((destination,p,t)=>PdfCompression.RunAsync(selected,destination,options,password,p,t),"Compress PDF");return;}
        var inputs=dialog.OrderedInputs;var pagesPerFile=dialog.PagesPerFile;var pdfPassword=dialog.DocumentOptions.Password;var ranges=dialog.PageRanges;
        await RunAsync((destination,p,t)=>Task.Run(()=>DocumentEngine.Pdf(choice.Id,inputs,destination,null,t,pagesPerFile:pagesPerFile,password:pdfPassword,pageRanges:ranges),t),choice.Title);
    }
    /// Conversions DocumentConversion does without LibreOffice: archives, subtitles, PDF to text
    /// or images and fixed-layout EPUB from a PDF.
    private static bool NeedsOffice(IReadOnlyList<string> inputs,string format)=>inputs.Any(input=>
    {
        var ext=FeatureCatalog.Extension(input);
        if(FeatureCatalog.IsArchive(input)||ext is "srt" or "vtt")return false;
        if(ext=="pdf"&&format is "txt" or "md" or "png" or "jpg" or "epub")return false;
        return true;
    });
    private async Task EditMetadataAsync(string[] selected)
    {
        if(selected.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p)&&FeatureCatalog.Extension(p)!="gif"))
        {
            if(!HasMedia){await OfferMediaSetup();return;}
            await RunAsync((destination,p,t)=>new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe")).ToolAsync("removeMetadata",selected,destination,new MediaOptions{RemoveAllMetadata=true},p,t),"Metadata");return;
        }
        if(metadata.Executable is null)
        {
            if(LinuxShell.FindInPath("perl") is null){await Dialogs.Command(this,"Set up metadata tools","ExifTool needs Perl. Install it from your distribution, then try again:",LinuxShell.InstallCommand("perl"));return;}
            if(!await Dialogs.Confirm(this,"Set up metadata tools","Download ExifTool (~11 MB) from its official SourceForge distribution? The SHA-256 is checked and it runs locally without an administrator password. Upstream licenses are retained.","Download"))return;
            if(!await SetupStepAsync("Set up metadata tools",(p,t)=>metadata.InstallAsync(p,t)))return;
        }
        var removeAll=selected.Length>1;Dictionary<string,string>? changes=null;
        if(selected.Length==1)
        {
            var values=await metadata.ReadAsync(selected[0]);var editor=new MetadataEditor(values);if(!await editor.ShowDialog<bool>(this))return;
            removeAll=editor.RemoveAll;changes=removeAll?null:editor.Updates;
        }
        await RunAsync((destination,p,t)=>metadata.WriteAsync(selected,destination,removeAll,changes,progress:p,token:t),"Metadata");
    }
    private async Task ReadQrAsync(IReadOnlyList<string> inputs)
    {
        SetBusy(true);var results=new List<(string File,string Value)>();
        try
        {
            foreach(var file in inputs)
            {
                if(FeatureCatalog.Extension(file)=="pdf")
                {
                    var pages=await LinuxPdf.RenderAsync(file,180);
                    for(var n=0;n<pages.Length;n++){using var page=Magick.FromPng(pages[n]);foreach(var value in ImageEngine.DecodeQr(page))results.Add((Path.GetFileName(file)+" · "+L10n.T($"Page {n+1}"),value));}
                }
                else if(FeatureCatalog.Extension(file)=="gif")
                {using var image=new ImageMagick.MagickImage(file,new ImageMagick.MagickReadSettings{FrameCount=1});foreach(var value in ImageEngine.DecodeQr(image))results.Add((Path.GetFileName(file),value));}
                else{using var image=await Task.Run(()=>ImageEngine.Load(file));foreach(var value in ImageEngine.DecodeQr(image))results.Add((Path.GetFileName(file),value));}
            }
        }
        finally{SetBusy(false);}
        await Dialogs.QrResults(this,results);
    }

    // ── File actions ───────────────────────────────────────────────────────────────────
    internal async Task PerformFileAction(string action,IReadOnlyList<string> paths)
    {
        if(busy||paths.Count==0)return;
        try
        {
            switch(action)
            {
                case "open":foreach(var path in paths.Take(10))LinuxShell.Open(path);break;
                case "reveal":LinuxShell.Reveal(paths[0]);break;
                case "copy":await ClipboardFiles.CopyAsync(this,paths);SetStatus("Files copied. Paste them wherever you need.");break;
                case "copyPath":if(Clipboard is {} clipboard)await clipboard.SetTextAsync(string.Join(Environment.NewLine,paths));SetStatus("File paths copied.");break;
                case "terminal":LinuxShell.Terminal(paths[0]);break;
                case "float":
                    if(preferences.Value.FloatBehavior=="addToExisting"&&shelves.LastOrDefault(s=>s.IsVisible) is {} existing){existing.Add(paths);existing.Activate();break;}
                    var shelf=new FloatShelf(paths,this);shelves.Add(shelf);shelf.Closed+=(_,_)=>shelves.Remove(shelf);shelf.Show();break;
                case "move":await RunAsync((destination,p,t)=>FileMove.MoveAsync(paths,destination,p,t),"Move");files.Clear();break;
            }
        }
        catch(Exception error){await Dialogs.Message(this,"File action could not finish",L10n.T(error.Message));}
    }
    internal void UseFiles(IEnumerable<string> paths){files.Clear();Add(paths);ShowHomeAndActivate();}
    private void ShowHomeAndActivate(){page="home";Build();Activate();}
}
