using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nashi.Core;
using Nashi.Linux.Engine;
namespace Nashi.Linux.UI;

public sealed record ToolChoice(string Id,string Title,string Symbol);

/// Home and Library, as on Windows: drop or choose files, pick a conversion, tool or file
/// action from the wheel or the quick actions, and find everything made in Library.
public sealed partial class MainWindow:Window
{
    internal readonly ObservableCollection<string> files=[];
    internal readonly LibraryStore library;
    internal readonly PreferencesStore preferences;
    internal readonly Toolchain tools=SelfTest.Tools();
    internal readonly LinuxOffice office=new();
    internal readonly MetadataService metadata=new(AppPaths.ToolsRoot,Environment.GetEnvironmentVariable("PATH"));
    private readonly Grid layers=new();private readonly Panel overlay=new(){IsVisible=false};private readonly Panel toastLayer=new(){HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(0,0,24,24)};
    private readonly ContentControl pageHost=new();private TextBlock status=new();
    private readonly TextBox link=new(){PlaceholderText="Paste a public video, image or audio page link"};
    private TextBox? librarySearch;private Border? dropCard;
    private string page="home";private bool busy;private string statusText="Files stay on your computer.";
    private readonly List<FloatShelf> shelves=[];
    internal bool Busy=>busy;

    public MainWindow(string[] arguments)
    {
        library=new LibraryStore();preferences=new PreferencesStore(library.DirectoryPath);
        L10n.Language=preferences.Value.Language;App.ApplyAppearance(library.Appearance);ApplyFeel();
        Title="Nashi";Width=900;Height=820;MinWidth=640;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        try{Icon=new WindowIcon(Ui.Asset("Icon"));}catch(FileNotFoundException){}
        DragDrop.SetAllowDrop(this,true);AddHandler(DragDrop.DragOverEvent,OnDragOver);AddHandler(DragDrop.DragLeaveEvent,(_,_)=>dropCard?.Classes.Remove("drop"));AddHandler(DragDrop.DropEvent,OnDrop);
        AddHandler(KeyDownEvent,OnKey,RoutingStrategies.Tunnel);
        Closing+=OnClosing;
        files.CollectionChanged+=(_,_)=>{if(page=="home")RenderPage();};
        Build();
        Add(arguments.Where(a=>!a.StartsWith("--",StringComparison.Ordinal)).Select(a=>a.StartsWith("file://",StringComparison.Ordinal)?new Uri(a).LocalPath:a));
        foreach(var notice in new[]{library.RecoveryNotice,preferences.RecoveryNotice})if(notice is not null)SetStatus(notice);
        SingleInstance.FilesReceived+=paths=>Dispatcher.UIThread.Post(()=>{Add(paths);Bring();});
        _=InitShortcutsAsync();
        if(HasMedia)Thumbnails.Ffmpeg=tools.Executable("ffmpeg");
        StartWatching();
    }

    /// Builds the whole window; used again when the language changes.
    internal void Build()
    {
        Title="Nashi";var root=new DockPanel{Margin=new(28,18,28,14)};
        var header=new DockPanel{Margin=new(0,0,0,16)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var controls=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(controls,Dock.Right);header.Children.Add(controls);
        controls.Children.Add(Ui.Button(SetupReady?"Tools ready":"Finish setup",async()=>await new SetupWindow(this).ShowDialog(this),"quiet"+(SetupReady?"":" active")));
        controls.Children.Add(Ui.Button("Home",ShowHome,"quiet"+(page=="home"?" active":"")));
        controls.Children.Add(Ui.Button("Library",ShowLibrary,"quiet"+(page=="library"?" active":"")));
        controls.Children.Add(Ui.Button("Settings",OpenSettingsAsync,"quiet"));
        var brand=Ui.Row(10,new Image{Source=Ui.Asset("Mark"),Width=40,Height=40},new TextBlock{Text="Nashi",FontSize=20,FontWeight=FontWeight.Bold,VerticalAlignment=VerticalAlignment.Center},new Border{Classes={"pill"},VerticalAlignment=VerticalAlignment.Center,Child=Ui.Text("On your PC",11,"muted")});
        header.Children.Add(brand);
        status=Ui.Text(statusText,12,"muted");status.Margin=new(4,12,4,0);DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
        if(pageHost.Parent is ContentControl previous)previous.Content=null;
        pageHost.Margin=new(0,0,14,0);
        root.Children.Add(new ScrollViewer{Content=pageHost,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});
        layers.Children.Clear();layers.Children.Add(root);layers.Children.Add(toastLayer);layers.Children.Add(overlay);Content=layers;
        RenderPage();
    }
    internal async Task OpenSettingsAsync()
    {
        if(await new SettingsWindow(this).ShowDialog<bool>(this)){L10n.Language=preferences.Value.Language;App.ApplyAppearance(library.Appearance);Build();await InitShortcutsAsync();}
    }
    internal bool HasMedia{get{try{tools.Executable("ffmpeg");tools.Executable("ffprobe");return true;}catch(FileNotFoundException){return false;}}}
    internal bool HasDownloader{get{try{tools.Executable("yt-dlp");return true;}catch(FileNotFoundException){return false;}}}
    internal bool SetupReady=>HasMedia&&HasDownloader&&office.Executable is not null;
    internal void SetStatus(string text){statusText=text;status.Text=L10n.T(text);}
    private void ShowHome(){page="home";Build();}
    private void ShowLibrary(){page="library";Build();librarySearch?.Focus();}
    private void RenderPage()=>pageHost.Content=page=="home"?HomeView():LibraryView();

    // ── Home ────────────────────────────────────────────────────────────────────────────
    private Control HomeView()
    {
        var home=new StackPanel{Spacing=0};
        home.Children.Add(Ui.Title(files.Count==0?"Your files. Their next form.":"Let’s make something of it.",30));
        var intro=Ui.Text("Convert, fine-tune, and send them on their way.",14,"muted");intro.Margin=new(0,6,0,20);home.Children.Add(intro);
        if(!SetupReady)home.Children.Add(SetupBanner());

        home.Children.Add(files.Count==0?EmptyDropCard():SelectionCard());

        home.Children.Add(DownloadCard());

        var recentHeader=new DockPanel{Margin=new(0,24,0,10)};var all=Ui.Button("See all  ↗",ShowLibrary,"quiet");DockPanel.SetDock(all,Dock.Right);recentHeader.Children.Add(all);
        recentHeader.Children.Add(Ui.Row(10,Ui.Title("Freshly made",19),Ui.Text(library.Entries.Count.ToString(),11,"muted",false)));home.Children.Add(recentHeader);
        var recent=new StackPanel{Spacing=4};foreach(var entry in library.Entries.Take(5))recent.Children.Add(LibraryRow(entry));
        if(library.Entries.Count==0)recent.Children.Add(Ui.Text("Nothing yet. Your fresh copies will appear here.",13,"muted"));
        home.Children.Add(Ui.Card(recent));
        return home;
    }
    /// Nothing selected: one big, dashed drop target that also teaches the shortcuts.
    private Control EmptyDropCard()
    {
        var text=new StackPanel{Spacing=8,VerticalAlignment=VerticalAlignment.Center};
        text.Children.Add(new Border{Classes={"pill"},Child=Ui.Text("✦  START HERE",11)});
        text.Children.Add(Ui.Text(L10n.T("Drop something good."),26,"title",false));
        text.Children.Add(Ui.Text("Images, videos, documents, and all the little things in between.",13,"muted"));
        text.Children.Add(new WrapPanel{Margin=new(0,10,0,0),Children={Ui.Primary("Choose files",ChooseFiles),Ui.Button("Paste",PasteAsync,tip:"Ctrl+V")}});
        var grid=new Grid{ColumnDefinitions=new("*,170"),Margin=new(30,26)};grid.Children.Add(text);
        var pear=new Image{Source=Ui.Asset("Mark"),Width=140,Height=140,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(pear,1);grid.Children.Add(pear);
        var stack=new StackPanel{Spacing=12,Children={new DashedOutline(grid)}};
        stack.Children.Add(ShortcutHints());
        dropCard=Ui.Card(stack);dropCard.Classes.Add("hero");return dropCard;
    }
    /// Files selected: previews you can remove one by one, then everything Nashi can do with them.
    private Control SelectionCard()
    {
        var head=new DockPanel();
        var happy=new Image{Source=Ui.Asset("MarkHappy"),Width=84,Height=84,VerticalAlignment=VerticalAlignment.Top};DockPanel.SetDock(happy,Dock.Right);head.Children.Add(happy);
        var title=new StackPanel{Spacing=6};
        title.Children.Add(new Border{Classes={"pill"},Child=Ui.Text("✦  READY WHEN YOU ARE",11)});
        // A single file shows its own name, which is never looked up in the translation catalog.
        title.Children.Add(Ui.Text(files.Count==1?Path.GetFileName(files[0]):L10n.T($"{files.Count} files, ready to go."),23,"title",false));
        title.Children.Add(Ui.Text(files.Count==1?Path.GetDirectoryName(files[0])!:L10n.T("Keep them together, or give each one a fresh start."),12,"muted",false));
        head.Children.Add(title);
        var picks=new WrapPanel{Margin=new(0,12,0,4),Children={Ui.Primary("Open wheel",ShowWheel),Ui.Button("Add or change files",ChooseFiles),Ui.Button("Paste",PasteAsync,tip:"Ctrl+V"),Ui.Button("Clear",()=>files.Clear())}};
        var tiles=new WrapPanel{Margin=new(0,8,0,0)};
        var shown=files.Take(60).ToList();
        for(var n=0;n<shown.Count;n++){var path=shown[n];var at=n;tiles.Children.Add(new FileTile(path,()=>files.Remove(path),open:()=>new PreviewWindow(this,shown,at).Show(this)));}
        if(files.Count>60)tiles.Children.Add(Ui.Text(L10n.T($"and {files.Count-60} more"),12,"muted",false).Also(t=>t.VerticalAlignment=VerticalAlignment.Center));
        var stack=new StackPanel{Spacing=6,Children={head,picks,tiles,SelectionActions(),ShortcutHints()}};
        dropCard=Ui.Card(stack);return dropCard;
    }
    private Control ShortcutHints()
    {
        var hints=new StackPanel{Spacing=3};
        hints.Children.Add(Ui.Text("Ctrl+O  Choose  ·  Ctrl+V  Paste  ·  Ctrl+Space  Wheel  ·  Ctrl+2  Library",11,"muted"));
        if(ShortcutsAvailable&&LinuxSettings.Shortcuts.GetValueOrDefault("gesture") is {Length:>0} gesture)
            hints.Children.Add(Ui.Text(L10n.T("Anywhere: start dragging files, press")+" "+gesture+" "+L10n.T("and move ←  →  ↑ to pick a wheel."),11,"gold",false));
        else hints.Children.Add(Ui.Text("Hold Ctrl, Shift or Alt while dropping files to open Tools, Convert or File actions.",11,"muted"));
        return hints;
    }
    private Control SetupBanner()
    {
        var missing=new List<string>();if(!HasMedia)missing.Add("FFmpeg");if(!HasDownloader)missing.Add("yt-dlp");if(office.Executable is null)missing.Add("LibreOffice");
        var row=new DockPanel();var button=Ui.Primary("Finish setup",async()=>{await new SetupWindow(this).ShowDialog(this);Build();});DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);
        row.Children.Add(Ui.Stack(2,Ui.Text("A few helpers are missing",16,"title"),Ui.Text(L10n.T("Core image and PDF tools work now. Media, downloads and office conversions need:")+" "+string.Join(", ",missing),12,"muted",false)));
        var card=Ui.Card(row);card.Margin=new(0,0,0,14);card.Classes.Add("warn");return card;
    }
    private Control DownloadCard()
    {
        var kind=Ui.Choice([("video","Video"),("audio","Audio"),("thumbnail","Thumbnail"),("image","Image")],"video");
        var quality=Ui.Choice([("best","Best"),("1080","1080p"),("720","720p")],"best");
        var row=new DockPanel{Margin=new(0,12,0,0)};var save=Ui.Primary("Download ↗",async()=>await DownloadAsync(Ui.Value(kind),Ui.Value(quality)));
        DockPanel.SetDock(save,Dock.Right);row.Children.Add(save);
        var options=Ui.Row(0,kind,quality);options.Margin=new(8,0,0,0);kind.Margin=new(0,0,8,0);quality.Margin=new(0,0,8,0);DockPanel.SetDock(options,Dock.Right);row.Children.Add(options);
        // The link box survives re-renders so a pasted link is kept; detach it from the previous view first.
        (link.Parent as Panel)?.Children.Remove(link);link.PlaceholderText=L10n.T("Paste a public video, image or audio page link");row.Children.Add(link);
        var card=Ui.Card(Ui.Stack(2,Ui.Text("Download a link",17,"title"),Ui.Text("Video, audio, or an image. Straight into your tools.",12,"muted"),row,Ui.Text("Ctrl+Shift+D  Use a copied link  ·  Public, downloadable media",11,"muted")));
        card.Margin=new(0,14,0,0);return card;
    }
    private Control SelectionActions()
    {
        var stack=new StackPanel{Spacing=4,Margin=new(0,6,0,0)};
        var formats=FeatureCatalog.Formats(files);
        if(formats.Length>0){stack.Children.Add(Ui.Text("Convert to",11,"muted"));var wrap=new WrapPanel();foreach(var format in formats)wrap.Children.Add(Ui.Button(format.ToUpperInvariant(),async()=>await EditAsync(new("convert:"+format,"Convert to "+format.ToUpperInvariant(),"↗")),"chip"));stack.Children.Add(wrap);}
        var toolChoices=ToolChoices();
        if(toolChoices.Count>0){stack.Children.Add(Ui.Text("Tools",11,"muted"));var wrap=new WrapPanel();foreach(var choice in toolChoices)wrap.Children.Add(Ui.Button(choice.Symbol+"  "+L10n.T(choice.Title),async()=>await EditAsync(choice),"chip"));stack.Children.Add(wrap);}
        stack.Children.Add(Ui.Text("File actions",11,"muted"));var actions=new WrapPanel();foreach(var choice in FileActionChoices())actions.Children.Add(Ui.Button(choice.Symbol+"  "+L10n.T(choice.Title),async()=>await PerformFileAction(choice.Id,files.ToArray()),"chip"));stack.Children.Add(actions);
        return stack;
    }
    internal IReadOnlyList<ToolChoice> ToolChoices()=>FeatureCatalog.Tools(files).Select(id=>new ToolChoice(id,FeatureCatalog.ToolNames.GetValueOrDefault(id,id),Ui.Symbol(id))).Concat(ExtraTools(files)).ToList();
    internal static IReadOnlyList<ToolChoice> FileActionChoices()=>[new("open","Open","↗"),new("reveal","Show in Folder","⌂"),new("float","Float","▣"),new("copy","Copy","▱"),new("move","Move","↗"),new("copyPath","Copy Path","↪"),new("terminal","Terminal",">_"),..ExtraActions()];
    internal IReadOnlyList<ToolChoice> WheelChoices(string mode)=>ChoicesFor(files,mode);
    internal void ShowWheel()=>ShowWheel(null);
    internal void ShowWheel(string? initial)
    {
        if(busy||files.Count==0)return;var mode=initial is not null&&WheelChoices(initial).Count>0?initial:new[]{"tools","convert","actions"}.First(m=>WheelChoices(m).Count>0);
        overlay.Children.Clear();overlay.Children.Add(new WheelOverlay(WheelChoices,mode,async choice=>{CloseOverlay();await ChooseFromWheel(choice);},CloseOverlay));overlay.IsVisible=true;
    }
    private void CloseOverlay(){overlay.IsVisible=false;overlay.Children.Clear();}
    private async Task ChooseFromWheel(ToolChoice choice)
    {
        if(choice.Id=="convert")ShowWheel("convert");
        else if(FileActionChoices().Any(a=>a.Id==choice.Id))await PerformFileAction(choice.Id,files.ToArray());
        else await EditAsync(choice);
    }

    // ── Library ─────────────────────────────────────────────────────────────────────────
    private Control LibraryView()
    {
        var stack=new StackPanel{Spacing=6};stack.Children.Add(Ui.Title("Your Library",30));stack.Children.Add(Ui.Text("Freshly made, pasted, and ready for another round.",14,"muted"));
        var label=Ui.Text("Find a file or tool  ·  Ctrl+F",12,"muted");label.Margin=new(4,16,4,0);stack.Children.Add(label);
        var rows=new StackPanel{Spacing=4};var query=librarySearch?.Text??"";librarySearch=new TextBox{PlaceholderText=L10n.T("Find a file or tool in Library"),Text=query};
        void Render(){rows.Children.Clear();var found=library.Search(librarySearch.Text??"",L10n.Language).ToList();foreach(var entry in found)rows.Children.Add(LibraryRow(entry));if(found.Count==0)rows.Children.Add(Ui.Text(library.Entries.Count==0?"Nothing yet. Your fresh copies will appear here.":"No matches.",13,"muted"));}
        librarySearch.TextChanged+=(_,_)=>Render();Render();stack.Children.Add(librarySearch);stack.Children.Add(Ui.Card(rows));
        stack.Children.Add(Ui.Text($"Library keeps the last {LibraryStore.Limit} successful exports and clipboard imports.",11,"muted"));
        return stack;
    }
    private Control LibraryRow(LibraryEntry entry)
    {
        var available=entry.AvailablePaths;var row=new DockPanel();
        var buttons=Ui.Row(0);DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
        if(available.Length>0)
        {
            buttons.Children.Add(Ui.Button("Use",()=>{files.Clear();Add(available);ShowHome();},"chip",tip:"Select these files again"));
            buttons.Children.Add(Ui.Button("Open",()=>LinuxShell.Open(available[0]),"chip"));
            buttons.Children.Add(Ui.Button("Show in Folder",()=>LinuxShell.Reveal(available[0]),"chip"));
        }
        var when=entry.Created.LocalDateTime.ToString("g",System.Globalization.CultureInfo.GetCultureInfo(L10n.Language=="tr"?"tr-TR":"en-US"));
        var preview=new Border{Width=44,Height=44,CornerRadius=new(8),ClipToBounds=true,Margin=new(0,0,12,0),Classes={"tile-frame"}};DockPanel.SetDock(preview,Dock.Left);row.Children.Add(preview);
        var (kind,color)=Thumbnails.Kind(entry.Paths[0]);preview.Background=new SolidColorBrush(color);preview.Child=new TextBlock{Text=kind,FontSize=kind.Length>4?9:11,FontWeight=FontWeight.Bold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        if(available.Length>0){DragOut.Attach(preview,()=>available);ToolTip.SetTip(preview,L10n.T("Drag the files anywhere"));}
        if(available.Length>0)_=Task.Run(async()=>{if(await Thumbnails.GetAsync(available[0],96) is {} bitmap)await Dispatcher.UIThread.InvokeAsync(()=>preview.Child=new Image{Source=bitmap,Stretch=Stretch.UniformToFill});});
        row.Children.Add(Ui.Stack(2,Ui.Text(entry.Name,14,null,false),Ui.Text(L10n.T(entry.Action)+"  ·  "+when+(available.Length<entry.Paths.Length?"  ·  "+L10n.T("Moved or deleted"):""),11,"muted",false)).Also(x=>x.VerticalAlignment=VerticalAlignment.Center));
        return new Border{Child=row,Padding=new(8,6),CornerRadius=new(10)};
    }
    internal void Record(string action,IEnumerable<string> paths){library.Record(action,paths);}

    // ── Input: choose, drop, paste, keys ────────────────────────────────────────────────
    internal void Add(IEnumerable<string> paths)
    {
        var added=0;foreach(var raw in paths)
        {
            string full;try{full=Path.GetFullPath(raw);}catch(Exception error)when(error is ArgumentException or NotSupportedException or PathTooLongException){continue;}
            if((File.Exists(full)||Directory.Exists(full))&&!files.Contains(full)){files.Add(full);added++;}
        }
        if(added>0)SetStatus("Files stay on your computer.");
    }
    private async Task ChooseFiles()
    {
        var picked=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title=L10n.T("Choose files"),AllowMultiple=true});
        var paths=picked.Select(f=>f.TryGetLocalPath()).OfType<string>().ToList();if(paths.Count==0)return;files.Clear();Add(paths);
    }
    private void OnDragOver(object? sender,DragEventArgs e)
    {
        e.DragEffects=!busy&&(e.DataTransfer.Contains(DataFormat.File)||e.DataTransfer.Contains(DataFormat.Text))?DragDropEffects.Copy:DragDropEffects.None;
        if(e.DragEffects!=DragDropEffects.None&&dropCard is not null&&!dropCard.Classes.Contains("drop"))dropCard.Classes.Add("drop");
    }
    private void OnDrop(object? sender,DragEventArgs e)
    {
        dropCard?.Classes.Remove("drop");if(busy)return;
        var dropped=e.DataTransfer.TryGetFiles()?.Select(f=>f.TryGetLocalPath()).OfType<string>().ToList()??[];
        if(dropped.Count>0)
        {
            if(page!="home")ShowHome();files.Clear();Add(dropped);
            // Like dragging with a modifier on macOS and Windows: the chord held while dropping opens its wheel.
            if(DropMode(e.KeyModifiers) is {} mode)Dispatcher.UIThread.Post(()=>ShowWheel(mode));
            return;
        }
        if(e.DataTransfer.TryGetText() is {} text&&Uri.TryCreate(text.Trim(),UriKind.Absolute,out var url)&&url.Scheme is "http" or "https"){link.Text=url.AbsoluteUri;if(page!="home")ShowHome();link.Focus();}
    }
    /// Ctrl → Tools, Shift → Convert, Alt → File actions by default; Settings can change the chords.
    private string? DropMode(KeyModifiers modifiers)
    {
        var parts=new List<string>();if(modifiers.HasFlag(KeyModifiers.Control))parts.Add("Ctrl");if(modifiers.HasFlag(KeyModifiers.Shift))parts.Add("Shift");if(modifiers.HasFlag(KeyModifiers.Alt))parts.Add("Alt");
        var chord=string.Join("+",parts);var p=preferences.Value;
        return chord.Length==0?null:chord==p.ToolsModifier?"tools":chord==p.ConvertModifier?"convert":chord==p.ActionsModifier?"actions":null;
    }
    private void OnKey(object? sender,KeyEventArgs e)
    {
        if(overlay.IsVisible){if(overlay.Children.FirstOrDefault() is WheelOverlay wheel)wheel.HandleKey(e);return;}
        var ctrl=e.KeyModifiers.HasFlag(KeyModifiers.Control);var shift=e.KeyModifiers.HasFlag(KeyModifiers.Shift);var inText=FocusManager?.GetFocusedElement() is TextBox;
        if(ctrl&&e.Key==Key.O){_=Ui.Guard(ChooseFiles);e.Handled=true;}
        else if(ctrl&&shift&&e.Key==Key.D){_=Ui.Guard(PasteLinkAsync);e.Handled=true;}
        else if(ctrl&&e.Key==Key.V&&!inText){_=Ui.Guard(PasteAsync);e.Handled=true;}
        else if(ctrl&&e.Key==Key.Space)
        {
            // Without files there is nothing to turn the wheel for: say so, offer the file chooser, then open the wheel.
            if(files.Count==0){SetStatus("Choose or drop files first, then press Ctrl+Space.");_=Ui.Guard(async()=>{await ChooseFiles();if(files.Count>0)ShowWheel();});}
            else ShowWheel();
            e.Handled=true;
        }
        else if(ctrl&&e.Key==Key.D1){ShowHome();e.Handled=true;}
        else if(ctrl&&(e.Key==Key.D2||e.Key==Key.F)){ShowLibrary();e.Handled=true;}
        else if(ctrl&&e.Key==Key.Q){Close();e.Handled=true;}
        // Delete on Home clears the selection (files stay where they are); single files have their own ✕.
        else if(e.Key==Key.Delete&&!inText&&page=="home"&&files.Count>0){files.Clear();e.Handled=true;}
    }
    private async Task PasteLinkAsync()
    {
        var text=Clipboard is {} clipboard?await clipboard.TryGetTextAsync():null;
        if(string.IsNullOrWhiteSpace(text)){SetStatus("Copy a public media link first.");return;}
        link.Text=text.Trim();if(page!="home")ShowHome();await DownloadAsync("video","best");
    }
    /// Files, file URIs, a link, an image or text from the clipboard. Text and images become new
    /// files in Library (UTF-8 text, lossless PNG); nothing watches the clipboard in the background.
    private async Task PasteAsync()
    {
        if(busy||Clipboard is not {} clipboard)return;
        if(await clipboard.TryGetFilesAsync() is {Length:>0} items)
        {var paths=items.Select(i=>i.TryGetLocalPath()).OfType<string>().ToList();if(paths.Count>0){files.Clear();Add(paths);return;}}
        foreach(var format in new[]{ClipboardFiles.GnomeFiles,ClipboardFiles.UriList})
        {
            var paths=ClipboardFiles.ParseUris(await clipboard.TryGetValueAsync(format) is {} bytes?System.Text.Encoding.UTF8.GetString(bytes):null);
            if(paths.Count>0){files.Clear();Add(paths);return;}
        }
        if(await clipboard.TryGetValueAsync(ClipboardFiles.Png) is {Length:>0} png)
        {
            var file=ClipboardFiles.SaveImport(library.DirectoryPath,"Pasted image","png",png);Record("Paste",[file]);files.Clear();Add([file]);SetStatus("Pasted image saved to Library.");return;
        }
        var value=await clipboard.TryGetTextAsync();if(string.IsNullOrWhiteSpace(value)){SetStatus("The clipboard is empty.");return;}
        var lines=ClipboardFiles.ParseUris(value);if(lines.Count>0){files.Clear();Add(lines);return;}
        if(Uri.TryCreate(value.Trim(),UriKind.Absolute,out var url)&&url.Scheme is "http" or "https"){link.Text=url.AbsoluteUri;SetStatus("Link pasted. Choose Download when ready.");return;}
        var saved=ClipboardFiles.SaveImport(library.DirectoryPath,"Pasted text","txt",new System.Text.UTF8Encoding(false).GetBytes(value));Record("Paste",[saved]);files.Clear();Add([saved]);SetStatus("Pasted text saved to Library.");
    }

    // ── Running work ───────────────────────────────────────────────────────────────────
    /// Picks the destination (beside the source, a fixed folder, or ask), shows progress with
    /// Cancel and records the result in Library. Exports are transactional in Nashi.Core.
    internal async Task RunAsync(Func<string,IProgress<OperationProgress>,CancellationToken,Task<IReadOnlyList<string>>> work,string label="Fresh copy",string? fixedDestination=null)
    {
        if(busy)return;string? destination=fixedDestination;
        if(destination is null)
        {
            var mode=preferences.Value.OutputMode;
            if(label=="Move"||mode=="ask"||files.Count==0)destination=await Dialogs.Folder(this,"Save new files to…");
            else if(mode=="folder"&&!string.IsNullOrWhiteSpace(preferences.Value.OutputFolder))destination=preferences.Value.OutputFolder;
            else destination=Path.GetDirectoryName(files[0]);
            if(destination is null)return;
        }
        SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=new Toast(L10n.T(label),cancellation.Cancel);toastLayer.Children.Clear();toastLayer.Children.Add(toast);
        // Hidden window (gesture or tray): a small card in the screen corner shows the progress instead.
        var hud=IsVisible?null:new ProgressHud(this,L10n.T(label),cancellation.Cancel);hud?.Show();
        try
        {
            var outputs=await work(destination,new Progress<OperationProgress>(p=>{toast.Update(p);hud?.Update(p);}),cancellation.Token);
            toast.Complete(outputs,()=>toastLayer.Children.Remove(toast));Record(label,outputs);_=NotifyDoneAsync(label,outputs);
            SetStatus($"Saved {outputs.Count} new {(outputs.Count==1?"file":"files")}. Originals kept.");
            if(preferences.Value.RevealFinished&&outputs.Count>0)LinuxShell.Reveal(outputs[0]);
        }
        catch(OperationCanceledException){toastLayer.Children.Remove(toast);SetStatus("Cancelled. Originals kept.");}
        catch(Exception error){toastLayer.Children.Remove(toast);if(!IsVisible){_=NotifyDoneAsync(label,[],error.Message);}else await Dialogs.Message(this,"Couldn’t finish",L10n.T(error.Message));}
        finally{hud?.Close();SetBusy(false);if(page=="home")RenderPage();}
    }
    private void SetBusy(bool value){busy=value;pageHost.IsEnabled=!value;}
    /// Long setup steps share the progress toast but are not recorded in Library.
    internal async Task<bool> SetupStepAsync(string label,Func<IProgress<OperationProgress>,CancellationToken,Task> work)
    {
        if(busy)return false;SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=new Toast(L10n.T(label),cancellation.Cancel);toastLayer.Children.Clear();toastLayer.Children.Add(toast);
        try{await work(new Progress<OperationProgress>(toast.Update),cancellation.Token);toast.Complete([],()=>toastLayer.Children.Remove(toast));return true;}
        catch(OperationCanceledException){toastLayer.Children.Remove(toast);SetStatus("Setup cancelled.");return false;}
        catch(Exception error){toastLayer.Children.Remove(toast);await Dialogs.Message(this,"Setup could not finish",L10n.T(error.Message));return false;}
        finally{SetBusy(false);}
    }
    private Task DownloadAsync(string kind,string quality)=>DownloadUrlAsync(link.Text??"",kind,quality);
    internal async Task DownloadUrlAsync(string text,string kind,string quality="best")
    {
        string url;try{url=WebDownload.Validate(text).AbsoluteUri;}catch(ArgumentException error){await Dialogs.Message(this,"Download a link",L10n.T(error.Message));return;}
        if(kind!="image"&&!HasDownloader)Bring();
        if(kind!="image"&&!HasDownloader)
        {
            if(await Dialogs.Confirm(this,"Set up media tools","Web downloads need yt-dlp. Download the verified media tools now? Existing tools on your system are kept.","Download"))await new SetupWindow(this).InstallMediaAsync();
            if(!HasDownloader)return;
        }
        var folder=preferences.Value.DownloadFolder;if(string.IsNullOrWhiteSpace(folder)||folder==new Preferences().DownloadFolder&&!Directory.Exists(folder))folder=AppPaths.Downloads;
        int? height=quality switch{"1080"=>1080,"720"=>720,_=>null};
        await RunAsync((destination,p,t)=>new WebDownload(tools).DownloadAsync(url,kind,destination,p,t,height),"Download "+kind,folder);
    }
}

internal static class ControlExtensions
{
    public static T Also<T>(this T value,Action<T> action){action(value);return value;}
}
