using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux.UI;

/// Global shortcuts, the gesture layer and running in the background.
public sealed partial class MainWindow
{
    internal LinuxSettings LinuxSettings{get;private set;}=LinuxSettings.Load();
    private GlobalShortcuts? shortcuts;private Notifications? notifications;private readonly List<GestureWindow> gestures=[];private bool quitting;
    /// Shortcut ids KDE refused (keys owned by another app), shown in Settings.
    internal Dictionary<string,string> ShortcutProblems{get;}=[];
    internal bool ShortcutsAvailable=>shortcuts is not null;

    /// The last tool or conversion, offered first on the Tools wheel when it fits the new files.
    internal ToolChoice? LastChoice{get;set;}
    internal IReadOnlyList<ToolChoice> ChoicesFor(IReadOnlyList<string> paths,string mode)
    {
        if(paths.Count==0)return [];
        if(mode=="actions")return FileActionChoices();
        var formats=FeatureCatalog.Formats(paths);
        if(mode=="convert")return formats.Select(f=>new ToolChoice("convert:"+f,f.ToUpperInvariant(),"↗")).ToList();
        var list=FeatureCatalog.Tools(paths).Select(id=>new ToolChoice(id,FeatureCatalog.ToolNames.GetValueOrDefault(id,id),Ui.Symbol(id))).ToList();
        if(formats.Length>0)list.Insert(0,new("convert","Convert","⇄"));
        list.AddRange(ExtraTools(paths));
        if(LastChoice is {} last&&(last.Id.StartsWith("convert:",StringComparison.Ordinal)?formats.Contains(last.Id[8..]):list.Any(c=>c.Id==last.Id)))
            list.Insert(0,new(last.Id,L10n.T("Again")+": "+L10n.T(last.Title),"↻"));
        return list;
    }
    /// What a dragged web link can become (like the macOS web wheel).
    internal static IReadOnlyList<ToolChoice> WebChoices(string url)
    {
        var image=Uri.TryCreate(url,UriKind.Absolute,out var uri)&&new[]{".jpg",".jpeg",".png",".webp",".gif",".avif"}.Any(e=>uri.AbsolutePath.EndsWith(e,StringComparison.OrdinalIgnoreCase));
        List<ToolChoice> list=image?[new("web:image","Download image","↓")]:[new("web:video","Download video","▷"),new("web:audio","Audio MP3","♫"),new("web:thumbnail","Thumbnail","▣")];
        list.Add(new("web:copy","Copy link","↪"));list.Add(new("web:open","Open link","↗"));return list;
    }
    internal async Task RunWebGesture(string url,ToolChoice choice)
    {
        switch(choice.Id)
        {
            case "web:copy":if(Clipboard is {} clipboard)await clipboard.SetTextAsync(url);break;
            case "web:open":LinuxShell.Open(url);break;
            default:await DownloadUrlAsync(url,choice.Id[4..]);break;
        }
    }

    internal async Task NotifyAsync(string summary,string body)
    {
        if(Environment.GetEnvironmentVariable("NASHI_NO_GLOBAL_SHORTCUTS")=="1")return;
        notifications??=await Notifications.TryCreateAsync();if(notifications is not null)await notifications.ShowAsync(summary,body);
    }
    /// Desktop notifications for work that finishes while the window is hidden.
    internal async Task NotifyDoneAsync(string label,IReadOnlyList<string> outputs,string? error=null)
    {
        if(IsVisible&&IsActive||Environment.GetEnvironmentVariable("NASHI_NO_GLOBAL_SHORTCUTS")=="1")return;
        notifications??=await Notifications.TryCreateAsync();if(notifications is null)return;
        if(error is not null){await notifications.ShowAsync(L10n.T("Couldn’t finish"),L10n.T(error),[("show",L10n.T("Open Nashi"))],_=>Bring());return;}
        var body=string.Join("\n",outputs.Take(4).Select(Path.GetFileName))+(outputs.Count>4?"\n"+L10n.T($"and {outputs.Count-4} more"):"");
        await notifications.ShowAsync(L10n.T(label)+" · "+L10n.T($"Saved {outputs.Count} new {(outputs.Count==1?"file":"files")}. Originals kept."),body,
            [("default",L10n.T("Open")),("reveal",L10n.T("Show in Folder"))],action=>{if(outputs.Count==0)return;if(action=="reveal")LinuxShell.Reveal(outputs[0]);else LinuxShell.Open(outputs[0]);});
    }
    internal async Task InitShortcutsAsync()
    {
        shortcuts?.Dispose();shortcuts=null;ShortcutProblems.Clear();
        // Tests set NASHI_NO_GLOBAL_SHORTCUTS so they never change the desktop's real shortcuts.
        if(!LinuxSettings.GlobalShortcuts||Environment.GetEnvironmentVariable("NASHI_NO_GLOBAL_SHORTCUTS")=="1")return;
        shortcuts=await GlobalShortcuts.TryCreateAsync();if(shortcuts is null)return;
        shortcuts.Pressed+=id=>Dispatcher.UIThread.Post(()=>OnGlobalShortcut(id));
        foreach(var (id,title,_) in LinuxSettings.Actions)
        {
            var combo=LinuxSettings.Shortcuts.GetValueOrDefault(id,"");
            try
            {
                var accepted=await shortcuts.BindAsync(id,L10n.T(title),combo);
                if(combo.Length>0&&accepted is null)ShortcutProblems[id]=await shortcuts.OwnerAsync(combo) is {} owner?L10n.T("Already used by")+" "+owner:L10n.T("KDE did not accept these keys.");
            }
            catch(Exception error)when(error is Tmds.DBus.Protocol.DBusExceptionBase or IOException){ShortcutProblems[id]=error.Message;}
        }
    }
    /// Who already owns these keys outside Nashi, for the shortcut editor.
    internal async Task<string?> ShortcutOwnerAsync(string combo)=>shortcuts is null?null:await shortcuts.OwnerAsync(combo);

    /// Runs a global shortcut as if KDE had sent it (tests and the tray menu).
    internal void PressShortcut(string id)=>OnGlobalShortcut(id);
    private void OnGlobalShortcut(string id)
    {
        GestureWindow.Log("shortcut "+id);
        switch(id)
        {
            case "gesture":StartGesture(null);break;
            case "convert" or "tools" or "actions":StartGesture(id);break;
            case "clipboard":_=Ui.Guard(ClipboardWheelAsync);break;
            case "capture":_=Ui.Guard(CaptureAsync);break;
            case "open":Bring();break;
            case "download":Bring();_=Ui.Guard(PasteLinkAsync);break;
        }
    }
    internal void StartGesture(string? fixedMode,string[]? presetFiles=null)
    {
        CloseGestures();if(busy){Bring();SetStatus("Finish the current operation first.");return;}
        var screens=presetFiles is null?Screens.All.ToList():[Screens.Primary??Screens.All[0]];
        foreach(var screen in screens){var layer=new GestureWindow(this,screen,fixedMode,CloseGestures,presetFiles);gestures.Add(layer);layer.Show();}
    }
    internal IReadOnlyList<GestureWindow> Gestures=>gestures;
    internal void CloseGestures(){foreach(var layer in gestures.ToList())layer.Close();gestures.Clear();}
    /// Files copied in a file manager (Ctrl+C) get a wheel to click, without dragging.
    private async Task ClipboardWheelAsync()
    {
        string[] paths=[];
        if(Clipboard is {} clipboard)
        {
            if(await Avalonia.Input.Platform.ClipboardExtensions.TryGetFilesAsync(clipboard) is {Length:>0} items)paths=items.Select(i=>Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(i)).OfType<string>().ToArray();
            if(paths.Length==0)foreach(var format in new[]{ClipboardFiles.GnomeFiles,ClipboardFiles.UriList})
                if(await Avalonia.Input.Platform.ClipboardExtensions.TryGetValueAsync(clipboard,format) is {} bytes&&ClipboardFiles.ParseUris(System.Text.Encoding.UTF8.GetString(bytes)) is {Count:>0} parsed){paths=parsed.ToArray();break;}
        }
        // A copied image (a screenshot, an image from a browser) becomes a file in Library first.
        if(paths.Length==0&&Clipboard is {} images)
        {
            byte[]? png=await Avalonia.Input.Platform.ClipboardExtensions.TryGetValueAsync(images,ClipboardFiles.Png);
            if(png is not {Length:>0}&&await Avalonia.Input.Platform.ClipboardExtensions.TryGetBitmapAsync(images) is {} bitmap){using var stream=new MemoryStream();
#pragma warning disable CS0618 // PNG defaults are what we want here.
                bitmap.Save(stream);
#pragma warning restore CS0618
                png=stream.ToArray();}
            if(png is {Length:>0}){var file=ClipboardFiles.SaveImport(library.DirectoryPath,"Pasted image","png",png);Record("Paste",[file]);paths=[file];}
        }
        if(paths.Length==0){Bring();SetStatus("Copy files in your file manager first, then press the shortcut.");return;}
        StartGesture("tools",paths);
    }
    /// Lets you pick a part of the screen, saves it to Library and opens the Tools wheel on it
    /// (annotate, redact, crop, read a QR code, convert…). Uses the desktop's own screenshot tool.
    private async Task CaptureAsync()
    {
        var folder=Path.Combine(library.DirectoryPath,"Clipboard");Directory.CreateDirectory(folder);
        var file=SafePaths.Unique(folder,$"Screenshot {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
        (string Tool,string[] Arguments)[] tools=[("spectacle",["-b","-n","-r","-o",file]),("gnome-screenshot",["-a","-f",file]),("xfce4-screenshooter",["-r","-s",file]),("flameshot",["gui","-p",folder])];
        foreach(var (tool,arguments) in tools)
        {
            if(LinuxShell.FindInPath(tool) is not {} executable)continue;
            try{await ProcessRunner.RunAsync(executable,arguments,CancellationToken.None);}catch(IOException){return;}
            var made=File.Exists(file)?file:tool=="flameshot"?Directory.GetFiles(folder,"*.png").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault(f=>File.GetLastWriteTimeUtc(f)>DateTime.UtcNow.AddMinutes(-2)):null;
            if(made is null)return;   // selection cancelled
            Record("Screenshot",[made]);StartGesture("tools",[made]);return;
        }
        await Dialogs.Command(this,"Capture part of the screen","Install a screenshot tool, then try again:",LinuxShell.InstallCommand("spectacle"));
    }
    /// Runs a slice chosen on the gesture layer. File actions run in place; tools and
    /// conversions bring Nashi forward for their options and progress.
    internal async Task RunGesture(string[] paths,ToolChoice choice)
    {
        files.Clear();Add(paths);
        if(choice.Id is "open" or "reveal" or "copy" or "copyPath" or "terminal" or "float" or "phone"){await PerformFileAction(choice.Id,paths);return;}
        if(choice.Id=="copyText"){await CopyTextAsync(paths);return;}
        if(FileActionChoices().Any(a=>a.Id==choice.Id)){Bring();await PerformFileAction(choice.Id,paths);return;}
        // Conversions and simple tools run at once with their saved defaults (Settings can turn this off);
        // a desktop notification reports the result while the window stays out of the way.
        var id=choice.Id.StartsWith("convert:",StringComparison.Ordinal)?"convert":choice.Id;
        var quick=LinuxSettings.GestureQuickRun&&Quick(id is "mergePDF"?"mergePDFs":id)&&preferences.Value.OutputMode!="ask";
        if(!quick)Bring();
        await EditAsync(choice,quick);
    }
    internal void Bring()
    {
        if(!IsVisible)Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
        if(page!="home"){page="home";Build();}Activate();
    }
    /// Closing the window keeps Nashi in the tray (when enabled), so shortcuts keep working.
    private void OnClosing(object? sender,WindowClosingEventArgs e)
    {
        if(busy){e.Cancel=true;SetStatus("Finish the current operation or choose Cancel before closing Nashi.");return;}
        if(LinuxSettings.RunInBackground&&!quitting&&e.CloseReason!=WindowCloseReason.OSShutdown){e.Cancel=true;Hide();}
    }
    internal async Task QuitAsync()
    {
        if(busy){Bring();SetStatus("Finish the current operation or choose Cancel before closing Nashi.");return;}
        quitting=true;CloseGestures();
        if(shortcuts is not null){try{await shortcuts.DeactivateAsync();}catch(Exception error)when(error is Tmds.DBus.Protocol.DBusExceptionBase or IOException){}shortcuts.Dispose();}
        Close();(Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
    /// Wheel size, animations and sound from the shared preferences.
    internal void ApplyFeel(){var p=preferences.Value;PetalWheel.Scale=Math.Clamp(p.WheelScale,.85,1.4);PetalWheel.Animate=p.Animations;Sound.Enabled=p.Feedback;}
    internal void SaveLinuxSettings(LinuxSettings next){next.Save();LinuxSettings=next;StartWatching();}
    private FolderWatcher? watcher;
    internal int WatchedFolders=>watcher?.Count??0;
    /// (Re)starts the watched-folder rules from Settings → Automation.
    internal void StartWatching()
    {
        watcher?.Dispose();watcher=null;if(LinuxSettings.Watches.Count==0)return;
        watcher=new FolderWatcher(LinuxSettings.Watches,
            (path,format,token)=>Engine.Cli.ConvertAsync([path],format,Path.GetDirectoryName(path)!,preferences,tools,new Progress<OperationProgress>(),token),
            (rule,outputs,error)=>Dispatcher.UIThread.Post(()=>
            {
                var label="Convert to "+rule.Format.ToUpperInvariant();
                if(outputs.Count>0){Record(label,outputs);if(page=="home")RenderPage();}
                _=NotifyDoneAsync(label,outputs,error);
            }));
    }
}
