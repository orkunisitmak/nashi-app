using Avalonia.Controls;
using Avalonia.Input.Platform;
using Nashi.Core;
using Nashi.Linux.Engine;
namespace Nashi.Linux.UI;

/// Linux-only extras on top of the shared 27-tool catalog: Copy Text (OCR) for images,
/// Send to Phone (KDE Connect) and Rename for any files.
public sealed partial class MainWindow
{
    internal static string? KdeConnect=>LinuxShell.FindInPath("kdeconnect-cli");
    /// Extra tools for these files, appended after the catalog's own.
    internal static IEnumerable<ToolChoice> ExtraTools(IReadOnlyList<string> paths)
    {
        if(paths.Count>0&&paths.All(p=>FeatureCatalog.IsImage(p)||FeatureCatalog.Extension(p)=="pdf")&&PdfDocumentMedia.OcrAvailable)yield return new("copyText","Copy Text","¶");
        if(paths.Count>0)yield return new("rename","Rename","✎");
        if(paths.Count==1&&FeatureCatalog.IsImage(paths[0])&&Wallpaper.Supported)yield return new("wallpaper","Set as Wallpaper","◩");
        // Videos over an app's upload limit: one petal shrinks them to fit, no options asked.
        if(paths.Count>0&&paths.All(FeatureCatalog.IsVideo))
        {
            long Largest(){try{return paths.Max(p=>new FileInfo(p).Length);}catch(IOException){return 0;}}
            var largest=Largest();
            foreach(var (id,title,kilobytes) in FitTargets)if(largest>kilobytes*1024L)yield return new("fit:"+id,title,"⇩");
        }
    }
    /// The gesture petals for ToolEditor.SizePresets (Discord and WhatsApp; email is rarely needed for one video).
    internal static readonly (string Id,string Title,int Kilobytes)[] FitTargets=[("discord","Fit for Discord",ToolEditor.SizePresets[0].Kilobytes),("whatsapp","Fit for WhatsApp",ToolEditor.SizePresets[1].Kilobytes)];
    /// Compresses videos to an upload limit with the shared two-pass encoder.
    private async Task FitAsync(IReadOnlyList<string> inputs,string target)
    {
        var (_,title,kilobytes)=FitTargets.First(f=>f.Id==target);
        if(!HasMedia){await OfferMediaSetup();return;}
        await RunAsync((destination,p,t)=>new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe")).ToolAsync("compress",inputs,destination,new MediaOptions{Quality=.8,TargetBytes=kilobytes*1024L},p,t),title);
    }
    internal static IEnumerable<ToolChoice> ExtraActions(){if(KdeConnect is not null)yield return new("phone","Send to Phone","📱");}

    /// Recognizes the text in images (and PDF pages) with Tesseract and puts it on the clipboard.
    private async Task CopyTextAsync(IReadOnlyList<string> inputs)
    {
        var texts=new List<string>();SetBusy(true);
        try
        {
            foreach(var file in inputs)
            {
                if(FeatureCatalog.Extension(file)=="pdf")texts.Add(await PdfDocumentMedia.Text(file,"",CancellationToken.None));
                else{var png=await Task.Run(()=>{using var image=ImageEngine.Load(file);return image.ToByteArray(ImageMagick.MagickFormat.Png);});texts.Add(await PdfDocumentMedia.Recognize(png,CancellationToken.None));}
            }
        }
        finally{SetBusy(false);}
        var text=string.Join("\n\n",texts.Where(t=>!string.IsNullOrWhiteSpace(t))).Trim();
        if(text.Length>0&&Clipboard is {} clipboard)await clipboard.SetTextAsync(text);
        if(!IsVisible){_=NotifyAsync(L10n.T(text.Length==0?"No text found":"Text copied"),text.Length==0?L10n.T("No readable text was found. Try a sharper or larger image."):text.Length>160?text[..160]+"…":text);return;}
        await Dialogs.TextResult(this,"Copy Text",text.Length==0?L10n.T("No readable text was found. Try a sharper or larger image."):text,text.Length>0);
    }

    private async Task SetWallpaperAsync(string path)
    {
        try
        {
            await Wallpaper.SetAsync(path);SetStatus(L10n.T("Wallpaper set."));
            if(!IsVisible)_=NotifyAsync(L10n.T("Wallpaper set."),Path.GetFileName(path));
        }
        catch(Exception error)when(error is IOException or InvalidOperationException or UnauthorizedAccessException or ImageMagick.MagickException)
        {
            if(!IsVisible)_=NotifyAsync(L10n.T("Set as Wallpaper"),L10n.T(error.Message));else await Dialogs.Message(this,"Set as Wallpaper",L10n.T(error.Message));
        }
    }

    private async Task RenameAsync(IReadOnlyList<string> paths)
    {
        Bring();var dialog=new RenameWindow(paths);if(!await dialog.ShowDialog<bool>(this)||dialog.Plan.Count==0)return;
        try
        {
            var renamed=RenameWindow.Apply(dialog.Plan);var map=dialog.Plan.Select((p,i)=>(p.From,To:renamed[i])).ToDictionary(p=>p.From,p=>p.To);
            var next=files.Select(f=>map.GetValueOrDefault(f,f)).ToList();files.Clear();Add(next);
            Record("Rename",renamed);SetStatus(L10n.T($"Renamed {renamed.Count} {(renamed.Count==1?"file":"files")}."));
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){await Dialogs.Message(this,"Rename",L10n.T(error.Message));}
    }
    /// Sends files to a phone paired in KDE Connect (Linux's answer to the Windows share sheet).
    private async Task SendToPhoneAsync(IReadOnlyList<string> paths)
    {
        var cli=KdeConnect;if(cli is null)return;
        var listed=(await ProcessRunner.RunAsync(cli,["-a","--id-name-only"])).Output.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
            .Select(l=>l.Split(' ',2)).Where(p=>p.Length==2).Select(p=>(Id:p[0],Name:p[1])).ToList();
        if(listed.Count==0){await Dialogs.Message(this,"Send to Phone","No phone is reachable. Open KDE Connect on your phone and pair it with this computer, then try again.");return;}
        var device=listed[0];
        if(listed.Count>1)
        {
            var choice=await Dialogs.Pick(this,"Send to Phone","Which device?",listed.Select(d=>d.Name).ToArray());if(choice is null)return;device=listed[choice.Value];
        }
        foreach(var path in paths)await ProcessRunner.RunAsync(cli,["-d",device.Id,"--share",path]);
        SetStatus(L10n.T($"Sent {paths.Count} {(paths.Count==1?"file":"files")} to")+" "+device.Name);
        if(!IsVisible)_=NotifyAsync(L10n.T("Send to Phone"),L10n.T($"Sent {paths.Count} {(paths.Count==1?"file":"files")} to")+" "+device.Name);
    }
}
