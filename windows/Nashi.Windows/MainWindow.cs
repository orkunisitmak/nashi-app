using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;

public sealed partial class MainWindow:Window
{
    private const string PrivacyNote="Everything is processed on this PC. Nothing is uploaded.";
    private readonly ObservableCollection<string> files=[];
    private readonly ListBox list=new(){MinHeight=0,MaxHeight=76};
    private readonly Grid actions=new();
    private readonly TextBlock selection=new(){FontSize=24,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly TextBlock status=Text(PrivacyNote,11,"Muted");
    private readonly TextBox link=new(){ToolTip="Paste a public video, image or audio page link"};
    private readonly ComboBox downloadKind=new(){ItemsSource=new[]{"video","audio","thumbnail"},SelectedIndex=0,MinWidth=110};
    private readonly Toolchain tools=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools","v1"),Environment.GetEnvironmentVariable("PATH"));
    private readonly LibraryStore library;
    private readonly PreferencesStore preferences;private GlobalDragController? globalDrag;
    private readonly StackPanel home=new(),libraryPage=new(),libraryRows=new(),recentRows=new();
    private Button? chooseFilesButton,openWheelButton;private readonly TextBlock fileDescription=Text("",12,"Muted",localize:false);
    private readonly TextBox search=new(){ToolTip="Find a file or tool in Library",Width=214,MinHeight=34,Margin=new(0,0,8,0)};
    private readonly StackPanel webContent=new();private readonly DockPanel webPage=new();private Window? webWindow;
    internal Window DownloadWindow=>webWindow??this;
    private readonly Button updateButton,toolsSetupButton;private readonly ComboBox appearance;
    private readonly Segmented nav=new(glass:true);
    private readonly Border dropCard;private readonly StackPanel emptyState=new(),selectionState=new();private readonly WrapPanel legend=new(){HorizontalAlignment=HorizontalAlignment.Center};
    private readonly TextBlock dropTitle=Text("Drop files here",30),dropSubtitle=Text("Images, video, audio, PDFs, documents and archives.",13,"Muted");
    private readonly TextBlock libraryCount=Text("",12,"Muted",localize:false);
    private readonly StackPanel workArea=new();
    private readonly ScrollViewer homeScroll=new(),libraryScroll=new();
    private bool busy,checking,toolSetupOffered;private WindowsUpdate? update;private Border? updateBanner;
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
        this.library=library??new LibraryStore();preferences=new(this.library.DirectoryPath);ApplyPreferences();Theme.Apply(this.library.Appearance);Theme.Attach(this,toolbar:true);
        Title="Nashi";Width=760;Height=690;MinWidth=700;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterScreen;AllowDrop=true;
        var root=new DockPanel();Content=root;AddHandler(UIElement.PreviewMouseWheelEvent,new MouseWheelEventHandler(RoutePageMouseWheel),true);
        // Toolbar (60 px): the app glyph, Home and Library centered on the window, then setup, search and settings.
        // The caption buttons sit to its right. Like the design's 1fr·auto·1fr grid, the switch only moves aside when space runs out.
        var toolbar=new Grid{Height=60};DockPanel.SetDock(toolbar,Dock.Top);root.Children.Add(toolbar);
        var brand=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(18,0,0,0)};brand.Children.Add(Ui.Pear(16));var appName=Text("Nashi",12,"Muted",localize:false);appName.Margin=new(8,0,0,0);brand.Children.Add(appName);toolbar.Children.Add(brand);
        var trail=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,0,146,0)};toolbar.Children.Add(trail);WindowChrome.SetIsHitTestVisibleInChrome(trail,true);
        nav.Add("Home",()=>ShowLibrary(false));nav.Add("Library",()=>ShowLibrary(true));nav.HorizontalAlignment=HorizontalAlignment.Left;WindowChrome.SetIsHitTestVisibleInChrome(nav,true);toolbar.Children.Add(nav);
        void PlaceNav(){var left=(toolbar.ActualWidth-nav.ActualWidth)/2;left=Math.Min(left,toolbar.ActualWidth-146-trail.ActualWidth-8-nav.ActualWidth);nav.Margin=new(Math.Max(left,18+brand.ActualWidth+8),0,0,0);}
        toolbar.SizeChanged+=(_,_)=>PlaceNav();trail.SizeChanged+=(_,_)=>PlaceNav();nav.SizeChanged+=(_,_)=>PlaceNav();
        search.Style=Ui.Style("Field");search.MinHeight=38;search.Tag="Ctrl+F";Ui.SetIcon(search,"search",14);Ui.SetLens(search,"control");search.SetResourceReference(BackgroundProperty,"Glass");search.SetResourceReference(BorderBrushProperty,"Rim");L10n.Bind(search,Ui.HintProperty,"Search files and tools");trail.Children.Add(search);
        var setupLabel=new StackPanel{Orientation=Orientation.Horizontal};var setupDot=new System.Windows.Shapes.Ellipse{Width=7,Height=7,Margin=new(0,0,6,0),VerticalAlignment=VerticalAlignment.Center};setupDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Coral");setupLabel.Children.Add(setupDot);setupLabel.Children.Add(Text("Finish setup",13,weight:FontWeights.Medium));
        toolsSetupButton=Button("",ShowToolSetupMenu);toolsSetupButton.Style=Ui.Style("GlassButton");toolsSetupButton.Content=setupLabel;toolsSetupButton.Margin=new(0,0,8,0);trail.Children.Add(toolsSetupButton);
        var settings=Button("",ShowSettings);settings.Style=Ui.Style("GlassRound");settings.Content=Icons.Make("gear",17);settings.Margin=new(0);settings.ToolTip="Settings";AutomationProperties.SetName(settings,"Settings");trail.Children.Add(settings);
        appearance=new ComboBox{ItemsSource=new[]{"System","Light","Dark"},SelectedItem=char.ToUpper(this.library.Appearance[0])+this.library.Appearance[1..]};
        updateButton=Button("Check for updates",async()=>await CheckUpdatesAsync(true));
        // The status line doubles as the privacy note.
        var note=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(20,8,20,12)};var lockIcon=Icons.Make("lock",12,"Muted");lockIcon.VerticalAlignment=VerticalAlignment.Top;lockIcon.Margin=new(0,1,6,0);note.Children.Add(lockIcon);status.MaxWidth=600;note.Children.Add(status);DockPanel.SetDock(note,Dock.Bottom);root.Children.Add(note);
        var pages=new Grid();homeScroll.Content=home;homeScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;homeScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;libraryScroll.Content=libraryPage;libraryScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;libraryScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;pages.Children.Add(homeScroll);pages.Children.Add(libraryScroll);root.Children.Add(pages);
        home.Margin=libraryPage.Margin=new(20,2,20,8);
        var updateRow=new DockPanel();updateButton.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(updateButton,Dock.Right);updateRow.Children.Add(updateButton);var updateIcon=Ui.TileIcon("download");updateIcon.Margin=new(0,0,12,0);DockPanel.SetDock(updateIcon,Dock.Left);updateRow.Children.Add(updateIcon);var updateCaption=new StackPanel{VerticalAlignment=VerticalAlignment.Center};updateCaption.Children.Add(Text("Update available",13,weight:FontWeights.SemiBold));updateCaption.Children.Add(Text("You choose when to download and install.",11.5,"Muted"));updateRow.Children.Add(updateCaption);updateBanner=Card(updateRow);updateBanner.Padding=new(11,9,9,9);updateBanner.CornerRadius=new(18);updateBanner.Margin=new(0,0,0,12);updateBanner.Visibility=Visibility.Collapsed;home.Children.Add(updateBanner);
        home.Children.Add(workArea);
        // Drop card: an invitation while empty, the selection and what to do with it once files arrive.
        var glyph=new System.Windows.Shapes.Path{Data=Geometry.Parse("M13,6.5 H28 L37,15.5 V40 A2.5,2.5 0 0 1 34.5,42.5 H13 A2.5,2.5 0 0 1 10.5,40 V9 A2.5,2.5 0 0 1 13,6.5 Z M28,6.5 V15.5 H37 M24,20.5 V34 M18.5,28.6 L24,34.1 L29.5,28.6"),StrokeThickness=1.9,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Ink");
        emptyState.Children.Add(new Viewbox{Width=56,Height=56,Margin=new(0,0,0,18),Child=new Canvas{Width=48,Height=48,Children={glyph}}});
        dropTitle.TextAlignment=dropSubtitle.TextAlignment=TextAlignment.Center;emptyState.Children.Add(dropTitle);dropSubtitle.Margin=new(0,6,0,0);emptyState.Children.Add(dropSubtitle);
        var picks=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,22,0,24)};chooseFilesButton=Primary("Choose Files…",Choose);Ui.SetIcon(chooseFilesButton,"plus");chooseFilesButton.Margin=new(0,0,10,0);picks.Children.Add(chooseFilesButton);var paste=Button("Paste",PasteClipboard);paste.Style=Ui.Style("Big");paste.Margin=new(0);Ui.SetIcon(paste,"paste");picks.Children.Add(paste);emptyState.Children.Add(picks);
        emptyState.Children.Add(new Border{BorderThickness=new(0,1,0,0),Padding=new(0,14,0,0),Child=legend});((Border)emptyState.Children[^1]).SetResourceReference(Border.BorderBrushProperty,"Line");
        var head=new DockPanel{Margin=new(2,2,2,16)};list.Margin=new(2,0,14,0);DockPanel.SetDock(list,Dock.Left);head.Children.Add(list);
        var headButtons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};var addFiles=Button("Add…",Choose);Ui.SetIcon(addFiles,"plus");addFiles.Margin=new(0,0,8,0);headButtons.Children.Add(addFiles);var clear=Button("Clear",()=>{files.Clear();Refresh();});clear.Style=Ui.Style("Plain");headButtons.Children.Add(clear);DockPanel.SetDock(headButtons,Dock.Right);head.Children.Add(headButtons);
        var headText=new StackPanel{VerticalAlignment=VerticalAlignment.Center};selection.SetResourceReference(TextBlock.FontFamilyProperty,"DisplayFont");headText.Children.Add(selection);fileDescription.TextWrapping=TextWrapping.NoWrap;fileDescription.TextTrimming=TextTrimming.CharacterEllipsis;fileDescription.Margin=new(0,3,0,0);headText.Children.Add(fileDescription);head.Children.Add(headText);
        selectionState.Children.Add(head);BuildFileStack();selectionState.Children.Add(actions);
        var dropContent=new Grid();dropContent.Children.Add(emptyState);dropContent.Children.Add(selectionState);dropCard=Card(dropContent);dropCard.CornerRadius=new(24);workArea.Children.Add(dropCard);BuildJobCards();
        openWheelButton=Primary("Open wheel",ShowWheel);Ui.SetIcon(openWheelButton,"convert");Ui.SetHint(openWheelButton,"Ctrl+Space");
        // Download from Link: the link, what it points to, and how to save it. The page outlives each dialog.
        webContent.Margin=new(20,6,20,8);BuildWebOptions();
        var save=Primary("Download",async()=>await DownloadAsync());Ui.SetIcon(save,"download");save.Margin=new(0);var webFooter=new DockPanel{Margin=new(20,8,20,18)};DockPanel.SetDock(save,Dock.Right);webFooter.Children.Add(save);webFooter.Children.Add(Text("Tip: copy a link anywhere and press Ctrl+Shift+D.",11.5,"Muted"));
        DockPanel.SetDock(webFooter,Dock.Bottom);webPage.Children.Add(webFooter);webPage.Children.Add(new ScrollViewer{Content=webContent,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        // Download from Link: a quiet row under the drop card.
        var linkRow=new DockPanel();var linkIcon=Ui.TileIcon("link");linkIcon.Margin=new(0,0,12,0);DockPanel.SetDock(linkIcon,Dock.Left);linkRow.Children.Add(linkIcon);
        var linkTrail=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};linkTrail.Children.Add(Ui.Keycap("Ctrl+Shift+D"));var chevron=Icons.Make("chevRight",12,"Faint");chevron.Margin=new(6,0,0,0);linkTrail.Children.Add(chevron);DockPanel.SetDock(linkTrail,Dock.Right);linkRow.Children.Add(linkTrail);
        var linkText=new StackPanel{VerticalAlignment=VerticalAlignment.Center};linkText.Children.Add(Text("Download from Link",13,weight:FontWeights.SemiBold));linkText.Children.Add(Text("Video, audio or images from a public page.",11.5,"Muted"));linkRow.Children.Add(linkText);
        var webButton=Button("",OpenDownload);webButton.Style=Ui.Style("CardButton");webButton.Content=linkRow;webButton.Margin=new(0,12,0,0);workArea.Children.Add(webButton);
        // Recent files
        var recentHeading=new DockPanel{Margin=new(4,20,4,9)};var all=Button("Show All",()=>ShowLibrary(true));all.Style=Ui.Style("Plain");all.FontSize=12;all.MinHeight=26;all.Margin=new(0);DockPanel.SetDock(all,Dock.Right);recentHeading.Children.Add(all);recentHeading.Children.Add(Text("Recent",17));home.Children.Add(recentHeading);var recentCard=Card(recentRows,20,new(6));home.Children.Add(recentCard);
        var libraryTitle=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(4,2,4,12)};libraryTitle.Children.Add(Text("Library",30));libraryCount.Margin=new(10,8,0,0);libraryTitle.Children.Add(libraryCount);libraryPage.Children.Add(libraryTitle);
        // Typing rebuilds the rows once the keys pause, not on every key.
        libraryPage.Children.Add(Card(libraryRows,20,new(6)));searchTyping.Tick+=(_,_)=>RenderLibrary();search.TextChanged+=(_,_)=>{searchTyping.Stop();searchTyping.Start();};
        var hints=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,14,0,0)};void Hint(string label,params string[] keys){var group=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(7,0,7,0)};foreach(var key in keys){var cap=Ui.Keycap(key);cap.Margin=new(0,0,4,0);group.Children.Add(cap);}var text=Text(label,11,"Muted");text.Margin=new(2,0,0,0);group.Children.Add(text);hints.Children.Add(group);}
        Hint("Select","↑","↓");Hint("Preview","Space");Hint("Use","Enter");libraryPage.Children.Add(hints);
        AutomationProperties.SetAutomationId(search,"LibrarySearch");AutomationProperties.SetAutomationId(link,"DownloadLink");AutomationProperties.SetAutomationId(list,"SelectedFiles");
        Closing+=(_,e)=>{if(busy){e.Cancel=true;L10n.SetText(status,"Finish the current operation or choose Cancel before closing Nashi.");}};
        DragOver+=(_,e)=>{e.Effects=!busy&&(e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent(DataFormats.UnicodeText)||e.Data.GetDataPresent(DataFormats.Html)||e.Data.GetDataPresent("UniformResourceLocatorW"))?DragDropEffects.Copy:DragDropEffects.None;ShowDropTarget(e.Effects==DragDropEffects.Copy?(e.Data.GetData(DataFormats.FileDrop) as string[])?.Length??0:null);e.Handled=true;};
        DragLeave+=(_,e)=>{var point=e.GetPosition(this);if(point.X<=0||point.Y<=0||point.X>=ActualWidth||point.Y>=ActualHeight)ShowDropTarget(null);};
        Drop+=(_,e)=>{ShowDropTarget(null);if(!busy){if(!e.Data.GetDataPresent(DataFormats.FileDrop)&&DragPayload.Parse(e.Data) is not null)OnGlobalDrop("download:auto",e.Data,null);else Import(e.Data);}e.Handled=true;};
        PreviewKeyDown+=(_,e)=>{if(HandleShortcut(e.Key,Keyboard.Modifiers,Keyboard.FocusedElement as DependencyObject))e.Handled=true;};
        if(initial is not null)Add(initial);else Refresh();ShowLibrary(false);RenderLibrary();Loaded+=(_,_)=>Theme.Entrance(LibraryVisible?libraryPage:home);
        if(this.library.RecoveryNotice is {} notice)L10n.SetText(status,notice);
        else if(preferences.RecoveryNotice is {} settingsNotice)L10n.SetText(status,settingsNotice);
        if(checkForUpdates)
        {
            Loaded+=(_,_)=>SetupDesktop();Closed+=(_,_)=>globalDrag?.Dispose();
            if(offerToolSetup)Loaded+=(_,_)=>Dispatcher.BeginInvoke(OfferFirstRunTools);
            Loaded+=async(_,_)=>{if(preferences.Value.AutomaticUpdateChecks)await CheckUpdatesAsync(false);};
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromHours(1)};timer.Tick+=async(_,_)=>{if(preferences.Value.AutomaticUpdateChecks)await CheckUpdatesAsync(false);};Closed+=(_,_)=>timer.Stop();timer.Start();
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
    // Display sizes use the display cut and tighter weight steps: 24+ bold, 17+ semibold.
    private static TextBlock Text(string value,double size,string? color=null,bool localize=true,FontWeight? weight=null)
    {
        var text=new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,FontWeight=weight??(size>=24?FontWeights.Bold:size>=17?FontWeights.SemiBold:FontWeights.Normal)};
        if(size>=17)text.SetResourceReference(TextBlock.FontFamilyProperty,"DisplayFont");if(color is not null)text.SetResourceReference(TextBlock.ForegroundProperty,color);return localize?L10n.Label(text):text;
    }
    internal bool WindowManagementEnabled=>library.WindowManagementEnabled;
    private static Border Card(UIElement content,double radius=20,Thickness? padding=null)=>Ui.Card(content,radius,padding);
    public static Button Button(string title,Action action){var button=L10n.Content(new Button{Content=title});button.Click+=(_,_)=>action();return button;}
    internal static Button Primary(string title,Action action){var button=Button(title,action);button.Style=Ui.Style("Signature");return button;}
    public static void OpenUrl(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    public static void Reveal(string path){var full=Path.GetFullPath(path);if(!File.Exists(full)&&!Directory.Exists(full)){var parent=Path.GetDirectoryName(full);if(parent is null||!Directory.Exists(parent))return;full=parent;Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=false,Arguments=Quoted(full)});return;}Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=false,Arguments="/select,"+Quoted(full)});}
    // One argument for explorer.exe: a trailing backslash (a drive root) must be doubled or it escapes the closing quote.
    private static string Quoted(string path)=>"\""+path+(path.EndsWith('\\')?"\\":"")+"\"";
    private void Choose(){if(busy)return;var picker=new OpenFileDialog{Multiselect=true,Title=L10n.Text("Choose files for Nashi")};if(picker.ShowDialog(this)==true){Add(picker.FileNames);ShowLibrary(false);}}
    private void Add(IEnumerable<string> paths){foreach(var path in paths.Where(p=>File.Exists(p)||Directory.Exists(p))){var full=Path.GetFullPath(path);if(!files.Contains(full))files.Add(full);}Refresh();}
    internal void ShowLibrary(bool visible)
    {
        var changed=LibraryVisible!=visible;
        if(!busy)L10n.SetText(status,PrivacyNote);
        selectedLibraryId=selectedLibraryId??library.Entries.FirstOrDefault()?.Id;home.Visibility=visible?Visibility.Collapsed:Visibility.Visible;libraryPage.Visibility=visible?Visibility.Visible:Visibility.Collapsed;homeScroll.IsHitTestVisible=!visible;libraryScroll.IsHitTestVisible=visible;
        nav.Select(visible?1:0);search.Visibility=visible?Visibility.Visible:Visibility.Collapsed;ShowSetupState();if(visible)RenderLibrary();if(changed&&IsLoaded)Theme.Entrance(visible?libraryPage:home);
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
            files.Clear();Add(result.Paths);Record(result.Label,result.Paths);L10n.SetText(status,result.Label+". Choose a tool or create a ZIP.");
        }
        catch(Exception error)when(ClipboardImport.IsClipboardError(error)){L10n.SetText(status,"Couldn’t paste: "+error.Message);}
    }
    private void PasteClipboard()
    {
        if(busy)return;try{Import(Clipboard.GetDataObject()??new DataObject());}catch(Exception error)when(ClipboardImport.IsClipboardError(error)){L10n.SetText(status,"Clipboard is busy. Try Paste again.");}
    }
    internal bool HandleShortcut(Key key,ModifierKeys modifiers,DependencyObject? focus)
    {
        if(modifiers==ModifierKeys.Control&&key==Key.OemComma){ShowSettings();return true;}
        if(modifiers==ModifierKeys.None&&key==Key.F1){ShowShortcuts();return true;}
        if(modifiers==ModifierKeys.Control&&key==Key.D1){ShowLibrary(false);return true;}
        if(modifiers==ModifierKeys.Control&&key==Key.D2){ShowLibrary(true);return true;}
        if(modifiers==ModifierKeys.Control&&key==Key.F){ShowLibrary(true);search.Focus();search.SelectAll();return true;}
        if(busy)return false;
        if(modifiers==ModifierKeys.Control&&key==Key.O){Choose();return true;}
        if(modifiers==ModifierKeys.Control&&key==Key.U){_ = CheckUpdatesAsync(true);return true;}
        if(LibraryVisible&&key==Key.Down&&modifiers==ModifierKeys.None&&ReferenceEquals(focus,search)){SelectLibrary(1);return true;}
        if(ClipboardImport.IsEditing(focus))return false;
        if(!LibraryVisible&&modifiers==ModifierKeys.None&&key==Key.Delete&&list.IsKeyboardFocusWithin)
        {
            var selected=list.SelectedItems.Cast<string>().ToArray();if(selected.Length==0)return false;
            foreach(var path in selected)files.Remove(path);Refresh();return true;
        }
        if(LibraryVisible&&modifiers==ModifierKeys.None&&key is Key.Up or Key.Down){SelectLibrary(key==Key.Down?1:-1);return true;}
        if(LibraryVisible&&modifiers==ModifierKeys.None&&key==Key.Space&&focus is not System.Windows.Controls.Button){PreviewSelectedLibrary();return true;}
        if(LibraryVisible&&modifiers==ModifierKeys.None&&key==Key.Enter&&focus is not System.Windows.Controls.Button){ReuseSelectedLibrary();return true;}
        if(modifiers==ModifierKeys.Control&&key==Key.V){PasteClipboard();return true;}
        if(modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&key==Key.D)
        {OpenCopiedLink();return true;}
        if((modifiers==ModifierKeys.Control&&key is Key.Space or Key.Enter)||(modifiers==ModifierKeys.None&&key==Key.Enter&&focus is not System.Windows.Controls.Button)){ShowWheel();return true;}
        return false;
    }
    private readonly System.Windows.Threading.DispatcherTimer searchTyping=new(){Interval=TimeSpan.FromMilliseconds(150)};
    // The reuse buttons of the rows on screen, so busy changes only enable or disable them instead of rebuilding every row.
    private readonly List<(Button Button,bool Available)> reuseButtons=[];
    private void RenderLibrary()
    {
        searchTyping.Stop();reuseButtons.Clear();
        libraryCards.Clear();libraryRows.Children.Clear();recentRows.Children.Clear();var filtered=SearchLibrary(search.Text).ToArray();
        libraryCount.Text=L10n.Text(library.Entries.Count==1?"1 file":$"{library.Entries.Count} files");
        if(filtered.Length==0)libraryRows.Children.Add(Empty(library.Entries.Count==0?"Nothing here yet. Paste something or make your first fresh copy.":"No matches. Try a file name or tool."));
        // Grouped by day, newest first, the way the entries are stored.
        string? group=null;foreach(var entry in filtered)
        {
            var day=entry.Created.ToLocalTime().Date;var next=day==DateTime.Today?"Today":day==DateTime.Today.AddDays(-1)?"Yesterday":"Earlier";
            if(next!=group){group=next;var heading=Text(next,12,"Muted",weight:FontWeights.SemiBold);heading.Margin=new(10,libraryRows.Children.Count==0?4:12,10,4);libraryRows.Children.Add(heading);}
            else if(libraryRows.Children.Count>0)libraryRows.Children.Add(Separator());
            libraryRows.Children.Add(LibraryRow(entry));
        }
        foreach(var entry in library.Entries.Take(3)){if(recentRows.Children.Count>0)recentRows.Children.Add(Separator());recentRows.Children.Add(LibraryRow(entry,true));}
        if(library.Entries.Count==0)recentRows.Children.Add(Empty("Your next fresh copy will land here."));
    }
    // A hairline between rows, starting where the text starts.
    private static Border Separator(){var line=new Border{Height=1,Margin=new(58,0,10,0),IsHitTestVisible=false};line.SetResourceReference(Border.BackgroundProperty,"Line");return line;}
    private static TextBlock Empty(string message){var text=Text(message,13,"Muted");text.Margin=new(12,14,12,14);return text;}
    // A row: thumbnail, name, what was done and when; preview, reuse and reveal appear on hover or selection.
    private Border LibraryRow(LibraryEntry entry,bool compact=false)
    {
        var row=new DockPanel();var available=entry.AvailablePaths;var buttons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Opacity=0};DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
        Button Action(string glyph,string tip,string name,Action action){var button=Button(glyph,action);button.Style=Ui.Style("IconPlain");L10n.Bind(button,FrameworkElement.ToolTipProperty,tip);AutomationProperties.SetName(button,name);buttons.Children.Add(button);return button;}
        var preview=Action("\uE890","Preview","Preview "+entry.Name,()=>PreviewEntry(entry));preview.IsEnabled=available.Length>0;
        var reuse=Action("\uE7A7","Use in Nashi","Use "+entry.Name+" again",()=>{if(busy)return;var existing=entry.AvailablePaths.ToArray();if(existing.Length==0){L10n.SetText(status,"These files have moved or were deleted.");RenderLibrary();return;}files.Clear();Add(existing);ShowLibrary(false);L10n.SetText(status,existing.Length==entry.Paths.Length?"Ready for another round.":"Available files selected. Some files have moved or were deleted.");});reuse.IsEnabled=!busy&&available.Length>0;reuseButtons.Add((reuse,available.Length>0));
        var reveal=Action("\uE838","Show in Explorer","Show "+entry.Name+" in Explorer",()=>{var existing=entry.AvailablePaths;if(existing.Length>0)Reveal(existing[0]);else{L10n.SetText(status,"This file has moved or was deleted.");RenderLibrary();}});reveal.IsEnabled=available.Length>0;
        var fileIcon=Ui.Thumb(entry.Paths[0],36);fileIcon.Margin=new(0,0,12,0);DockPanel.SetDock(fileIcon,Dock.Left);row.Children.Add(fileIcon);
        var text=new StackPanel{Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};var name=Text(entry.Name,13,localize:false,weight:FontWeights.SemiBold);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.ToolTip=string.Join(Environment.NewLine,entry.Paths);text.Children.Add(name);
        var info=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,2,0,0)};info.Children.Add(Text(entry.Action,11.5,"Muted"));info.Children.Add(Text(" · "+entry.Created.ToLocalTime().ToString("g"),11.5,"Muted",localize:false));if(available.Length<entry.Paths.Length){info.Children.Add(Text(" · ",11.5,"Muted",localize:false));info.Children.Add(Text("Files missing",11.5,"Muted"));fileIcon.Opacity=name.Opacity=.45;}text.Children.Add(info);row.Children.Add(text);
        var card=new Border{Child=row,CornerRadius=new(14),Padding=new(10,7,8,7),MinHeight=50,BorderThickness=new(1),Background=Brushes.Transparent,BorderBrush=Brushes.Transparent};
        card.MouseEnter+=(_,_)=>Mark(card);card.MouseLeave+=(_,_)=>Mark(card);card.GotKeyboardFocus+=(_,_)=>Mark(card);card.LostKeyboardFocus+=(_,_)=>Mark(card);
        if(!compact)ConfigureLibraryRow(card,entry);return card;
    }
    private void OpenDownload()
    {
        if(webWindow?.IsVisible==true){webWindow.Activate();link.Focus();return;}
        var dialog=new Window{Owner=this,Title="Download from the web · Nashi",Width=700,Height=580,MinWidth=640,MinHeight=400,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=webPage};Theme.Attach(dialog);L10n.Bind(dialog,Ui.HeadingProperty,"Download from Link");webWindow=dialog;
        dialog.Closing+=(_,e)=>{if(busy){e.Cancel=true;L10n.SetText(status,"Finish or cancel the current operation first.");}};
        dialog.Closed+=(_,_)=>{inspection?.Cancel();dialog.Content=null;webWindow=null;};dialog.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!busy){dialog.Close();e.Handled=true;}};
        // The frame template arrives as the window opens and rebuilds its content, so the link field takes keyboard focus
        // again once the window has drawn, and once more after input already queued.
        void FocusLink(){if(webWindow is null)return;webWindow.Activate();Keyboard.Focus(link);}
        dialog.Loaded+=(_,_)=>FocusLink();dialog.ContentRendered+=(_,_)=>FocusLink();dialog.Show();link.Focus();dialog.Dispatcher.BeginInvoke(FocusLink,System.Windows.Threading.DispatcherPriority.Input);
    }
    private void SetBusy(bool value){busy=value;updateButton.IsEnabled=!value&&!checking;workArea.IsEnabled=!value;webPage.IsEnabled=!value;ShowSetupState();foreach(var (button,available) in reuseButtons)button.IsEnabled=!value&&available;}
    // Finish Setup stays in the toolbar only while media or office tools are missing.
    private void ShowSetupState()=>toolsSetupButton.Visibility=!LibraryVisible&&!(tools.Ready&&officeRuntime.Executable is not null)?Visibility.Visible:Visibility.Collapsed;
    internal const string ShortcutHelp="Ctrl+O  Choose files\nCtrl+V  Paste files, text, an image or link\nCtrl+Space / Enter  Open tools and conversions\nDelete  Remove selected files from Nashi\nCtrl+1 / Ctrl+2  Home / Library\nCtrl+F  Find in Library\nCtrl+Shift+D  Use a copied download link\nCtrl+U  Check for updates\n\nWheel: arrows / Tab to choose, Enter to open, Esc to close\nMedia: Space play/pause, ←/→ 1 second, Shift+←/→ 1/30s, I/O set range\nCrop: drag, Shift locks ratio, Enter saves\nEditing text: normal paste and arrow keys are preserved.";
    internal void ShowShortcuts()=>MessageBox.Show(this,L10n.Text(ShortcutHelp),L10n.Text("Nashi · Features & shortcuts"));
    private bool All(string[] extensions)=>files.Count>0&&files.All(p=>extensions.Contains(Path.GetExtension(p).TrimStart('.').ToLowerInvariant()));
    private bool Images=>files.Count>0&&files.All(FeatureCatalog.IsImage);
    private bool Video=>All(["mp4","mov","mkv","webm","avi","wmv","m4v","gif"]);
    private bool Audio=>All(["mp3","m4a","wav","flac","ogg","opus","aiff","aif","wma","aac"]);
    private IReadOnlyList<ToolChoice> Choices()
    {
        return FeatureCatalog.Tools(files).Select(id=>new ToolChoice(id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>id},FeatureCatalog.ToolNames[id],ToolSymbol(id))).ToArray();
    }
    private void Refresh()
    {
        // UI copy goes through L10n so it follows the language setting; file names and folder paths stay verbatim.
        emptyState.Visibility=files.Count==0?Visibility.Visible:Visibility.Collapsed;selectionState.Visibility=files.Count==0?Visibility.Collapsed:Visibility.Visible;
        if(files.Count==1){selection.Text=Path.GetFileName(files[0]);fileDescription.Text=Path.GetDirectoryName(files[0])??"";}
        else if(files.Count>1){var names=files.Select(Path.GetFileName).ToArray();fileDescription.Text=files.Count==2?$"{names[0]} {L10n.Text("and")} {names[1]}":$"{names[0]}, {names[1]} {L10n.Text($"and {files.Count-2} more")}";L10n.SetText(selection,$"{files.Count} files");}
        if(files.Count>0&&SelectionSize() is {} size)fileDescription.Text+=(fileDescription.Text.Length>0?" · ":"")+size;
        actions.Children.Clear();
        BuildSelectionActions();
    }
    private void ShowWheel()
    {
        if(busy)return;var choices=Choices().ToList();if(FeatureCatalog.Formats(files).Length>0)choices.Insert(0,new("convert","Convert","⇄"));
        if(choices.Count>0)new WheelWindow(this,choices,async choice=>{if(choice.Id=="convert")ShowConversionWheel();else await EditAsync(choice);}).Show();
    }
    private void ShowConversionWheel()
    {
        if(busy)return;var choices=FeatureCatalog.Formats(files).Select(format=>new ToolChoice("convert:"+format,format.ToUpperInvariant(),"↗")).ToArray();
        if(choices.Length>0)new WheelWindow(this,choices,async choice=>await EditAsync(choice)).Show();
    }
    private async Task EditAsync(ToolChoice choice)
    {
        if(busy||files.Count==0)return;string? temporary=null;try
        {
            var requestedFormat=choice.Id.StartsWith("convert:",StringComparison.Ordinal)?choice.Id[8..]:null;if(requestedFormat is not null)choice=new("convert","Convert to "+requestedFormat.ToUpperInvariant(),"↗");
            choice=choice with{Id=choice.Id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>choice.Id}};
            SetBusy(true);var imageSelection=Images;var mediaSelection=Video||Audio;var selected=files.ToArray();MediaInfo? info=null;BitmapSource? preview=null;var input=selected[0];
            if(choice.Id=="removeMetadata"){SetBusy(false);await EditMetadataAsync(selected);return;}
            if(choice.Id=="readQRCode"){SetBusy(false);await ReadQrAsync(selected);return;}
            if(!imageSelection&&!mediaSelection&&choice.Id is not ("extractArchive" or "unzip")){SetBusy(false);await EditDocumentAsync(choice,selected,requestedFormat);return;}
            // Decoding a large photo takes a moment; it happens off the UI thread so the window stays responsive.
            if(imageSelection)preview=await Task.Run(()=>ImageEngine.Load(input));
            else if(mediaSelection)
            {
                if(!tools.Ready){MessageBox.Show(this,L10n.Text("Choose Set up media tools once to enable media editing."),L10n.Text("Media setup"));return;}
                var engine=new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe"));info=await engine.InspectAsync(input);
                if(choice.Id is "cropVideo" or "redactVideo")
                {
                    temporary=Path.Combine(Path.GetTempPath(),"Nashi-preview-"+Guid.NewGuid().ToString("N")+".png");
                    await ProcessRunner.RunAsync(tools.Executable("ffmpeg"),["-v","error","-i",input,"-frames:v","1",temporary]);var frame=temporary;preview=await Task.Run(()=>ImageEngine.Load(frame));
                }
            }
            SetBusy(false);var editor=new ToolEditor(this,choice.Id,choice.Title,input,info,preview,selected,choice.Id=="convert"?FeatureCatalog.Formats(selected):null);editor.ConfigureProfiles(preferences,requestedFormat);editor.ConfigureMediaRuntime(tools);var accepted=editor.ShowDialog()==true;if(temporary is not null)File.Delete(temporary);if(!accepted)return;
            var inputs=choice.Id is "cropVideo" or "redactVideo" or "annotate"?new[]{input}:editor.OrderedInputs;
            if(imageSelection)await RunAsync(async(destination,p,t)=>
            {
                using var tx=new ExportTransaction(destination);var exported=await OnStaAsync(()=>ImageEngine.Apply(choice.Id,inputs,tx.StagingDirectory,editor.ImageOptions with{EncoderPath=tools.Ready?tools.Executable("ffmpeg"):null},choice.Id is "convert" or "compress"?editor.Format:null,t,p));
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
    private async Task RunAsync(Func<string,IProgress<OperationProgress>,CancellationToken,Task<IReadOnlyList<string>>> work,string label="Fresh copy")
    {
        if(busy)return;var owner=webWindow?.IsActive==true?webWindow:this;string destination;
        if(label=="Move"||preferences.Value.OutputMode=="ask"||(files.Count==0&&owner==this)){var folder=new OpenFolderDialog{Title=L10n.Text("Save new files to…"),InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};if(folder.ShowDialog(owner)!=true)return;destination=folder.FolderName;}
        else destination=owner==webWindow?preferences.Value.DownloadFolder:preferences.Value.OutputMode=="folder"&&!string.IsNullOrWhiteSpace(preferences.Value.OutputFolder)?preferences.Value.OutputFolder:Path.GetDirectoryName(files[0])!;
        // Progress shows in the window while it is in view; otherwise in a toast. Either way a toast confirms the result.
        SetBusy(true);using var cancellation=new CancellationTokenSource();var inWindow=owner==this&&IsVisible&&WindowState!=WindowState.Minimized&&jobCard is not null;ToastWindow? toast=null;
        if(inWindow)ShowJob(label,cancellation.Cancel);else{toast=new ToastWindow(owner,cancellation.Cancel,files.FirstOrDefault());toast.Show();}
        try{var outputs=await work(destination,new Progress<OperationProgress>(p=>{if(inWindow)UpdateJob(p);else toast?.Update(p);}),cancellation.Token);HideJob();if(toast is null){toast=new ToastWindow(owner,null,outputs.FirstOrDefault());toast.Show();}toast.Complete(outputs);Record(label,outputs);L10n.SetText(status,$"Saved {outputs.Count} new {(outputs.Count==1?"file":"files")}. Originals kept.");if(preferences.Value.RevealFinished&&outputs.Count>0)Reveal(outputs[0]);}
        catch(OperationCanceledException){HideJob();toast?.Close();L10n.SetText(status,"Cancelled. Originals kept.");}
        catch(Exception error){HideJob();toast?.Close();if(inWindow)ShowError("Couldn’t finish",error.Message,()=>_=RunAsync(work,label));else MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Couldn’t finish"),MessageBoxButton.OK,MessageBoxImage.Information);}
        finally{SetBusy(false);}
    }
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
    internal void OfferUpdate(WindowsUpdate candidate){update=candidate;L10n.Bind(updateButton,ContentControl.ContentProperty,"Update to "+candidate.Version);if(updateBanner is not null)updateBanner.Visibility=Visibility.Visible;tray?.UpdateAvailable(candidate.Version);}
    private async Task CheckUpdatesAsync(bool user)
    {
        if(busy||checking)return;checking=true;updateButton.IsEnabled=false;if(user)L10n.SetText(status,"Checking for updates…");try
        {
            if(update is null){update=await PublicUpdates.CheckAsync(UpdateInstaller.Current);if(update is null){if(user)L10n.SetText(status,"You have the latest published Windows version.");return;}OfferUpdate(update);}
            // Background checks only offer the update (banner and tray); asking to install is for when the user asks.
            if(!user||busy)return;
            if(MessageBox.Show(this,L10n.Text("Download, verify and install Nashi "+update.Version+"? The app will restart. No account or token is needed."),L10n.Text("Nashi update"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=new ToastWindow(this,cancellation.Cancel);toast.Show();
            try{await UpdateInstaller.StageAsync(update,new Progress<OperationProgress>(toast.Update),cancellation.Token);busy=false;Application.Current.Shutdown();}finally{toast.Close();SetBusy(false);}
        }
        catch(OperationCanceledException){L10n.SetText(status,"Update cancelled.");}catch(Exception error){if(user)MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Couldn’t check for updates"));}
        finally{checking=false;updateButton.IsEnabled=!busy;}
    }
}
