using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nashi.Core;
namespace Nashi.Windows;

internal static class DownloadSaveChecks
{
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Children(child))yield return nested;}
    }
    private static void Capture(Window window,string path)
    {
        window.UpdateLayout();var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);ImageEngine.Save(image,path,"png");
    }
    internal static void Run(string root,Action<bool,string> check)
    {
        var directory=Path.Combine(root,"save-dialog-"+Guid.NewGuid().ToString("N"));var library=new LibraryStore(Path.Combine(directory,"library"));var defaultFolder=Path.Combine(directory,"default");var chosenFolder=Path.Combine(directory,"chosen");
        var preferences=new PreferencesStore(library.DirectoryPath);preferences.Save(new(){KeepInTray=false,Feedback=false,DownloadFolder=defaultFolder});
        var owner=new MainWindow(library:library,checkForUpdates:false,offerToolSetup:false);owner.Show();
        var previousLanguage=L10n.Language;var previousTheme=Theme.Dark?"dark":"light";
        try
        {
            foreach(var appearance in new[]{"light","dark"})foreach(var language in new[]{"en","tr"})
            {
                Theme.Apply(appearance);L10n.Language=language;
                var dialog=new DownloadSaveWindow(owner,defaultFolder,"Web media",_=>Task.FromResult("YouTube başlığı — İzmir 🍐")){Width=520};
                dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
                {
                    check(dialog.NameField.Text=="YouTube başlığı — İzmir 🍐"&&dialog.DownloadButton.IsEnabled,"Media title prefills the editable name in "+language);
                    Capture(dialog,Path.Combine(root,$"download-save-{appearance}-{language}.png"));
                    dialog.NameField.Text="../outside";check(!dialog.DownloadButton.IsEnabled,"Invalid names disable Download");
                    if(appearance=="dark"&&language=="tr")Capture(dialog,Path.Combine(root,"download-save-error-dark-tr.png"));
                    dialog.Close();
                });dialog.ShowDialog();
                var settings=new SettingsWindow(owner,preferences,()=>{},()=>Task.CompletedTask,library);settings.Show();DesktopInput.Pump(50);Children(settings).OfType<ListBox>().First().SelectedItem="Folders";DesktopInput.Pump(50);
                var toggle=Children(settings).OfType<CheckBox>().Single(box=>box.Content as string==L10n.Text("Ask for a name and folder before downloading media"));
                check(toggle.IsChecked==true,"Ask-before-download is discoverable and enabled in Folders");Capture(settings,Path.Combine(root,$"download-settings-{appearance}-{language}.png"));
                toggle.IsChecked=false;check(!new PreferencesStore(library.DirectoryPath).Value.AskBeforeDownload,"Settings persists direct-download mode");toggle.IsChecked=true;settings.Close();
            }
            var title=new TaskCompletionSource<string>();var edited=new DownloadSaveWindow(owner,defaultFolder,"Web media",_=>title.Task);
            edited.Loaded+=(_,_)=>edited.Dispatcher.BeginInvoke(async()=>
            {
                edited.NameField.Text="My chosen name";title.SetResult("Original YouTube title");await Task.Delay(50);
                check(edited.NameField.Text=="My chosen name"&&edited.DownloadButton.IsEnabled,"A late media title never replaces a name the user edited");edited.Close();
            });edited.ShowDialog();
            var cancelled=false;var pending=new DownloadSaveWindow(owner,defaultFolder,"Web media",token=>{token.Register(()=>cancelled=true);return Task.Delay(Timeout.Infinite,token).ContinueWith(_=>"unused",token);});
            pending.Loaded+=(_,_)=>pending.Dispatcher.BeginInvoke(()=>pending.Close());pending.ShowDialog();DesktopInput.Pump(50);check(cancelled,"Closing the dialog cancels pending title lookup");
            var failed=new DownloadSaveWindow(owner,defaultFolder,"Web media",_=>Task.FromException<string>(new IOException("Fixture")));
            failed.Loaded+=(_,_)=>failed.Dispatcher.BeginInvoke(()=>{failed.NameField.Text="Manual title";check(failed.DownloadButton.IsEnabled,"Title lookup failure still allows a manual name");failed.Close();});failed.ShowDialog();
            var accessible=new DownloadSaveWindow(owner,defaultFolder,"Web media");
            accessible.Loaded+=async(_,_)=>
            {
                var status=Children(accessible).OfType<TextBlock>().Single(text=>AutomationProperties.GetAutomationId(text)=="DownloadStatus");
                check(UIElementAutomationPeer.CreatePeerForElement(status).GetLiveSetting()==AutomationLiveSetting.Polite,"Download status exposes a polite UIA live region");
                var handle=new WindowInteropHelper(accessible).Handle;AutomationElement? element=null;var notification=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);AutomationEventHandler listener=(_,_)=>notification.TrySetResult(true);
                try
                {
                    await Task.Run(()=>{element=AutomationElement.FromHandle(handle);Automation.AddAutomationEventHandler(AutomationElementIdentifiers.LiveRegionChangedEvent,element,TreeScope.Subtree,listener);});
                    accessible.NameField.Focus();accessible.NameField.Text="../outside";await Task.WhenAny(notification.Task,Task.Delay(3000));
                    check(notification.Task.IsCompletedSuccessfully,"Changing validation status raises a native UIA live-region event");
                    check(accessible.NameField.IsKeyboardFocused,"Announcing an invalid name keeps keyboard focus in the name field");
                }
                finally{if(element is not null)await Task.Run(()=>Automation.RemoveAutomationEventHandler(AutomationElementIdentifiers.LiveRegionChangedEvent,element,listener));accessible.Close();}
            };accessible.ShowDialog();

            const string image="https://raw.githubusercontent.com/orkunisitmak/nashi-app/4f051ab04bc48ea2efb5338abed4990536cb4d97/Resources/Brand/NashiMark.png";
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            timer.Tick+=(_,_)=>
            {
                var dialog=Application.Current.Windows.OfType<DownloadSaveWindow>().FirstOrDefault();if(dialog is null||dialog.ReadingTitle)return;timer.Stop();dialog.NameField.Text="Özel indirme";dialog.FolderField.Text=chosenFolder;dialog.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();owner.OnGlobalDrop("download:image",new DataObject(DataFormats.UnicodeText,image),null);
            var until=DateTime.UtcNow.AddSeconds(20);while(library.Entries.Count==0&&DateTime.UtcNow<until)DesktopInput.Pump(50);timer.Stop();
            var output=library.Entries.SingleOrDefault()?.AvailablePaths.SingleOrDefault();
            check(output is not null&&output==Path.Combine(chosenFolder,"Özel indirme.png")&&new FileInfo(output).Length>0,"A confirmed wheel download uses the chosen name and folder");
            check(new PreferencesStore(library.DirectoryPath).Value.DownloadFolder==defaultFolder,"A one-time folder choice preserves the default download folder");
            var cancelTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};cancelTimer.Tick+=(_,_)=>{var dialog=Application.Current.Windows.OfType<DownloadSaveWindow>().FirstOrDefault();if(dialog is null)return;cancelTimer.Stop();dialog.Close();};
            cancelTimer.Start();owner.OnGlobalDrop("download:image",new DataObject(DataFormats.UnicodeText,image),null);DesktopInput.Pump(250);cancelTimer.Stop();
            check(library.Entries.Count==1&&!Directory.Exists(defaultFolder),"Cancelling the save dialog starts no download and creates no output folder");
        }
        finally
        {
            foreach(var child in Application.Current.Windows.OfType<Window>().Where(window=>window.Owner==owner).ToArray())child.Close();owner.Close();L10n.Language=previousLanguage;Theme.Apply(previousTheme);
        }
    }
}
