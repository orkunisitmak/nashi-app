using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nashi.Core;
namespace Nashi.Windows;

internal static class LinkDownloadChecks
{
    private static IEnumerable<DependencyObject> Children(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Children(child))yield return nested;}}
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Capture(Window window,string path){window.UpdateLayout();var content=(FrameworkElement)window.Content;var margin=content.Margin;var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth+margin.Left+margin.Right),(int)Math.Ceiling(content.ActualHeight+margin.Top+margin.Bottom),96,96,PixelFormats.Pbgra32);bitmap.Render(window);ImageEngine.Save(bitmap,path,"png");}
    internal static void Run(string root)
    {
        var results=new List<object>();var failures=new List<string>();void Check(bool passed,string message){results.Add(new{passed,message});if(!passed)failures.Add(message);}
        try{Run(root,Check);}catch(Exception error){failures.Add(error.ToString());}
        File.WriteAllText(Path.Combine(root,"link-checks.json"),JsonSerializer.Serialize(new{checks=results.Count,failures,results}));if(failures.Count>0)throw new Exception(string.Join(Environment.NewLine,failures));
    }
    private static void Run(string root,Action<bool,string> check)
    {
        const string reel="https://www.instagram.com/reels/DZLzjtnRpVN/";const string canonical="https://www.instagram.com/reel/DZLzjtnRpVN/";
        const string image="https://raw.githubusercontent.com/orkunisitmak/nashi-app/4f051ab04bc48ea2efb5338abed4990536cb4d97/Resources/Brand/NashiMark.png";
        var oldLanguage=L10n.Language;var oldAppearance=Theme.Dark?"dark":"light";var oldWheel=WheelPresentation.Preferences;WheelPresentation.Preferences=oldWheel with{Animations=false,WheelFinish="solid"};
        var library=new LibraryStore(Path.Combine(root,"library"));var preferences=new PreferencesStore(library.DirectoryPath);var destination=Path.Combine(root,"downloads");preferences.Save(new(){KeepInTray=false,Feedback=false,DownloadFolder=destination});
        var owner=new MainWindow(library:library,checkForUpdates:false,offerToolSetup:false);owner.Show();
        try
        {
            foreach(var appearance in new[]{"light","dark"})foreach(var locale in new[]{"en","tr"})
            {
                Theme.Apply(appearance);L10n.Language=locale;string? action=null;string? url=null;var clipboardReads=0;
                var window=new LinkDownloadWindow(owner,(payload,id)=>{action=id;url=payload.Url;},()=>{clipboardReads++;return reel;}){Width=520,Height=420};window.Show();
                check(clipboardReads==0,"Opening the link picker never reads the clipboard automatically");window.Paste();
                check(window.DownloadButtons.Select(button=>AutomationProperties.GetAutomationId(button)).SequenceEqual(new[]{"download:video","download:audio"}),"A copied Instagram reel offers video and audio in "+locale+"/"+appearance);
                check(window.Address.Text==reel&&clipboardReads==1,"Paste reads once and displays the exact supplied address");Capture(window,Path.Combine(root,$"link-{appearance}-{locale}.png"));
                window.SetLink("not a link");check(window.DownloadButtons.Count==0,"Invalid edited links remove stale download choices");if(appearance=="dark"&&locale=="tr")Capture(window,Path.Combine(root,"link-invalid-dark-tr.png"));
                window.SetLink(reel);Click(window.DownloadButtons.Single(button=>AutomationProperties.GetAutomationId(button)=="download:audio"));check(action=="download:audio"&&url==canonical&&!window.IsVisible,"Choosing audio dispatches the canonical reel once and closes the picker");
            }
            var busy=new LinkDownloadWindow(owner,(_,_)=>{},()=>throw new COMException("Clipboard busy",unchecked((int)0x800401D0)));busy.Show();busy.SetLink(reel);busy.Paste();check(busy.Address.Text==""&&busy.DownloadButtons.Count==0,"A busy clipboard clears stale link choices");Capture(busy,Path.Combine(root,"link-clipboard-busy.png"));busy.Close();
            var empty=new LinkDownloadWindow(owner,(_,_)=>{});empty.Show();check(empty.DownloadButtons.Count==0,"An empty picker offers paste without starting a download");Capture(empty,Path.Combine(root,"link-empty.png"));empty.SetLink("https://www.youtube.com/watch?v=jNQXAC9IVRw");check(empty.DownloadButtons.Count==3,"YouTube keeps video, audio and thumbnail choices");empty.SetLink("https://www.instagram.com/p/Fixture/");check(empty.DownloadButtons.Count==1&&AutomationProperties.GetAutomationId(empty.DownloadButtons[0])=="download:photos","Photo posts retain their photo-selection route");empty.Close();
            // Real WPF OLE handlers, with a carried browser URL and Ctrl state; no physical pointer dependency.
            var selected=new List<string>();string? carried=null;var wheel=new GlobalDropWheel("tools",1,(data,id)=>{selected.Add(id);carried=DragPayload.Parse(data)?.Url;},()=>"https://www.youtube.com/watch?v=jNQXAC9IVRw",()=>true);wheel.Show();DesktopInput.Pump(40);
            var constructor=typeof(DragEventArgs).GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(c=>c.GetParameters().Length==5);
            var drag=(DragEventArgs)constructor.Invoke([new DataObject(DataFormats.UnicodeText,reel),DragDropKeyStates.ControlKey|DragDropKeyStates.LeftMouseButton,DragDropEffects.Copy,wheel,new Point(wheel.ActualWidth/2,wheel.ActualHeight*.18)]);drag.RoutedEvent=DragDrop.DragEnterEvent;wheel.RaiseEvent(drag);
            check(wheel.Revealed&&drag.Effects==DragDropEffects.Copy,"Ctrl/tools OLE link entry reveals a selectable Instagram download wheel");Capture(wheel,Path.Combine(root,"ctrl-link-wheel.png"));drag.Handled=false;drag.RoutedEvent=DragDrop.DropEvent;wheel.RaiseEvent(drag);
            check(selected.SequenceEqual(new[]{"download:video"})&&carried==canonical,"Ctrl link OLE drop dispatches video once and preserves the carried reel over hints");
            owner.OpenCopiedLink(()=>reel);var opened=Application.Current.Windows.OfType<LinkDownloadWindow>().Single();check(opened.Address.Text==reel,"The main copied-link entry fills the chooser from the explicit clipboard read");owner.OpenCopiedLink(()=>reel);check(Application.Current.Windows.OfType<LinkDownloadWindow>().Count()==1,"Repeated copied-link entry reuses one picker");
            var setBusy=typeof(MainWindow).GetMethod("SetBusy",BindingFlags.Instance|BindingFlags.NonPublic)!;setBusy.Invoke(owner,[true]);
            check(opened.IsVisible&&opened.DownloadButtons.All(button=>!button.IsEnabled)&&!opened.Address.IsEnabled,"A main operation disables copied-link actions while preserving the open picker");Capture(opened,Path.Combine(root,"link-operation-busy.png"));
            Click(opened.DownloadButtons[0]);check(opened.IsVisible&&opened.Address.Text==reel,"A forced busy selection does not close or lose the copied link");
            check(Children(opened).OfType<TextBlock>().Any(text=>text.Text==L10n.Text("Finish or cancel the current operation first.")),"The busy picker explains how to continue");setBusy.Invoke(owner,[false]);check(opened.DownloadButtons.All(button=>button.IsEnabled)&&opened.Address.IsEnabled&&opened.Address.Text==reel,"Completing the main operation restores the original link choices");opened.Close();
            var cancelSeen=false;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};timer.Tick+=(_,_)=>{var save=Application.Current.Windows.OfType<DownloadSaveWindow>().FirstOrDefault();if(save is null)return;cancelSeen=save.DirectoryPath==destination;timer.Stop();save.Close();};
            owner.OpenCopiedLink(()=>image);opened=Application.Current.Windows.OfType<LinkDownloadWindow>().Single();timer.Start();Click(opened.DownloadButtons.Single());DesktopInput.Pump(200);timer.Stop();check(cancelSeen&&library.Entries.Count==0&&!Directory.Exists(destination),"Copied-link download honors ask-before-download and cancelling creates no output");
            preferences.Save(preferences.Value with{AskBeforeDownload=false});owner.Close();owner=new MainWindow(library:library,checkForUpdates:false,offerToolSetup:false);owner.Show();owner.OpenCopiedLink(()=>image);opened=Application.Current.Windows.OfType<LinkDownloadWindow>().Single();Click(opened.DownloadButtons.Single());
            var until=DateTime.UtcNow.AddSeconds(25);while(library.Entries.Count==0&&DateTime.UtcNow<until)DesktopInput.Pump(40);var output=library.Entries.SingleOrDefault()?.AvailablePaths.SingleOrDefault();
            check(output is not null&&Path.GetDirectoryName(output)==destination&&Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(output))).Equals("27128b31fa9f3b7cf6c21c4b377be387b1503159b0227335144a17c2badf489a",StringComparison.OrdinalIgnoreCase),"With asking off, a copied-link action downloads unchanged image bytes into the saved folder");
            check(new PreferencesStore(library.DirectoryPath).Value.DownloadFolder==destination,"Both entry routes preserve the default destination preference");
        }
        finally{foreach(var child in Application.Current.Windows.OfType<Window>().Where(window=>window.Owner==owner).ToArray())child.Close();owner.Close();L10n.Language=oldLanguage;Theme.Apply(oldAppearance);WheelPresentation.Preferences=oldWheel;}
    }
}
