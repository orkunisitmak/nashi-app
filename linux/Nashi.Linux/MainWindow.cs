using Avalonia.Input.Platform;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nashi.Core;
using Nashi.Windows;
namespace Nashi.Linux;

public sealed class MainWindow:Window
{
    private readonly ObservableCollection<string> files=[];
    private readonly LibraryStore library;
    private readonly LinuxEngine engine=new();
    private readonly PreferencesStore preferences;
    private TextBox? librarySearch;
    private X11DragMonitor? dragMonitor;
    private WheelWindow? dragWheel;
    private long dragSession;
    private string? dragMode;
    private readonly TextBlock status=NashiTheme.Text("Files stay on your computer.",12,true);
    private readonly ContentControl page=new();
    private readonly ProgressBar progress=new(){IsVisible=false,Height=4};
    private CancellationTokenSource? operation;
    private bool libraryVisible;
    private readonly List<string> initial;
    public MainWindow(string[]? paths=null,LibraryStore? store=null,bool monitorDesktop=true)
    {
        library=store??new();preferences=new(library.DirectoryPath);NashiTheme.Language=preferences.Value.Language;initial=(paths??[]).Where(p=>!p.StartsWith("--",StringComparison.Ordinal)).ToList();
        NashiTheme.Apply(library.Appearance);FontFamily=new FontFamily("DejaVu Sans");Title="Nashi";Width=900;Height=760;MinWidth=640;MinHeight=520;
        using(var icon=AssetLoader.Open(new Uri("avares://Nashi/Assets/NashiIcon.png")))Icon=new WindowIcon(icon);
        status.TextWrapping=TextWrapping.Wrap;Build();DragDrop.SetAllowDrop(this,true);AddHandler(DragDrop.DropEvent,Drop);AddHandler(DragDrop.DragOverEvent,DragOver);KeyDown+=OnKey;
        Opened+=(_,_)=>{if(monitorDesktop&&!(paths??[]).Contains("--smoke-test")&&preferences.Value.Enabled)dragMonitor=new X11DragMonitor(drag=>Dispatcher.UIThread.Post(()=>DesktopDragChanged(drag)),()=>preferences.Value);Add(initial);if(library.RecoveryNotice is {} notice)status.Text=notice;if((paths??[]).Contains("--download"))_ = Download();if((paths??[]).Contains("--library"))ShowPage(true);};
        Closed+=(_,_)=>{dragMonitor?.Dispose();dragWheel?.Close();};
        Closing+=(_,e)=>{if(operation is not null){operation.Cancel();e.Cancel=true;status.Text="Cancelling… Close again when the operation finishes.";}};
    }
    internal void HandleLaunch(string[] args)
    {
        Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();var incoming=args.Where(a=>!a.StartsWith("--",StringComparison.Ordinal)&&!(Uri.TryCreate(a,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http")).ToArray();if(incoming.Length>0)Add(incoming);
        if(args.Contains("--download"))_ = Download();if(args.Contains("--library"))ShowPage(true);
    }
    private void Build()
    {
        Background=NashiTheme.Paper;Foreground=NashiTheme.Ink;status.Foreground=NashiTheme.Muted;
        var shell=new DockPanel{Margin=new(28)};
        var header=new Grid{ColumnDefinitions=new("*,Auto"),Margin=new(0,0,0,24)};
        var identity=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10,VerticalAlignment=VerticalAlignment.Center};
        using(var stream=AssetLoader.Open(new Uri("avares://Nashi/Assets/NashiMark.png")))identity.Children.Add(new Image{Source=new Bitmap(stream),Width=38,Height=38});
        identity.Children.Add(NashiTheme.Text("Nashi",20));header.Children.Add(identity);
        var nav=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};Grid.SetColumn(nav,1);
        nav.Children.Add(NashiTheme.Button("Set up tools",()=>_ = ShowSetup()));nav.Children.Add(NashiTheme.Button("Home",()=>ShowPage(false)));nav.Children.Add(NashiTheme.Button("Library",()=>ShowPage(true)));nav.Children.Add(NashiTheme.Button("Settings",()=>_ = Settings()));header.Children.Add(nav);
        DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
        var footer=new StackPanel{Spacing=10,Margin=new(0,16,0,0)};footer.Children.Add(progress);footer.Children.Add(status);DockPanel.SetDock(footer,Dock.Bottom);shell.Children.Add(footer);
        shell.Children.Add(new ScrollViewer{Content=page,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});Content=shell;ShowPage(libraryVisible);
    }
    private void ShowPage(bool showLibrary)
    {
        libraryVisible=showLibrary;var body=new StackPanel{Spacing=18};page.Content=body;
        if(showLibrary)
        {
            body.Children.Add(NashiTheme.Text("Library",28));
            var search=new TextBox{PlaceholderText="Find a file or tool"};librarySearch=search;body.Children.Add(search);var rows=new StackPanel{Spacing=8};body.Children.Add(rows);
            void Refresh(){rows.Children.Clear();foreach(var entry in library.Search(search.Text??""))rows.Children.Add(LibraryRow(entry));if(rows.Children.Count==0)rows.Children.Add(NashiTheme.Text("Your successful exports and clipboard imports will appear here.",14,true));}
            search.TextChanged+=(_,_)=>Refresh();Refresh();return;
        }
        body.Children.Add(NashiTheme.Text("Your files. Their next form.",30));body.Children.Add(NashiTheme.Text("Convert, fine-tune, and send them on their way.",14,true));
        var drop=new StackPanel{Spacing=16};drop.Children.Add(NashiTheme.Text(files.Count==0?"Drop something good.":$"{files.Count} file{(files.Count==1?"":"s")} ready.",24));
        drop.Children.Add(NashiTheme.Text("Images, videos, documents, and all the little things in between.",13,true));
        var choose=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};choose.Children.Add(NashiTheme.Button("Choose files",()=>_ = Choose(),true));choose.Children.Add(NashiTheme.Button("Paste",()=>_ = Paste()));
        if(files.Count>0){choose.Children.Add(NashiTheme.Button("Tools",ShowWheel));choose.Children.Add(NashiTheme.Button("Clear",()=>{files.Clear();ShowPage(false);}));}drop.Children.Add(choose);
        if(files.Count>0)
        {
            var list=new ListBox{ItemsSource=files.Select(Path.GetFileName).ToArray(),MaxHeight=140};drop.Children.Add(list);
            var formats=FeatureCatalog.Formats(files);var choices=new WrapPanel{Orientation=Orientation.Horizontal};
            foreach(var format in formats){var button=NashiTheme.Button(format.ToUpperInvariant(),()=>_ = Edit("convert",format));button.Margin=new(0,0,6,6);choices.Children.Add(button);}drop.Children.Add(choices);
            var actions=new WrapPanel();foreach(var action in FeatureCatalog.Tools(files)){var button=NashiTheme.Button(FeatureCatalog.ToolNames[action],()=>_ = Edit(action));button.Margin=new(0,0,6,6);actions.Children.Add(button);}drop.Children.Add(actions);
            var fileActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};fileActions.Children.Add(NashiTheme.Button("File actions",()=>_ = FileActions()));fileActions.Children.Add(NashiTheme.Button("Preview",()=>_ = Preview()));drop.Children.Add(fileActions);
        }
        var dropGrid=new Grid{ColumnDefinitions=new("*,180")};dropGrid.Children.Add(drop);var garden=new FileGarden{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(garden,1);dropGrid.Children.Add(garden);
        body.Children.Add(NashiTheme.Card(dropGrid));body.Children.Add(NashiTheme.Button("↓   Download a link",()=>_ = Download()));
        var recent=new StackPanel{Spacing=12,Margin=new(0,16,0,0)};recent.Children.Add(NashiTheme.Text("Freshly made",20));
        foreach(var entry in library.Entries.Take(4))recent.Children.Add(LibraryRow(entry));
        if(library.Entries.Count==0)recent.Children.Add(NashiTheme.Text("New copies will appear here. Your originals stay safe.",14,true));body.Children.Add(recent);
    }
    private Control LibraryRow(LibraryEntry entry)
    {
        var row=new StackPanel{Spacing=7};row.Children.Add(NashiTheme.Text(entry.Name,16));row.Children.Add(NashiTheme.Text(entry.Action+" · "+entry.Created.ToLocalTime().ToString("g"),12,true));
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};actions.Children.Add(NashiTheme.Button("Use again",()=>{files.Clear();Add(entry.AvailablePaths);}));actions.Children.Add(NashiTheme.Button("Show folder",()=>_ = SafeOpen(Path.GetDirectoryName(entry.Paths[0])!)));row.Children.Add(actions);return NashiTheme.Card(row);
    }
    public void Add(IEnumerable<string> paths)
    {
        foreach(var path in paths){var full=Path.GetFullPath(path);if((File.Exists(full)||Directory.Exists(full))&&!files.Contains(full))files.Add(full);}ShowPage(false);
    }
    private async Task Choose()
    {
        if(operation is not null)return;
        var selected=await StorageProvider.OpenFilePickerAsync(new(){Title="Choose files for Nashi",AllowMultiple=true});Add(selected.Select(p=>p.TryGetLocalPath()).OfType<string>());
    }
    private void Drop(object? sender,DragEventArgs e)
    {
        if(operation is not null)return;
        var items=e.DataTransfer.TryGetFiles();if(items is not null)Add(items.Select(p=>p.TryGetLocalPath()).OfType<string>());e.Handled=true;
    }
    private void DragOver(object? sender,DragEventArgs e)
    {
        if(operation is not null||!preferences.Value.Enabled)return;
        var mode=X11DragMonitor.ModeFor(e.KeyModifiers.HasFlag(KeyModifiers.Control),e.KeyModifiers.HasFlag(KeyModifiers.Shift),e.KeyModifiers.HasFlag(KeyModifiers.Alt),preferences.Value);
        if(mode is null){if(dragSession==-1)DesktopDragChanged(null);return;}var items=e.DataTransfer.TryGetFiles();var paths=items?.Select(p=>p.TryGetLocalPath()).OfType<string>().ToArray();if(paths is not {Length:>0})return;
        DesktopDragChanged(new(paths,mode,this.PointToScreen(e.GetPosition(this)),-1));e.DragEffects=DragDropEffects.Copy;e.Handled=true;
    }
    private void DesktopDragChanged(DesktopDrag? drag)
    {
        if(operation is not null||!IsVisible)return;
        if(drag is null){var old=dragWheel;_ = CloseEndedDrag(old);return;}
        if(dragWheel?.IsVisible==true&&dragSession==drag.Session&&dragMode==drag.Mode)return;
        dragWheel?.Close();dragSession=drag.Session;dragMode=drag.Mode;
        var choices=drag.Mode switch{
            "tools"=>FeatureCatalog.Tools(drag.Files).Select(id=>(Id:id,Label:FeatureCatalog.ToolNames[id])).ToArray(),
            "convert"=>FeatureCatalog.Formats(drag.Files).Select(format=>(Id:"convert:"+format,Label:format.ToUpperInvariant())).ToArray(),
            _=>new[]{(Id:"copy",Label:"Copy"),(Id:"move",Label:"Move"),(Id:"copyPath",Label:"Copy paths"),(Id:"float",Label:"Float"),(Id:"zip",Label:"Create ZIP")}
        };
        if(choices.Length==0)return;
        dragWheel=new WheelWindow(choices,id=>{
            files.Clear();Add(drag.Files);Show();Activate();
            if(id.StartsWith("convert:",StringComparison.Ordinal))_ = Edit("convert",id[8..]);
            else if(drag.Mode=="tools")_ = Edit(id);
            else if(id=="copy"||id=="move")_ = CopyFiles(id=="move");
            else if(id=="copyPath")_ = CopyPaths();
            else if(id=="float")new FloatWindow(drag.Files,Add).Show(this);
            else if(id=="zip")_ = Run((dest,p,t)=>Task.FromResult<IReadOnlyList<string>>([DocumentEngine.Zip(drag.Files,dest,p,t)]),"ZIP");
        },drag.Mode=="tools"?"Tools":drag.Mode=="convert"?"Convert":"Files",drag.Pointer);
        dragWheel.Show();
    }
    private async Task CloseEndedDrag(WheelWindow? wheel){await Task.Delay(180);if(wheel?.IsVisible==true&&!wheel.Dropped)wheel.Close();}
    private async Task CopyPaths(){if(Clipboard is not null)await Clipboard.SetTextAsync(string.Join('\n',files));}
    private async Task Paste()
    {
        if(operation is not null||Clipboard is null)return;
        try
        {
            var items=await Clipboard.TryGetFilesAsync();if(items is not null){Add(items.Select(p=>p.TryGetLocalPath()).OfType<string>());return;}
            using var bitmap=await Clipboard.TryGetBitmapAsync();
            if(bitmap is not null){var folder=Path.Combine(library.DirectoryPath,"Imports");Directory.CreateDirectory(folder);var file=SafePaths.Unique(folder,"Clipboard.png");bitmap.Save(file,new PngBitmapEncoderOptions());library.Record("Clipboard",[file]);Add([file]);return;}
            var text=await Clipboard.TryGetTextAsync();if(string.IsNullOrWhiteSpace(text))return;
            if(Uri.TryCreate(text,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http"){await Download(text);return;}
            var lines=text.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            if(lines.All(File.Exists)){Add(lines);return;}
            var imports=Path.Combine(library.DirectoryPath,"Imports");Directory.CreateDirectory(imports);var path=SafePaths.Unique(imports,"Clipboard.txt");await File.WriteAllTextAsync(path,text);library.Record("Clipboard",[path]);Add([path]);
        }
        catch(Exception error){status.Text=error.Message;}
    }
    private async Task Edit(string action,string? format=null)
    {
        if(operation is not null||files.Count==0)return;
        try
        {
            var selected=files.ToArray();
            if(action=="removeMetadata"){
                var media=selected.All(p=>FeatureCatalog.IsVideo(p)||FeatureCatalog.IsAudio(p));
                IReadOnlyDictionary<string,string> tags=selected.Length==1?(media?await MediaMetadata.Read(engine.Tools,selected[0]):await new MetadataService(engine.Tools.DirectoryPath).ReadAsync(selected[0])):new Dictionary<string,string>();
                var metadata=new MetadataEditor(this,tags,media);if(!await metadata.ShowDialog<bool>(this))return;
                var metadataOptions=media?new LinuxOptions{Media=new(){RemoveAllMetadata=metadata.RemoveAll,Metadata=metadata.Updates}}:new LinuxOptions{RemoveAllMetadata=metadata.RemoveAll,Metadata=metadata.Updates};
                await Run((dest,p,t)=>engine.RunAsync(action,selected,dest,metadataOptions,progress:p,token:t),"Metadata");return;
            }
            if(action=="organizePDF"){
                status.Text="Rendering PDF pages…";var images=await NativePdf.RenderAsync(selected[0],72,CancellationToken.None);var organizer=new PdfOrganizer(images);var organization=await organizer.ShowDialog<PdfOrganization?>(this);if(organization is null)return;
                await Run((dest,p,t)=>engine.RunAsync(action,selected,dest,new(){Pages=organization.Pages,PageRotations=organization.Rotations,UniformWidth=organization.UniformWidth},progress:p,token:t),"Organize PDF");return;
            }
            var editor=new ToolEditor(action,format,selected,engine);
            var options=await editor.ShowDialog<LinuxOptions?>(this);if(options is null)return;selected=editor.OrderedInputs;
            await Run((dest,p,t)=>engine.RunAsync(action,selected,dest,options,format,p,t),action=="convert"?"Convert to "+format:FeatureCatalog.ToolNames.GetValueOrDefault(action,action));
        }
        catch(Exception error){status.Text=error.Message;}
    }
    private async Task Run(Func<string,IProgress<OperationProgress>,CancellationToken,Task<IReadOnlyList<string>>> work,string label,bool forceAsk=false,string? preferredDestination=null,Action<IReadOnlyList<string>>? completed=null)
    {
        string? destination=preferredDestination;
        if(destination is null&&!forceAsk&&preferences.Value.OutputMode=="beside"&&files.Count>0)destination=Path.GetDirectoryName(files[0]);
        else if(destination is null&&!forceAsk&&preferences.Value.OutputMode=="folder"&&!string.IsNullOrWhiteSpace(preferences.Value.OutputFolder))destination=preferences.Value.OutputFolder;
        if(destination is null){var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title="Save new copies",AllowMultiple=false});destination=folders.FirstOrDefault()?.TryGetLocalPath();}if(destination is null)return;
        operation=new();progress.IsVisible=true;progress.IsIndeterminate=true;
        var cancellation=operation;var cancel=NashiTheme.Button("Cancel",()=>cancellation.Cancel());
        var toast=new Window{Title=label,Width=380,Height=130,Content=new StackPanel{Margin=new(20),Spacing=12,Children={NashiTheme.Text("Working on your new copies…"),cancel}}};toast.Show(this);
        var report=new Progress<OperationProgress>(p=>{status.Text=p.Label;progress.IsIndeterminate=p.Fraction is null;if(p.Fraction is {} f)progress.Value=f*100;});
        try
        {
            var outputs=await Task.Run(()=>work(destination,report,cancellation.Token));completed?.Invoke(outputs);library.Record(label,outputs);status.Text=$"Saved {outputs.Count} new cop{(outputs.Count==1?"y":"ies")}.";ShowPage(libraryVisible);if(preferences.Value.RevealFinished)await OpenPath(destination);
        }
        catch(IncompleteMoveException error){completed?.Invoke(error.Outputs.Concat(error.RetainedOriginals).ToArray());library.Record(label+" (copies retained)",error.Outputs);ShowPage(libraryVisible);status.Text=$"Copied {error.Outputs.Count} item(s); {error.RetainedOriginals.Count} original(s) could not be removed. Copies: {destination}. Retained originals: "+string.Join(", ",error.RetainedOriginals);}
        catch(OperationCanceledException){status.Text="Cancelled. Original files are safe.";}
        catch(Exception error){status.Text=error.Message;}
        finally{operation.Dispose();operation=null;progress.IsVisible=false;toast.Close();}
    }
    private void ShowWheel()
    {
        if(operation is not null||files.Count==0)return;
        var choices=FeatureCatalog.Tools(files).Select(a=>(Id:a,Label:FeatureCatalog.ToolNames[a])).ToArray();
        if(choices.Length==0)return;new WheelWindow(choices,a=>_ = Edit(a)).Show(this);
    }
    private void OnKey(object? sender,KeyEventArgs e)
    {
        var ctrl=e.KeyModifiers.HasFlag(KeyModifiers.Control);if(!ctrl)return;
        if(e.Key==Key.O){_ = Choose();e.Handled=true;}
        else if(e.Key==Key.V&&FocusManager?.GetFocusedElement() is not TextBox){_ = Paste();e.Handled=true;}
        else if(e.Key==Key.F){ShowPage(true);librarySearch?.Focus();e.Handled=true;}
        else if(e.Key==Key.U){status.Text="Linux updates will be offered after a verified Linux release channel is published.";e.Handled=true;}
        else if(e.Key==Key.Space){ShowWheel();e.Handled=true;}
        else if(e.Key==Key.D&&e.KeyModifiers.HasFlag(KeyModifiers.Shift)){_ = Download();e.Handled=true;}
        else if(e.Key==Key.D1){ShowPage(false);e.Handled=true;}else if(e.Key==Key.D2){ShowPage(true);e.Handled=true;}
    }
    private async Task Settings()
    {
        var panel=new StackPanel{Margin=new(24),Spacing=12};panel.Children.Add(NashiTheme.Text("Settings",24));
        panel.Children.Add(NashiTheme.Text("Appearance"));var appearance=new ComboBox{ItemsSource=new[]{"system","light","dark"},SelectedItem=library.Appearance};panel.Children.Add(appearance);
        panel.Children.Add(NashiTheme.Text("Language"));var language=new ComboBox{ItemsSource=new[]{"en","tr"},SelectedItem=preferences.Value.Language};panel.Children.Add(language);
        panel.Children.Add(NashiTheme.Text("Save new copies"));var outputMode=new ComboBox{ItemsSource=new[]{"beside","ask","folder"},SelectedItem=preferences.Value.OutputMode};panel.Children.Add(outputMode);
        var outputFolder=new TextBox{PlaceholderText="Output folder",Text=preferences.Value.OutputFolder};panel.Children.Add(outputFolder);
        panel.Children.Add(NashiTheme.Button("Choose output folder",async()=>{var selected=await StorageProvider.OpenFolderPickerAsync(new(){AllowMultiple=false});if(selected.FirstOrDefault()?.TryGetLocalPath() is {} path)outputFolder.Text=path;}));
        panel.Children.Add(NashiTheme.Text("Download folder"));var downloads=new TextBox{Text=preferences.Value.DownloadFolder};panel.Children.Add(downloads);
        var enabled=new CheckBox{Content="Enable drag shortcuts",IsChecked=preferences.Value.Enabled};panel.Children.Add(enabled);
        var chords=new[]{"Ctrl","Shift","Alt","Ctrl+Shift","Ctrl+Alt","Shift+Alt"};
        panel.Children.Add(NashiTheme.Text("Tools drag shortcut"));var toolsChord=new ComboBox{ItemsSource=chords,SelectedItem=preferences.Value.ToolsModifier};panel.Children.Add(toolsChord);
        panel.Children.Add(NashiTheme.Text("Conversion drag shortcut"));var convertChord=new ComboBox{ItemsSource=chords,SelectedItem=preferences.Value.ConvertModifier};panel.Children.Add(convertChord);
        panel.Children.Add(NashiTheme.Text("File actions drag shortcut"));var actionsChord=new ComboBox{ItemsSource=chords,SelectedItem=preferences.Value.ActionsModifier};panel.Children.Add(actionsChord);
        var reveal=new CheckBox{Content="Show finished files in file manager",IsChecked=preferences.Value.RevealFinished};panel.Children.Add(reveal);
        var dialog=new Window{Title="Nashi · Settings",Width=540,Height=620,Background=NashiTheme.Paper,Content=new ScrollViewer{Content=panel}};
        var message=NashiTheme.Text("",12);panel.Children.Add(message);
        panel.Children.Add(NashiTheme.Button("Save",()=>{
            try{
                var value=appearance.SelectedItem as string??"system";preferences.Save(preferences.Value with{Enabled=enabled.IsChecked==true,ToolsModifier=toolsChord.SelectedItem as string??"Ctrl",ConvertModifier=convertChord.SelectedItem as string??"Shift",ActionsModifier=actionsChord.SelectedItem as string??"Alt",Language=language.SelectedItem as string??"en",OutputMode=outputMode.SelectedItem as string??"ask",OutputFolder=outputFolder.Text??"",DownloadFolder=downloads.Text??"",RevealFinished=reveal.IsChecked==true});
                if(preferences.Value.Enabled&&dragMonitor is null)dragMonitor=new X11DragMonitor(drag=>Dispatcher.UIThread.Post(()=>DesktopDragChanged(drag)),()=>preferences.Value);
                library.SetAppearance(value);NashiTheme.Language=preferences.Value.Language;NashiTheme.Apply(value);Build();dialog.Close();
            }catch(Exception error){message.Text=error.Message;}
        },true));await dialog.ShowDialog(this);
    }
    private async Task ShowSetup()
    {
        var panel=new StackPanel{Margin=new(24),Spacing=12};panel.Children.Add(NashiTheme.Text("Tools for Linux",22));
        foreach(var tool in new[]{"ffmpeg","ffprobe","yt-dlp","deno","libreoffice","pdftoppm","tesseract","exiftool"})panel.Children.Add(NashiTheme.Text(tool+": "+(SystemTools.Find(tool)??"Install with your distribution's package manager")));
        panel.Children.Add(NashiTheme.Text("Image, archive, subtitle and PDF organizing tools work without these optional programs.",13,true));
        panel.Children.Add(NashiTheme.Text(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")=="wayland"?"Wayland: hold Ctrl / Shift / Alt while dragging onto Nashi. Global drag wheels are limited to X11 sources; native Wayland apps need desktop-specific integration.":"Drag shortcuts: Ctrl tools, Shift conversion, Alt file actions. Keep Nashi running.",13,true));
        var dialog=new Window{Title="Nashi · Set up tools",Width=640,Height=510,Content=panel};panel.Children.Add(NashiTheme.Button("Close",dialog.Close));await dialog.ShowDialog(this);
    }
    private async Task Download(string? initialLink=null)
    {
        var dialog=new Window{Title="Nashi · Download a link",Width=560,Height=310};var panel=new StackPanel{Margin=new(24),Spacing=16};dialog.Content=panel;
        panel.Children.Add(NashiTheme.Text("Download a link",24));var link=new TextBox{PlaceholderText="Public video, image or audio link",Text=initialLink};panel.Children.Add(link);
        var kind=new ComboBox{ItemsSource=new[]{"video","audio","thumbnail","photos","image"},SelectedIndex=0};panel.Children.Add(kind);
        string? selectedLink=null;string selectedKind="video";panel.Children.Add(NashiTheme.Button("Continue",()=>{selectedLink=link.Text;selectedKind=(string)kind.SelectedItem!;dialog.Close();},true));await dialog.ShowDialog(this);
        if(string.IsNullOrWhiteSpace(selectedLink))return;var value=selectedLink;await Run((dest,p,t)=>new WebDownload(engine.Tools).DownloadAsync(value,selectedKind,dest,p,t),"Download",preferredDestination:preferences.Value.DownloadFolder);
    }
    internal static async Task OpenPath(string path)
    {
        var executable=SystemTools.Find("xdg-open")??throw new FileNotFoundException("Install xdg-utils to open files and folders.");await ProcessRunner.RunAsync(executable,[Path.GetFullPath(path)]);
    }
    private async Task SafeOpen(string path){try{await OpenPath(path);}catch(Exception error){status.Text=error.Message;}}
    private async Task Preview()
    {
        try{if(files.Count==0)return;await OpenPath(files[0]);}catch(Exception error){status.Text=error.Message;}
    }
    private async Task FileActions()
    {
        if(operation is not null)return;
        var dialog=new Window{Title="Nashi · File actions",Width=420,Height=330};var panel=new StackPanel{Margin=new(24),Spacing=10};dialog.Content=panel;
        panel.Children.Add(NashiTheme.Button("Copy paths",async()=>{if(Clipboard is not null)await Clipboard.SetTextAsync(string.Join('\n',files));dialog.Close();}));
        panel.Children.Add(NashiTheme.Button("Copy files to folder",()=>{dialog.Close();_ = CopyFiles(false);}));
        panel.Children.Add(NashiTheme.Button("Move files to folder",()=>{dialog.Close();_ = CopyFiles(true);}));
        panel.Children.Add(NashiTheme.Button("Create ZIP",()=>{dialog.Close();_ = Run((dest,p,t)=>Task.FromResult<IReadOnlyList<string>>([DocumentEngine.Zip(files.ToArray(),dest,p,t)]),"ZIP");}));
        panel.Children.Add(NashiTheme.Button("Float files",()=>{new FloatWindow(files.ToArray(),Add).Show(this);dialog.Close();}));await dialog.ShowDialog(this);
    }
    internal async Task CopyFiles(bool move,string? destination=null)
    {
        var selected=files.ToArray();await Run((dest,p,t)=>FileTransfer.RunAsync(selected,dest,move,p,t),move?"Move":"Copy",forceAsk:true,preferredDestination:destination,completed:outputs=>{if(move){files.Clear();foreach(var output in outputs)files.Add(output);}});
    }
}
