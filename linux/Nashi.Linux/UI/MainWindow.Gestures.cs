using Avalonia.Controls;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux.UI;

/// Global shortcuts, the gesture layer and running in the background.
public sealed partial class MainWindow
{
    internal LinuxSettings LinuxSettings{get;private set;}=LinuxSettings.Load();
    private GlobalShortcuts? shortcuts;private readonly List<GestureWindow> gestures=[];private bool quitting;
    /// Shortcut ids KDE refused (keys owned by another app), shown in Settings.
    internal Dictionary<string,string> ShortcutProblems{get;}=[];
    internal bool ShortcutsAvailable=>shortcuts is not null;

    internal IReadOnlyList<ToolChoice> ChoicesFor(IReadOnlyList<string> paths,string mode)
    {
        if(paths.Count==0)return [];
        if(mode=="actions")return FileActionChoices();
        var formats=FeatureCatalog.Formats(paths);
        if(mode=="convert")return formats.Select(f=>new ToolChoice("convert:"+f,f.ToUpperInvariant(),"↗")).ToList();
        var list=FeatureCatalog.Tools(paths).Select(id=>new ToolChoice(id,FeatureCatalog.ToolNames.GetValueOrDefault(id,id),Ui.Symbol(id))).ToList();
        if(formats.Length>0)list.Insert(0,new("convert","Convert","⇄"));return list;
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

    private void OnGlobalShortcut(string id)
    {
        GestureWindow.Log("shortcut "+id);
        switch(id)
        {
            case "gesture":StartGesture(null);break;
            case "convert" or "tools" or "actions":StartGesture(id);break;
            case "clipboard":_=Ui.Guard(ClipboardWheelAsync);break;
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
        if(paths.Length==0){Bring();SetStatus("Copy files in your file manager first, then press the shortcut.");return;}
        StartGesture("tools",paths);
    }
    /// Runs a slice chosen on the gesture layer. File actions run in place; tools and
    /// conversions bring Nashi forward for their options and progress.
    internal async Task RunGesture(string[] paths,ToolChoice choice)
    {
        files.Clear();Add(paths);
        if(choice.Id is "open" or "reveal" or "copy" or "copyPath" or "terminal" or "float"){await PerformFileAction(choice.Id,paths);return;}
        Bring();
        if(FileActionChoices().Any(a=>a.Id==choice.Id))await PerformFileAction(choice.Id,paths);else await EditAsync(choice);
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
    internal void SaveLinuxSettings(LinuxSettings next){next.Save();LinuxSettings=next;}
}
