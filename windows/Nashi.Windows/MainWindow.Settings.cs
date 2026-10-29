using System.Windows;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class MainWindow
{
    private string[]? pendingLaunch;private System.Windows.Threading.DispatcherTimer? launchTimer;
    internal void AcceptLaunch(string[] paths)
    {
        ShowMain();if(paths.Length==0)return;if(busy||!DesktopGeometry.IsInteractive(this)){pendingLaunch=paths;launchTimer??=new(){Interval=TimeSpan.FromMilliseconds(200)};launchTimer.Stop();launchTimer.Tick-=AcceptPending;launchTimer.Tick+=AcceptPending;launchTimer.Start();return;}files.Clear();Add(paths);ShowLibrary(false);
    }
    private void AcceptPending(object? sender,EventArgs e){if(busy||!DesktopGeometry.IsInteractive(this))return;launchTimer?.Stop();if(pendingLaunch is {} paths){pendingLaunch=null;AcceptLaunch(paths);}}
    private TrayHost? tray;private bool quitting;
    private void SetupDesktop()
    {
        globalDrag=new(this,preferences,OnGlobalDrop,OpenCopiedLink);
        try{tray=new(this,ShowMain,TrayMenu);}catch(InvalidOperationException error){L10n.SetText(status,error.Message);}
        Closing+=(_,e)=>{if(!e.Cancel&&tray is not null&&preferences.Value.KeepInTray&&!quitting){e.Cancel=true;Hide();}};
        // Without a tray icon (Explorer not ready at sign-in), a hidden Nashi could not be reached.
        Closed+=(_,_)=>{launchTimer?.Stop();tray?.Dispose();};if(tray is not null&&Environment.GetCommandLineArgs().Contains("--background"))Hide();
    }
    private void ShowMain(){Show();WindowState=WindowState.Normal;Activate();if(monitorToolSetup)Dispatcher.BeginInvoke(OfferFirstRunTools);}
    private System.Windows.Controls.ContextMenu TrayMenu()
    {
        var menu=new System.Windows.Controls.ContextMenu();
        void Item(string text,Action action){var item=new System.Windows.Controls.MenuItem();L10n.Bind(item,System.Windows.Controls.HeaderedItemsControl.HeaderProperty,text);item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("Open Nashi",ShowMain);Item("Library",()=>{ShowMain();ShowLibrary(true);});Item("Download copied link",OpenCopiedLink);Item("Settings",()=>{ShowMain();ShowSettings();});Item(update is null?"Check for updates":"Update to "+update.Version,()=>{ShowMain();_ = CheckUpdatesAsync(true);});menu.Items.Add(new System.Windows.Controls.Separator());Item("Quit Nashi",()=>{if(busy){ShowMain();L10n.SetText(status,"Finish or cancel the current operation before quitting.");return;}quitting=true;Application.Current.Shutdown();});return menu;
    }
    private void ApplyPreferences(){L10n.Language=preferences.Value.Language;Feedback.Enabled=preferences.Value.Feedback;WheelPresentation.Preferences=preferences.Value;}
    private void ShowSettings(){new SettingsWindow(this,preferences,ApplyPreferences,()=>CheckUpdatesAsync(true),library).ShowDialog();appearance.SelectedItem=char.ToUpper(library.Appearance[0])+library.Appearance[1..];}
    private void OpenCopiedLink()
    {
        if(busy)return;try{var payload=WebMediaPayload.Parse(Clipboard.GetText());if(payload is null){L10n.SetText(status,"Copy a public media link first.");return;}Show();WindowState=WindowState.Normal;Activate();link.Text=payload.Url;SetDownloadKind(payload);OpenDownload();}catch(Exception error)when(ClipboardImport.IsClipboardError(error)){L10n.SetText(status,"Clipboard is busy. Try again.");}
    }
    private void SetDownloadKind(WebMediaPayload payload,string? chosen=null)
    {
        var formats=payload.Kind switch{"youtube"=>new[]{"video","audio","thumbnail"},"instagramPhotos"=>["photos"],"instagramReel"=>["video","audio"],"image"=>["image"],_=>["video","audio","image"]};downloadKind.ItemsSource=formats;downloadKind.SelectedItem=formats.Contains(chosen??"")?chosen:formats[0];
    }
    private void OnGlobalDrop(string action,IDataObject data,string? hint)
    {
        if(busy)return;var paths=data.GetData(DataFormats.FileDrop) as string[];var payload=DragPayload.Parse(data,hint);
        Dispatcher.BeginInvoke(async()=>
        {
            if(busy)return;
            if(paths is {Length:>0}&&!action.StartsWith("download:",StringComparison.Ordinal)){files.Clear();Add(paths);if(action.StartsWith("convert:",StringComparison.Ordinal))await EditAsync(new(action,"Convert","↗"));else if(new[]{"share","float","copy","move","copyPath","terminal"}.Contains(action))await PerformFileAction(action,paths);else await EditAsync(new(action,FeatureCatalog.ToolNames.GetValueOrDefault(action,action),"✦"));}
            else if(payload is not null){link.Text=payload.Url;SetDownloadKind(payload,action.StartsWith("download:",StringComparison.Ordinal)?action[9..]:null);Show();WindowState=WindowState.Normal;Activate();OpenDownload();}
        });
    }
}
