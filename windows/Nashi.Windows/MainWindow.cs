using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Text.Json;
using System.Text;
using System.Net.Http;
using Microsoft.Win32;
using Nashi.Core;
using DeviceClass=Windows.Devices.Enumeration.DeviceClass;
using DeviceInformation=Windows.Devices.Enumeration.DeviceInformation;
using BitmapAlphaMode=Windows.Graphics.Imaging.BitmapAlphaMode;
using BitmapPixelFormat=Windows.Graphics.Imaging.BitmapPixelFormat;
using SoftwareBitmap=Windows.Graphics.Imaging.SoftwareBitmap;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.Media.Control;
namespace Nashi.Windows;

public sealed partial class MainWindow:Window
{
    private readonly ObservableCollection<string> files=[];
    private readonly ListBox list=new(){MinHeight=0,MaxHeight=110};
    private readonly Grid actions=new();
    private readonly TextBlock selection=new(){FontSize=23,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock status=Text("Files stay on your computer.",12,"Muted");
    private readonly TextBox link=new(){ToolTip="Paste a public video, image or audio page link"};
    private readonly ComboBox downloadKind=new(){ItemsSource=new[]{"video","audio","thumbnail"},SelectedIndex=0,MinWidth=110};
    private readonly Toolchain tools=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools","v1"),Environment.GetEnvironmentVariable("PATH"));
    private readonly LibraryStore library;
    private readonly PreferencesStore preferences;private GlobalDragController? globalDrag;
    private readonly StackPanel home=new(),libraryPage=new(),libraryRows=new(),recentRows=new();
    private readonly TextBox recentSearch=new(){ToolTip="Find a recent file",Width=165};private readonly TextBlock recentCount=Text("0",11,"Muted");private Button? chooseFilesButton,openWheelButton;private readonly TextBlock fileDescription=Text("Images, videos, documents, and all the little things in between.",12,"Muted");
    private readonly TextBox search=new(){ToolTip="Find a file or tool in Library",MinWidth=200};
    private readonly DockPanel manage=new();private readonly FileGarden garden=new();private readonly StackPanel webContent=new();private Window? webWindow;
    internal Window DownloadWindow=>webWindow??this;
    private readonly Button homeButton,libraryButton,updateButton,toolsSetupButton;private readonly ComboBox appearance;
    private readonly StackPanel workArea=new();
    private readonly ScrollViewer homeScroll=new(),libraryScroll=new();
    private Border? islandPanel,islandPill;private Button? islandAction;private Popup? islandActionPopup;private TextBlock? islandHint;private Grid? islandDashboard,islandNowView,islandShapeContent;private UIElement? mainWorkspace,islandCompactFace;private bool islandExpanded,pillModeEnabled,islandHiddenByAutomation;
    private Border? islandNowMedia,islandNowDropArea;private ColumnDefinition? islandNowMediaColumn,islandNowGapColumn;private Image? islandNowArtwork;private TextBlock? islandNowTitle,islandNowArtist;private Slider? islandNowSeek;private WrapPanel? islandFilePreviews,islandFormatChips;private StackPanel? islandDropPrompt,pillEqualizer;private ProgressBar? islandTrayProgress;private Rectangle? islandDropOutline;private Ellipse? pillBusyIndicator;private bool islandTrayDragActive;private Border? islandOnboarding;private Button? islandSettingsButton;
    private readonly Dictionary<string,Button> islandDockButtons=[];private Button? islandSearchButton;
    private Button? islandLabelButton,islandNowMediaToggle,islandMediaPlaybackToggle,islandCompactMediaToggle;private Popup? islandMediaPopup;private TextBox? islandActionSearchField;private Slider? islandCompactMediaSeek;private Image? islandCompactPopupArtwork;private TextBlock? islandCompactMediaPosition,islandCompactMediaDuration,islandCompactMediaTitle;private bool islandCompactMediaSeeking;
    private Grid? islandWidgetHost;private readonly Dictionary<string,Button> islandTabButtons=[];private readonly Dictionary<string,string> islandBluetoothNames=[];private RichTextBox? islandNote;private TextBox? islandTodoEntry;private TextBlock? islandTimerLabel,islandMediaTitle,islandMediaArtistLabel,islandMediaEmpty,islandMediaPosition,islandMediaDuration,islandMirrorStatus;private Slider? islandMediaSeek;private Image? islandMediaArtwork,islandMirrorImage;private BitmapSource? islandCurrentArtwork;private Button? islandMirrorToggle,islandMediaPrevious,islandMediaNext;private MediaCapture? islandMirrorCapture;private System.Windows.Threading.DispatcherTimer? islandMirrorRefresh,islandBluetoothPoll;private bool islandMirrorFrameBusy,islandBluetoothInitialScan=true,islandBluetoothPollBusy,islandMediaPlaying,islandMediaSeekUserChanging,islandMediaRefreshBusy;private string? islandCurrentMedia,islandMediaArtist,islandMediaArtworkKey,islandBluetoothActivity,islandCurrentWidgetTab;private DateTimeOffset islandBluetoothActivityUntil;private readonly string islandWidgetPath;private string islandNoteText="";private string? islandNoteRichText;private string? islandCalendarPath,islandCalendarUrl;private readonly ObservableCollection<string> islandTodos=[];private readonly ObservableCollection<string> islandCompletedTodos=[];private readonly HashSet<string> islandFavoriteTodos=new(StringComparer.Ordinal);private readonly ObservableCollection<string> islandTray=[];private readonly ObservableCollection<string> islandPocket=[];private TimeSpan islandTimerRemaining=TimeSpan.FromMinutes(25);private System.Windows.Threading.DispatcherTimer? islandTimer,islandCalendarRefresh,islandStatusTimer;private GlobalSystemMediaTransportControlsSessionManager? islandMediaManager;private GlobalSystemMediaTransportControlsSession? islandObservedMediaSession;private bool islandMediaEventsConnected,islandTimerRunning;private static readonly HttpClient islandCalendarHttp=new(){Timeout=TimeSpan.FromSeconds(20)};
    private TextBox? islandPrompterScript;private TextBlock? islandPrompterPreview,islandPrompterSpeedLabel;private ScrollViewer? islandPrompterScroll;private Button? islandPrompterToggle;private System.Windows.Threading.DispatcherTimer? islandPrompterTimer;private string islandPrompterText="";private double islandPrompterSpeed=34,islandPrompterOffset;private bool islandPrompterRunning;
    private bool busy,checking,toolSetupOffered;private WindowsUpdate? update;private Border? updateBanner;private HwndSource? pillSource;private bool hotkeyRegistered;private bool? appliedPillMode;
    private ToolSetupWindow? toolSetupWindow;
    private readonly bool monitorToolSetup;
    internal bool UpdateBannerVisible=>updateBanner?.Visibility==Visibility.Visible;
    internal IReadOnlyList<string> SelectedFiles=>files;
    internal bool LibraryVisible=>libraryPage.Visibility==Visibility.Visible;
    internal bool UIAuditIslandExpanded=>islandExpanded;
    internal bool UIAuditOnboardingVisible=>islandOnboarding?.Visibility==Visibility.Visible;
    internal bool UIAuditSettingsDockVisible=>islandSettingsButton is {IsVisible:true};
    internal string? UIAuditMediaEmptyMessage=>islandMediaEmpty?.Text;
    internal string? UIAuditIslandTab=>islandCurrentWidgetTab;
    internal bool UIAuditHotkeyRegistered=>hotkeyRegistered;
    internal int UIAuditHotkeyMessages{get;private set;}
    internal bool UIAuditPillModeEnabled=>pillModeEnabled;
    internal bool UIAuditHiddenByAutomation=>islandHiddenByAutomation;
    internal bool UIAuditActionsPopupOpen=>islandActionPopup?.IsOpen==true;
    internal FrameworkElement? UIAuditActionsPopupSurface=>islandActionPopup?.Child as FrameworkElement;
    internal IReadOnlyList<Button> UIAuditToolButtons=>islandWidgetHost?.Children.OfType<UniformGrid>().SelectMany(grid=>grid.Children.OfType<Button>()).ToArray()??[];
    internal void UIAuditSetFiles(IEnumerable<string> paths){files.Clear();Add(paths);}
    internal void UIAuditShowWorkspace(bool libraryView)=>ShowIslandWorkspace(libraryView);
    internal void UIAuditPresentIsland(bool expanded,string tab="actions")
    {
        if(!pillModeEnabled)SwitchPillMode(true,initialExpanded:expanded);
        if(!IsVisible)Show();
        SetIslandExpanded(expanded,false);
        if(expanded)ShowIslandWidgetTab(tab);
        UpdateLayout();
    }
    internal void UIAuditShowActionsPopup()=>ShowIslandActions();
    internal bool UIAuditClickAction(string title)
    {
        ShowIslandActions();DesktopInput.Pump(100);
        if(islandActionPopup?.Child is not Border {Child:ScrollViewer {Content:DockPanel content}})return false;
        var buttons=content.Children.OfType<StackPanel>().SelectMany(panel=>panel.Children.OfType<Button>());
        var match=buttons.FirstOrDefault(button=>string.Equals(button.Tag as string,title,StringComparison.Ordinal));
        if(match is null)return false;DesktopInput.Click(match);return true;
    }
    internal bool UIAuditActionSearchContains(string query)
    {
        ShowIslandActions();DesktopInput.Pump(100);if(islandActionSearchField is null)return false;islandActionSearchField.Text=query;DesktopInput.Pump(100);
        if(islandActionPopup?.Child is not Border {Child:ScrollViewer {Content:DockPanel content}})return false;
        return content.Children.OfType<StackPanel>().SelectMany(panel=>panel.Children.OfType<Button>()).Any(button=>button.Visibility==Visibility.Visible&&(button.Tag as string)?.Contains(query,StringComparison.CurrentCultureIgnoreCase)==true);
    }
    internal bool UIAuditSubmitActionSearch(string query,string tab)
    {
        if(!UIAuditActionSearchContains(query)||islandActionSearchField is null)return false;Keyboard.Focus(islandActionSearchField);DesktopInput.Key(this,Key.Enter);return islandCurrentWidgetTab==tab;
    }
    internal bool UIAuditClickDock(string key)
    {
        if(key=="search"){if(islandSearchButton is null)return false;DesktopInput.Click(islandSearchButton);return true;}
        if(!islandDockButtons.TryGetValue(key,out var button))return false;DesktopInput.Click(button);return true;
    }
    internal RichTextBox? UIAuditNoteEditor=>islandNote;
    internal TextBox? UIAuditTodoEditor=>islandTodoEntry;
    internal string UIAuditWidgetStatePath=>islandWidgetPath;
    internal bool UIAuditTimerRunning=>islandTimerRunning;
    internal NashiFilePickerWindow UIAuditCreateFilePicker(string directory)=>new(this,directory);
    internal TextBox SearchField=>search;
    internal TextBox LinkField=>link;
    public MainWindow(string[]? initial=null,LibraryStore? library=null,bool checkForUpdates=true,bool offerToolSetup=true)
    {
        monitorToolSetup=checkForUpdates;
        this.library=library??new LibraryStore();preferences=new(this.library.DirectoryPath);islandWidgetPath=Path.Combine(App.IsSmokeTest?this.library.DirectoryPath:Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)+Path.DirectorySeparatorChar+"Nashi","island-widgets.json");LoadIslandWidgets();ApplyPreferences();Theme.Apply(this.library.Appearance,preferences.Value.PillAccentColor);Theme.Attach(this);
        Title="Nashi";Width=980;Height=720;MinWidth=760;MinHeight=540;WindowStyle=WindowStyle.SingleBorderWindow;AllowsTransparency=false;Background=Theme.Brush("Paper");ResizeMode=ResizeMode.CanResize;Topmost=false;ShowInTaskbar=true;WindowStartupLocation=WindowStartupLocation.CenterScreen;AllowDrop=true;
        Icon=BitmapFrame.Create(new Uri("pack://application:,,,/Assets/NashiMascot.png"));
        var root=new DockPanel{Margin=new(28,18,28,18)};Background=Brushes.Transparent;AddHandler(UIElement.PreviewMouseWheelEvent,new MouseWheelEventHandler(RoutePageMouseWheel),true);
        var header=new DockPanel{Margin=new(0,0,0,18)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var controls=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(controls,Dock.Right);header.Children.Add(controls);
        homeButton=Button("Home",()=>ShowLibrary(false));libraryButton=Button("Library",()=>ShowLibrary(true));toolsSetupButton=Button(tools.Ready&&officeRuntime.Executable is not null?"Tools ready":"Finish setup",ShowToolSetupMenu);controls.Children.Add(Button("Nashi",()=>SetIslandDashboardVisible(true)));controls.Children.Add(toolsSetupButton);controls.Children.Add(homeButton);controls.Children.Add(libraryButton);
        appearance=new ComboBox{ItemsSource=new[]{"System","Light","Dark"},SelectedItem=char.ToUpper(this.library.Appearance[0])+this.library.Appearance[1..]};
        updateButton=Button("Check for updates",async()=>await CheckUpdatesAsync(true));
        controls.Children.Add(Text("On your PC",11,"Muted"));controls.Children.Add(Button("Settings",ShowSettings));
        foreach(var control in controls.Children.OfType<Button>()){control.Padding=new(10,6,10,6);control.FontSize=11;}
        DockPanel.SetDock(status,Dock.Bottom);status.Margin=new(4,14,4,0);root.Children.Add(status);
        var pages=new Grid();homeScroll.Content=home;homeScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;homeScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;libraryScroll.Content=libraryPage;libraryScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;libraryScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;pages.Children.Add(homeScroll);pages.Children.Add(libraryScroll);root.Children.Add(pages);
        var updateRow=new DockPanel();DockPanel.SetDock(updateButton,Dock.Right);updateRow.Children.Add(updateButton);var updateCaption=new StackPanel();updateCaption.Children.Add(Text("A little fresher. Ready when you are.",16));updateCaption.Children.Add(Text("You choose when to download and install.",12,"Muted"));updateRow.Children.Add(updateCaption);updateBanner=Card(updateRow);updateBanner.Visibility=Visibility.Collapsed;home.Children.Add(updateBanner);
        home.Children.Add(homeTitle);var intro=Text("Convert, fine-tune, and send them on their way.",14,"Muted");intro.Margin=new(0,6,0,22);home.Children.Add(intro);
        home.Children.Add(workArea);
        var drop=new Grid();drop.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});drop.ColumnDefinitions.Add(new(){Width=new(166)});
        var dropText=new StackPanel();drop.Children.Add(dropText);dropText.Children.Add(selectionPill);selection.Margin=new(0,12,0,8);dropText.Children.Add(selection);dropText.Children.Add(fileDescription);
        var picks=new WrapPanel{Margin=new(0,14,0,8)};chooseFilesButton=Primary("Choose files",Choose);picks.Children.Add(chooseFilesButton);openWheelButton=Primary("Open wheel",ShowWheel);picks.Children.Add(Button("Paste",PasteClipboard));picks.Children.Add(openWheelButton);dropText.Children.Add(picks);
        Grid.SetColumn(garden,1);drop.Children.Add(garden);
        var dropStack=new StackPanel();dropStack.Children.Add(drop);list.ItemsSource=files;list.SelectionMode=SelectionMode.Extended;
        BuildFileChips();var clear=Button("Clear",()=>{files.Clear();Refresh();});DockPanel.SetDock(clear,Dock.Right);manage.Children.Add(clear);manage.Children.Add(list);dropStack.Children.Add(manage);
        shortcutHints.Orientation=Orientation.Horizontal;shortcutHints.Margin=new(0,12,0,0);dropStack.Children.Add(shortcutHints);workArea.Children.Add(Card(dropStack));
        actions.Margin=new(0,10,0,0);
        var web=new StackPanel();web.Children.Add(Text("Download a link",17));web.Children.Add(Text("Video, audio, or an image. Straight into your tools.",12,"Muted"));
        var download=new DockPanel{Margin=new(0,12,0,0)};var save=Primary("Download ↗",async()=>await DownloadAsync());DockPanel.SetDock(save,Dock.Right);download.Children.Add(save);DockPanel.SetDock(downloadKind,Dock.Right);download.Children.Add(downloadKind);download.Children.Add(link);web.Children.Add(download);
        web.Children.Add(Text("Ctrl+Shift+D  Use a copied link  ·  Public, downloadable media",11,"Muted"));webContent.Margin=new(24);webContent.Children.Add(Text("Download from the web",27));webContent.Children.Add(Text("A link. A file. Yours to work with.",14,"Muted"));var webCard=Card(web);webCard.Margin=new(0,20,0,0);webContent.Children.Add(webCard);BuildWebOptions();
        var webHeader=new DockPanel();var shortcut=Text("Ctrl+Shift+D",12,"Muted");DockPanel.SetDock(shortcut,Dock.Right);webHeader.Children.Add(shortcut);var arrow=Text("↓",28,"Gold");arrow.Margin=new(0,0,14,0);DockPanel.SetDock(arrow,Dock.Left);webHeader.Children.Add(arrow);var caption=new StackPanel();caption.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Left);caption.Children.Add(Text("Download a link",15));caption.Children.Add(Text("Video, audio, or an image. Straight into your tools.",12,"Muted"));webHeader.Children.Add(caption);var webButton=Button("",OpenDownload);webButton.Content=webHeader;webButton.HorizontalContentAlignment=HorizontalAlignment.Stretch;webButton.Margin=new(0);webButton.Padding=new(18);webButton.BorderThickness=new(0);var webLanding=Card(webButton);webLanding.Padding=new(0);workArea.Children.Add(webLanding);workArea.Children.Add(actions);
        var recentHeading=new DockPanel{Margin=new(0,24,0,10)};var all=Button("See all  ↗",()=>ShowLibrary(true));DockPanel.SetDock(all,Dock.Right);recentHeading.Children.Add(all);recentSearch.TextChanged+=(_,_)=>RenderLibrary();DockPanel.SetDock(recentSearch,Dock.Right);recentHeading.Children.Add(recentSearch);var recentTitle=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};recentTitle.Children.Add(Text("Freshly made",19));recentCount.Margin=new(10,0,0,0);recentTitle.Children.Add(recentCount);recentHeading.Children.Add(recentTitle);home.Children.Add(recentHeading);var recentCard=Card(recentRows);recentCard.Padding=new(7);home.Children.Add(recentCard);
        var footer=new WrapPanel{Margin=new(0,14,0,0)};footer.Children.Add(Button("Set up media tools",async()=>await SetupAsync()));footer.Children.Add(Button("Set up office tools",async()=>await SetupOfficeAsync()));footer.Children.Add(Button("Features & shortcuts",ShowShortcuts));home.Children.Add(new Expander{Header="Tools & shortcuts",Content=footer,Margin=new(4,8,4,0)});
        libraryPage.Children.Add(Text("Your Library",30));libraryPage.Children.Add(Text("Freshly made, pasted, and ready for another round.",14,"Muted"));
        var searchLabel=Text("Find a file or tool  ·  Ctrl+F",12,"Muted");searchLabel.Margin=new(4,20,4,0);libraryPage.Children.Add(searchLabel);libraryPage.Children.Add(search);libraryPage.Children.Add(libraryRows);search.TextChanged+=(_,_)=>RenderLibrary();
        AutomationProperties.SetAutomationId(search,"LibrarySearch");AutomationProperties.SetAutomationId(link,"DownloadLink");AutomationProperties.SetAutomationId(list,"SelectedFiles");
        Closing+=(_,e)=>{if(busy){e.Cancel=true;L10n.SetText(status,"Finish the current operation or choose Cancel before closing Nashi.");}};Deactivated+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>{if(islandActionPopup is {IsOpen:true} actionPopup&&islandAction?.IsMouseOver!=true&&actionPopup.IsMouseOver!=true)actionPopup.SetCurrentValue(Popup.IsOpenProperty,false);if(islandMediaPopup is {IsOpen:true} mediaPopup&&islandLabelButton?.IsMouseOver!=true&&mediaPopup.IsMouseOver!=true)mediaPopup.SetCurrentValue(Popup.IsOpenProperty,false);}),System.Windows.Threading.DispatcherPriority.Background);Closed+=async(_,_)=>{await StopIslandMirror();StopIslandBluetoothMonitor();StopIslandPrompter();};
        DragOver+=(_,e)=>{e.Effects=!busy&&(e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent(DataFormats.UnicodeText)||e.Data.GetDataPresent(DataFormats.Html)||e.Data.GetDataPresent("UniformResourceLocatorW"))?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        Drop+=(_,e)=>{if(!busy){if(!e.Data.GetDataPresent(DataFormats.FileDrop)&&DragPayload.Parse(e.Data) is not null)OnGlobalDrop("download:auto",e.Data,null);else Import(e.Data);}e.Handled=true;};
        BuildIslandShell(root,initial is {Length:>0});
        PreviewKeyDown+=(_,e)=>{if(pillModeEnabled&&islandExpanded&&e.Key==Key.K&&Keyboard.Modifiers==ModifierKeys.Control){ShowIslandActions();e.Handled=true;}else if(e.Key==Key.Escape&&islandActionPopup?.IsOpen==true){islandActionPopup.SetCurrentValue(Popup.IsOpenProperty,false);e.Handled=true;}else if(e.Key==Key.Escape&&islandExpanded){SetIslandExpanded(false);e.Handled=true;}else if(HandleShortcut(e.Key,Keyboard.Modifiers,Keyboard.FocusedElement as DependencyObject))e.Handled=true;};
        if(initial is not null)Add(initial);else Refresh();ShowLibrary(false);RenderLibrary();Loaded+=(_,_)=>Theme.Entrance(LibraryVisible?libraryPage:home);
        if(this.library.RecoveryNotice is {} notice)L10n.SetText(status,notice);
        else if(preferences.RecoveryNotice is {} settingsNotice)L10n.SetText(status,settingsNotice);
        if(checkForUpdates)
        {
            Loaded+=(_,_)=>SetupDesktop();Closed+=(_,_)=>globalDrag?.Dispose();
            if(offerToolSetup)Loaded+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(islandExpanded)OfferFirstRunTools();});
            Loaded+=async(_,_)=>{if(preferences.Value.AutomaticUpdateChecks)await CheckUpdatesAsync(false);};
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromHours(1)};timer.Tick+=async(_,_)=>{if(preferences.Value.AutomaticUpdateChecks)await CheckUpdatesAsync(false);};Closed+=(_,_)=>timer.Stop();timer.Start();
        }
    }
    private void BuildIslandShell(DockPanel content,bool hasInitialFiles)
    {
        mainWorkspace=content;
        islandPanel=new Border{Width=PillGeometry.CollapsedWidth,Height=PillGeometry.CollapsedHeight,Background=new SolidColorBrush(Color.FromRgb(5,5,7)),BorderThickness=new Thickness(0),Effect=new DropShadowEffect{Color=Colors.Black,Opacity=.42,BlurRadius=22,ShadowDepth=8},Visibility=Visibility.Visible};
        islandPill=islandPanel;
        islandPanel.Clip=PillGeometry.Create(PillGeometry.CollapsedWidth,PillGeometry.CollapsedHeight);
        islandPanel.SizeChanged+=(_,_)=>UpdatePillClip();
        islandShapeContent=new Grid();islandPanel.Child=islandShapeContent;
        var face=new DockPanel{LastChildFill=true,Margin=new Thickness(10,0,10,0)};
        var mark=new Image{Source=FileGarden.Artwork("NashiMark"),Width=18,Height=18,Stretch=System.Windows.Media.Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center};
        AutomationProperties.SetName(mark,"Nashi");DockPanel.SetDock(mark,Dock.Left);face.Children.Add(mark);
        var activity=new Grid{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Width=82,Height=20};
        pillEqualizer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
        foreach(var height in new[]{5d,11d,7d})pillEqualizer.Children.Add(new Rectangle{Width=2.5,Height=height,RadiusX=1.25,RadiusY=1.25,Fill=Brushes.White,Margin=new Thickness(1,0,1,0),VerticalAlignment=VerticalAlignment.Center});
        activity.Children.Add(pillEqualizer);pillBusyIndicator=new Ellipse{Width=13,Height=13,Stroke=IslandColor("#D3B46B"),StrokeThickness=1.5,StrokeDashArray=new DoubleCollection{2.2,1.1},Visibility=Visibility.Collapsed,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform()};activity.Children.Add(pillBusyIndicator);
        islandHint=new TextBlock{Text="",FontSize=10,Foreground=new SolidColorBrush(Color.FromArgb(190,255,255,255)),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right,TextAlignment=TextAlignment.Right,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=76};activity.Children.Add(islandHint);face.Children.Add(activity);
        islandCompactFace=face;islandShapeContent.Children.Add(face);
        var clickContent=new TextBlock{Text=""};AutomationProperties.SetName(clickContent,"Open Nashi pill");var clickTarget=new Button{Background=Brushes.Transparent,BorderThickness=new Thickness(0),Focusable=true,Cursor=Cursors.Hand,Content=clickContent};AutomationProperties.SetName(clickTarget,"Open Nashi pill");
        clickTarget.Click+=(_,_)=>{if(islandExpanded)SetIslandExpanded(false);else if(preferences.Value.PillVisibilityMode!="shortcut"){SetNoActivate(false);Activate();SetIslandExpanded(true);}};islandLabelButton=clickTarget;islandShapeContent.Children.Add(clickTarget);
        islandDashboard=BuildIslandDashboard();islandDashboard.PreviewMouseDown+=(_,_)=>ActivatePillForInput();islandDashboard.Visibility=Visibility.Collapsed;islandShapeContent.Children.Add(islandDashboard);
        AttachIslandPointerInteractions(islandPanel);
        SizeChanged+=(_,_)=>{if(pillModeEnabled)PositionIsland();};
        SourceInitialized+=(_,_)=>{if(pillModeEnabled)SetNoActivate(true);InitializePillHotkey();};
        Loaded+=(_,_)=>
        {
            if(pillModeEnabled)PositionIsland();
            if(pillModeEnabled&&preferences.Value.PillVisibilityMode=="shortcut"&&!islandExpanded)HidePillWindow();
            if(!App.IsSmokeTest)
            {
                islandStatusTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};islandStatusTimer.Tick+=(_,_)=>{UpdateIslandHint();RenderIslandDashboard();};islandStatusTimer.Start();
                StartIslandBluetoothMonitor();_ = InitializeIslandMediaEvents();UpdateIslandHint();
                if(islandCalendarUrl is not null){islandCalendarRefresh=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMinutes(15)};islandCalendarRefresh.Tick+=async(_,_)=>await RefreshIslandCalendarSubscription();islandCalendarRefresh.Start();_ = RefreshIslandCalendarSubscription();}
            }
        };
        Closed+=(_,_)=>{islandStatusTimer?.Stop();islandCalendarRefresh?.Stop();islandPrompterTimer?.Stop();UnregisterPillHotkey();StopIslandMediaEvents();};
        var firstPillOpen=preferences.Value.PillModeEnabled&&!preferences.Value.PillOnboardingCompleted;
        var openAtStartup=preferences.Value.PillModeEnabled&&preferences.Value.PillStartupBehavior=="expanded";
        SwitchPillMode(preferences.Value.PillModeEnabled,initialExpanded:hasInitialFiles||firstPillOpen||openAtStartup);
    }
    private void UpdatePillClip(){if(islandPanel is {ActualWidth:>0,ActualHeight:>0})islandPanel.Clip=PillGeometry.Create(islandPanel.ActualWidth,islandPanel.ActualHeight);}
    private void SwitchPillMode(bool enabled,bool initialExpanded=false)
    {
        if(islandPanel is null||mainWorkspace is null)return;
        initialExpanded|=enabled&&(!preferences.Value.PillOnboardingCompleted||preferences.Value.PillStartupBehavior=="expanded");
        pillModeEnabled=enabled;appliedPillMode=enabled;WindowStyle=enabled?WindowStyle.None:WindowStyle.SingleBorderWindow;AllowsTransparency=enabled;Background=enabled?Brushes.Transparent:Theme.Brush("Paper");ResizeMode=enabled?ResizeMode.NoResize:ResizeMode.CanResize;Topmost=enabled;ShowInTaskbar=!enabled;
        if(enabled)
        {
            Content=null;Content=islandPanel;islandPanel.Visibility=Visibility.Visible;Width=(initialExpanded?PillGeometry.ExpandedWidth:PillGeometry.CollapsedWidth)*preferences.Value.PillScale;Height=(initialExpanded?PillGeometry.ExpandedHeight:PillGeometry.CollapsedHeight)*preferences.Value.PillScale;islandPanel.Width=Width;islandPanel.Height=Height;
            if(islandCompactFace is not null)islandCompactFace.Visibility=initialExpanded?Visibility.Collapsed:Visibility.Visible;if(islandDashboard is not null)islandDashboard.Visibility=initialExpanded?Visibility.Visible:Visibility.Collapsed;
            islandExpanded=initialExpanded;MinWidth=0;MinHeight=0;WindowStartupLocation=WindowStartupLocation.Manual;
            if(preferences.Value.PillVisibilityMode=="always"&&!initialExpanded)SetIslandExpanded(false,false);
            if(!App.IsSmokeTest||App.IsPillModeSmoke)InitializePillHotkey();
            SetNoActivate(true);
            if(IsLoaded&&preferences.Value.PillVisibilityMode=="shortcut"&&!initialExpanded)HidePillWindow();
            if(IsLoaded)Show();
        }
        else
        {
            UnregisterPillHotkey();if(islandExpanded)SetIslandExpanded(false,false);
            Content=null;Content=mainWorkspace;MinWidth=760;MinHeight=540;Width=Math.Max(980,Width);Height=Math.Max(720,Height);WindowStartupLocation=WindowStartupLocation.CenterScreen;ShowInTaskbar=true;Show();Activate();
        }
        UpdatePillClip();
    }
    internal void ApplyPillModePreference()
    {
        if(appliedPillMode!=preferences.Value.PillModeEnabled){SwitchPillMode(preferences.Value.PillModeEnabled);return;}
        if(!pillModeEnabled)return;
        UnregisterPillHotkey();InitializePillHotkey();
    }
    private void InitializePillHotkey()
    {
        if(!pillModeEnabled||App.IsSmokeTest&&!App.IsPillModeSmoke||!PillGeometry.TryParseHotkey(preferences.Value.PillHotkey,out var modifiers,out var key))return;
        var handle=new WindowInteropHelper(this).Handle;if(handle==IntPtr.Zero)return;
        if(pillSource is null){pillSource=HwndSource.FromHwnd(handle);pillSource?.AddHook(PillHotkeyHook);}
        if(!hotkeyRegistered)hotkeyRegistered=RegisterHotKey(handle,0x4E53,modifiers,key);
        if(!hotkeyRegistered&&islandHint is not null)islandHint.Text=IslandText("Shortcut unavailable","Kısayol kullanılamıyor");
    }
    private IntPtr PillHotkeyHook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==0x0312&&wParam.ToInt32()==0x4E53){UIAuditHotkeyMessages++;handled=true;Dispatcher.BeginInvoke(new Action(TogglePillFromShortcut),System.Windows.Threading.DispatcherPriority.Input);}return IntPtr.Zero;
    }
    private void TogglePillFromShortcut()
    {
        if(!pillModeEnabled)return;
        if(!IsVisible||islandHiddenByAutomation){islandHiddenByAutomation=false;Show();SetNoActivate(false);Activate();SetIslandExpanded(true);}
        else{SetIslandExpanded(false,false);HidePillWindow();}
    }
    private void HidePillWindow()
    {
        if(!pillModeEnabled)return;islandHiddenByAutomation=true;Hide();StopHiddenPolling();
    }
    private void StopHiddenPolling(){islandStatusTimer?.Stop();islandBluetoothPoll?.Stop();}
    private void UnregisterPillHotkey()
    {
        if(hotkeyRegistered){var handle=new WindowInteropHelper(this).Handle;if(handle!=IntPtr.Zero)UnregisterHotKey(handle,0x4E53);hotkeyRegistered=false;}
        if(pillSource is not null){pillSource.RemoveHook(PillHotkeyHook);pillSource=null;}
    }
    private void SetNoActivate(bool value)
    {
        var handle=new WindowInteropHelper(this).Handle;if(handle==IntPtr.Zero)return;var style=GetWindowLongPtr(handle,-20).ToInt64();var next=value?style|0x08000000:style&~0x08000000L;SetWindowLongPtr(handle,-20,new IntPtr(next));
    }
    private void ActivatePillForInput()
    {
        if(!pillModeEnabled)return;SetNoActivate(false);if(!IsActive)Activate();
    }
    [DllImport("user32.dll",SetLastError=true)]private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint virtualKey);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool UnregisterHotKey(IntPtr window,int id);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)]private static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]private static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
    private Grid BuildIslandDashboard()
    {
        var page=new Grid{Margin=new(20,13,20,12)};page.RowDefinitions.Add(new(){Height=GridLength.Auto});page.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});page.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var onboardingContent=new DockPanel{LastChildFill=true};var gotIt=Button(IslandText("Got it","Anladım"),()=>{try{preferences.Save(preferences.Value with{PillOnboardingCompleted=true});if(islandOnboarding is not null)islandOnboarding.Visibility=Visibility.Collapsed;}catch(Exception error){MessageBox.Show(this,L10n.Text(error.Message),"Nashi");}});gotIt.Padding=new Thickness(12,5,12,5);gotIt.Margin=new Thickness(10,0,0,0);gotIt.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(gotIt,Dock.Right);onboardingContent.Children.Add(gotIt);
        var shortcutTip=Text(IslandText($"Show / hide: {preferences.Value.PillHotkey} · change in Settings",$"Göster / gizle: {preferences.Value.PillHotkey} · Ayarlardan değiştirebilirsin"),11,"Muted");onboardingContent.Children.Add(shortcutTip);islandOnboarding=new Border{Child=onboardingContent,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,10),CornerRadius=new CornerRadius(12),Background=IslandColor("#1C1C20"),BorderBrush=IslandColor("#34343A"),BorderThickness=new Thickness(1),Visibility=preferences.Value.PillModeEnabled&&!preferences.Value.PillOnboardingCompleted?Visibility.Visible:Visibility.Collapsed};Grid.SetRow(islandOnboarding,0);page.Children.Add(islandOnboarding);
        islandNowView=new Grid{Margin=new(0,0,0,9)};islandNowView.ColumnDefinitions.Add(islandNowMediaColumn=new(){Width=new GridLength(278)});islandNowView.ColumnDefinitions.Add(islandNowGapColumn=new(){Width=new GridLength(20)});islandNowView.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var media=new Grid{Visibility=Visibility.Collapsed};media.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});media.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var mediaTop=new Grid{Margin=new(0,1,0,7)};mediaTop.ColumnDefinitions.Add(new(){Width=new GridLength(64)});mediaTop.ColumnDefinitions.Add(new(){Width=new GridLength(12)});mediaTop.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        islandNowArtwork=new Image{Width=64,Height=64,Stretch=System.Windows.Media.Stretch.UniformToFill,Clip=new RectangleGeometry(new Rect(0,0,64,64),12,12),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(islandNowArtwork,0);mediaTop.Children.Add(islandNowArtwork);
        var mediaDetails=new StackPanel{VerticalAlignment=VerticalAlignment.Center};islandNowTitle=new TextBlock{TextTrimming=TextTrimming.CharacterEllipsis,FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=IslandColor("#F5F5F7"),MaxWidth=198};islandNowArtist=new TextBlock{TextTrimming=TextTrimming.CharacterEllipsis,FontSize=12,Foreground=IslandColor("#99999F"),MaxWidth=198,Margin=new(0,2,0,5)};L10n.Bind(islandNowTitle,TextBlock.TextProperty,"No active media session.");mediaDetails.Children.Add(islandNowTitle);mediaDetails.Children.Add(islandNowArtist);
        islandNowSeek=new Slider{Minimum=0,Maximum=1,Height=14,IsEnabled=false,IsMoveToPointEnabled=true,Padding=new(0),Focusable=true,ToolTip=L10n.Text("Media position")};AutomationProperties.SetName(islandNowSeek,L10n.Text("Media position"));islandNowSeek.ValueChanged+=(_,_)=>{if(islandMediaPosition is not null&&islandNowSeek.IsMouseCaptureWithin)islandMediaPosition.Text=FormatIslandMediaTime(TimeSpan.FromSeconds(islandNowSeek.Value));};islandNowSeek.PreviewMouseLeftButtonDown+=(_,_)=>islandCompactMediaSeeking=true;islandNowSeek.PreviewMouseLeftButtonUp+=async(_,_)=>{if(islandCompactMediaSeeking){islandCompactMediaSeeking=false;await SeekIslandMedia(islandNowSeek.Value);}};islandNowSeek.KeyUp+=async(_,e)=>{if(e.Key is Key.Left or Key.Right)await SeekIslandMedia(islandNowSeek.Value);};mediaDetails.Children.Add(islandNowSeek);Grid.SetColumn(mediaDetails,2);mediaTop.Children.Add(mediaDetails);Grid.SetRow(mediaTop,0);media.Children.Add(mediaTop);
        var mediaControls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,0,0,1)};Button MediaControl(string glyph,string label,string action){var button=Button(glyph,async()=>await ControlIslandMedia(action));button.FontFamily=new FontFamily("Segoe UI Symbol");button.FontSize=17;button.FontWeight=FontWeights.Normal;button.Width=38;button.Height=30;button.Padding=new(0);button.Margin=new(3,0,3,0);button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.ToolTip=L10n.Text(label);AutomationProperties.SetName(button,L10n.Text(label));return button;}mediaControls.Children.Add(MediaControl("⏮","Previous track","previous"));islandNowMediaToggle=MediaControl("⏯","Play or pause media","toggle");mediaControls.Children.Add(islandNowMediaToggle);mediaControls.Children.Add(MediaControl("⏭","Next track","next"));Grid.SetRow(mediaControls,1);media.Children.Add(mediaControls);islandNowMedia=new Border{Child=media,Background=Brushes.Transparent};Grid.SetColumn(islandNowMedia,0);islandNowView.Children.Add(islandNowMedia);
        var trayContent=new Grid();trayContent.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});trayContent.RowDefinitions.Add(new(){Height=GridLength.Auto});islandDropPrompt=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};var dropArrow=Text("↓",24);dropArrow.Foreground=IslandColor("#B9B9C0");islandDropPrompt.Children.Add(dropArrow);var dropLabel=Text("Drop file",13);dropLabel.Foreground=IslandColor("#E9E9ED");dropLabel.HorizontalAlignment=HorizontalAlignment.Center;islandDropPrompt.Children.Add(L10n.Label(dropLabel));trayContent.Children.Add(islandDropPrompt);
        islandFilePreviews=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};trayContent.Children.Add(islandFilePreviews);var lower=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom};islandFormatChips=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center,Visibility=Visibility.Collapsed};lower.Children.Add(islandFormatChips);islandTrayProgress=new ProgressBar{Height=3,Margin=new(0,7,0,0),IsIndeterminate=true,Visibility=Visibility.Collapsed,Foreground=IslandColor("#D2B36B"),Background=IslandColor("#292620")};lower.Children.Add(islandTrayProgress);Grid.SetRow(lower,1);trayContent.Children.Add(lower);
        islandDropOutline=new Rectangle{RadiusX=16,RadiusY=16,Stroke=IslandColor("#45454B"),StrokeThickness=1,StrokeDashArray=new DoubleCollection{4,3},IsHitTestVisible=false};var trayGrid=new Grid();trayGrid.Children.Add(trayContent);trayGrid.Children.Add(islandDropOutline);islandNowDropArea=new Border{CornerRadius=new(16),Background=Brushes.Transparent,Child=trayGrid,AllowDrop=true};islandNowDropArea.DragEnter+=(_,e)=>{if(SupportsIslandDrop(e.Data)){islandTrayDragActive=true;UpdateIslandDropVisual();e.Effects=DragDropEffects.Copy;e.Handled=true;}};islandNowDropArea.DragOver+=(_,e)=>{if(SupportsIslandDrop(e.Data)){islandTrayDragActive=true;UpdateIslandDropVisual();e.Effects=DragDropEffects.Copy;e.Handled=true;}};islandNowDropArea.DragLeave+=(_,_)=>{islandTrayDragActive=false;UpdateIslandDropVisual();};islandNowDropArea.Drop+=(_,e)=>{islandTrayDragActive=false;UpdateIslandDropVisual();if(!busy){Import(e.Data);ShowIslandWidgetTab("now");}e.Handled=true;};Grid.SetColumn(islandNowDropArea,2);islandNowView.Children.Add(islandNowDropArea);Grid.SetRow(islandNowView,1);page.Children.Add(islandNowView);
        islandWidgetHost=new Grid{Visibility=Visibility.Collapsed};var widgetScroll=new ScrollViewer{Content=islandWidgetHost,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new(1,0,1,9)};Grid.SetRow(widgetScroll,1);page.Children.Add(widgetScroll);
        var dock=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};foreach(var (key,glyph,label) in new[]{("now","◉","Now"),("tray","▤","Tray"),("media","♫","Media"),("notes","✎","Notes"),("todo","☑","To-do"),("tools","✦","Tools")})
        {var button=MakeIslandDockButton(key,glyph,label);islandDockButtons[key]=button;dock.Children.Add(button);}islandSearchButton=MakeIslandDockButton("search","⌕","Search");islandAction=islandSearchButton;islandSearchButton.ToolTip=L10n.Text("Search tools and modules");dock.Children.Add(islandSearchButton);islandSettingsButton=MakeIslandDockButton("settings","⚙","Settings");islandDockButtons["settings"]=islandSettingsButton;dock.Children.Add(islandSettingsButton);Grid.SetRow(dock,2);page.Children.Add(dock);
        ShowIslandWidgetTab("now");RenderIslandDashboard();
        return page;
    }
    private Button MakeIslandDockButton(string key,string glyph,string label)
    {
        var icon=new TextBlock{Text=glyph,FontSize=16,FontFamily=new FontFamily("Segoe UI Symbol"),Foreground=IslandColor("#BFC0C5"),VerticalAlignment=VerticalAlignment.Center};var caption=Text(label,11);caption.SetResourceReference(TextBlock.ForegroundProperty,"Gold");caption.Margin=new(5,0,2,0);caption.Visibility=Visibility.Collapsed;var content=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};content.Children.Add(icon);content.Children.Add(caption);
        var button=new Button{Content=content,Background=Brushes.Transparent,BorderThickness=new(0),Padding=new(9,7,9,7),Margin=new(2,0,2,0),Height=34,Cursor=Cursors.Hand,Focusable=true};L10n.Bind(button,FrameworkElement.ToolTipProperty,label);L10n.Bind(button,AutomationProperties.NameProperty,label);button.Click+=(_,_)=>{if(key=="search"){ShowIslandActions();return;}if(key=="settings"){ShowSettings();return;}ShowIslandWidgetTab(key=="tools"?"actions":key);if(islandExpanded)SetIslandExpanded(true,false);};return button;
    }
    private static Border IslandActionCard(string title,string subtitle,Action action,string glyph)
    {
        var content=new StackPanel();content.Children.Add(Text(glyph,22,"Gold"));var heading=Text(title,16);heading.Margin=new(0,10,0,3);content.Children.Add(heading);content.Children.Add(Text(subtitle,11,"Muted"));var button=Button("",action);button.Content=content;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.VerticalContentAlignment=VerticalAlignment.Top;button.Padding=new(18);button.Margin=new(5);button.Height=126;button.BorderThickness=new(1);button.SetResourceReference(Control.BackgroundProperty,"Surface");button.SetResourceReference(Control.BorderBrushProperty,"Line");return new Border{Child=button};
    }
    private void ShowIslandWidgetTab(string tab)
    {
        if(islandWidgetHost is null)return;islandCurrentWidgetTab=tab;var showNow=tab=="now";if(islandNowView is not null)islandNowView.Visibility=showNow?Visibility.Visible:Visibility.Collapsed;islandWidgetHost.Visibility=showNow?Visibility.Collapsed:Visibility.Visible;islandWidgetHost.Children.Clear();foreach(var (key,button) in islandDockButtons){var selected=key==tab;button.Background=selected?IslandColor("#28282D"):Brushes.Transparent;if(button.Content is StackPanel dockContent){if(dockContent.Children.Count>0&&dockContent.Children[0] is TextBlock glyph){if(selected)glyph.SetResourceReference(TextBlock.ForegroundProperty,"Gold");else glyph.Foreground=IslandColor("#BFC0C5");}if(dockContent.Children.Count>1&&dockContent.Children[1] is TextBlock label)label.Visibility=selected?Visibility.Visible:Visibility.Collapsed;}}if(islandSearchButton is not null)islandSearchButton.Background=Brushes.Transparent;foreach(var (key,button) in islandTabButtons){var selected=key==tab;button.Background=selected?Theme.Primary:Theme.Brush("Surface");button.Foreground=selected?Brushes.White:Theme.Brush("Ink");button.BorderBrush=selected?Theme.Primary:Theme.Brush("Line");button.BorderThickness=new(1);}if(showNow){if(tab!="mirror")_ = StopIslandMirror();RenderIslandDashboard();return;}if(tab!="media"){islandMediaSeek=null;islandMediaPosition=null;islandMediaDuration=null;islandMediaSeekUserChanging=false;}if(tab!="mirror")_ = StopIslandMirror();
        if(tab=="actions")
        {
            var tools=new UniformGrid{Columns=5};foreach(var (id,title) in FeatureCatalog.ToolNames.OrderBy(pair=>pair.Value,StringComparer.CurrentCultureIgnoreCase))
            {
                var button=new Button{Height=30,Margin=new(1),Padding=new(2),Background=Brushes.Transparent,BorderThickness=new(0),Cursor=Cursors.Hand,ToolTip=L10n.Text(title)};var choice=new ToolChoice(id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>id},title,ToolSymbol(id));
                var content=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};content.Children.Add(new TextBlock{Text=ToolSymbol(id),FontFamily=new FontFamily("Segoe UI Symbol"),FontSize=15,Foreground=Theme.Brush("Gold"),VerticalAlignment=VerticalAlignment.Center});content.Children.Add(new TextBlock{Text=L10n.Text(title),FontSize=9,Foreground=Theme.Brush("Ink"),Margin=new(5,0,0,0),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=92});button.Content=content;AutomationProperties.SetName(button,L10n.Text(title));button.Click+=async(_,_)=>{if(busy)return;if(files.Count==0||!Choices().Any(item=>item.Id==choice.Id)){await ChooseForTool(choice);return;}await EditAsync(choice);};tools.Children.Add(button);
            }
            islandWidgetHost.Children.Add(tools);return;
        }
        if(tab=="notes")
        {
            var panel=new StackPanel();panel.Children.Add(Text("Quick note",19));panel.Children.Add(Text(IslandText("Saved on this PC. Select text to format it.","Bu bilgisayara kaydedilir. Biçim vermek için metni seç."),12,"Muted",localize:false));var tools=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,3,0,0)};var bold=Button("B",()=>ToggleIslandNoteStyle(TextElement.FontWeightProperty,FontWeights.Bold,FontWeights.Normal));bold.FontWeight=FontWeights.Bold;var italic=Button("I",()=>ToggleIslandNoteStyle(TextElement.FontStyleProperty,FontStyles.Italic,FontStyles.Normal));italic.FontStyle=FontStyles.Italic;var underline=Button("U",()=>ToggleIslandNoteUnderline());underline.Content=new TextBlock{Text="U",TextDecorations=TextDecorations.Underline};tools.Children.Add(bold);tools.Children.Add(italic);tools.Children.Add(underline);panel.Children.Add(tools);islandNote=new RichTextBox{AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,MinHeight=94,MaxHeight=110,Margin=new(0,2,0,0),Padding=new(6),BorderThickness=new(.6),BorderBrush=Theme.Brush("Line"),Background=Theme.Brush("Inset"),Foreground=Theme.Brush("Ink"),FocusVisualStyle=null,Template=IslandNoteTemplate()};islandNote.GotKeyboardFocus+=(_,_)=>islandNote.BorderBrush=Theme.Brush("Gold");islandNote.LostKeyboardFocus+=(_,_)=>islandNote.BorderBrush=Theme.Brush("Line");LoadIslandNoteDocument();islandNote.TextChanged+=(_,_)=>{islandNoteText=new TextRange(islandNote.Document.ContentStart,islandNote.Document.ContentEnd).Text.TrimEnd("\r\n".ToCharArray());islandNoteRichText=SerializeIslandNote();SaveIslandWidgets();};panel.Children.Add(islandNote);islandWidgetHost.Children.Add(panel);return;
        }
        if(tab=="pocket")
        {
            var panel=new StackPanel();var heading=new DockPanel();var clear=Button("Clear",ClearIslandPocket);clear.IsEnabled=islandPocket.Count>0;DockPanel.SetDock(clear,Dock.Right);heading.Children.Add(clear);heading.Children.Add(Text("Pocket",20));panel.Children.Add(heading);panel.Children.Add(Text("Save copied text here when you choose. Nashi does not watch your clipboard.",12,"Muted"));var capture=Primary("Save copied text",CaptureIslandPocket);capture.HorizontalAlignment=HorizontalAlignment.Left;capture.Margin=new(0,12,0,8);panel.Children.Add(capture);
            foreach(var value in islandPocket.ToArray()){var row=new DockPanel{Margin=new(0,3,0,3)};var remove=Button("×",()=>{islandPocket.Remove(value);SaveIslandWidgets();ShowIslandWidgetTab("pocket");});remove.Padding=new(9,5,9,5);remove.Margin=new(2);DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);var preview=value.Length>220?value[..220]+"…":value;var restore=Button("",()=>RestoreIslandPocket(value));restore.HorizontalContentAlignment=HorizontalAlignment.Stretch;restore.Padding=new(12,9,12,9);restore.Margin=new(2);restore.Content=new TextBlock{Text=preview,TextWrapping=TextWrapping.Wrap,MaxHeight=52,TextTrimming=TextTrimming.CharacterEllipsis};restore.ToolTip=L10n.Text("Click to copy this snippet back to the clipboard.");row.Children.Add(restore);panel.Children.Add(row);}
            if(islandPocket.Count==0)panel.Children.Add(Text("Your Pocket is empty. Copy text, then save it here.",13,"Muted"));islandWidgetHost.Children.Add(panel);return;
        }
        if(tab=="todo")
        {
            var panel=new StackPanel();panel.Children.Add(Text("A little list",19));panel.Children.Add(Text(IslandText("Check an item to archive it. Star the ones you want to keep at the top.","Bir görevi arşivlemek için işaretle; üste sabitlemek için yıldızla."),12,"Muted",localize:false));var row=new DockPanel{Margin=new(0,12,0,8)};islandTodoEntry=new TextBox{MinWidth=260,ToolTip="Add a task"};islandTodoEntry.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){AddIslandTodo();e.Handled=true;}};var add=Primary("Add",AddIslandTodo);DockPanel.SetDock(add,Dock.Right);row.Children.Add(add);row.Children.Add(islandTodoEntry);panel.Children.Add(row);var list=new StackPanel();foreach(var todo in islandTodos.OrderByDescending(islandFavoriteTodos.Contains).ToArray()){var taskRow=new DockPanel{Margin=new(2,3,2,3)};var favorite=islandFavoriteTodos.Contains(todo);var star=Button(favorite?"★":"☆",()=>{if(!islandFavoriteTodos.Add(todo))islandFavoriteTodos.Remove(todo);SaveIslandWidgets();ShowIslandWidgetTab("todo");});star.Width=34;star.Height=34;star.Padding=new(0);star.Foreground=favorite?Theme.Brush("Gold"):Theme.Brush("Muted");DockPanel.SetDock(star,Dock.Right);taskRow.Children.Add(star);var check=new CheckBox{Content=todo,Margin=new(7,8,7,8),FontSize=14,VerticalAlignment=VerticalAlignment.Center};check.Checked+=(_,_)=>{islandTodos.Remove(todo);islandFavoriteTodos.Remove(todo);islandCompletedTodos.Insert(0,todo);SaveIslandWidgets();ShowIslandWidgetTab("todo");};taskRow.Children.Add(check);list.Children.Add(taskRow);}if(islandTodos.Count==0)list.Children.Add(Text("Nothing on your list yet.",13,"Muted"));panel.Children.Add(list);var archivedPanel=new StackPanel();foreach(var done in islandCompletedTodos.Take(30).ToArray()){var archivedRow=new DockPanel{Margin=new(2,2,2,2)};var remove=Button("×",()=>{islandCompletedTodos.Remove(done);SaveIslandWidgets();ShowIslandWidgetTab("todo");});DockPanel.SetDock(remove,Dock.Right);archivedRow.Children.Add(remove);var restore=Button(done,()=>{islandCompletedTodos.Remove(done);islandTodos.Add(done);SaveIslandWidgets();ShowIslandWidgetTab("todo");});restore.HorizontalContentAlignment=HorizontalAlignment.Left;restore.Margin=new(0);restore.Padding=new(8,5,8,5);archivedRow.Children.Add(restore);archivedPanel.Children.Add(archivedRow);}if(islandCompletedTodos.Count==0)archivedPanel.Children.Add(Text(IslandText("Completed tasks will appear here.","Tamamlanan görevler burada görünür."),12,"Muted",localize:false));panel.Children.Add(new Expander{Header=$"{IslandText("Completed","Tamamlandı")} · {islandCompletedTodos.Count}",Content=archivedPanel,Margin=new(2,12,2,0)});islandWidgetHost.Children.Add(panel);return;
        }
        if(tab=="timer")
        {
            var panel=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};panel.Children.Add(Text("Focus timer",16));islandTimerLabel=Text(FormatIslandTimer(),40);islandTimerLabel.HorizontalAlignment=HorizontalAlignment.Center;islandTimerLabel.Margin=new(0,5,0,6);panel.Children.Add(islandTimerLabel);var presets=new UniformGrid{Columns=4,MaxWidth=460};foreach(var minutes in new[]{5,15,25,45}){var preset=Button(L10n.Language=="tr"?$"{minutes} dk":$"{minutes} min",()=>SetIslandTimer(minutes));preset.Height=28;preset.Padding=new(4,2,4,2);presets.Children.Add(preset);}panel.Children.Add(presets);var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,4,0,0)};var toggle=Primary(islandTimerRunning?"Pause":"Start",ToggleIslandTimer);toggle.Height=32;toggle.Padding=new(10,3,10,3);controls.Children.Add(toggle);var reset=Button("Reset",()=>SetIslandTimer(25));reset.Height=32;reset.Padding=new(10,3,10,3);controls.Children.Add(reset);panel.Children.Add(controls);islandWidgetHost.Children.Add(panel);return;
        }
        if(tab=="tray")
        {
            var panel=new StackPanel();panel.Children.Add(Text("File tray",19));panel.Children.Add(Text("Park files here to keep them close. Stored locally.",12,"Muted"));var drop=new Border{CornerRadius=new(14),BorderThickness=new(1),BorderBrush=Theme.Brush("Line"),Background=Theme.Brush("Inset"),Padding=new(15),Margin=new(0,12,0,10),AllowDrop=true,Child=Text("Drop files here  ·  or add from Nashi",13,"Muted")};drop.DragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};drop.Drop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] paths)AddIslandTray(paths);e.Handled=true;};panel.Children.Add(drop);foreach(var path in islandTray.ToArray()){var row=new DockPanel{Margin=new(2,4,2,4)};var remove=Button("×",()=>{islandTray.Remove(path);SaveIslandWidgets();ShowIslandWidgetTab("tray");});remove.Padding=new(9,5,9,5);remove.Margin=new(0);DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);var name=Button(Path.GetFileName(path),()=>{if(File.Exists(path)||Directory.Exists(path))Reveal(path);});name.HorizontalContentAlignment=HorizontalAlignment.Left;name.Padding=new(9,6,9,6);name.Margin=new(0);name.ToolTip=L10n.Text("Drag this file out to another app.");name.PreviewMouseLeftButtonDown+=(_,e)=>name.Tag=e.GetPosition(name);name.PreviewMouseMove+=(_,e)=>{if(e.LeftButton!=MouseButtonState.Pressed||name.Tag is not Point start)return;var point=e.GetPosition(name);if(Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;if(File.Exists(path)||Directory.Exists(path))System.Windows.DragDrop.DoDragDrop(name,new DataObject(DataFormats.FileDrop,new[]{path}),DragDropEffects.Copy);};row.Children.Add(name);panel.Children.Add(row);}if(islandTray.Count==0)panel.Children.Add(Text("Your tray is empty.",13,"Muted"));islandWidgetHost.Children.Add(panel);return;
        }
        if(tab=="media")
        {
            var panel=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MaxWidth=440};var nowPlaying=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,0,0,7)};var artworkGrid=new Grid{Width=64,Height=64};artworkGrid.Children.Add(new TextBlock{Text="♫",FontSize=26,Foreground=Theme.Brush("Muted"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});islandMediaArtwork=new Image{Source=islandCurrentArtwork,Stretch=System.Windows.Media.Stretch.UniformToFill,Clip=new RectangleGeometry(new Rect(0,0,64,64),12,12)};artworkGrid.Children.Add(islandMediaArtwork);nowPlaying.Children.Add(artworkGrid);var mediaDetails=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(12,0,0,0),Width=310};islandMediaTitle=Text("No active media session.",14);islandMediaTitle.FontWeight=FontWeights.SemiBold;islandMediaTitle.TextTrimming=TextTrimming.CharacterEllipsis;islandMediaArtistLabel=Text("",12,"Muted");islandMediaArtistLabel.TextTrimming=TextTrimming.CharacterEllipsis;mediaDetails.Children.Add(islandMediaTitle);mediaDetails.Children.Add(islandMediaArtistLabel);islandMediaEmpty=Text(IslandText("Start a track in a media app to see it here.","Görmek için bir medya uygulamasında parça başlat."),12,"Muted");islandMediaEmpty.Margin=new(0,5,0,0);mediaDetails.Children.Add(islandMediaEmpty);nowPlaying.Children.Add(mediaDetails);panel.Children.Add(nowPlaying);
            islandMediaSeek=new Slider{Minimum=0,Maximum=1,IsEnabled=false,IsMoveToPointEnabled=true,Width=376,Height=14,Margin=new(0,0,0,0)};islandMediaSeek.ValueChanged+=(_,_)=>{if(islandMediaPosition is not null)islandMediaPosition.Text=FormatIslandMediaTime(TimeSpan.FromSeconds(islandMediaSeek.Value));};islandMediaSeek.PreviewMouseLeftButtonDown+=(_,_)=>islandMediaSeekUserChanging=true;islandMediaSeek.PreviewMouseLeftButtonUp+=async(_,_)=>{if(islandMediaSeekUserChanging){islandMediaSeekUserChanging=false;await SeekIslandMedia(islandMediaSeek.Value);}};islandMediaSeek.KeyUp+=async(_,e)=>{if(e.Key is Key.Left or Key.Right)await SeekIslandMedia(islandMediaSeek.Value);};panel.Children.Add(islandMediaSeek);
            var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,3,0,0)};Button MediaControl(string glyph,string label,string action){var button=Button(glyph,async()=>await ControlIslandMedia(action));button.FontFamily=new FontFamily("Segoe UI Symbol");button.FontSize=17;button.Width=38;button.Height=32;button.Padding=new(0);button.Margin=new(3,0,3,0);button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.ToolTip=L10n.Text(label);button.IsEnabled=!string.IsNullOrWhiteSpace(islandCurrentMedia);AutomationProperties.SetName(button,L10n.Text(label));return button;}islandMediaPrevious=MediaControl("⏮","Previous track","previous");controls.Children.Add(islandMediaPrevious);islandMediaPlaybackToggle=MediaControl(islandMediaPlaying?"⏸":"⏯","Play or pause media","toggle");controls.Children.Add(islandMediaPlaybackToggle);islandMediaNext=MediaControl("⏭","Next track","next");controls.Children.Add(islandMediaNext);panel.Children.Add(controls);islandWidgetHost.Children.Add(panel);_ = RefreshIslandMedia();return;
        }
        if(tab=="prompter")
        {
            var panel=new StackPanel();var heading=new DockPanel();heading.Children.Add(Text("Teleprompter",19));panel.Children.Add(heading);panel.Children.Add(Text("Your script stays on this PC. Start the scroll, then collapse the island if you need the screen.",12,"Muted"));
            islandPrompterScript=new TextBox{Text=islandPrompterText,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=42,MaxHeight=54,Margin=new(0,3,0,3),ToolTip=L10n.Text("Write or paste your script")};islandPrompterScript.TextChanged+=(_,_)=>{islandPrompterText=islandPrompterScript.Text;if(islandPrompterPreview is not null)islandPrompterPreview.Text=string.IsNullOrWhiteSpace(islandPrompterText)?L10n.Text("Your script preview will appear here."):islandPrompterText;if(islandPrompterToggle is not null)islandPrompterToggle.IsEnabled=!string.IsNullOrWhiteSpace(islandPrompterText);SaveIslandWidgets();};panel.Children.Add(islandPrompterScript);
            islandPrompterSpeedLabel=Text(IslandText("Scroll speed","Kaydırma hızı")+" · "+Math.Round(islandPrompterSpeed)+" px/s",11,"Muted",localize:false);panel.Children.Add(islandPrompterSpeedLabel);var speed=new Slider{Minimum=12,Maximum=100,Value=islandPrompterSpeed,IsMoveToPointEnabled=true,Margin=new(0,0,0,5),Width=360,MaxWidth=440,HorizontalAlignment=HorizontalAlignment.Left};speed.ValueChanged+=(_,_)=>{islandPrompterSpeed=speed.Value;if(islandPrompterSpeedLabel is not null)islandPrompterSpeedLabel.Text=IslandText("Scroll speed","Kaydırma hızı")+" · "+Math.Round(islandPrompterSpeed)+" px/s";SaveIslandWidgets();};panel.Children.Add(speed);
            var controls=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,3,0,8)};islandPrompterToggle=Primary(islandPrompterRunning?"Pause scrolling":"Start scrolling",ToggleIslandPrompter);islandPrompterToggle.IsEnabled=!string.IsNullOrWhiteSpace(islandPrompterText);controls.Children.Add(islandPrompterToggle);var rewind=Button("Restart",()=>{islandPrompterOffset=0;islandPrompterScroll?.ScrollToVerticalOffset(0);if(islandPrompterRunning)SetIslandPrompterRunning(false);});controls.Children.Add(rewind);panel.Children.Add(controls);
            islandPrompterPreview=Text(string.IsNullOrWhiteSpace(islandPrompterText)?"Your script preview will appear here.":islandPrompterText,18);islandPrompterPreview.TextWrapping=TextWrapping.Wrap;islandPrompterPreview.LineHeight=24;islandPrompterScroll=new ScrollViewer{Content=islandPrompterPreview,Height=68,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};islandPrompterScroll.ScrollChanged+=(_,_)=>islandPrompterOffset=islandPrompterScroll.VerticalOffset;panel.Children.Add(islandPrompterScroll);islandWidgetHost.Children.Add(panel);islandPrompterScroll.Dispatcher.BeginInvoke(()=>islandPrompterScroll.ScrollToVerticalOffset(islandPrompterOffset));return;
        }
        if(tab=="mirror")
        {
            var panel=new StackPanel{HorizontalAlignment=HorizontalAlignment.Stretch};var heading=new DockPanel();islandMirrorToggle=Primary(IslandText("Start camera","Kamerayı başlat"),async()=>{if(islandMirrorCapture is null)await StartIslandMirror();else await StopIslandMirror();});DockPanel.SetDock(islandMirrorToggle,Dock.Right);heading.Children.Add(islandMirrorToggle);heading.Children.Add(Text(IslandText("Mirror","Ayna"),19));panel.Children.Add(heading);islandMirrorStatus=Text(IslandText("Camera is off. Start it when you want a live preview.","Kamera kapalı. Canlı görüntü için başlat."),12,"Muted");islandMirrorStatus.Margin=new(0,4,0,12);panel.Children.Add(islandMirrorStatus);islandMirrorImage=new Image{Height=112,MaxWidth=560,Stretch=System.Windows.Media.Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center};var frame=new Border{MinHeight=108,CornerRadius=new(20),Background=Theme.Brush("Inset"),BorderBrush=Theme.Brush("Line"),BorderThickness=new(1),Padding=new(8),Child=islandMirrorImage};panel.Children.Add(frame);islandWidgetHost.Children.Add(panel);if(islandMirrorCapture is not null){islandMirrorToggle.Content=IslandText("Stop camera","Kamerayı durdur");islandMirrorStatus.Text=IslandText("Camera preview is active on this PC.","Kamera önizlemesi bu bilgisayarda açık.");StartIslandMirrorFrames();}return;
        }
        if(tab=="calendar")
        {
            var panel=new StackPanel();var heading=new DockPanel();var import=Button(islandCalendarPath is null?"Import .ics":"Change calendar",ChooseIslandCalendar);DockPanel.SetDock(import,Dock.Right);heading.Children.Add(import);var subscribe=Button(IslandText("Subscribe","Abone ol"),OpenCalendarSubscription);subscribe.ToolTip=IslandText("Sync a private iCalendar link","Özel iCalendar bağlantısını eşitle");DockPanel.SetDock(subscribe,Dock.Right);heading.Children.Add(subscribe);heading.Children.Add(Text("Coming up",19));panel.Children.Add(heading);panel.Children.Add(Text(IslandText("Calendar data stays on this PC.","Takvim verileri bu bilgisayarda kalır."),12,"Muted"));
            var upcoming=ReadIslandCalendarEvents();if(upcoming.Count==0)panel.Children.Add(Text(islandCalendarPath is null?"Import an .ics calendar to see upcoming events here.":"No upcoming events were found.",13,"Muted"));
            foreach(var item in upcoming){var row=new DockPanel{Margin=new(2,7,2,7)};if(item.Link is not null){var join=Button("Join",()=>OpenUrl(item.Link));join.Padding=new(10,5,10,5);DockPanel.SetDock(join,Dock.Right);row.Children.Add(join);}var details=new StackPanel();details.Children.Add(Text(item.Title,14));details.Children.Add(Text(item.Start.ToLocalTime().ToString("ddd, d MMM · HH:mm",System.Globalization.CultureInfo.CurrentCulture),11,"Muted"));row.Children.Add(details);panel.Children.Add(row);}
            islandWidgetHost.Children.Add(panel);return;
        }
    }
    private async Task StartIslandMirror()
    {
        if(islandMirrorStatus is null)return;islandMirrorStatus.Text=IslandText("Connecting to a camera…","Kameraya bağlanılıyor…");if(islandMirrorToggle is not null)islandMirrorToggle.IsEnabled=false;
        try
        {
            var devices=await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);if(devices.Count==0){islandMirrorStatus.Text=IslandText("No camera was found on this PC.","Bu bilgisayarda kamera bulunamadı.");return;}
            var capture=new MediaCapture();try{await capture.InitializeAsync(new MediaCaptureInitializationSettings{VideoDeviceId=devices[0].Id,StreamingCaptureMode=StreamingCaptureMode.Video,MemoryPreference=MediaCaptureMemoryPreference.Cpu});await capture.StartPreviewAsync();islandMirrorCapture=capture;capture=null;}finally{capture?.Dispose();}islandMirrorStatus.Text=IslandText($"Previewing {devices[0].Name}. This preview stays on your PC.",$"{devices[0].Name} görüntüleniyor. Görüntü bu bilgisayarda kalır.");if(islandMirrorToggle is not null)islandMirrorToggle.Content=IslandText("Stop camera","Kamerayı durdur");StartIslandMirrorFrames();
        }
        catch(Exception error){islandMirrorStatus.Text=error.HResult==-2147024891?IslandText("Windows blocked camera access. Allow Nashi to use the camera in Windows Privacy settings.","Windows kamera erişimini engelledi. Gizlilik ayarlarından masaüstü uygulamalarının kameraya erişmesine izin ver."):IslandText("Could not start the camera. Check that it is connected and not in use by another app.","Kamera açılamadı. Bağlı olduğunu ve başka bir uygulama tarafından kullanılmadığını kontrol et.");}
        finally{if(islandMirrorToggle is not null)islandMirrorToggle.IsEnabled=true;}
    }
    private void StartIslandMirrorFrames()
    {
        islandMirrorRefresh?.Stop();islandMirrorRefresh=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(160)};islandMirrorRefresh.Tick+=async(_,_)=>await RefreshIslandMirrorFrame();islandMirrorRefresh.Start();_ = RefreshIslandMirrorFrame();
    }
    private async Task RefreshIslandMirrorFrame()
    {
        var capture=islandMirrorCapture;if(capture is null||islandMirrorImage is null||islandMirrorFrameBusy)return;islandMirrorFrameBusy=true;
        try
        {
            var props=capture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;if(props is null)return;var width=Math.Clamp((int)props.Width,1,1280);var height=Math.Clamp((int)props.Height,1,720);using var target=new VideoFrame(BitmapPixelFormat.Bgra8,width,height);using var frame=await capture.GetPreviewFrameAsync(target);var bitmap=frame.SoftwareBitmap;if(bitmap is null)return;var converted=bitmap.BitmapPixelFormat==BitmapPixelFormat.Bgra8&&bitmap.BitmapAlphaMode==BitmapAlphaMode.Premultiplied?bitmap:SoftwareBitmap.Convert(bitmap,BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied);var ownsConverted=!ReferenceEquals(converted,bitmap);var buffer=new global::Windows.Storage.Streams.Buffer((uint)(converted.PixelWidth*converted.PixelHeight*4));converted.CopyToBuffer(buffer);var reader=DataReader.FromBuffer(buffer);var pixels=new byte[buffer.Length];reader.ReadBytes(pixels);reader.Dispose();var source=new WriteableBitmap(converted.PixelWidth,converted.PixelHeight,96,96,PixelFormats.Bgra32,null);source.WritePixels(new Int32Rect(0,0,converted.PixelWidth,converted.PixelHeight),pixels,converted.PixelWidth*4,0);islandMirrorImage.Source=source;if(ownsConverted)converted.Dispose();
        }
        catch(Exception){if(islandMirrorStatus is not null)islandMirrorStatus.Text=IslandText("Camera preview paused. Try stopping and starting it again.","Kamera önizlemesi durakladı. Kamerayı kapatıp yeniden açmayı dene.");}
        finally{islandMirrorFrameBusy=false;}
    }
    private async Task StopIslandMirror()
    {
        islandMirrorRefresh?.Stop();islandMirrorRefresh=null;var capture=islandMirrorCapture;islandMirrorCapture=null;if(capture is not null){try{await capture.StopPreviewAsync();}catch(Exception){}try{capture.Dispose();}catch(Exception){}}if(islandMirrorImage is not null)islandMirrorImage.Source=null;if(islandMirrorToggle is not null)islandMirrorToggle.Content=IslandText("Start camera","Kamerayı başlat");if(islandMirrorStatus is not null)islandMirrorStatus.Text=IslandText("Camera is off. Start it when you want a live preview.","Kamera kapalı. Canlı görüntü için başlat.");
    }
    private static string IslandText(string english,string turkish)=>L10n.Language=="tr"?turkish:english;
    private void AddIslandTodo(){var value=islandTodoEntry?.Text.Trim();if(string.IsNullOrEmpty(value))return;islandTodos.Add(value);SaveIslandWidgets();ShowIslandWidgetTab("todo");}
    private void AddIslandTray(IEnumerable<string> paths){foreach(var path in paths.Where(p=>File.Exists(p)||Directory.Exists(p))){var full=Path.GetFullPath(path);if(!islandTray.Contains(full))islandTray.Add(full);}SaveIslandWidgets();UpdateIslandHint();ShowIslandWidgetTab("tray");}
    private void CaptureIslandPocket()
    {
        try{if(!Clipboard.ContainsText(TextDataFormat.UnicodeText)){L10n.SetText(status,"Copy some text before saving it to Pocket.");return;}var value=Clipboard.GetText(TextDataFormat.UnicodeText);if(string.IsNullOrWhiteSpace(value)){L10n.SetText(status,"Copy some text before saving it to Pocket.");return;}if(value.Length>16000){L10n.SetText(status,"This snippet is too long for Pocket.");return;}islandPocket.Remove(value);islandPocket.Insert(0,value);while(islandPocket.Count>12)islandPocket.RemoveAt(islandPocket.Count-1);SaveIslandWidgets();ShowIslandWidgetTab("pocket");}
        catch(Exception error)when(error is COMException or InvalidOperationException){L10n.SetText(status,"The clipboard is busy. Try saving the snippet again.");}
    }
    private void RestoreIslandPocket(string value){try{Clipboard.SetText(value,TextDataFormat.UnicodeText);L10n.SetText(status,"Snippet copied from Pocket.");}catch(Exception error)when(error is COMException or InvalidOperationException){L10n.SetText(status,"The clipboard is busy. Try again.");}}
    private void ClearIslandPocket(){islandPocket.Clear();SaveIslandWidgets();ShowIslandWidgetTab("pocket");}
    private void LoadIslandNoteDocument()
    {
        if(islandNote is null)return;
        if(!string.IsNullOrWhiteSpace(islandNoteRichText))
        {
            try{using var stream=new MemoryStream(Encoding.UTF8.GetBytes(islandNoteRichText));new TextRange(islandNote.Document.ContentStart,islandNote.Document.ContentEnd).Load(stream,DataFormats.Xaml);return;}
            catch(Exception error)when(error is IOException or ArgumentException or System.Windows.Markup.XamlParseException){islandNoteRichText=null;}
        }
        islandNote.Document.Blocks.Clear();islandNote.Document.Blocks.Add(new Paragraph(new Run(islandNoteText)));
    }
    private string SerializeIslandNote()
    {
        if(islandNote is null)return "";using var stream=new MemoryStream();new TextRange(islandNote.Document.ContentStart,islandNote.Document.ContentEnd).Save(stream,DataFormats.Xaml);return Encoding.UTF8.GetString(stream.ToArray());
    }
    private void ToggleIslandNoteStyle(DependencyProperty property,object active,object inactive)
    {
        if(islandNote is null)return;var selection=islandNote.Selection;var current=selection.GetPropertyValue(property);selection.ApplyPropertyValue(property,Equals(current,active)?inactive:active);islandNote.Focus();
    }
    private void ToggleIslandNoteUnderline()
    {
        if(islandNote is null)return;var selection=islandNote.Selection;var current=selection.GetPropertyValue(Inline.TextDecorationsProperty);var isUnderlined=current is TextDecorationCollection decorations&&decorations.Any(item=>item.Location==TextDecorationLocation.Underline);selection.ApplyPropertyValue(Inline.TextDecorationsProperty,isUnderlined?TextDecorations.Baseline:TextDecorations.Underline);islandNote.Focus();
    }
    private void LoadIslandWidgets()
    {
        try{if(!File.Exists(islandWidgetPath))return;var saved=JsonSerializer.Deserialize<IslandWidgetState>(File.ReadAllText(islandWidgetPath));if(saved is null)return;islandNoteText=saved.Note??"";islandNoteRichText=saved.NoteRichText;islandPrompterText=saved.PrompterText??"";islandPrompterSpeed=Math.Clamp(saved.PrompterSpeed,12,100);islandCalendarPath=saved.CalendarPath;islandCalendarUrl=saved.CalendarUrl;foreach(var done in (saved.CompletedTodos??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Take(100))islandCompletedTodos.Add(done);foreach(var favorite in (saved.FavoriteTodos??[]).Where(x=>!string.IsNullOrWhiteSpace(x)))islandFavoriteTodos.Add(favorite);foreach(var task in (saved.Todos??[]).Where(x=>!string.IsNullOrWhiteSpace(x)))islandTodos.Add(task);foreach(var snippet in (saved.Pocket??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Take(12))islandPocket.Add(snippet);foreach(var path in (saved.Tray??[]).Where(p=>File.Exists(p)||Directory.Exists(p)))islandTray.Add(path);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){islandNoteText="";}
    }
    private void SaveIslandWidgets()
    {
        try{var folder=Path.GetDirectoryName(islandWidgetPath)!;Directory.CreateDirectory(folder);File.WriteAllText(islandWidgetPath,JsonSerializer.Serialize(new IslandWidgetState{Note=islandNoteText,NoteRichText=islandNoteRichText,PrompterText=islandPrompterText,PrompterSpeed=islandPrompterSpeed,CalendarPath=islandCalendarPath,CalendarUrl=islandCalendarUrl,Todos=islandTodos.ToList(),CompletedTodos=islandCompletedTodos.ToList(),FavoriteTodos=islandFavoriteTodos.ToList(),Tray=islandTray.ToList(),Pocket=islandPocket.ToList()}));}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){L10n.SetText(status,"Quick widgets could not be saved.");}
    }
    private void SetIslandTimer(int minutes){islandTimer?.Stop();islandTimerRemaining=TimeSpan.FromMinutes(minutes);islandTimerRunning=false;islandTimer=null;ShowIslandWidgetTab("timer");}
    private void ToggleIslandTimer()
    {
        if(islandTimerRemaining<=TimeSpan.Zero)islandTimerRemaining=TimeSpan.FromMinutes(25);islandTimerRunning=!islandTimerRunning;
        if(islandTimerRunning){islandTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};islandTimer.Tick+=(_,_)=>{islandTimerRemaining-=TimeSpan.FromSeconds(1);if(islandTimerRemaining<=TimeSpan.Zero){islandTimerRemaining=TimeSpan.Zero;islandTimerRunning=false;islandTimer.Stop();System.Media.SystemSounds.Asterisk.Play();}if(islandTimerLabel is not null)islandTimerLabel.Text=FormatIslandTimer();UpdateIslandHint();};islandTimer.Start();}
        else islandTimer?.Stop();UpdateIslandHint();ShowIslandWidgetTab("timer");
    }
    private void ToggleIslandPrompter(){SetIslandPrompterRunning(!islandPrompterRunning);}
    private void SetIslandPrompterRunning(bool running)
    {
        islandPrompterRunning=running;
        if(running){islandPrompterTimer??=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};islandPrompterTimer.Tick-=AdvanceIslandPrompter;islandPrompterTimer.Tick+=AdvanceIslandPrompter;islandPrompterTimer.Start();}
        else islandPrompterTimer?.Stop();
        if(islandPrompterToggle is not null)L10n.Bind(islandPrompterToggle,ContentControl.ContentProperty,running?"Pause scrolling":"Start scrolling");
    }
    private void AdvanceIslandPrompter(object? sender,EventArgs e)
    {
        if(islandPrompterScroll is null)return;var next=islandPrompterScroll.VerticalOffset+islandPrompterSpeed/20;if(next>=islandPrompterScroll.ScrollableHeight){islandPrompterScroll.ScrollToVerticalOffset(islandPrompterScroll.ScrollableHeight);islandPrompterOffset=islandPrompterScroll.ScrollableHeight;SetIslandPrompterRunning(false);return;}islandPrompterScroll.ScrollToVerticalOffset(next);
    }
    private void StopIslandPrompter(){islandPrompterTimer?.Stop();islandPrompterTimer=null;islandPrompterRunning=false;}
    private async Task InitializeIslandMediaEvents()
    {
        try
        {
            islandMediaManager??=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if(!islandMediaEventsConnected)
            {
                islandMediaManager.SessionsChanged+=OnIslandMediaSessionsChanged;
                islandMediaManager.CurrentSessionChanged+=OnIslandMediaCurrentSessionChanged;
                islandMediaEventsConnected=true;
            }
            await RefreshIslandMedia();
        }
        catch(Exception){UpdateIslandHint();}
    }
    private void OnIslandMediaSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender,SessionsChangedEventArgs args)=>QueueIslandMediaRefresh();
    private void OnIslandMediaCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,CurrentSessionChangedEventArgs args)=>QueueIslandMediaRefresh();
    private void QueueIslandMediaRefresh()=>Dispatcher.BeginInvoke(new Action(()=>_ = RefreshIslandMedia()),System.Windows.Threading.DispatcherPriority.Background);
    private void OnIslandMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,MediaPropertiesChangedEventArgs args)=>QueueIslandMediaRefresh();
    private void OnIslandMediaPlaybackChanged(GlobalSystemMediaTransportControlsSession sender,PlaybackInfoChangedEventArgs args)=>QueueIslandMediaRefresh();
    private void OnIslandMediaTimelineChanged(GlobalSystemMediaTransportControlsSession sender,TimelinePropertiesChangedEventArgs args)=>QueueIslandMediaRefresh();
    private void ObserveIslandMediaSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if(ReferenceEquals(session,islandObservedMediaSession))return;
        UnobserveIslandMediaSession();islandObservedMediaSession=session;
        if(session is null)return;
        session.MediaPropertiesChanged+=OnIslandMediaPropertiesChanged;
        session.PlaybackInfoChanged+=OnIslandMediaPlaybackChanged;
        session.TimelinePropertiesChanged+=OnIslandMediaTimelineChanged;
    }
    private void UnobserveIslandMediaSession()
    {
        if(islandObservedMediaSession is not {} session)return;
        session.MediaPropertiesChanged-=OnIslandMediaPropertiesChanged;
        session.PlaybackInfoChanged-=OnIslandMediaPlaybackChanged;
        session.TimelinePropertiesChanged-=OnIslandMediaTimelineChanged;
        islandObservedMediaSession=null;
    }
    private void StopIslandMediaEvents()
    {
        UnobserveIslandMediaSession();
        if(islandMediaManager is not null&&islandMediaEventsConnected)
        {
            islandMediaManager.SessionsChanged-=OnIslandMediaSessionsChanged;
            islandMediaManager.CurrentSessionChanged-=OnIslandMediaCurrentSessionChanged;
        }
        islandMediaEventsConnected=false;
    }
    private GlobalSystemMediaTransportControlsSession? GetIslandMediaSession()
    {
        if(islandMediaManager is null)return null;
        return islandMediaManager.GetCurrentSession()??islandMediaManager.GetSessions().FirstOrDefault();
    }
    private async Task RefreshIslandMedia()
    {
        if(App.IsSmokeTest)return;
        if(islandMediaRefreshBusy)return;islandMediaRefreshBusy=true;
        try
        {
            islandMediaManager??=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();var session=GetIslandMediaSession();ObserveIslandMediaSession(session);
            if(session is null){islandCurrentMedia=null;islandMediaArtist=null;islandMediaArtworkKey=null;islandCurrentArtwork=null;islandMediaPlaying=false;if(islandMediaArtwork is not null)islandMediaArtwork.Source=null;if(islandNowArtwork is not null)islandNowArtwork.Source=null;if(islandCompactPopupArtwork is not null)islandCompactPopupArtwork.Source=null;if(islandMediaTitle is not null)islandMediaTitle.Text=L10n.Text("No active media session.");if(islandMediaArtistLabel is not null)islandMediaArtistLabel.Text="";if(islandMediaEmpty is not null)islandMediaEmpty.Visibility=Visibility.Visible;if(islandCompactMediaTitle is not null)islandCompactMediaTitle.Text=L10n.Text("No active media session.");if(islandMediaSeek is not null)islandMediaSeek.IsEnabled=false;if(islandCompactMediaSeek is not null)islandCompactMediaSeek.IsEnabled=false;if(islandMediaPrevious is not null)islandMediaPrevious.IsEnabled=false;if(islandMediaNext is not null)islandMediaNext.IsEnabled=false;if(islandMediaPlaybackToggle is not null)islandMediaPlaybackToggle.IsEnabled=false;if(islandMediaPosition is not null)islandMediaPosition.Text="00:00";if(islandMediaDuration is not null)islandMediaDuration.Text="00:00";if(islandNowSeek is not null)islandNowSeek.IsEnabled=false;RenderIslandDashboard();UpdateIslandHint();return;}
            var info=await session.TryGetMediaPropertiesAsync();islandMediaArtist=info.Artist;var artist=string.IsNullOrWhiteSpace(info.Artist)?"":$" · {info.Artist}";islandCurrentMedia=string.IsNullOrWhiteSpace(info.Title)?L10n.Text("Media is ready."):info.Title+artist;islandMediaPlaying=session.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;if(islandMediaEmpty is not null)islandMediaEmpty.Visibility=Visibility.Collapsed;foreach(var control in new[]{islandMediaPrevious,islandMediaNext,islandMediaPlaybackToggle})if(control is not null)control.IsEnabled=true;
            var artworkKey=session.SourceAppUserModelId+"\0"+info.AlbumTitle+"\0"+info.Title+"\0"+info.Artist;if(!string.Equals(artworkKey,islandMediaArtworkKey,StringComparison.Ordinal)){islandMediaArtworkKey=artworkKey;islandCurrentArtwork=info.Thumbnail is null?null:await ReadIslandMediaArtwork(info.Thumbnail);if(islandMediaArtwork is not null)islandMediaArtwork.Source=islandCurrentArtwork;if(islandCompactPopupArtwork is not null)islandCompactPopupArtwork.Source=islandCurrentArtwork;}
            if(islandMediaTitle is not null)islandMediaTitle.Text=string.IsNullOrWhiteSpace(info.Title)?L10n.Text("Media is ready."):info.Title;if(islandMediaArtistLabel is not null)islandMediaArtistLabel.Text=info.Artist;if(islandCompactMediaTitle is not null)islandCompactMediaTitle.Text=islandCurrentMedia;UpdateIslandMediaTimeline(session);var playbackGlyph=islandMediaPlaying?"⏸":"⏯";var playbackLabel=islandMediaPlaying?IslandText("Pause media","Medyayı duraklat"):IslandText("Play media","Medyayı sürdür");foreach(var control in new[]{islandNowMediaToggle,islandMediaPlaybackToggle,islandCompactMediaToggle})if(control is not null){control.Content=playbackGlyph;control.ToolTip=playbackLabel;}RenderIslandDashboard();UpdateIslandHint();
        }
        catch(Exception){islandCurrentMedia=null;islandMediaPlaying=false;if(islandMediaTitle is not null)islandMediaTitle.Text=L10n.Text("Media controls are not available.");UpdateIslandHint();}
        finally{islandMediaRefreshBusy=false;}
    }
    private static async Task<BitmapSource?> ReadIslandMediaArtwork(global::Windows.Storage.Streams.IRandomAccessStreamReference thumbnail)
    {
        try{using var stream=await thumbnail.OpenReadAsync();var decoder=await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);if((long)decoder.PixelWidth*decoder.PixelHeight>8_000_000)return null;using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied);var buffer=new global::Windows.Storage.Streams.Buffer(checked((uint)(bitmap.PixelWidth*bitmap.PixelHeight*4)));bitmap.CopyToBuffer(buffer);using var reader=global::Windows.Storage.Streams.DataReader.FromBuffer(buffer);var pixels=new byte[checked((int)buffer.Length)];reader.ReadBytes(pixels);var result=new WriteableBitmap(bitmap.PixelWidth,bitmap.PixelHeight,96,96,PixelFormats.Bgra32,null);result.WritePixels(new Int32Rect(0,0,bitmap.PixelWidth,bitmap.PixelHeight),pixels,bitmap.PixelWidth*4,0);result.Freeze();return result;}
        catch(Exception){return null;}
    }
    private void UpdateIslandMediaTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        var timeline=session.GetTimelineProperties();var mediaDuration=timeline.EndTime-timeline.StartTime;var max=Math.Max(timeline.MaxSeekTime.TotalSeconds,mediaDuration.TotalSeconds);if(!double.IsFinite(max)||max<=0){if(islandMediaSeek is not null)islandMediaSeek.IsEnabled=false;if(islandCompactMediaSeek is not null)islandCompactMediaSeek.IsEnabled=false;return;}
        if(islandMediaSeek is not null){islandMediaSeek.IsEnabled=true;islandMediaSeek.Maximum=max;if(!islandMediaSeekUserChanging)islandMediaSeek.Value=Math.Clamp(timeline.Position.TotalSeconds,0,max);if(islandMediaPosition is not null&&!islandMediaSeekUserChanging)islandMediaPosition.Text=FormatIslandMediaTime(TimeSpan.FromSeconds(islandMediaSeek.Value));if(islandMediaDuration is not null)islandMediaDuration.Text=FormatIslandMediaTime(TimeSpan.FromSeconds(max));}
        if(islandCompactMediaSeek is not null){islandCompactMediaSeek.IsEnabled=true;islandCompactMediaSeek.Maximum=max;if(!islandCompactMediaSeeking)islandCompactMediaSeek.Value=Math.Clamp(timeline.Position.TotalSeconds,0,max);if(islandCompactMediaPosition is not null&&!islandCompactMediaSeeking)islandCompactMediaPosition.Text=FormatIslandMediaTime(TimeSpan.FromSeconds(islandCompactMediaSeek.Value));if(islandCompactMediaDuration is not null)islandCompactMediaDuration.Text=FormatIslandMediaTime(TimeSpan.FromSeconds(max));}
        if(islandNowSeek is not null){islandNowSeek.IsEnabled=true;islandNowSeek.Maximum=max;if(!islandCompactMediaSeeking)islandNowSeek.Value=Math.Clamp(timeline.Position.TotalSeconds,0,max);}
    }
    private async Task ShowIslandCompactMedia()
    {
        if(islandLabelButton is null||islandCurrentMedia is null){SetIslandExpanded(true);return;}islandMediaPopup?.SetCurrentValue(Popup.IsOpenProperty,false);
        try
        {
            islandMediaManager??=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();var session=GetIslandMediaSession();if(session is null){SetIslandExpanded(true);return;}var timeline=session.GetTimelineProperties();var duration=timeline.EndTime-timeline.StartTime;var max=Math.Max(timeline.MaxSeekTime.TotalSeconds,duration.TotalSeconds);if(!double.IsFinite(max)||max<=0)max=1;
            var panel=new StackPanel{Width=332};var header=new DockPanel{Margin=new(0,0,0,10)};var artGrid=new Grid{Width=54,Height=54};artGrid.Children.Add(new TextBlock{Text="♫",FontSize=24,Foreground=Theme.Brush("Muted"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});islandCompactPopupArtwork=new Image{Source=islandCurrentArtwork,Width=54,Height=54,Stretch=System.Windows.Media.Stretch.UniformToFill,Clip=new RectangleGeometry(new Rect(0,0,54,54),12,12)};artGrid.Children.Add(islandCompactPopupArtwork);var art=new Border{Width=54,Height=54,CornerRadius=new(12),Background=Theme.Brush("Inset"),Child=artGrid,Margin=new(0,0,12,0)};DockPanel.SetDock(art,Dock.Left);header.Children.Add(art);var details=new StackPanel{VerticalAlignment=VerticalAlignment.Center};islandCompactMediaTitle=Text(islandCurrentMedia,14);islandCompactMediaTitle.MaxWidth=254;islandCompactMediaTitle.TextTrimming=TextTrimming.CharacterEllipsis;details.Children.Add(islandCompactMediaTitle);details.Children.Add(Text(IslandText("Now playing","Şimdi çalıyor"),11,"Muted"));header.Children.Add(details);panel.Children.Add(header);
            islandCompactMediaSeek=new Slider{Minimum=0,Maximum=max,Value=Math.Clamp(timeline.Position.TotalSeconds,0,max),IsMoveToPointEnabled=true,Margin=new(0,3,0,0),MinWidth=300,MaxWidth=332};islandCompactMediaSeek.ValueChanged+=(_,_)=>{if(islandCompactMediaPosition is not null)islandCompactMediaPosition.Text=FormatIslandMediaTime(TimeSpan.FromSeconds(islandCompactMediaSeek.Value));};islandCompactMediaSeek.PreviewMouseLeftButtonDown+=(_,_)=>islandCompactMediaSeeking=true;islandCompactMediaSeek.PreviewMouseLeftButtonUp+=async(_,_)=>{if(islandCompactMediaSeeking){islandCompactMediaSeeking=false;await SeekIslandMedia(islandCompactMediaSeek.Value);}};islandCompactMediaSeek.KeyUp+=async(_,e)=>{if(e.Key is Key.Left or Key.Right)await SeekIslandMedia(islandCompactMediaSeek.Value);};AutomationProperties.SetName(islandCompactMediaSeek,"Media position");panel.Children.Add(islandCompactMediaSeek);
            var times=new DockPanel{Margin=new(4,0,4,8)};islandCompactMediaPosition=Text(FormatIslandMediaTime(timeline.Position),10,"Muted");islandCompactMediaDuration=Text(FormatIslandMediaTime(TimeSpan.FromSeconds(max)),10,"Muted");DockPanel.SetDock(islandCompactMediaDuration,Dock.Right);times.Children.Add(islandCompactMediaDuration);times.Children.Add(islandCompactMediaPosition);panel.Children.Add(times);
            var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};controls.Children.Add(Button("|◀",async()=>await ControlIslandMedia("previous")));islandCompactMediaToggle=Primary(islandMediaPlaying?"Ⅱ":"▶",async()=>await ControlIslandMedia("toggle"));controls.Children.Add(islandCompactMediaToggle);controls.Children.Add(Button("▶|",async()=>await ControlIslandMedia("next")));panel.Children.Add(controls);var more=Button("Open full media controls  →",()=>{islandMediaPopup?.SetCurrentValue(Popup.IsOpenProperty,false);OpenIslandWidget("media");});more.Margin=new(0,6,0,0);panel.Children.Add(more);
            var surface=new Border{Width=354,CornerRadius=new(20),Padding=new(12),Background=new SolidColorBrush(Color.FromRgb(0,0,0)),BorderBrush=new SolidColorBrush(Color.FromRgb(62,64,70)),BorderThickness=new(1),Child=panel};var popup=new Popup{PlacementTarget=islandLabelButton,Placement=PlacementMode.Bottom,AllowsTransparency=true,StaysOpen=true,Child=surface};popup.Closed+=(_,_)=>{if(ReferenceEquals(islandMediaPopup,popup)){islandCompactMediaSeek=null;islandCompactMediaPosition=null;islandCompactMediaDuration=null;islandCompactMediaTitle=null;islandCompactPopupArtwork=null;islandCompactMediaToggle=null;islandCompactMediaSeeking=false;}};islandMediaPopup=popup;popup.IsOpen=true;
        }
        catch(Exception){SetIslandExpanded(true);}
    }
    private async Task SeekIslandMedia(double seconds)
    {
        try{if(!double.IsFinite(seconds)||seconds<0)return;islandMediaManager??=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();var session=GetIslandMediaSession();if(session is null)return;await session.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(seconds).Ticks);await RefreshIslandMedia();}catch(Exception){}
    }
    private static string FormatIslandMediaTime(TimeSpan time)=>$"{(int)Math.Max(0,time.TotalMinutes):00}:{Math.Max(0,time.Seconds):00}";
    private async Task ControlIslandMedia(string action)
    {
        try
        {
            islandMediaManager??=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();var session=GetIslandMediaSession();if(session is null){await RefreshIslandMedia();return;}
            if(action=="previous")await session.TrySkipPreviousAsync();else if(action=="next")await session.TrySkipNextAsync();else await session.TryTogglePlayPauseAsync();await RefreshIslandMedia();
        }
        catch(Exception){if(islandMediaTitle is not null)islandMediaTitle.Text=L10n.Text("Media controls are not available.");}
    }
    private void ChooseIslandCalendar()
    {
        var picker=new OpenFileDialog{Title=L10n.Text("Import calendar"),Filter="iCalendar (*.ics)|*.ics",CheckFileExists=true};if(picker.ShowDialog(this)!=true)return;islandCalendarUrl=null;islandCalendarRefresh?.Stop();islandCalendarPath=picker.FileName;SaveIslandWidgets();ShowIslandWidgetTab("calendar");
    }
    private void OpenCalendarSubscription()
    {
        var input=new TextBox{Text=islandCalendarUrl??"",MinWidth=460,ToolTip="https://… or webcal://…"};var message=Text(IslandText("Use a private .ics link from your calendar provider.","Takvim sağlayıcının özel .ics bağlantısını kullan."),12,"Muted");var panel=new StackPanel{Margin=new(20)};panel.Children.Add(Text(IslandText("Sync a calendar","Takvimi eşitle"),20));panel.Children.Add(message);input.Margin=new(0,12,0,8);panel.Children.Add(input);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=Button(IslandText("Cancel","Vazgeç"),()=>{});var dialog=new Window{Owner=this,Title=IslandText("Calendar sync · Nashi","Takvim eşitleme · Nashi"),Width=560,Height=205,MinWidth=500,MinHeight=205,MaxHeight=205,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,Content=panel};Theme.Attach(dialog);cancel.Click+=(_,_)=>dialog.DialogResult=false;var save=Primary(IslandText("Subscribe","Eşitle"),async()=>{var value=input.Text.Trim();if(value.StartsWith("webcal://",StringComparison.OrdinalIgnoreCase))value="https://"+value[9..];if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps){message.Text=IslandText("Enter a valid HTTPS or webcal calendar link.","Geçerli bir HTTPS veya webcal takvim bağlantısı gir.");return;}var previous=islandCalendarUrl;islandCalendarUrl=uri.AbsoluteUri;if(await RefreshIslandCalendarSubscription()){if(islandCalendarRefresh is null){islandCalendarRefresh=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMinutes(15)};islandCalendarRefresh.Tick+=async(_,_)=>await RefreshIslandCalendarSubscription();islandCalendarRefresh.Start();}ShowIslandWidgetTab("calendar");dialog.DialogResult=true;}else islandCalendarUrl=previous;});buttons.Children.Add(cancel);buttons.Children.Add(save);panel.Children.Add(buttons);dialog.ShowDialog();
    }
    private async Task<bool> RefreshIslandCalendarSubscription()
    {
        if(islandCalendarUrl is null)return false;
        try
        {
            using var response=await islandCalendarHttp.GetAsync(islandCalendarUrl,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength is >5_000_000){L10n.SetText(status,"Calendar file is too large to sync.");return false;}var bytes=await response.Content.ReadAsByteArrayAsync();if(bytes.Length>5_000_000){L10n.SetText(status,"Calendar file is too large to sync.");return false;}var content=Encoding.UTF8.GetString(bytes);if(!content.Contains("BEGIN:VCALENDAR",StringComparison.OrdinalIgnoreCase)){L10n.SetText(status,"The link did not return an iCalendar file.");return false;}var path=Path.Combine(Path.GetDirectoryName(islandWidgetPath)!,"calendar-subscription.ics");Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}islandCalendarPath=path;SaveIslandWidgets();if(islandCurrentWidgetTab=="calendar")ShowIslandWidgetTab("calendar");return true;
        }
        catch(Exception error)when(error is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or UriFormatException){L10n.SetText(status,"Calendar sync could not reach the link; saved events remain available.");return false;}
    }
    private IReadOnlyList<IslandEvent> ReadIslandCalendarEvents()
    {
        if(islandCalendarPath is null||!File.Exists(islandCalendarPath))return [];
        try
        {
            var unfolded=System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(islandCalendarPath),@"\r?\n[ \t]","");var events=new List<IslandEvent>();Dictionary<string,(string Value,string Parameters)>? current=null;
            foreach(var line in unfolded.Split(["\r\n","\n"],StringSplitOptions.RemoveEmptyEntries))
            {
                if(line.Equals("BEGIN:VEVENT",StringComparison.OrdinalIgnoreCase)){current=new(StringComparer.OrdinalIgnoreCase);continue;}
                if(line.Equals("END:VEVENT",StringComparison.OrdinalIgnoreCase))
                {
                    if(current is not null&&current.TryGetValue("DTSTART",out var start)&&TryParseIslandDate(start.Value,start.Parameters,out var date)&&(!current.TryGetValue("STATUS",out var state)||!state.Value.Equals("CANCELLED",StringComparison.OrdinalIgnoreCase)))
                    {
                        var title=current.TryGetValue("SUMMARY",out var summary)?UnescapeIslandCalendar(summary.Value):"Untitled event";var link=current.TryGetValue("URL",out var url)?UnescapeIslandCalendar(url.Value):null;
                        if(link is null&&current.TryGetValue("DESCRIPTION",out var description)){var match=System.Text.RegularExpressions.Regex.Match(description.Value,@"https?://[^\s\]<>\\]+",System.Text.RegularExpressions.RegexOptions.IgnoreCase);if(match.Success)link=match.Value.TrimEnd('.',',',';');}
                        events.Add(new(title,date,link));
                    }
                    current=null;continue;
                }
                if(current is null)continue;var colon=line.IndexOf(':');if(colon<=0)continue;var parts=line[..colon].Split(';');current[parts[0]]=(line[(colon+1)..],string.Join(";",parts.Skip(1)));
            }
            return events.Where(e=>e.Start>=DateTimeOffset.Now.AddMinutes(-5)).OrderBy(e=>e.Start).Take(8).ToArray();
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){return [];}
    }
    private static bool TryParseIslandDate(string value,string parameters,out DateTimeOffset result)
    {
        var formats=new[]{"yyyyMMdd'T'HHmmss'Z'","yyyyMMdd'T'HHmm'Z'","yyyyMMdd'T'HHmmss","yyyyMMdd'T'HHmm","yyyyMMdd"};if(!DateTime.TryParseExact(value,formats,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var parsed)){result=default;return false;}
        if(value.EndsWith('Z')){result=new DateTimeOffset(DateTime.SpecifyKind(parsed,DateTimeKind.Utc));return true;}
        var zone=System.Text.RegularExpressions.Regex.Match(parameters,@"TZID=([^;:]+)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);if(zone.Success)try{var tz=TimeZoneInfo.FindSystemTimeZoneById(zone.Groups[1].Value.Trim('"'));result=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed,DateTimeKind.Unspecified),tz));return true;}catch(TimeZoneNotFoundException){}catch(InvalidTimeZoneException){}
        result=new DateTimeOffset(DateTime.SpecifyKind(parsed,DateTimeKind.Local));return true;
    }
    private static string UnescapeIslandCalendar(string value)=>value.Replace("\\n","\n",StringComparison.OrdinalIgnoreCase).Replace("\\,",",").Replace("\\;",";").Replace("\\\\","\\");
    private sealed record IslandEvent(string Title,DateTimeOffset Start,string? Link);
    private string FormatIslandTimer()=>$"{(int)islandTimerRemaining.TotalMinutes:00}:{islandTimerRemaining.Seconds:00}";
    private string? ReadBatteryActivity()
    {
        if(!GetSystemPowerStatus(out var status)||status.BatteryLifePercent>100)return null;var state=status.ACLineStatus==1?L10n.Text("Charging"):L10n.Text("Battery");return $"{state} · {status.BatteryLifePercent}%";
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]private struct SystemPowerStatus{public byte ACLineStatus,BatteryFlag,BatteryLifePercent,Reserved;public int BatteryLifeTime,BatteryFullLifeTime;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]private struct NativeBluetoothDeviceSearch{public uint Size;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]public bool ReturnAuthenticated;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]public bool ReturnRemembered;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]public bool ReturnUnknown;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]public bool ReturnConnected;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]public bool IssueInquiry;public byte TimeoutMultiplier;public IntPtr Radio;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,CharSet=System.Runtime.InteropServices.CharSet.Unicode)]private struct NativeBluetoothDeviceInfo{public uint Size;public ulong Address;public uint ClassOfDevice;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]public bool Connected;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]public bool Remembered;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]public bool Authenticated;private SystemTime LastSeen,LastUsed;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr,SizeConst=248)]public string Name;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]private struct SystemTime{public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Milliseconds;}
    [System.Runtime.InteropServices.DllImport("bthprops.cpl",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]private static extern IntPtr BluetoothFindFirstDevice(ref NativeBluetoothDeviceSearch search,ref NativeBluetoothDeviceInfo deviceInfo);
    [System.Runtime.InteropServices.DllImport("bthprops.cpl",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]private static extern bool BluetoothFindNextDevice(IntPtr find,ref NativeBluetoothDeviceInfo deviceInfo);
    [System.Runtime.InteropServices.DllImport("bthprops.cpl",SetLastError=true)]private static extern bool BluetoothFindDeviceClose(IntPtr find);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    private sealed class IslandWidgetState{public string? Note{get;set;}public string? NoteRichText{get;set;}public string? PrompterText{get;set;}public double PrompterSpeed{get;set;}=34;public string? CalendarPath{get;set;}public string? CalendarUrl{get;set;}public string? MediaSourceAppId{get;set;}public List<string> Todos{get;set;}=[];public List<string> CompletedTodos{get;set;}=[];public List<string> FavoriteTodos{get;set;}=[];public List<string> Tray{get;set;}=[];public List<string> Pocket{get;set;}=[];}
    private void ShowIslandWorkspace(bool libraryView)
    {
        if(mainWorkspace is null)return;
        if(pillModeEnabled){Content=null;Content=mainWorkspace;WindowStyle=WindowStyle.SingleBorderWindow;AllowsTransparency=false;ResizeMode=ResizeMode.CanResize;Topmost=false;ShowInTaskbar=true;Width=980;Height=720;}
        if(libraryView)ShowLibrary(true);
    }
    private void SetIslandDashboardVisible(bool visible)
    {
        if(!pillModeEnabled){if(visible)Show();return;}
        if(visible){if(mainWorkspace is not null){Content=null;Content=mainWorkspace;WindowStyle=WindowStyle.SingleBorderWindow;AllowsTransparency=false;ResizeMode=ResizeMode.CanResize;Topmost=false;ShowInTaskbar=true;Width=980;Height=720;}}
        else SwitchPillMode(true);
    }
    private static ControlTemplate IslandNoteTemplate()
    {
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
        var content=new FrameworkElementFactory(typeof(ScrollViewer),"PART_ContentHost");content.SetValue(ScrollViewer.FocusableProperty,false);border.AppendChild(content);
        return new ControlTemplate(typeof(RichTextBox)){VisualTree=border};
    }    private static Button IslandButton(string glyph,string tip,Action action)
    {
        var button=new Button{Content=glyph,Width=31,Height=31,Padding=new(0),Margin=new(1),FontSize=glyph=="+"?21:14,FontWeight=FontWeights.Medium,Foreground=new SolidColorBrush(Color.FromRgb(232,233,236)),Background=new SolidColorBrush(Color.FromRgb(37,39,43)),BorderBrush=new SolidColorBrush(Color.FromRgb(58,60,65)),BorderThickness=new(1),Cursor=Cursors.Hand,ToolTip=tip};button.Click+=(_,_)=>action();return button;
    }
    private void ShowIslandActions()
    {
        if(islandAction is null)return;islandActionPopup?.SetCurrentValue(Popup.IsOpenProperty,false);var items=new StackPanel();
        var search=new TextBox{Margin=new(3,2,3,7),Padding=new(10,7,10,7),MinHeight=34,Background=Theme.Brush("Inset"),Foreground=Theme.Brush("Ink"),BorderBrush=Theme.Brush("Line"),BorderThickness=new(1),ToolTip=L10n.Text("Search tools and modules")};AutomationProperties.SetName(search,L10n.Text("Search tools and modules"));
        var quickHeading=Text("QUICK WIDGETS",9,"Muted");quickHeading.Margin=new(9,4,8,2);items.Children.Add(quickHeading);
        AddIslandActionItem(items,"Settings…",ShowSettings);
        AddIslandActionItem(items,islandTimerRunning?IslandText("Pause timer","Zamanlayıcıyı duraklat")+" · "+FormatIslandTimer():"Start 25-minute timer",()=>{if(!islandTimerRunning&&islandTimerRemaining<=TimeSpan.Zero)islandTimerRemaining=TimeSpan.FromMinutes(25);ToggleIslandTimer();OpenIslandWidget("timer");});
        AddIslandActionItem(items,"Quick note…",()=>OpenIslandWidget("notes"));
        AddIslandActionItem(items,"Open Pocket",()=>OpenIslandWidget("pocket"));
        AddIslandActionItem(items,"Open file tray",()=>OpenIslandWidget("tray"));
        AddIslandActionItem(items,"Open calendar",()=>OpenIslandWidget("calendar"));
        AddIslandActionItem(items,"Media controls",()=>OpenIslandWidget("media"));
        AddIslandActionItem(items,"Open teleprompter",()=>OpenIslandWidget("prompter"));
        AddIslandActionItem(items,"Open mirror",()=>OpenIslandWidget("mirror"));
        items.Children.Add(new Border{Height=1,Margin=new(7,6,7,6),Background=Theme.Brush("Line")});
        var catalogHeading=Text("TOOLS AND FORMATS",9,"Muted");catalogHeading.Margin=new(9,4,8,2);items.Children.Add(catalogHeading);
        foreach(var (id,title) in FeatureCatalog.ToolNames.OrderBy(pair=>pair.Value,StringComparer.CurrentCultureIgnoreCase))
            AddIslandActionItem(items,"Tool · "+title,()=>{var choice=new ToolChoice(id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>id},title,ToolSymbol(id));if(files.Count==0||((choice.Id is "splitPDF" or "organizePDF")&&!All(["pdf"]))||!Choices().Any(item=>item.Id==choice.Id)) {_ = ChooseForTool(choice);return;}_ = EditAsync(choice);});
        foreach(var format in FeatureCatalog.Conversions.Values.SelectMany(values=>values).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))
            AddIslandActionItem(items,"Convert to "+format.ToUpperInvariant(),()=>{if(files.Count==0){Choose();return;}_ = EditAsync(new ToolChoice("convert:"+format,"Convert to "+format.ToUpperInvariant(),"↗"));});
        if(!string.IsNullOrWhiteSpace(islandNoteText))AddIslandActionItem(items,"Note · "+IslandSearchExcerpt(islandNoteText),()=>OpenIslandWidget("notes"));
        foreach(var task in islandTodos)AddIslandActionItem(items,"To-do · "+task,()=>OpenIslandWidget("todo"));
        foreach(var snippet in islandPocket)AddIslandActionItem(items,"Pocket · "+IslandSearchExcerpt(snippet),()=>OpenIslandWidget("pocket"));
        foreach(var path in islandTray)AddIslandActionItem(items,"File · "+Path.GetFileName(path),()=>OpenIslandWidget("tray"));
        if(files.Count==0)
        {
            AddIslandActionItem(items,"Choose files…",Choose);
            AddIslandActionItem(items,"Download from a link…",OpenDownload);
        }
        else
        {
            var choices=Choices().ToList();
            foreach(var format in FeatureCatalog.Formats(files))choices.Insert(0,new ToolChoice("convert:"+format,"Convert to "+format.ToUpperInvariant(),"↗"));
            if(choices.Count==0)AddIslandActionItem(items,"No actions available for these files",()=>{}).IsEnabled=false;
            else foreach(var choice in choices)AddIslandActionItem(items,choice.Title,()=>_ = EditAsync(choice));
            items.Children.Add(new Border{Height=1,Margin=new(7,6,7,6),Background=Theme.Brush("Line")});AddIslandActionItem(items,"Add more files…",Choose);AddIslandActionItem(items,"Clear selection",()=>{files.Clear();Refresh();});
        }
        islandActionSearchField=search;search.TextChanged+=(_,_)=>{var query=search.Text.Trim();foreach(var item in items.Children.OfType<Button>()){var source=item.Tag as string??"";var localized=L10n.Text(source);item.Visibility=string.IsNullOrWhiteSpace(query)||source.Contains(query,StringComparison.CurrentCultureIgnoreCase)||localized.Contains(query,StringComparison.CurrentCultureIgnoreCase)?Visibility.Visible:Visibility.Collapsed;}};
        search.PreviewKeyDown+=(_,e)=>{var visible=items.Children.OfType<Button>().Where(item=>item.Visibility==Visibility.Visible&&item.IsEnabled).ToArray();if(e.Key==Key.Escape){islandActionPopup?.SetCurrentValue(Popup.IsOpenProperty,false);e.Handled=true;}else if(e.Key==Key.Enter&&visible.Length>0){visible[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));e.Handled=true;}else if((e.Key is Key.Down or Key.Up)&&visible.Length>0){var index=Array.FindIndex(visible,item=>item.IsKeyboardFocused);Keyboard.Focus(visible[Math.Clamp(index+(e.Key==Key.Down?1:-1),0,visible.Length-1)]);e.Handled=true;}};
        var content=new DockPanel();DockPanel.SetDock(search,Dock.Top);content.Children.Add(search);content.Children.Add(items);var actionScroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=Math.Max(140,Math.Min(420,SystemParameters.WorkArea.Height-160)),Background=Brushes.Transparent};var surface=new Border{Width=310,CornerRadius=new(17),Padding=new(8),Background=Brushes.Black,BorderBrush=new SolidColorBrush(Color.FromRgb(62,64,70)),BorderThickness=new(1),Child=actionScroll};
        AutomationProperties.SetAutomationId(surface,"IslandActionPopup");var popup=new Popup{PlacementTarget=islandAction,Placement=PlacementMode.Bottom,AllowsTransparency=true,StaysOpen=true,Child=surface};islandActionPopup=popup;Dispatcher.BeginInvoke(new Action(()=>{if(!ReferenceEquals(islandActionPopup,popup))return;popup.IsOpen=true;search.Focus();Keyboard.Focus(search);}),System.Windows.Threading.DispatcherPriority.Background);
    }
    private static string IslandSearchExcerpt(string value)=>value.Length>90?value[..90]+"…":value;
    private Button AddIslandActionItem(Panel host,string title,Action action)
    {
        var item=Button(title,()=>{islandCollapseTimer?.Stop();islandActionPopup?.SetCurrentValue(Popup.IsOpenProperty,false);action();});item.Tag=title;item.HorizontalContentAlignment=HorizontalAlignment.Left;item.Padding=new(12,9,12,9);item.Margin=new(2);item.Background=Brushes.Transparent;item.BorderThickness=new(0);item.Foreground=new SolidColorBrush(Color.FromRgb(245,246,248));host.Children.Add(item);return item;
    }
    private void OpenIslandWidget(string tab){ShowIslandWidgetTab(tab);if(pillModeEnabled&&(!IsVisible||islandHiddenByAutomation)){islandHiddenByAutomation=false;Show();}SetIslandExpanded(true);}
    internal static void ThemePopupMenu(ContextMenu menu)
    {
        menu.SetResourceReference(Control.BackgroundProperty,"Surface");menu.SetResourceReference(Control.ForegroundProperty,"Ink");menu.SetResourceReference(Control.BorderBrushProperty,"Line");menu.BorderThickness=new(1);menu.Padding=new(6);menu.SnapsToDevicePixels=true;
        foreach(var item in menu.Items.OfType<MenuItem>()){item.SetResourceReference(Control.BackgroundProperty,"Surface");item.SetResourceReference(Control.ForegroundProperty,"Ink");item.Padding=new(12,8,12,8);item.SnapsToDevicePixels=true;foreach(var child in item.Items.OfType<MenuItem>()){child.SetResourceReference(Control.BackgroundProperty,"Surface");child.SetResourceReference(Control.ForegroundProperty,"Ink");child.Padding=new(12,8,12,8);}}
        foreach(var separator in menu.Items.OfType<Separator>()){separator.SetResourceReference(Control.BackgroundProperty,"Line");separator.Height=1;separator.Margin=new(6,5,6,5);}
    }
    private void SetIslandExpanded(bool expanded,bool animate=true)
    {
        if(!pillModeEnabled||!ReferenceEquals(Content,islandPanel)||islandPanel is null||islandPill is null)return;
        islandActionPopup?.SetCurrentValue(Popup.IsOpenProperty,false);islandMediaPopup?.SetCurrentValue(Popup.IsOpenProperty,false);
        if(expanded)islandCollapseTimer?.Stop();
        var currentWidth=ActualWidth>0?ActualWidth:Width;var currentHeight=ActualHeight>0?ActualHeight:Height;islandExpanded=expanded;
        if(islandCompactFace is not null)islandCompactFace.Visibility=expanded?Visibility.Collapsed:Visibility.Visible;
        if(islandDashboard is not null)islandDashboard.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
        if(islandLabelButton is not null)islandLabelButton.Visibility=expanded?Visibility.Collapsed:Visibility.Visible;
        var scale=preferences.Value.PillScale;var targetWidth=expanded?Math.Min(PillGeometry.ExpandedWidth*scale,Math.Max(180,SystemParameters.PrimaryScreenWidth-24)):PillGeometry.CollapsedWidth*scale;
        var desiredHeight=expanded&&islandCurrentWidgetTab=="mirror"?320*scale:PillGeometry.ExpandedHeight*scale;
        var targetHeight=expanded?Math.Min(desiredHeight,Math.Max(160,SystemParameters.WorkArea.Height*.4)):PillGeometry.CollapsedHeight*scale;
        Width=targetWidth;Height=targetHeight;islandPanel.Width=targetWidth;islandPanel.Height=targetHeight;
        if(animate&&preferences.Value.Animations&&SystemParameters.ClientAreaAnimation)
        {
            var easing=new BackEase{EasingMode=EasingMode.EaseOut,Amplitude=.12};var duration=TimeSpan.FromMilliseconds(350);
            BeginAnimation(WidthProperty,new DoubleAnimation(currentWidth,targetWidth,duration){EasingFunction=easing});BeginAnimation(HeightProperty,new DoubleAnimation(currentHeight,targetHeight,duration){EasingFunction=easing});
            islandPill.BeginAnimation(WidthProperty,new DoubleAnimation(islandPill.ActualWidth,targetWidth,duration){EasingFunction=easing});
            if(expanded){islandPanel.Opacity=0;islandPanel.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(220)){BeginTime=TimeSpan.FromMilliseconds(90),EasingFunction=easing});}
        }
        else{islandPill.Width=targetWidth;islandPanel.Opacity=1;}
        PositionIsland();
        if(expanded){if(!IsActive)SetNoActivate(true);}
        else SetNoActivate(true);
        if(expanded&&IsLoaded&&monitorToolSetup)Dispatcher.BeginInvoke(OfferFirstRunTools);
    }
    private void StartIslandBluetoothMonitor()
    {
        islandBluetoothInitialScan=true;islandBluetoothPoll=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};islandBluetoothPoll.Tick+=async(_,_)=>await RefreshIslandBluetoothDevices();islandBluetoothPoll.Start();_ = RefreshIslandBluetoothDevices();
    }
    private async Task RefreshIslandBluetoothDevices()
    {
        if(islandBluetoothPollBusy)return;islandBluetoothPollBusy=true;
        try
        {
            var devices=await Task.Run(ReadConnectedBluetoothDevices);var current=devices.ToDictionary(device=>device.Id,device=>device.Name,StringComparer.OrdinalIgnoreCase);
            if(!islandBluetoothInitialScan){foreach(var old in islandBluetoothNames.Where(pair=>!current.ContainsKey(pair.Key)).ToArray())SetIslandBluetoothActivity(old.Value,false);foreach(var connected in current.Where(pair=>!islandBluetoothNames.ContainsKey(pair.Key)))SetIslandBluetoothActivity(connected.Value,true);}
            islandBluetoothNames.Clear();foreach(var device in current)islandBluetoothNames[device.Key]=device.Value;islandBluetoothInitialScan=false;
        }
        catch(Exception){}
        finally{islandBluetoothPollBusy=false;}
    }
    private static IReadOnlyList<(string Id,string Name)> ReadConnectedBluetoothDevices()
    {
        var results=new List<(string Id,string Name)>();IntPtr search=IntPtr.Zero;
        try
        {
            var parameters=new NativeBluetoothDeviceSearch{Size=(uint)Marshal.SizeOf<NativeBluetoothDeviceSearch>(),ReturnConnected=true};var device=new NativeBluetoothDeviceInfo{Size=(uint)Marshal.SizeOf<NativeBluetoothDeviceInfo>(),Name=string.Empty};search=BluetoothFindFirstDevice(ref parameters,ref device);if(search==IntPtr.Zero)return results;
            do{if(device.Connected){var id=device.Address.ToString("X12",System.Globalization.CultureInfo.InvariantCulture);var name=string.IsNullOrWhiteSpace(device.Name)?IslandText("Bluetooth device","Bluetooth cihazı"):device.Name;results.Add((id,name));}device=new NativeBluetoothDeviceInfo{Size=(uint)Marshal.SizeOf<NativeBluetoothDeviceInfo>(),Name=string.Empty};}while(BluetoothFindNextDevice(search,ref device));
        }
        catch(Exception error)when(error is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException){}
        finally{if(search!=IntPtr.Zero)BluetoothFindDeviceClose(search);}
        return results;
    }
    private void SetIslandBluetoothActivity(string device,bool connected)
    {
        islandBluetoothActivity=$"Bluetooth · {IslandText(connected?"Connected":"Disconnected",connected?"Bağlandı":"Bağlantı kesildi")} · {device}";islandBluetoothActivityUntil=DateTimeOffset.UtcNow.AddSeconds(9);UpdateIslandHint();
    }
    private void StopIslandBluetoothMonitor()
    {
        islandBluetoothPoll?.Stop();islandBluetoothPoll=null;
    }
    private void PositionIsland()
    {
        if(!IsLoaded||!pillModeEnabled)return;var area=DesktopGeometry.WorkArea(this);Left=area.Left+Math.Max(0,(area.Width-ActualWidth)/2);Top=Math.Max(0,area.Top);
    }
    private void UpdateIslandHint()
    {
        if(islandHint is null)return;
        var playing=islandMediaPlaying&&!string.IsNullOrWhiteSpace(islandCurrentMedia);
        if(pillEqualizer is not null)pillEqualizer.Visibility=playing&&!busy?Visibility.Visible:Visibility.Collapsed;
        islandHint.Text=!busy&&!playing&&islandTimerRunning?FormatIslandTimer():"";
        if(pillBusyIndicator is not null)
        {
            pillBusyIndicator.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
            if(pillBusyIndicator.RenderTransform is RotateTransform rotation)
                rotation.BeginAnimation(RotateTransform.AngleProperty,busy&&preferences.Value.Animations&&SystemParameters.ClientAreaAnimation?new DoubleAnimation(0,360,TimeSpan.FromSeconds(.9)){RepeatBehavior=RepeatBehavior.Forever}:null);
        }
        if(pillEqualizer is not null)
        {
            var heights=new[]{5d,11d,7d};var index=0;
            foreach(var bar in pillEqualizer.Children.OfType<Rectangle>())
            {
                bar.BeginAnimation(FrameworkElement.HeightProperty,null);bar.Height=heights[index];
                if(playing&&preferences.Value.Animations&&SystemParameters.ClientAreaAnimation)bar.BeginAnimation(FrameworkElement.HeightProperty,new DoubleAnimation(3,heights[index],TimeSpan.FromMilliseconds(320+index*90)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,BeginTime=TimeSpan.FromMilliseconds(index*80)});
                index++;
            }
        }
    }
    private void RenderIslandDashboard()
    {
        if(islandNowView is null)return;
        var hasMedia=!string.IsNullOrWhiteSpace(islandCurrentMedia);
        if(islandNowMedia is not null)islandNowMedia.Visibility=hasMedia?Visibility.Visible:Visibility.Collapsed;
        if(islandNowMediaColumn is not null)islandNowMediaColumn.Width=hasMedia?new GridLength(278):new GridLength(0);
        if(islandNowGapColumn is not null)islandNowGapColumn.Width=hasMedia?new GridLength(20):new GridLength(0);
        if(islandNowTitle is not null)islandNowTitle.Text=hasMedia?IslandMediaTitleOnly(islandCurrentMedia!):L10n.Text("No active media session.");
        if(islandNowArtist is not null)islandNowArtist.Text=hasMedia?islandMediaArtist??"":"";
        if(islandNowArtwork is not null)islandNowArtwork.Source=islandCurrentArtwork;
        if(islandNowSeek is not null)islandNowSeek.IsEnabled=hasMedia;
        if(islandFilePreviews is not null)
        {
            islandFilePreviews.Children.Clear();
            foreach(var path in files.Take(6))
            {
                var preview=new Image{Width=48,Height=48,Stretch=System.Windows.Media.Stretch.UniformToFill,Margin=new(4),ToolTip=Path.GetFileName(path)};
                FileArtwork.Bind(preview,path);islandFilePreviews.Children.Add(new Border{Width=56,Height=56,CornerRadius=new(9),Background=IslandColor("#242428"),Child=preview});
            }
            islandFilePreviews.Visibility=files.Count==0?Visibility.Collapsed:Visibility.Visible;
        }
        if(islandDropPrompt is not null)islandDropPrompt.Visibility=files.Count==0?Visibility.Visible:Visibility.Collapsed;
        if(islandFormatChips is not null)
        {
            islandFormatChips.Children.Clear();
            var formats=busy?Array.Empty<string>():FeatureCatalog.Formats(files).Take(5).ToArray();
            foreach(var format in formats)
            {
                var chip=Button(format.ToUpperInvariant(),()=>_ = EditAsync(new ToolChoice("convert:"+format,"Convert to "+format.ToUpperInvariant(),"↗")));
                chip.Padding=new(9,4,9,4);chip.Margin=new(3,0,3,0);chip.Height=27;chip.FontSize=11;chip.Background=IslandColor("#29292E");chip.BorderBrush=IslandColor("#45454B");chip.BorderThickness=new(1);chip.ToolTip=IslandText("Convert selected files to "+format.ToUpperInvariant(),"Seçili dosyaları "+format.ToUpperInvariant()+" biçimine dönüştür");
                islandFormatChips.Children.Add(chip);
            }
            islandFormatChips.Visibility=formats.Length==0?Visibility.Collapsed:Visibility.Visible;
        }
        if(islandTrayProgress is not null)islandTrayProgress.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
        UpdateIslandDropVisual();
    }
    private static SolidColorBrush IslandColor(string color)=>new((Color)ColorConverter.ConvertFromString(color));
    private static string IslandMediaTitleOnly(string value){var separator=value.IndexOf(" · ",StringComparison.Ordinal);return separator<0?value:value[..separator];}
    private void UpdateIslandDropVisual()
    {
        if(islandDropOutline is null||islandNowDropArea is null)return;
        islandDropOutline.Stroke=IslandColor(islandTrayDragActive?"#D9B96E":"#45454B");
        islandDropOutline.StrokeThickness=islandTrayDragActive?2:1;
        islandNowDropArea.Background=islandTrayDragActive?IslandColor("#241E12"):Brushes.Transparent;
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
    private static Border Card(UIElement content){var card=new Border{CornerRadius=new(18),Padding=new(20),Margin=new(0,0,0,6),Child=content,BorderThickness=new(1)};card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Line");return card;}
    public static Button Button(string title,Action action){var button=L10n.Content(new Button{Content=title});button.Click+=(_,_)=>action();return button;}
    internal static Button Primary(string title,Action action){var button=Button(title,action);button.Background=Theme.Primary;button.Foreground=Brushes.White;return button;}
    public static void OpenUrl(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true})?.Dispose();
    public static void Reveal(string path){var full=Path.GetFullPath(path);if(!File.Exists(full)&&!Directory.Exists(full)){var parent=Path.GetDirectoryName(full);if(parent is null||!Directory.Exists(parent))return;full=parent;Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=false,Arguments=Quoted(full)})?.Dispose();return;}Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=false,Arguments="/select,"+Quoted(full)})?.Dispose();}
    // One argument for explorer.exe: a trailing backslash (a drive root) must be doubled or it escapes the closing quote.
    private static string Quoted(string path)=>"\""+path+(path.EndsWith('\\')?"\\":"")+"\"";
    private void Choose(){if(busy)return;var picker=new NashiFilePickerWindow(this);if(picker.ShowDialog()==true){Add(picker.SelectedPaths);ShowLibrary(false);}}
    private async Task ChooseForTool(ToolChoice choice)
    {
        if(busy)return;var pdfOnly=choice.Id is "organizePDF" or "splitPDF";string[]? extensions=pdfOnly?["pdf"]:choice.Id=="audioToVideo"?["mp3","m4a","wav","flac"]:null;var picker=new NashiFilePickerWindow(this,fileExtensions:extensions,allowMultiple:choice.Id!="organizePDF",purpose:pdfOnly?"pdf":"generic");if(picker.ShowDialog()!=true)return;files.Clear();Add(picker.SelectedPaths);ShowLibrary(false);if(!Choices().Any(item=>item.Id==choice.Id)){L10n.SetText(status,"This tool does not support the selected files.");return;}await EditAsync(choice);
    }
    private void Add(IEnumerable<string> paths){foreach(var path in paths.Where(p=>File.Exists(p)||Directory.Exists(p))){var full=Path.GetFullPath(path);if(!files.Contains(full))files.Add(full);}Refresh();}
    internal void ShowLibrary(bool visible)
    {
        var changed=LibraryVisible!=visible;
        if(!busy)L10n.SetText(status,"Files stay on your computer.");
        selectedLibraryId=selectedLibraryId??library.Entries.FirstOrDefault()?.Id;home.Visibility=visible?Visibility.Collapsed:Visibility.Visible;libraryPage.Visibility=visible?Visibility.Visible:Visibility.Collapsed;homeScroll.IsHitTestVisible=!visible;libraryScroll.IsHitTestVisible=visible;
        homeButton.SetResourceReference(Control.BackgroundProperty,visible?"Surface":"Sage");libraryButton.SetResourceReference(Control.BackgroundProperty,visible?"Sage":"Surface");if(visible)RenderLibrary();if(changed&&IsLoaded)Theme.Entrance(visible?libraryPage:home);
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
    private void RenderLibrary()
    {
        libraryCards.Clear();libraryRows.Children.Clear();recentRows.Children.Clear();var filtered=SearchLibrary(search.Text).ToArray();
        if(filtered.Length==0)libraryRows.Children.Add(Card(Text(library.Entries.Count==0?"Nothing here yet. Paste something or make your first fresh copy.":"No matches. Try a file name or tool.",14,"Muted")));
        foreach(var entry in filtered)libraryRows.Children.Add(LibraryRow(entry));
        recentCount.Text=library.Entries.Count.ToString();foreach(var entry in SearchLibrary(recentSearch.Text).Take(3))recentRows.Children.Add(LibraryRow(entry,true));
        if(library.Entries.Count==0)recentRows.Children.Add(Card(Text("Your next fresh copy will land here.",13,"Muted")));
    }
    private Border LibraryRow(LibraryEntry entry,bool compact=false)
    {
        var row=new DockPanel();var available=entry.AvailablePaths;var buttons=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
        var reuse=Button("↖",()=>{if(busy)return;var existing=entry.AvailablePaths.ToArray();if(existing.Length==0){L10n.SetText(status,"These files have moved or were deleted.");RenderLibrary();return;}files.Clear();Add(existing);ShowLibrary(false);L10n.SetText(status,existing.Length==entry.Paths.Length?"Ready for another round.":"Available files selected. Some files have moved or were deleted.");});reuse.IsEnabled=!busy&&available.Length>0;L10n.Bind(reuse,System.Windows.FrameworkElement.ToolTipProperty,"Use in Nashi");AutomationProperties.SetName(reuse,"Use "+entry.Name+" again");buttons.Children.Add(reuse);
        var reveal=Button("▱",()=>{var existing=entry.AvailablePaths;if(existing.Length>0)Reveal(existing[0]);else{L10n.SetText(status,"This file has moved or was deleted.");RenderLibrary();}});reveal.IsEnabled=available.Length>0;L10n.Bind(reveal,System.Windows.FrameworkElement.ToolTipProperty,"Show in Explorer");AutomationProperties.SetName(reveal,"Show "+entry.Name+" in Explorer");buttons.Children.Add(reveal);
        var fileIcon=new Image{Width=29,Height=29,Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};FileArtwork.Bind(fileIcon,entry.Paths[0]);DockPanel.SetDock(fileIcon,Dock.Left);row.Children.Add(fileIcon);var text=new StackPanel{Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};var name=Text(entry.Name,12,localize:false);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.ToolTip=string.Join(Environment.NewLine,entry.Paths);text.Children.Add(name);var info=new StackPanel{Orientation=Orientation.Horizontal};info.Children.Add(Text(entry.Action,10,"Muted"));info.Children.Add(Text("  ·  "+entry.Created.ToLocalTime().ToString("g"),10,"Muted",localize:false));if(available.Length<entry.Paths.Length){info.Children.Add(Text("  ·  ",10,"Muted",localize:false));info.Children.Add(Text("Files missing",10,"Muted"));}text.Children.Add(info);row.Children.Add(text);
        var preview=Button("◉",()=>PreviewEntry(entry));preview.IsEnabled=available.Length>0;L10n.Bind(preview,System.Windows.FrameworkElement.ToolTipProperty,"Preview");AutomationProperties.SetName(preview,"Preview "+entry.Name);buttons.Children.Insert(0,preview);
        var card=Card(row);if(!compact)ConfigureLibraryRow(card,entry);card.Padding=new(14,10,14,10);if(compact){card.CornerRadius=new(12);card.BorderThickness=new(0);card.Margin=new(0,0,0,2);}return card;
    }
    private void OpenDownload()
    {
        if(webWindow?.IsVisible==true){webWindow.Activate();link.Focus();return;}
        var dialog=new Window{Owner=this,Title="Download from the web · Nashi",Width=800,Height=650,MinWidth=640,MinHeight=400,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new ScrollViewer{Content=webContent,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};Theme.Attach(dialog);webWindow=dialog;
        dialog.Closing+=(_,e)=>{if(busy){e.Cancel=true;L10n.SetText(status,"Finish or cancel the current operation first.");}};
        dialog.Closed+=(_,_)=>{inspection?.Cancel();if(dialog.Content is ScrollViewer scroll)scroll.Content=null;dialog.Content=null;webWindow=null;};dialog.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!busy){dialog.Close();e.Handled=true;}};dialog.Show();link.Focus();
    }
    private void SetBusy(bool value){busy=value;UpdateIslandHint();RenderIslandDashboard();if(islandAction is not null)islandAction.IsEnabled=!value;updateButton.IsEnabled=!value&&!checking;workArea.IsEnabled=!value;webContent.IsEnabled=!value;L10n.Bind(toolsSetupButton,ContentControl.ContentProperty,tools.Ready&&officeRuntime.Executable is not null?"Tools ready":"Finish setup");RenderLibrary();}
    internal const string ShortcutHelp="Ctrl+O  Choose files\nCtrl+V  Paste files, text, an image or link\nCtrl+Space / Enter  Open tools and conversions\nDelete  Remove selected files from Nashi\nCtrl+1 / Ctrl+2  Home / Library\nCtrl+F  Find in Library\nCtrl+Shift+D  Use a copied download link\nCtrl+U  Check for updates\n\nWheel: arrows / Tab to choose, Enter to open, Esc to close\nMedia: Space play/pause, ←/→ 1 second, Shift+←/→ 1/30s, I/O set range\nCrop: drag, Shift locks ratio, Enter saves\nEditing text: normal paste and arrow keys are preserved.";
    private void ShowShortcuts()=>MessageBox.Show(this,L10n.Text(ShortcutHelp),L10n.Text("Nashi · Features & shortcuts"));
    private sealed class FileNameConverter:System.Windows.Data.IValueConverter
    {
        public object Convert(object value,Type type,object parameter,System.Globalization.CultureInfo culture)=>Path.GetFileName((string)value);
        public object ConvertBack(object value,Type type,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
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
        UpdateIslandHint();RenderIslandDashboard();L10n.SetText(selectionPill,files.Count==0?"✦  START HERE":"✦  READY WHEN YOU ARE");
        if(files.Count==1){selection.Text=Path.GetFileName(files[0]);fileDescription.Text=Path.GetDirectoryName(files[0])!;}
        else{L10n.SetText(selection,files.Count==0?"Drop something good.":$"{files.Count} files, ready to go.");L10n.SetText(fileDescription,files.Count==0?"Images, videos, documents, and all the little things in between.":"Keep them together, or give each one a fresh start.");}
        if(chooseFilesButton is not null){L10n.Bind(chooseFilesButton,ContentControl.ContentProperty,files.Count==0?"Choose files":"Add or change files");if(files.Count==0){chooseFilesButton.Background=Theme.Primary;chooseFilesButton.Foreground=new SolidColorBrush(Color.FromRgb(43,54,28));}else{chooseFilesButton.SetResourceReference(Control.BackgroundProperty,"Surface");chooseFilesButton.SetResourceReference(Control.ForegroundProperty,"Ink");}}if(openWheelButton is not null)openWheelButton.Visibility=files.Count>0?Visibility.Visible:Visibility.Collapsed;list.Visibility=manage.Visibility=files.Count==0?Visibility.Collapsed:Visibility.Visible;actions.Children.Clear();garden.SetSelected(files.Count>0);
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
    private async Task RunAsync(Func<string,IProgress<OperationProgress>,CancellationToken,Task<IReadOnlyList<string>>> work,string label="Fresh copy")
    {
        if(busy)return;var owner=webWindow?.IsActive==true?webWindow:this;string destination;
        if(label=="Move"||preferences.Value.OutputMode=="ask"||(files.Count==0&&owner==this)){var folder=new OpenFolderDialog{Title=L10n.Text("Save new files to…"),InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};if(folder.ShowDialog(owner)!=true)return;destination=folder.FolderName;}
        else destination=owner==webWindow?preferences.Value.DownloadFolder:preferences.Value.OutputMode=="folder"&&!string.IsNullOrWhiteSpace(preferences.Value.OutputFolder)?preferences.Value.OutputFolder:Path.GetDirectoryName(files[0])!;
        SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=new ToastWindow(owner,cancellation.Cancel);toast.Show();
        try{var outputs=await work(destination,new Progress<OperationProgress>(toast.Update),cancellation.Token);toast.Complete(outputs);Record(label,outputs);L10n.SetText(status,$"Saved {outputs.Count} new {(outputs.Count==1?"file":"files")}. Originals kept.");if(preferences.Value.RevealFinished&&outputs.Count>0)Reveal(outputs[0]);}
        catch(OperationCanceledException){toast.Close();L10n.SetText(status,"Cancelled. Originals kept.");}
        catch(Exception error){toast.Close();MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Couldn’t finish"),MessageBoxButton.OK,MessageBoxImage.Information);}
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
    private async Task SetupAsync()
    {
        if(busy)return;if(tools.Ready){L10n.SetText(status,"Media tools are already installed.");return;}
        if(MessageBox.Show(this,L10n.Text("Download FFmpeg (~180 MB), yt-dlp and Deno directly from their official GitHub releases? SHA-256 hashes are verified before use. FFmpeg is GPL; the other upstream licenses and source links are listed on our Features page. No administrator password is needed."),L10n.Text("Set up media tools"),MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
        SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=new ToastWindow(this,cancellation.Cancel);toast.Show();
        try{await tools.InstallAsync(new Progress<OperationProgress>(toast.Update),cancellation.Token);toast.Close();L10n.SetText(status,"Media tools ready. You can edit, convert and download media.");}
        catch(OperationCanceledException){toast.Close();L10n.SetText(status,"Setup cancelled.");}catch(Exception error){toast.Close();MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Setup couldn’t finish"));}finally{SetBusy(false);}
    }
    internal void OfferUpdate(WindowsUpdate candidate){update=candidate;L10n.Bind(updateButton,ContentControl.ContentProperty,"Update to "+candidate.Version);if(updateBanner is not null)updateBanner.Visibility=Visibility.Visible;tray?.UpdateAvailable(candidate.Version);}
    private async Task CheckUpdatesAsync(bool user)
    {
        if(busy||checking)return;checking=true;updateButton.IsEnabled=false;if(user)L10n.SetText(status,"Checking for updates…");try
        {
            if(update is null){update=await PublicUpdates.CheckAsync(UpdateInstaller.Current);if(update is null){if(user)L10n.SetText(status,"You have the latest published Windows version.");return;}OfferUpdate(update);if(!user)return;}
            if(busy)return;
            if(MessageBox.Show(this,L10n.Text("Download, verify and install Nashi "+update.Version+"? The app will restart. No account or token is needed."),L10n.Text("Nashi update"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=new ToastWindow(this,cancellation.Cancel);toast.Show();
            try{await UpdateInstaller.StageAsync(update,new Progress<OperationProgress>(toast.Update),cancellation.Token);busy=false;Application.Current.Shutdown();}finally{toast.Close();SetBusy(false);}
        }
        catch(OperationCanceledException){L10n.SetText(status,"Update cancelled.");}catch(Exception error){if(user)MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Couldn’t check for updates"));}
        finally{checking=false;updateButton.IsEnabled=!busy;}
    }
}
