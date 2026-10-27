using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;

public sealed partial class MainWindow:Window
{
    private readonly ObservableCollection<string> files=[];
    private readonly Grid actions=new();
    private readonly TextBlock selection=new(){FontSize=23,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock status=Text("Files stay on your computer.",12,"Muted");
    private readonly TextBox link=new(){ToolTip="Paste a public video, image or audio page link"};
    private readonly ComboBox downloadKind=new(){ItemsSource=new[]{"video","audio","thumbnail"},SelectedIndex=0,MinWidth=110};
    private readonly Toolchain tools=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools","v1"));
    private readonly LibraryStore library;
    private readonly PreferencesStore preferences;private GlobalDragController? globalDrag;
    private readonly StackPanel home=new(),libraryPage=new(),libraryRows=new(),recentRows=new();
    private readonly TextBox recentSearch=new(){ToolTip="Find a recent file",Width=165};private readonly TextBlock recentCount=Text("0",11,"Muted");private Button? chooseFilesButton,clearButton;private readonly TextBlock fileDescription=Text("Images, videos, documents, and all the little things in between.",12,"Muted");
    private readonly TextBox search=new(){ToolTip="Find a file or tool in Library",MinWidth=200};
    private readonly DockPanel manage=new();private readonly FileGarden garden=new();private readonly FileDesk desk;private Button? infoButton;private readonly StackPanel webContent=new();private Window? webWindow;
    internal Window DownloadWindow=>webWindow??this;
    private readonly Button homeButton,libraryButton,updateButton,toolsSetupButton;private readonly ComboBox appearance;
    private readonly StackPanel workArea=new();
    private readonly ScrollViewer homeScroll=new(),libraryScroll=new();
    private bool busy,checking,toolSetupOffered,altHandled;private WindowsUpdate? update;private Border? updateBanner;
    private ToolSetupWindow? toolSetupWindow;
    private readonly bool monitorToolSetup;
    internal bool UpdateBannerVisible=>updateBanner?.Visibility==Visibility.Visible;
    internal IReadOnlyList<string> SelectedFiles=>files;
    internal bool LibraryVisible=>libraryPage.Visibility==Visibility.Visible;
    internal TextBox SearchField=>search;
    internal TextBox LinkField=>link;
    public MainWindow(string[]? initial=null,LibraryStore? library=null,bool checkForUpdates=true,bool offerToolSetup=true)
    {
        monitorToolSetup=checkForUpdates;
        this.library=library??new LibraryStore();preferences=new(this.library.DirectoryPath);ApplyPreferences();desk=new(files,preferences,DeskChoices,(choice,paths)=>_=DeskChosenAsync(choice,paths),()=>busy,DuplicateFile);desk.SelectionChanged+=QueueActions;desk.Preview=PreviewFile;desk.AddFiles=Choose;desk.Paste=PasteClipboard;desk.Clear=ClearDesk;Thumbnails.Ffmpeg=tools.Ready?tools.Executable("ffmpeg"):null;Theme.Apply(this.library.Appearance);Theme.Attach(this);
        // About one and a half default console windows, never larger than the work area.
        var work=SystemParameters.WorkArea;Title="Nashi";Width=Math.Min(1500,work.Width*.94);Height=Math.Min(840,work.Height*.92);MinWidth=900;MinHeight=640;WindowStartupLocation=WindowStartupLocation.CenterScreen;AllowDrop=true;
        Icon=BitmapFrame.Create(new Uri("pack://application:,,,/Assets/NashiIcon.png"));
        var root=new DockPanel{Margin=new(32,18,32,14)};Content=root;AddHandler(UIElement.PreviewMouseWheelEvent,new MouseWheelEventHandler(RoutePageMouseWheel),true);Notifications.Host(this);
        var header=new DockPanel{Margin=new(0,0,0,18)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var controls=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(controls,Dock.Right);header.Children.Add(controls);
        homeButton=Button("Home",()=>ShowLibrary(false));libraryButton=Button("Library",()=>{CloseWorkspace();ShowLibrary(true);});toolsSetupButton=Button(tools.Ready&&officeRuntime.Executable is not null?"Tools ready":"Finish setup",ShowToolSetupMenu);controls.Children.Add(toolsSetupButton);
        // Home and Library are one segmented control rather than two loose buttons.
        var pagesSwitch=new StackPanel{Orientation=Orientation.Horizontal};foreach(var tab in new[]{homeButton,libraryButton}){tab.Margin=new(0);tab.BorderThickness=new(0);pagesSwitch.Children.Add(tab);}var segment=new Border{CornerRadius=new(10),Padding=new(3),Margin=new(8,0,8,0),Child=pagesSwitch};segment.SetResourceReference(Border.BackgroundProperty,"Inset");controls.Children.Add(segment);
        appearance=new ComboBox{ItemsSource=new[]{"System","Light","Dark"},SelectedItem=char.ToUpper(this.library.Appearance[0])+this.library.Appearance[1..]};
        updateButton=Button("Check for updates",()=>ShowSettings("Updates",true));
        controls.Children.Add(Button("Settings",()=>ShowSettings()));
        var brand=new StackPanel{Orientation=Orientation.Horizontal};brand.Children.Add(new Image{Source=FileGarden.Artwork("NashiMark"),Width=30,Height=30,Margin=new(0,0,10,0)});brand.Children.Add(Text("Nashi",17));header.Children.Add(brand);foreach(var control in controls.Children.OfType<Button>().Concat(pagesSwitch.Children.OfType<Button>())){control.Padding=new(12,6,12,6);control.FontSize=12;}
        DockPanel.SetDock(status,Dock.Bottom);status.Margin=new(4,14,4,0);root.Children.Add(status);
        // Pages share one Grid cell; each ScrollViewer follows its page's visibility so the hidden one cannot swallow clicks and wheel input.
        var pages=new Grid();foreach(var(page,scroller)in new[]{((FrameworkElement)home,homeScroll),(libraryPage,libraryScroll)}){scroller.Content=page;scroller.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;scroller.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;scroller.SetBinding(VisibilityProperty,new System.Windows.Data.Binding(nameof(Visibility)){Source=page});pages.Children.Add(scroller);}root.Children.Add(pages);
        var updateRow=new DockPanel();DockPanel.SetDock(updateButton,Dock.Right);updateRow.Children.Add(updateButton);var updateCaption=new StackPanel();updateCaption.Children.Add(Text("A little fresher. Ready when you are.",16));updateCaption.Children.Add(Text("You choose when to download and install.",12,"Muted"));updateRow.Children.Add(updateCaption);updateBanner=Card(updateRow);updateBanner.Visibility=Visibility.Collapsed;home.Children.Add(updateBanner);
        // The title row carries the shortcut help as an info button at its far end.
        var titleRow=new DockPanel{Margin=new(0,10,0,0)};infoButton=new Button{Style=(Style)FindResource("IconButton"),Content=Icons.Create("info",17),Width=34,Height=34,VerticalAlignment=VerticalAlignment.Center};BuildInfo();AutomationProperties.SetName(infoButton,"Shortcuts");DockPanel.SetDock(infoButton,Dock.Right);titleRow.Children.Add(infoButton);homeTitle.FontSize=28;titleRow.Children.Add(homeTitle);home.Children.Add(titleRow);var intro=Text("Convert, fine-tune, and send them on their way.",14,"Muted");intro.Margin=new(0,4,0,0);home.Children.Add(intro);
        home.Children.Add(workArea);
        // The desk is the page: a toolbar on top, cards below, an invitation when empty.
        var toolbar=new DockPanel{Margin=new(20,14,14,6)};var picks=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};chooseFilesButton=Button("Add files",Choose);picks.Children.Add(chooseFilesButton);picks.Children.Add(Button("Paste",PasteClipboard));clearButton=Button("",ClearDesk);clearButton.Content=Icons.Labeled("broom","Clear",14);AutomationProperties.SetName(clearButton,"Clear");L10n.Bind(clearButton,ToolTipProperty,"Empty the desk. Its files stay in Library.");picks.Children.Add(clearButton);DockPanel.SetDock(picks,Dock.Right);toolbar.Children.Add(picks);
        var summary=new StackPanel{VerticalAlignment=VerticalAlignment.Center};selection.FontSize=16;selection.FontWeight=FontWeights.SemiBold;selection.TextTrimming=TextTrimming.CharacterEllipsis;selection.TextWrapping=TextWrapping.NoWrap;summary.Children.Add(selection);fileDescription.TextTrimming=TextTrimming.CharacterEllipsis;fileDescription.TextWrapping=TextWrapping.NoWrap;summary.Children.Add(fileDescription);toolbar.Children.Add(summary);
        var invitation=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};garden.HorizontalAlignment=HorizontalAlignment.Center;invitation.Children.Add(garden);var inviteTitle=Text("Drop files here",19);inviteTitle.HorizontalAlignment=HorizontalAlignment.Center;inviteTitle.Margin=new(0,18,0,4);invitation.Children.Add(inviteTitle);var inviteText=Text("Photos, videos, PDFs and documents can sit together.",13,"Muted");inviteText.HorizontalAlignment=HorizontalAlignment.Center;invitation.Children.Add(inviteText);var inviteButton=Primary("Choose files",Choose);inviteButton.HorizontalAlignment=HorizontalAlignment.Center;inviteButton.Margin=new(0,16,0,0);invitation.Children.Add(inviteButton);
        desk.SetChrome(toolbar,invitation);workArea.Children.Add(desk);
        actions.Margin=new(0,10,0,0);
        var web=new StackPanel();web.Children.Add(Text("Download a link",17));web.Children.Add(Text("Video, audio, or an image. Straight into your tools.",12,"Muted"));
        var download=new DockPanel{Margin=new(0,12,0,0)};var save=Primary("Download ↗",async()=>await DownloadAsync());DockPanel.SetDock(save,Dock.Right);download.Children.Add(save);DockPanel.SetDock(downloadKind,Dock.Right);download.Children.Add(downloadKind);download.Children.Add(link);web.Children.Add(download);
        web.Children.Add(Text("Ctrl+Shift+D  Use a copied link  ·  Public, downloadable media",11,"Muted"));webContent.Margin=new(24);webContent.Children.Add(Text("Download from the web",27));webContent.Children.Add(Text("A link. A file. Yours to work with.",14,"Muted"));var webCard=Card(web);webCard.Margin=new(0,20,0,0);webContent.Children.Add(webCard);BuildWebOptions();
        var webHeader=new DockPanel();var shortcut=Text("Ctrl+Shift+D",12,"Muted");DockPanel.SetDock(shortcut,Dock.Right);webHeader.Children.Add(shortcut);var arrow=Icons.Create("download-simple",26,"Gold");arrow.VerticalAlignment=VerticalAlignment.Center;arrow.Margin=new(0,0,16,0);DockPanel.SetDock(arrow,Dock.Left);webHeader.Children.Add(arrow);var caption=new StackPanel();caption.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Left);caption.Children.Add(Text("Download a link",15));caption.Children.Add(Text("Video, audio, or an image. Straight into your tools.",12,"Muted"));webHeader.Children.Add(caption);var webButton=Button("",OpenDownload);webButton.Content=webHeader;webButton.HorizontalContentAlignment=HorizontalAlignment.Stretch;webButton.Margin=new(0);webButton.Padding=new(18);webButton.BorderThickness=new(0);var webLanding=Card(webButton);webLanding.Padding=new(0);webLanding.Margin=new(0,14,0,0);workArea.Children.Add(actions);workArea.Children.Add(webLanding);
        var recentHeading=new DockPanel{Margin=new(0,24,0,10)};var all=Button("See all  ↗",()=>ShowLibrary(true));DockPanel.SetDock(all,Dock.Right);recentHeading.Children.Add(all);recentSearch.TextChanged+=(_,_)=>RenderLibrary();DockPanel.SetDock(recentSearch,Dock.Right);recentHeading.Children.Add(recentSearch);var recentTitle=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};recentTitle.Children.Add(Text("Freshly made",19));recentCount.Margin=new(10,0,0,0);recentTitle.Children.Add(recentCount);recentHeading.Children.Add(recentTitle);home.Children.Add(recentHeading);var recentCard=Card(recentRows);recentCard.Padding=new(7);home.Children.Add(recentCard);
        libraryPage.Children.Add(Text("Your Library",30));libraryPage.Children.Add(Text("Freshly made, pasted, and ready for another round.",14,"Muted"));
        var searchLabel=Text("Find a file or tool  ·  Ctrl+F",12,"Muted");searchLabel.Margin=new(4,20,4,0);libraryBrowse.Children.Add(searchLabel);libraryBrowse.Children.Add(search);libraryRows.Orientation=Orientation.Horizontal;var strip=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=libraryRows,Margin=new(0,12,0,0),Padding=new(0,0,0,6)};strip.PreviewMouseWheel+=(_,e)=>{if(strip.ScrollableWidth<=0)return;strip.ScrollToHorizontalOffset(strip.HorizontalOffset-e.Delta);e.Handled=true;};libraryBrowse.Children.Add(strip);BuildWorkspace();search.TextChanged+=(_,_)=>RenderLibrary();
        AutomationProperties.SetAutomationId(search,"LibrarySearch");AutomationProperties.SetAutomationId(link,"DownloadLink");
        Closing+=(_,e)=>{if(busy){e.Cancel=true;L10n.SetText(status,"Finish the current operation or choose Cancel before closing Nashi.");}};
        DragOver+=(_,e)=>{e.Effects=!busy&&(e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent(DataFormats.UnicodeText)||e.Data.GetDataPresent(DataFormats.Html)||e.Data.GetDataPresent("UniformResourceLocatorW"))?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        Drop+=(_,e)=>{if(!busy){if(!e.Data.GetDataPresent(DataFormats.FileDrop)&&DragPayload.Parse(e.Data) is not null)OnGlobalDrop("download:auto",e.Data,null);else{if(!LibraryVisible&&desk.Contains(e))desk.ExpectCardsAt(desk.DeskPoint(e));Import(e.Data);}}e.Handled=true;};DropImage.Attach(this);
        PreviewKeyUp+=(_,e)=>{if(ActiveDesk.Tap(e,false,TapAllowed()))e.Handled=true;};PreviewMouseDown+=(_,_)=>ActiveDesk.SpoilTap();PreviewMouseWheel+=(_,_)=>ActiveDesk.SpoilTap();
        // Alt+key arrives as Key.System; the real key is in SystemKey.
        PreviewKeyDown+=(_,e)=>{ActiveDesk.Tap(e,true,false);var key=e.Key==Key.System?e.SystemKey:e.Key;if(HandleShortcut(key,Keyboard.Modifiers,Keyboard.FocusedElement as DependencyObject)){e.Handled=true;altHandled=(Keyboard.Modifiers&ModifierKeys.Alt)!=0;}};
        // A handled Alt shortcut must not reach Windows as a menu key, or it plays the "no menu" warning.
        SourceInitialized+=(_,_)=>System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook((IntPtr _,int message,IntPtr _,IntPtr _,ref bool handled)=>{if(message==0x0106&&altHandled){altHandled=false;handled=true;}return IntPtr.Zero;});
        if(initial is not null)Add(initial);else Refresh();ShowLibrary(false);RenderLibrary();Loaded+=(_,_)=>Theme.Entrance(LibraryVisible?libraryPage:home);
        if(this.library.RecoveryNotice is {} notice)L10n.SetText(status,notice);
        else if(preferences.RecoveryNotice is {} settingsNotice)L10n.SetText(status,settingsNotice);
        if(checkForUpdates)
        {
            Loaded+=(_,_)=>SetupDesktop();Closed+=(_,_)=>globalDrag?.Dispose();
            if(offerToolSetup)Loaded+=(_,_)=>Dispatcher.BeginInvoke(OfferFirstRunTools);
            Loaded+=async(_,_)=>{if(preferences.Value.AutomaticUpdateChecks)await CheckUpdatesAsync();};
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromHours(1)};timer.Tick+=async(_,_)=>{if(preferences.Value.AutomaticUpdateChecks)await CheckUpdatesAsync();};Closed+=(_,_)=>timer.Stop();timer.Start();
        }
    }
    private void RoutePageMouseWheel(object sender,MouseWheelEventArgs e)
    {
        var page=home.Visibility==Visibility.Visible?homeScroll:libraryScroll;
        for(var source=e.OriginalSource as DependencyObject;source is not null&&!ReferenceEquals(source,page);source=source is Visual?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source))
        {
            if(source is not ScrollViewer nested)continue;
            var canScroll=e.Delta>0?nested.VerticalOffset>.5:nested.VerticalOffset<nested.ScrollableHeight-.5;
            if(canScroll)return;
            page.ScrollToVerticalOffset(page.VerticalOffset-e.Delta);
            e.Handled=true;
            return;
        }
        page.ScrollToVerticalOffset(page.VerticalOffset-e.Delta);
        e.Handled=true;
    }
    private IEnumerable<LibraryEntry> SearchLibrary(string query)
    {
        var original=library.Search(query).Select(e=>e.Id).ToHashSet();
        return library.Entries.Where(e=>original.Contains(e.Id)||AppLocalization.Matches(e.Action,query,L10n.Language));
    }
    private static TextBlock Text(string value,double size,string? color=null,bool localize=true)
    {
        var text=new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,FontWeight=size>=17?FontWeights.SemiBold:FontWeights.Normal};if(color is not null)text.SetResourceReference(TextBlock.ForegroundProperty,color);return localize?L10n.Label(text):text;
    }
    internal bool WindowManagementEnabled=>library.WindowManagementEnabled;
    private static Border Card(UIElement content){var card=new Border{CornerRadius=new(16),Padding=new(20),Margin=new(0,0,0,6),Child=content,BorderThickness=new(1)};card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Line");return card;}
    public static Button Button(string title,Action action){var button=L10n.Content(new Button{Content=title});button.Click+=(_,_)=>action();return button;}
    internal static Button Primary(string title,Action action){var button=Button(title,action);button.Background=Theme.Primary;button.Foreground=new SolidColorBrush(Theme.OnPrimary);button.BorderThickness=new(0);return button;}
    public static void OpenUrl(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    // Explorer parses "/select," itself; a fully quoted argument makes it fall back to Documents.
    public static void Reveal(string path){Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+Path.GetFullPath(path)+"\""){UseShellExecute=false});}
    private void Choose(){if(busy)return;var picker=new OpenFileDialog{Multiselect=true,Title="Choose files for Nashi"};if(picker.ShowDialog(this)==true){Add(picker.FileNames);ShowLibrary(false);}}
    /// Adds to the desk (never replaces) and selects what just arrived.
    private void Add(IEnumerable<string> paths){var arrived=new List<string>();foreach(var path in paths.Where(p=>File.Exists(p)||Directory.Exists(p))){var full=Path.GetFullPath(path);if(!files.Contains(full))files.Add(full);arrived.Add(full);}if(arrived.Count>0)desk.Select(arrived);Refresh();}
    internal void ShowLibrary(bool visible)
    {
        var changed=LibraryVisible!=visible;
        if(!busy)L10n.SetText(status,"Files stay on your computer.");
        selectedLibraryId=selectedLibraryId??library.Entries.FirstOrDefault()?.Id;home.Visibility=visible?Visibility.Collapsed:Visibility.Visible;libraryPage.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        homeButton.SetResourceReference(Control.BackgroundProperty,visible?"Inset":"Surface");libraryButton.SetResourceReference(Control.BackgroundProperty,visible?"Surface":"Inset");if(visible)RenderLibrary();if(changed&&IsLoaded){Theme.Entrance(visible?libraryPage:home,visible?36:-36);Sounds.Page();}
    }
    private void Record(string label,IReadOnlyList<string> paths)
    {
        try{library.Record(label,paths);RenderLibrary();}catch(Exception error)when(error is IOException or UnauthorizedAccessException){L10n.SetText(status,"Your files are ready, but Library could not be saved.");}
    }
    internal void Import(IDataObject data)
    {
        if(busy)return;
        try
        {
            var result=ClipboardImport.Read(data,library);ShowLibrary(false);
            if(result.Link is {} url){var payload=WebMediaPayload.Parse(url);link.Text=payload?.Url??url;if(payload is not null)SetDownloadKind(payload);OpenDownload();link.Focus();L10n.SetText(status,"Link ready. Choose a format, then Download.");return;}
            Add(result.Paths);Record(result.Label,result.Paths);L10n.SetText(status,result.Label+". Choose a tool or create a ZIP.");
        }
        catch(Exception error)when(ClipboardImport.IsClipboardError(error)){L10n.SetText(status,"Couldn’t paste: "+error.Message);}
    }
    private void PasteClipboard()
    {
        if(busy)return;try{Import(Clipboard.GetDataObject()??new DataObject());}catch(Exception error)when(ClipboardImport.IsClipboardError(error)){L10n.SetText(status,"Clipboard is busy. Try Paste again.");}
    }
    internal bool HandleShortcut(Key key,ModifierKeys modifiers,DependencyObject? focus)
    {
        // Commands answer to the keys chosen in Settings › Shortcuts (or their defaults).
        bool Is(string id)=>Shortcuts.Matches(preferences.Value,id,key,modifiers);
        if(Is("settings")){ShowSettings();return true;}
        if(Is("home")){ShowLibrary(false);return true;}
        if(Is("library")){ShowLibrary(true);return true;}
        if(Is("find")){CloseWorkspace();ShowLibrary(true);UpdateLayout();Keyboard.Focus(search);search.SelectAll();return true;}
        if(busy)return false;
        if(modifiers==ModifierKeys.None&&key is Key.Delete or Key.Back&&ActiveDesk.IsKeyboardFocusWithin&&ActiveDesk.Selection.Count>0){var list=InWorkspace?workspaceFiles:files;foreach(var path in ActiveDesk.Selection)list.Remove(path);Refresh();return true;}
        if(Is("open")){Choose();return true;}
        if(Is("updates")){ShowSettings("Updates",true);return true;}
        if(LibraryBrowsing&&key==Key.Down&&modifiers==ModifierKeys.None&&ReferenceEquals(focus,search)){SelectLibrary(1);return true;}
        if(ClipboardImport.IsEditing(focus))return false;
        if(LibraryBrowsing&&modifiers==ModifierKeys.None&&key is Key.Up or Key.Down){SelectLibrary(key==Key.Down?1:-1);return true;}
        if(LibraryBrowsing&&modifiers==ModifierKeys.None&&key==Key.Space&&focus is not System.Windows.Controls.Button){PreviewSelectedLibrary();return true;}
        if(LibraryBrowsing&&modifiers==ModifierKeys.None&&key==Key.Enter&&focus is not System.Windows.Controls.Button){ReuseSelectedLibrary();return true;}
        if(Is("paste")){PasteClipboard();return true;}
        if(Is("download")){OpenCopiedLink();return true;}
        if(Is("clear")){ClearDesk();return true;}
        if(Is("tools")||(modifiers==ModifierKeys.Control&&key==Key.Enter)||(modifiers==ModifierKeys.None&&key==Key.Enter&&focus is not System.Windows.Controls.Button)){ShowWheel();return true;}
        return false;
    }
    private void RenderLibrary()
    {
        libraryCards.Clear();libraryRows.Children.Clear();recentRows.Children.Clear();var filtered=SearchLibrary(search.Text).ToArray();if(LibraryVisible)SyncWorkspace();if(library.Entries.Count>0)libraryRows.Children.Add(AllFilesCard());
        if(filtered.Length==0)libraryRows.Children.Add(Card(Text(library.Entries.Count==0?"Nothing here yet. Paste something or make your first fresh copy.":"No matches. Try a file name or tool.",14,"Muted")));
        foreach(var entry in filtered)libraryRows.Children.Add(LibraryRow(entry));
        recentCount.Text=library.Entries.Count.ToString();foreach(var entry in SearchLibrary(recentSearch.Text).Take(3))recentRows.Children.Add(LibraryRow(entry,true));
        if(library.Entries.Count==0)recentRows.Children.Add(Card(Text("Your next fresh copy will land here.",13,"Muted")));
    }
    private void OpenDownload()
    {
        if(webWindow?.IsVisible==true){webWindow.Activate();link.Focus();return;}
        var dialog=new Window{Owner=this,Title="Download from the web · Nashi",Width=800,Height=650,MinWidth=640,MinHeight=400,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new ScrollViewer{Content=webContent,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};Theme.Attach(dialog);webWindow=dialog;
        dialog.Closing+=(_,e)=>{if(busy){e.Cancel=true;L10n.SetText(status,"Finish or cancel the current operation first.");}};
        dialog.Closed+=(_,_)=>{inspection?.Cancel();if(dialog.Content is ScrollViewer scroll)scroll.Content=null;dialog.Content=null;webWindow=null;};dialog.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!busy){dialog.Close();e.Handled=true;}};dialog.Show();link.Focus();
    }
    private void SetBusy(bool value){busy=value;Thumbnails.Ffmpeg=tools.Ready?tools.Executable("ffmpeg"):null;updateButton.IsEnabled=!value&&!checking;workArea.IsEnabled=!value;webContent.IsEnabled=!value;L10n.Bind(toolsSetupButton,ContentControl.ContentProperty,tools.Ready&&officeRuntime.Executable is not null?"Tools ready":"Finish setup");RenderLibrary();}
    private static bool All(IReadOnlyList<string> paths,string[] extensions)=>paths.Count>0&&paths.All(p=>extensions.Contains(Path.GetExtension(p).TrimStart('.').ToLowerInvariant()));
    private static bool Images(IReadOnlyList<string> paths)=>paths.Count>0&&paths.All(FeatureCatalog.IsImage);
    private static bool Video(IReadOnlyList<string> paths)=>All(paths,["mp4","mov","mkv","webm","avi","wmv","m4v","gif"]);
    private static bool Audio(IReadOnlyList<string> paths)=>All(paths,["mp3","m4a","wav","flac","ogg","opus","aiff","aif","wma","aac"]);
    private static readonly string[] FileActionIds=["share","float","copy","move","copyPath","terminal"];
    /// The same three wheels the desktop drag offers, for cards dragged on the desk.
    private IReadOnlyList<ToolChoice> DeskChoices(string mode,IReadOnlyList<string> paths)=>mode switch
    {
        "convert"=>FeatureCatalog.Formats(paths).Select(f=>new ToolChoice("convert:"+f,f.ToUpperInvariant(),"")).ToArray(),
        "actions"=>[new("share","Share","share-network"),new("float","Float","push-pin"),new("copy","Copy","copy"),new("move","Move","folder-open"),new("copyPath","Copy Path","path"),new("terminal","Terminal","terminal-window")],
        _=>FeatureCatalog.Tools(paths).Select(id=>new ToolChoice(id,FeatureCatalog.ToolNames[id],ToolSymbol(id))).ToArray()
    };
    private async Task DeskChosenAsync(ToolChoice choice,IReadOnlyList<string> paths)
    {
        paths=paths.Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();if(busy||paths.Count==0)return;if(FileActionIds.Contains(choice.Id))await PerformFileAction(choice.Id,paths);else await EditAsync(choice,paths);
    }
    /// Tools act on the selected cards, or on the whole desk when nothing is selected.
    private IReadOnlyList<string> Targets=>(ActiveDesk.Selection.Count>0?ActiveDesk.Selection:ActiveFiles).Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
    private bool TapAllowed()=>!busy&&(!LibraryVisible||InWorkspace)&&IsActive&&!ClipboardImport.IsEditing(Keyboard.FocusedElement as DependencyObject);
    private bool actionsQueued;
    /// Rebuilds the action tiles only when what they act on changed. A focus change alone must not
    /// replace a tile between its mouse down and mouse up, or the first click is lost.
    private void QueueActions(){if(actionsQueued)return;actionsQueued=true;Dispatcher.BeginInvoke(()=>{actionsQueued=false;var key=string.Join("|",files)+"#"+string.Join("|",Targets)+"#"+L10n.Language+"#"+InWorkspace;if(key==actionsKey&&actions.Children.Count>0)return;actionsKey=key;actions.Children.Clear();BuildSelectionActions();},System.Windows.Threading.DispatcherPriority.Background);}
    private string? actionsKey;
    private string? DuplicateFile(string path)
    {
        try
        {
            var folder=Path.GetDirectoryName(path)!;var name=Path.GetFileNameWithoutExtension(path);var extension=Path.GetExtension(path);var suffix=L10n.Text("copy");string copy;var n=1;
            do{copy=Path.Combine(folder,n==1?$"{name} {suffix}{extension}":$"{name} {suffix} {n}{extension}");n++;}while(File.Exists(copy)||Directory.Exists(copy));
            File.Copy(path,copy);var list=InWorkspace&&workspaceFiles.Contains(path)?workspaceFiles:files;list.Insert(list.IndexOf(path)+1,copy);Refresh();return copy;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or NotSupportedException){L10n.SetText(status,"Couldn’t duplicate this file.");return null;}
    }
    private IReadOnlyList<ToolChoice> Choices()
    {
        return FeatureCatalog.Tools(Targets).Select(id=>new ToolChoice(id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>id},FeatureCatalog.ToolNames[id],ToolSymbol(id))).ToArray();
    }
    private void Refresh()
    {
        L10n.SetText(selectionPill,files.Count==0?"✦  START HERE":"✦  READY WHEN YOU ARE");if(files.Count==1)selection.Text=Path.GetFileName(files[0]);else L10n.SetText(selection,files.Count==0?"Drop something good.":$"{files.Count} files, ready to go.");if(files.Count==1)fileDescription.Text=Path.GetDirectoryName(files[0])!;else L10n.SetText(fileDescription,files.Count==0?"Images, videos, documents, and all the little things in between.":"Keep them together, or give each one a fresh start.");if(clearButton is not null)clearButton.Visibility=files.Count>0?Visibility.Visible:Visibility.Collapsed;actions.Children.Clear();garden.SetSelected(files.Count>0);
        BuildSelectionActions();
    }
    private void ShowWheel()
    {
        if(busy||ActiveFiles.Count==0)return;var targets=Targets.ToArray();if(targets.Length==0){L10n.SetText(status,"These files were deleted. Remove their cards or add new files.");return;}var choices=Choices();
        if(choices.Count==0){L10n.SetText(status,"These files don’t share a tool. Select files of one kind.");return;}
        new WheelWindow(this,choices,async choice=>await EditAsync(choice,targets)).Show();
    }
    private async Task EditAsync(ToolChoice choice,IReadOnlyList<string>? only=null)
    {
        var chosenFiles=(only??files).ToArray();if(busy||chosenFiles.Length==0)return;string? temporary=null;try
        {
            var requestedFormat=choice.Id.StartsWith("convert:",StringComparison.Ordinal)?choice.Id[8..]:null;if(requestedFormat is not null)choice=new("convert","Convert to "+requestedFormat.ToUpperInvariant(),"↗");
            choice=choice with{Id=choice.Id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>choice.Id}};
            SetBusy(true);var selected=chosenFiles;var imageSelection=Images(selected);var mediaSelection=Video(selected)||Audio(selected);MediaInfo? info=null;BitmapSource? preview=null;var input=selected[0];
            if(choice.Id=="removeMetadata"){SetBusy(false);await EditMetadataAsync(selected);return;}
            if(choice.Id=="readQRCode"){SetBusy(false);await ReadQrAsync(selected);return;}
            if(!imageSelection&&!mediaSelection&&choice.Id is not ("extractArchive" or "unzip")){SetBusy(false);await EditDocumentAsync(choice,selected,requestedFormat);return;}
            if(imageSelection)preview=ImageEngine.Load(input);
            else if(mediaSelection)
            {
                if(!tools.Ready){MessageBox.Show(this,L10n.Text("Choose Set up media tools once to enable media editing."),L10n.Text("Media setup"));return;}
                var engine=new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe"));info=await engine.InspectAsync(input);
                if(choice.Id is "cropVideo" or "redactVideo")
                {
                    temporary=Path.Combine(Path.GetTempPath(),"Nashi-preview-"+Guid.NewGuid().ToString("N")+".png");
                    await ProcessRunner.RunAsync(tools.Executable("ffmpeg"),["-v","error","-i",input,"-frames:v","1",temporary]);preview=ImageEngine.Load(temporary);
                }
            }
            SetBusy(false);var editor=new ToolEditor(this,choice.Id,choice.Title,input,info,preview,selected,choice.Id=="convert"?FeatureCatalog.Formats(selected):null);editor.ConfigureProfiles(preferences,requestedFormat);editor.ConfigureMediaRuntime(tools);var accepted=editor.ShowDialog()==true;if(temporary is not null)File.Delete(temporary);if(!accepted)return;
            var inputs=choice.Id is "cropVideo" or "redactVideo" or "annotate"?new[]{input}:editor.OrderedInputs;
            if(imageSelection)await RunAsync(async(destination,p,t)=>
            {
                using var tx=new ExportTransaction(destination);var exported=await OnStaAsync(()=>ImageEngine.Apply(choice.Id,inputs,tx.StagingDirectory,editor.ImageOptions with{EncoderPath=tools.Ready?tools.Executable("ffmpeg"):null},choice.Id=="convert"?editor.Format:null,t,p));
                if(choice.Id=="convert"&&editor.PreserveImageMetadata&&editor.Format is not ("pdf" or "bmp" or "gif"))
                {
                    await metadataService.InstallAsync(p,t);for(var n=0;n<exported.Count;n++)await metadataService.CopyImageTagsAsync(inputs[n],exported[n],t);
                }
                return tx.Publish(exported,t);
            },choice.Title);
            else if(mediaSelection)await RunAsync((destination,p,t)=>{var engine=new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe"));return choice.Id=="convert"?engine.ConvertAsync(inputs,editor.Format,destination,editor.MediaOptions,p,t):engine.ToolAsync(choice.Id,inputs,destination,editor.MediaOptions,p,t);},choice.Title);
            else if(choice.Id is "unzip" or "extractArchive")await RunAsync((destination,p,t)=>Task.Run<IReadOnlyList<string>>(()=>{using var tx=new ExportTransaction(destination);var outputs=inputs.SelectMany(file=>ArchiveEngine.Extract(file,tx.StagingDirectory,editor.DocumentOptions.Password,p,t)).ToList();return tx.Publish(outputs,t);},t));
            else await RunAsync((destination,p,t)=>Task.Run(()=>DocumentEngine.Pdf(choice.Id,inputs,destination,editor.Pages,t),t),choice.Title);
        }
        catch(Exception error){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Couldn’t open this tool"),MessageBoxButton.OK,MessageBoxImage.Information);}
        finally{if(temporary is not null&&File.Exists(temporary))File.Delete(temporary);SetBusy(false);}
    }
    private sealed record QueuedJob(Func<string,IProgress<OperationProgress>,CancellationToken,Task<IReadOnlyList<string>>> Work,string Label,string Destination,FileDesk Desk,IReadOnlyList<string> Sources,NotificationCard Card,CancellationTokenSource Cancellation,TaskCompletionSource Done,Guid? Space=null);
    private readonly Queue<QueuedJob> jobs=new();private QueuedJob? runningJob;
    internal int QueuedJobs=>jobs.Count+(runningJob is null?0:1);
    /// Jobs line up and run one after another; each has a card in the top-right notifications
    /// showing its place in line, its progress and, at the end, its new files. Nashi stays usable meanwhile.
    private async Task RunAsync(Func<string,IProgress<OperationProgress>,CancellationToken,Task<IReadOnlyList<string>>> work,string label="Fresh copy")
    {
        var owner=webWindow?.IsActive==true?webWindow:this;string destination;var source=ActiveFiles.FirstOrDefault(p=>File.Exists(p)||Directory.Exists(p));
        if(label=="Move"||preferences.Value.OutputMode=="ask"||(source is null&&owner==this)){var folder=new OpenFolderDialog{Title="Save new files to…",InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};if(folder.ShowDialog(owner)!=true)return;destination=folder.FolderName;}
        else destination=owner==webWindow?preferences.Value.DownloadFolder:preferences.Value.OutputMode=="folder"&&!string.IsNullOrWhiteSpace(preferences.Value.OutputFolder)?preferences.Value.OutputFolder:Path.GetDirectoryName(source!)!;
        var cancellation=new CancellationTokenSource();var card=Notifications.Shared.Job(label,cancellation.Cancel);
        var sources=Targets.ToArray();var job=new QueuedJob(work,label,destination,ActiveDesk,sources,card,cancellation,new(TaskCreationOptions.RunContinuationsAsynchronously),InWorkspace?null:deskSession);jobs.Enqueue(job);ShowQueue();
        if(runningJob is null)_=DrainJobsAsync();
        await job.Done.Task;
    }
    private void ShowQueue(){var n=0;foreach(var waiting in jobs)waiting.Card.Queued(++n);}
    private async Task DrainJobsAsync()
    {
        while(jobs.TryDequeue(out var job))
        {
            runningJob=job;ShowQueue();var card=job.Card;
            try
            {
                if(job.Cancellation.IsCancellationRequested)throw new OperationCanceledException();
                card.Start();job.Desk.Shake(job.Sources);var outputs=await job.Work(job.Destination,new Progress<OperationProgress>(card.Update),job.Cancellation.Token);
                // Files made from something that came from the internet (an extracted archive, a converted
                // document) keep Windows' internet mark, so SmartScreen and Protected View still warn.
                if(job.Label is not ("Move" or "Copy")&&!job.Label.StartsWith("Download",StringComparison.Ordinal))MarkOfTheWeb.Apply(outputs,MarkOfTheWeb.Strongest(job.Sources));
                card.Complete(outputs,path=>PreviewFile(path,false));Record(job.Label,outputs);if(job.Label is not ("Move" or "Copy"))AddResults(outputs,job.Sources);if(outputs.Count>0)Sounds.Success();
                // The space the job started in remembers it, even if another space is open by now.
                if(job.Space is {} space)try{library.LogSpace(space,job.Label,outputs.Count);if(LibraryVisible)RenderLibrary();}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
                L10n.SetText(status,$"Saved {outputs.Count} new {(outputs.Count==1?"file":"files")}. Originals kept.");if(preferences.Value.RevealFinished&&outputs.Count>0)Reveal(outputs[0]);
            }
            catch(OperationCanceledException){card.Cancelled();L10n.SetText(status,"Cancelled. Originals kept.");}
            catch(Exception error){card.Fail(error.Message);Sounds.Failure();L10n.SetText(status,"That one couldn’t finish. Originals kept.");}
            finally{job.Cancellation.Dispose();runningJob=null;job.Done.TrySetResult();}
        }
        ShowQueue();
    }
    /// Every finished operation (convert, edit, download…) puts its new files on the desk in view.
    /// New cards land beside the file they came from and pop out of it.
    private void AddResults(IReadOnlyList<string> outputs,IReadOnlyList<string>? sources=null)
    {
        var list=InWorkspace?workspaceFiles:files;var arrived=new List<string>();var source=sources?.FirstOrDefault(list.Contains);if(source is not null)ActiveDesk.ExpectBeside(source);quietWorkspace=true;
        try{foreach(var path in outputs.Where(p=>File.Exists(p)||Directory.Exists(p))){var full=Path.GetFullPath(path);if(!list.Contains(full))list.Add(full);arrived.Add(full);}}finally{quietWorkspace=false;}
        if(arrived.Count==0)return;ActiveDesk.Select(arrived);Refresh();ActiveDesk.Arrive(arrived,source);
    }
    /// Opens the desk's files in the previewer, starting at this one, so its arrows move along the desk.
    private void PreviewFile(string path,bool play){if(!File.Exists(path)){L10n.SetText(status,"This file has moved or was deleted.");return;}var list=ActiveFiles.Where(File.Exists).ToArray();new FilePreviewWindow(this,list.Contains(path)?list:[path],tools,officeRuntime,path){AutoPlay=play}.Show();}
    private static Task<IReadOnlyList<string>> OnStaAsync(Func<IReadOnlyList<string>> work)
    {
        var completion=new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{try{completion.SetResult(work());}catch(Exception error){completion.SetException(error);}}){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;
    }
    private async Task DownloadAsync()
    {
        if(busy)return;
        var payload=WebMediaPayload.Parse(link.Text);var kind=downloadKind.SelectedItem as string??"video";
        if(!tools.Ready&&kind!="image"&&payload?.Kind!="image"&&!(kind=="thumbnail"&&payload?.Kind=="youtube")){MessageBox.Show(webWindow??this,L10n.Text("Choose Set up media tools once to enable web downloads."),L10n.Text("Media setup"));return;}
        if(kind=="photos"&&inspectedLink!=link.Text){await InspectWebLinkAsync();return;}
        try{var url=WebDownload.Validate(payload?.Url??link.Text).AbsoluteUri;await RunAsync((destination,p,t)=>new WebDownload(tools).DownloadAsync(url,kind,destination,p,t,webQuality.SelectedIndex==1?1080:webQuality.SelectedIndex==2?720:null,kind=="photos"?new HashSet<int>(selectedWebPhotos):null),"Download "+kind);}catch(Exception error){MessageBox.Show(webWindow??this,L10n.Text(error.Message),L10n.Text("Check your link"));}
    }
    private async Task SetupAsync()
    {
        if(busy)return;if(tools.Ready){L10n.SetText(status,"Media tools are already installed.");return;}
        if(MessageBox.Show(this,L10n.Text("Download FFmpeg (~180 MB), yt-dlp and Deno directly from their official GitHub releases? SHA-256 hashes are verified before use. FFmpeg is GPL; the other upstream licenses and source links are listed on our Features page. No administrator password is needed."),L10n.Text("Set up media tools"),MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
        SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=Notifications.Shared.Job("Working…",cancellation.Cancel);toast.Start();
        try{await tools.InstallAsync(new Progress<OperationProgress>(toast.Update),cancellation.Token);toast.Close();L10n.SetText(status,"Media tools ready. You can edit, convert and download media.");}
        catch(OperationCanceledException){toast.Close();L10n.SetText(status,"Setup cancelled.");}catch(Exception error){toast.Close();MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Setup couldn’t finish"));}finally{SetBusy(false);}
    }
    internal void OfferUpdate(WindowsUpdate candidate){update=candidate;L10n.SetContent(updateButton,"Update to "+candidate.Version);if(updateBanner is not null)updateBanner.Visibility=Visibility.Visible;tray?.UpdateAvailable(candidate.Version);}
    /// The hourly background check only finds updates (Home shows a banner); Settings › Updates installs them.
    private async Task CheckUpdatesAsync()
    {
        if(checking||update is not null)return;checking=true;
        try{if(await PublicUpdates.CheckAsync(UpdateInstaller.Current) is {} found)OfferUpdate(found);}
        catch(Exception){/* Offline or a bad feed: the next hourly check tries again. */}
        finally{checking=false;}
    }
    /// What Settings › Updates needs: the known update, a fresh check, and staging (then Nashi restarts).
    private UpdateFlow Updates()=>new(()=>update,async()=>{var found=await PublicUpdates.CheckAsync(UpdateInstaller.Current);if(found is not null)OfferUpdate(found);return found;},
        async(candidate,progress,token)=>{if(busy||QueuedJobs>0)throw new InvalidOperationException(L10n.Text("Finish or cancel the current operation before updating."));SetBusy(true);try{await UpdateInstaller.StageAsync(candidate,progress,token);busy=false;Application.Current.Shutdown();}finally{SetBusy(false);}});
}
