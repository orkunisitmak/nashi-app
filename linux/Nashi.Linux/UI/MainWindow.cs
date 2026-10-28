// Avalonia 11.3 marks the IDataObject drag/clipboard API obsolete in favour of
// DataTransfer; it remains supported for 11.x, which this app targets.
#pragma warning disable CS0618
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

/// Home and Library for Linux, laid out like the Windows MainWindow. Global
/// drag gestures need OS input hooks that Wayland deliberately does not offer,
/// so files arrive by drop, Open with › Nashi, paste, the picker or the CLI.
internal sealed class MainWindow:Window
{
    private readonly LibraryStore library;private readonly PreferencesStore preferences;private readonly Operations operations=new(Paths.Data);
    private readonly ObservableCollection<string> files=[];
    private readonly StackPanel home=new(),libraryPage=new(),libraryRows=new(),recentRows=new(),actions=new(),toolArea=new();
    private readonly TextBlock status,selection,selectionPill,fileDescription,homeTitle,recentCount;
    private readonly ListBox list=new(){MaxHeight=120,SelectionMode=SelectionMode.Multiple};
    private readonly TextBox search=new(){Watermark=L10n.T("Find a file or tool in Library"),MinWidth=220},recentSearch=new(){Watermark=L10n.T("Find a recent file"),Width=170},link=new(){Watermark=L10n.T("Paste a public video, image or audio page link")};
    private readonly ComboBox downloadKind=new(){MinWidth=120},downloadQuality=new(){MinWidth=110};
    private readonly ProgressBar progress=new(){Minimum=0,Maximum=1,Height=6,IsVisible=false,MinWidth=160};
    private readonly Button cancelButton,toolsButton,homeButton,libraryButton;
    private readonly Panel overlay=new(){IsVisible=false};
    private readonly Border manage;private readonly Button chooseButton,wheelButton;
    private CancellationTokenSource? running;private bool busy;
    internal IReadOnlyList<string> SelectedFiles=>files;
    internal bool LibraryVisible=>libraryPage.IsVisible;

    public MainWindow(LibraryStore library,PreferencesStore preferences,IReadOnlyList<string>? initial=null)
    {
        this.library=library;this.preferences=preferences;
        Title="Nashi";Width=860;Height=820;MinWidth=640;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Icon=new WindowIcon(Brand.Asset("NashiIcon"));this.Res(BackgroundProperty,"Paper");DragDrop.SetAllowDrop(this,true);
        status=Brand.Text("Files stay on your computer.",12,"Muted");selection=Brand.Text("",23);selectionPill=Brand.Text("",11,"Gold");fileDescription=Brand.Text("",12,"Muted");homeTitle=Brand.Text("",30);recentCount=Brand.Text("0",11,"Muted",false);
        var root=new DockPanel{Margin=new(28,18,28,14)};
        // Header
        var header=new DockPanel{Margin=new(0,0,0,18)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Spacing=2};DockPanel.SetDock(controls,Dock.Right);header.Children.Add(controls);
        controls.Children.Add(Brand.Text("On your Linux desktop",11,"Muted"));
        toolsButton=Brand.Button("Finish setup",ShowTools);homeButton=Brand.Button("Home",()=>ShowLibrary(false));libraryButton=Brand.Button("Library",()=>ShowLibrary(true));
        foreach(var button in new[]{toolsButton,homeButton,libraryButton,Brand.Button("Settings",ShowSettings)}){button.Margin=new(6,0,0,0);button.FontSize=12;controls.Children.Add(button);}
        var brand=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};brand.Children.Add(new Image{Source=Brand.Asset("NashiMark"),Width=40,Height=40});brand.Children.Add(Brand.Text("Nashi",19,null,false));header.Children.Add(brand);
        // Status bar
        var footer=new DockPanel{Margin=new(4,12,4,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        cancelButton=Brand.Button("Cancel",()=>running?.Cancel());cancelButton.IsVisible=false;cancelButton.Margin=new(10,0,0,0);
        DockPanel.SetDock(cancelButton,Dock.Right);footer.Children.Add(cancelButton);DockPanel.SetDock(progress,Dock.Right);progress.Margin=new(10,0,0,0);footer.Children.Add(progress);footer.Children.Add(status);
        // Pages
        var pages=new Panel();root.Children.Add(pages);
        pages.Children.Add(new ScrollViewer{Content=home,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});pages.Children.Add(new ScrollViewer{Content=libraryPage,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        home.Children.Add(homeTitle);var intro=Brand.Text("Convert, fine-tune, and send them on their way.",14,"Muted");intro.Margin=new(0,6,0,22);home.Children.Add(intro);
        var dropText=new StackPanel{Spacing=6};dropText.Children.Add(selectionPill);dropText.Children.Add(selection);dropText.Children.Add(fileDescription);
        var picks=new WrapPanel{Margin=new(0,10,0,0)};chooseButton=Brand.Primary("Choose files",async()=>await ChooseAsync());picks.Children.Add(chooseButton);picks.Children.Add(Brand.Button("Paste",async()=>await PasteAsync()));
        wheelButton=Brand.Primary("Open wheel",ShowWheel);picks.Children.Add(wheelButton);dropText.Children.Add(picks);
        list.ItemsSource=files;list.Res(ListBox.BackgroundProperty,"Inset");list.CornerRadius=new(12);list.ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<string>((path,_)=>new TextBlock{Text=Path.GetFileName(path),[ToolTip.TipProperty]=path});
        var clear=Brand.Button("Clear",()=>{files.Clear();CloseTool();Refresh();});var remove=Brand.Button("Remove",()=>{foreach(var item in list.SelectedItems?.OfType<string>().ToArray()??[])files.Remove(item);CloseTool();Refresh();});
        var manageButtons=new StackPanel{Spacing=4};manageButtons.Children.Add(clear);manageButtons.Children.Add(remove);var manageRow=new DockPanel();DockPanel.SetDock(manageButtons,Dock.Right);manageRow.Children.Add(manageButtons);manageRow.Children.Add(list);
        manage=new Border{Child=manageRow,Margin=new(0,10,0,0)};dropText.Children.Add(manage);
        dropText.Children.Add(Brand.Text("Drop files here or use Open with › Nashi in your file manager.",11,"Muted"));
        var hints=new WrapPanel{Margin=new(0,6,0,0)};foreach(var hint in new[]{"Ctrl+O  Choose files","Ctrl+V  Paste files, text, an image or link","Ctrl+Space  Tools wheel"}){var pill=Brand.Card(Brand.Text(hint,11,"Muted"),0);pill.Padding=new(9,5);pill.Margin=new(0,0,7,6);pill.CornerRadius=new(9);hints.Children.Add(pill);}dropText.Children.Add(hints);
        var garden=new Image{Source=Brand.Asset("NashiMarkHappy"),Width=130,Height=130,VerticalAlignment=VerticalAlignment.Top,Margin=new(16,0,0,0),Opacity=.95};
        var drop=new DockPanel();DockPanel.SetDock(garden,Dock.Right);drop.Children.Add(garden);drop.Children.Add(dropText);home.Children.Add(Brand.Card(drop));
        actions.Margin=new(0,10,0,0);home.Children.Add(actions);toolArea.Margin=new(0,4,0,0);home.Children.Add(toolArea);
        home.Children.Add(BuildDownload());
        var recentHeading=new DockPanel{Margin=new(0,24,0,10)};var all=Brand.Button("See all  ↗",()=>ShowLibrary(true));all.Margin=new(8,0,0,0);DockPanel.SetDock(all,Dock.Right);recentHeading.Children.Add(all);DockPanel.SetDock(recentSearch,Dock.Right);recentHeading.Children.Add(recentSearch);
        var recentTitle=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10,VerticalAlignment=VerticalAlignment.Center};recentTitle.Children.Add(Brand.Text("Freshly made",19));recentTitle.Children.Add(recentCount);recentHeading.Children.Add(recentTitle);
        home.Children.Add(recentHeading);var recentCard=Brand.Card(recentRows,7);home.Children.Add(recentCard);
        recentSearch.TextChanged+=(_,_)=>RenderLibrary();
        libraryPage.Children.Add(Brand.Text("Your Library",30));libraryPage.Children.Add(Brand.Text("Freshly made, pasted, and ready for another round.",14,"Muted"));
        var searchLabel=Brand.Text("Find a file or tool  ·  Ctrl+F",12,"Muted");searchLabel.Margin=new(4,20,4,6);libraryPage.Children.Add(searchLabel);libraryPage.Children.Add(search);libraryRows.Margin=new(0,10,0,0);libraryPage.Children.Add(libraryRows);search.TextChanged+=(_,_)=>RenderLibrary();
        Avalonia.Automation.AutomationProperties.SetAutomationId(search,"LibrarySearch");Avalonia.Automation.AutomationProperties.SetAutomationId(link,"DownloadLink");Avalonia.Automation.AutomationProperties.SetAutomationId(list,"SelectedFiles");
        overlay.PointerPressed+=(_,e)=>{if(e.Source==overlay)HideWheel();};var layers=new Panel();layers.Children.Add(root);layers.Children.Add(overlay);Content=layers;
        AddHandler(DragDrop.DragOverEvent,(_,e)=>{e.DragEffects=!busy&&(e.Data.Contains(DataFormats.Files)||e.Data.Contains(DataFormats.Text))?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;});
        AddHandler(DragDrop.DropEvent,OnDrop);
        AddHandler(KeyDownEvent,OnKey,RoutingStrategies.Tunnel);
        Closing+=(_,e)=>{if(busy){e.Cancel=true;SetStatus("Finish the current operation or choose Cancel before closing Nashi.");}};
        if(library.RecoveryNotice is {} notice)SetStatus(notice);else if(preferences.RecoveryNotice is {} settingsNotice)SetStatus(settingsNotice);
        Add(initial??[]);ShowLibrary(false);RenderLibrary();UpdateToolsButton();
        Opened+=(_,_)=>{if(operations.Status().Any(s=>s.Path is null&&s.InstallKey is "ffmpeg" or "office"))SetStatus("Some tools are missing. Choose Finish setup to see how to install them.");};
    }
    private void SetStatus(string text)=>status.Text=L10n.T(text);
    private void UpdateToolsButton()=>toolsButton.Content=L10n.T(operations.InstallCommand().Length==0?"Tools ready":"Finish setup");
    internal void ShowLibrary(bool visible)
    {
        home.IsVisible=!visible;libraryPage.IsVisible=visible;
        homeButton.Res(BackgroundProperty,visible?"Surface":"Sage");libraryButton.Res(BackgroundProperty,visible?"Sage":"Surface");if(visible)RenderLibrary();
    }
    private void Add(IEnumerable<string> paths)
    {
        foreach(var path in paths){var full=Desktop.LocalPath(path);if(full is not null&&!files.Contains(full))files.Add(full);}Refresh();
    }
    private void Refresh()
    {
        selectionPill.Text=L10n.T(files.Count==0?"✦  START HERE":"✦  READY WHEN YOU ARE");
        selection.Text=files.Count==0?L10n.T("Drop something good."):files.Count==1?Path.GetFileName(files[0]):L10n.T($"{files.Count} files, ready to go.");
        fileDescription.Text=files.Count==0?L10n.T("Images, videos, documents, and all the little things in between."):files.Count==1?Path.GetDirectoryName(files[0])!:L10n.T("Keep them together, or give each one a fresh start.");
        homeTitle.Text=L10n.T(files.Count==0?"Your files. Their next form.":"Let’s make something of it.");
        chooseButton.Content=L10n.T(files.Count==0?"Choose files":"Add or change files");wheelButton.IsVisible=files.Count>0;manage.IsVisible=files.Count>0;
        BuildActions();
    }
    // Three tiles, as on Windows: Convert, Fine-tune, Move & share.
    private void BuildActions()
    {
        actions.Children.Clear();if(files.Count==0)return;
        var formats=FeatureCatalog.Formats(files);var tools=Operations.Choices(files);
        Control Tile(string title,string subtitle,string symbol,IEnumerable<Button> buttons)
        {
            var stack=new StackPanel{Spacing=6};var head=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};head.Children.Add(Brand.Text(symbol,22,"Gold",false));
            var caption=new StackPanel();var heading=Brand.Text(title,14);heading.FontWeight=FontWeight.SemiBold;caption.Children.Add(heading);caption.Children.Add(Brand.Text(subtitle,11,"Muted"));head.Children.Add(caption);stack.Children.Add(head);
            var wrap=new WrapPanel{Margin=new(0,6,0,0)};foreach(var button in buttons)wrap.Children.Add(button);if(wrap.Children.Count==0)wrap.Children.Add(Brand.Text("Not available for this selection.",11,"Muted"));stack.Children.Add(wrap);
            var card=Brand.Card(stack,16);card.CornerRadius=new(18);return card;
        }
        actions.Children.Add(Tile("Convert",L10n.T($"{formats.Length} ways to change it"),"⇄",formats.Select(f=>Brand.Button(f.ToUpperInvariant(),async()=>await OpenToolAsync("convert",f),false))));
        actions.Children.Add(Tile("Fine-tune","Edit, compress, and more","☷",tools.Select(t=>Brand.Button(t.Symbol+"  "+L10n.T(t.Title),async()=>await OpenToolAsync(t.Id,null),false))));
        actions.Children.Add(Tile("Move & share","Send it on its way","↗",[
            Brand.Button("Open folder",()=>Desktop.Reveal(files[0])),Brand.Button("Copy path",async()=>{await SetClipboardAsync(string.Join('\n',files));SetStatus("Path copied.");}),
            Brand.Button("Copy to…",async()=>await CopyOrMoveAsync(false)),Brand.Button("Move to…",async()=>await CopyOrMoveAsync(true)),
            Brand.Button("Open terminal here",()=>{if(!Desktop.Terminal(files[0]))SetStatus("No terminal was found. Set $TERMINAL to your terminal.");})]));
    }
    private string? openTool;private ToolPanel? panel;private string? openFormat;
    private void CloseTool(){toolArea.Children.Clear();openTool=null;panel=null;openFormat=null;}
    internal async Task OpenToolAsync(string id,string? format)
    {
        if(busy||files.Count==0)return;var selected=files.ToArray();
        if(id=="readQRCode"){await ReadQrAsync(selected);return;}
        var title=id=="convert"?L10n.T("Convert to "+format!.ToUpperInvariant()):L10n.T(FeatureCatalog.ToolNames.GetValueOrDefault(id)??Operations.Choices(selected).FirstOrDefault(c=>c.Id==id)?.Title??id);
        try
        {
            SetBusy(true,"Preparing "+Path.GetFileName(selected[0])+"…");using var token=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            panel=await ToolPanel.CreateAsync(id,format,selected,preferences,operations,token.Token);openTool=id;openFormat=format;
        }
        catch(Exception error)when(error is not OutOfMemoryException){SetBusy(false);await Dialogs.MessageAsync(this,"Couldn’t open this tool",error.Message);return;}
        SetBusy(false);
        toolArea.Children.Clear();var stack=new StackPanel{Spacing=10};var head=new DockPanel();var close=Brand.Icon("✕","Close",CloseTool);DockPanel.SetDock(close,Dock.Right);head.Children.Add(close);head.Children.Add(Brand.Text(title,19,null,false));stack.Children.Add(head);
        stack.Children.Add(Brand.Text(selected.Length==1?Path.GetFileName(selected[0]):L10n.T($"{selected.Length} files, ready to go."),12,"Muted",false));
        if(panel.Info is {} info)stack.Children.Add(Brand.Text($"{TimeSpan.FromSeconds(info.Duration):hh\\:mm\\:ss\\.f}"+(info.Video is {} v?$"  ·  {v.Width}×{v.Height}":""),11,"Muted",false));
        stack.Children.Add(panel);var run=Brand.Primary("Run",async()=>await RunToolAsync());run.HorizontalAlignment=HorizontalAlignment.Left;stack.Children.Add(run);
        toolArea.Children.Add(Brand.Card(stack));run.Focus();Dispatcher.UIThread.Post(()=>toolArea.BringIntoView(),DispatcherPriority.Background);
        SetStatus("Choose one tool from the wheel or the tiles below.");
    }
    internal async Task RunToolAsync()
    {
        if(openTool is not {} id||panel is null)return;JobSettings settings;
        try{settings=panel.Settings(Cli.Settings([],openFormat));}catch(Exception error)when(error is ArgumentException or FormatException){await Dialogs.MessageAsync(this,"Check your settings",error.Message);return;}
        var selected=files.ToArray();var format=openFormat;var label=id=="convert"?"Convert to "+format!.ToUpperInvariant():FeatureCatalog.ToolNames.GetValueOrDefault(id)??Operations.Choices(selected).FirstOrDefault(c=>c.Id==id)?.Title??id;
        if(await RunAsync((destination,p,t)=>operations.RunAsync(id,format,selected,destination,settings,p,t),label,Path.GetDirectoryName(selected[0])!))CloseTool();
    }
    /// Destination follows Settings › Output: beside the original, a fixed folder or ask.
    private async Task<bool> RunAsync(Func<string,IProgress<OperationProgress>,CancellationToken,Task<IReadOnlyList<string>>> work,string label,string beside,bool download=false)
    {
        if(busy)return false;string? destination;var prefs=preferences.Value;
        if(download)destination=prefs.DownloadFolder;
        else if(prefs.OutputMode=="ask")destination=await Dialogs.FolderAsync(this,"Save new files to…");
        else destination=prefs.OutputMode=="folder"&&!string.IsNullOrWhiteSpace(prefs.OutputFolder)?prefs.OutputFolder:beside;
        if(destination is null)return false;
        running=new CancellationTokenSource();SetBusy(true,L10n.T(label)+"…");
        try
        {
            var reporter=new Progress<OperationProgress>(p=>{progress.IsIndeterminate=p.Fraction is null;if(p.Fraction is {} f)progress.Value=f;status.Text=L10n.T(p.Label);});
            var outputs=await Task.Run(()=>work(destination,reporter,running.Token));
            try{library.Record(label,outputs);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){SetStatus("Your files are ready, but Library could not be saved.");}
            RenderLibrary();status.Text=L10n.T($"Saved {outputs.Count} new {(outputs.Count==1?"file":"files")}. Originals kept.");
            if(prefs.RevealFinished&&outputs.Count>0)_=Task.Run(()=>Desktop.Reveal(outputs[0]));
            return true;
        }
        catch(OperationCanceledException){SetStatus("Cancelled. Originals kept.");return false;}
        catch(Exception error)when(error is not OutOfMemoryException){SetStatus("Couldn’t finish");await Dialogs.MessageAsync(this,"Couldn’t finish",error.Message);return false;}
        finally{running.Dispose();running=null;SetBusy(false);}
    }
    private void SetBusy(bool value,string? label=null)
    {
        busy=value;progress.IsVisible=value;progress.IsIndeterminate=true;progress.Value=0;cancelButton.IsVisible=value&&running is not null;
        actions.IsEnabled=!value;toolArea.IsEnabled=!value;chooseButton.IsEnabled=!value;if(label is not null)status.Text=L10n.T(label);
    }
    private async Task ChooseAsync()
    {
        if(busy)return;
        var picked=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{AllowMultiple=true,Title=L10n.T("Choose files for Nashi")});
        var paths=picked.Select(f=>f.TryGetLocalPath()).OfType<string>().ToArray();if(paths.Length==0)return;
        files.Clear();CloseTool();Add(paths);ShowLibrary(false);
    }
    private async Task ChooseThenWheelAsync()
    {
        SetStatus("Choose files first. The wheel shows the tools for them.");await ChooseAsync();if(files.Count>0)ShowWheel();
    }
    private void OnDrop(object? sender,DragEventArgs e)
    {
        e.Handled=true;if(busy)return;
        var dropped=e.Data.GetFiles()?.Select(f=>f.TryGetLocalPath()).OfType<string>().ToArray()??[];
        if(dropped.Length>0){files.Clear();CloseTool();Add(dropped);ShowLibrary(false);if(files.Count>0)ShowWheel();return;}
        if(e.Data.GetText() is {} text)ImportText(text);
    }
    private async Task PasteAsync()
    {
        if(busy||Clipboard is not {} clipboard)return;
        try
        {
            var formats=await clipboard.GetFormatsAsync();
            if(formats.Contains(DataFormats.Files)&&await clipboard.GetDataAsync(DataFormats.Files) is IEnumerable<IStorageItem> items)
            {var paths=items.Select(i=>i.TryGetLocalPath()).OfType<string>().ToArray();if(paths.Length>0){files.Clear();CloseTool();Add(paths);Record("Pasted files",files);ShowLibrary(false);return;}}
            foreach(var format in new[]{"image/png","image/jpeg","PNG"})
            {
                if(!formats.Contains(format)||await clipboard.GetDataAsync(format) is not byte[] bytes||bytes.Length==0)continue;
                var path=Import(format=="image/jpeg"?"jpg":"png",bytes);files.Clear();CloseTool();Add([path]);Record("Pasted image",[path]);SetStatus("Pasted image. Choose a tool or create a ZIP.");return;
            }
            if(await clipboard.GetTextAsync() is {} text&&text.Length>0){ImportText(text);return;}
            SetStatus("Nothing to paste. Copy files, an image, text or a link first.");
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException){SetStatus("Couldn’t paste: "+error.Message);}
    }
    private void ImportText(string text)
    {
        // A copied link opens the downloader; copied file URIs select the files;
        // any other text becomes a UTF-8 file in Library, like Windows.
        var lines=text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var paths=lines.Select(Desktop.LocalPath).OfType<string>().ToArray();
        if(paths.Length>0&&paths.Length==lines.Length){files.Clear();CloseTool();Add(paths);ShowLibrary(false);return;}
        if(lines.Length==1&&Uri.TryCreate(lines[0],UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https"){link.Text=WebMediaPayload.Parse(lines[0])?.Url??lines[0];ShowLibrary(false);link.Focus();SetStatus("Link ready. Choose a format, then Download.");return;}
        var file=Import("txt",new System.Text.UTF8Encoding(false).GetBytes(text));files.Clear();CloseTool();Add([file]);Record("Pasted text",[file]);SetStatus("Pasted text. Choose a tool or create a ZIP.");
    }
    private string Import(string extension,byte[] bytes)
    {
        if(bytes.Length>200_000_000)throw new InvalidDataException("The clipboard item is larger than 200 MB.");
        var folder=Path.Combine(library.DirectoryPath,"Imports");Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,$"Clipboard {DateTime.Now:yyyy-MM-dd HH-mm-ss}-{Guid.NewGuid().ToString("N")[..6]}.{extension}");File.WriteAllBytes(path,bytes);return path;
    }
    private void Record(string label,IEnumerable<string> paths)
    {
        try{library.Record(label,paths);RenderLibrary();}catch(Exception error)when(error is IOException or UnauthorizedAccessException){SetStatus("Your files are ready, but Library could not be saved.");}
    }
    private Task SetClipboardAsync(string text)=>Clipboard?.SetTextAsync(text)??Task.CompletedTask;
    private async Task CopyOrMoveAsync(bool move)
    {
        var selected=files.ToArray();var folder=await Dialogs.FolderAsync(this,move?"Move to…":"Copy to…");if(folder is null)return;
        await RunAsync((destination,p,t)=>Task.Run<IReadOnlyList<string>>(()=>
        {
            var outputs=new List<string>();
            foreach(var file in selected)
            {
                t.ThrowIfCancellationRequested();var target=SafePaths.Unique(destination,Path.GetFileName(file));
                if(Directory.Exists(file)){if(move)Directory.Move(file,target);else CopyDirectory(file,target);}else if(move)File.Move(file,target);else File.Copy(file,target);
                outputs.Add(target);p.Report(new(outputs.Count/(double)selected.Length,move?"Moving…":"Copying…"));
            }
            return outputs;
        },t),move?"Move":"Copy",folder);
        if(move){files.Clear();Refresh();}
    }
    private static void CopyDirectory(string source,string target)
    {
        Directory.CreateDirectory(target);
        foreach(var entry in Directory.EnumerateFileSystemEntries(source))
        {
            if((File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0)continue;var destination=Path.Combine(target,Path.GetFileName(entry));
            if(Directory.Exists(entry))CopyDirectory(entry,destination);else File.Copy(entry,destination);
        }
    }
    // Radial tools wheel. Arrow keys / Tab move, Enter opens, Escape closes.
    private readonly List<Button> wheelItems=[];private int wheelIndex;
    internal bool WheelVisible=>overlay.IsVisible;
    internal void ShowWheel()
    {
        if(busy||files.Count==0)return;var choices=FeatureCatalog.Formats(files).Take(6).Select(f=>(Id:"convert",Format:(string?)f,Title:f.ToUpperInvariant(),Symbol:"⇄")).Concat(Operations.Choices(files).Select(c=>(Id:c.Id,Format:(string?)null,Title:L10n.T(c.Title),Symbol:c.Symbol))).Take(14).ToArray();
        if(choices.Length==0)return;
        overlay.Children.Clear();wheelItems.Clear();overlay.Background=new SolidColorBrush(Color.FromArgb(120,10,16,12));
        var size=Math.Min(560,Math.Min(Bounds.Width,Bounds.Height)-40);var canvas=new Canvas{Width=size,Height=size,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        var disc=new Border{Width=size,Height=size,CornerRadius=new(size/2),BorderThickness=new(1),Opacity=.97}.Res(Border.BackgroundProperty,"Surface").Res(Border.BorderBrushProperty,"Line");canvas.Children.Add(disc);
        var center=new StackPanel{Width=150,Spacing=4};center.Children.Add(new Image{Source=Brand.Asset("NashiMark"),Width=46,Height=46});var label=Brand.Text(files.Count==1?Path.GetFileName(files[0]):L10n.T($"{files.Count} files, ready to go."),12,"Muted",false);label.TextAlignment=TextAlignment.Center;label.MaxHeight=48;center.Children.Add(label);
        Canvas.SetLeft(center,size/2-75);Canvas.SetTop(center,size/2-48);canvas.Children.Add(center);
        var radius=size/2-62;
        for(var n=0;n<choices.Length;n++)
        {
            var choice=choices[n];var angle=-Math.PI/2+n*2*Math.PI/choices.Length;
            var content=new StackPanel{Spacing=2};content.Children.Add(Brand.Text(choice.Symbol,18,"Gold",false));var title=Brand.Text(choice.Title,11,null,false);title.TextAlignment=TextAlignment.Center;title.MaxWidth=92;content.Children.Add(title);
            ((TextBlock)content.Children[0]).HorizontalAlignment=HorizontalAlignment.Center;title.HorizontalAlignment=HorizontalAlignment.Center;
            var button=new Button{Content=content,Width=100,Height=72,CornerRadius=new(18),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center};
            Avalonia.Automation.AutomationProperties.SetName(button,choice.Title);
            button.Click+=async(_,_)=>{HideWheel();await OpenToolAsync(choice.Id,choice.Format);};
            Canvas.SetLeft(button,size/2+Math.Cos(angle)*radius-50);Canvas.SetTop(button,size/2+Math.Sin(angle)*radius-36);canvas.Children.Add(button);wheelItems.Add(button);
        }
        overlay.Children.Add(canvas);overlay.IsVisible=true;wheelIndex=0;wheelItems[0].Focus();
    }
    private void HideWheel(){overlay.IsVisible=false;overlay.Children.Clear();wheelItems.Clear();}
    private void OnKey(object? sender,KeyEventArgs e)
    {
        var ctrl=e.KeyModifiers==KeyModifiers.Control;var editing=FocusManager?.GetFocusedElement() is TextBox;
        if(WheelVisible)
        {
            if(e.Key==Key.Escape){HideWheel();e.Handled=true;return;}
            if(e.Key is Key.Right or Key.Down or Key.Left or Key.Up&&wheelItems.Count>0){wheelIndex=(wheelIndex+(e.Key is Key.Right or Key.Down?1:wheelItems.Count-1))%wheelItems.Count;wheelItems[wheelIndex].Focus();e.Handled=true;}
            return;
        }
        if(e.Key==Key.Escape&&openTool is not null&&!busy){CloseTool();e.Handled=true;return;}
        if(ctrl&&e.Key==Key.OemComma){ShowSettings();e.Handled=true;}
        else if(ctrl&&e.Key==Key.D1){ShowLibrary(false);e.Handled=true;}
        else if(ctrl&&e.Key==Key.D2){ShowLibrary(true);e.Handled=true;}
        else if(ctrl&&e.Key==Key.F){ShowLibrary(true);search.Focus();search.SelectAll();e.Handled=true;}
        else if(busy)return;
        else if(ctrl&&e.Key==Key.O){_=ChooseAsync();e.Handled=true;}
        else if(ctrl&&e.Key==Key.V&&!editing){_=PasteAsync();e.Handled=true;}
        else if(e.KeyModifiers==(KeyModifiers.Control|KeyModifiers.Shift)&&e.Key==Key.D){_=PasteLinkAsync();e.Handled=true;}
        // With nothing selected the wheel has no tools to show, so Ctrl+Space
        // starts with the picker and opens the wheel for the chosen files.
        else if(ctrl&&e.Key==Key.Space){if(files.Count==0)_=ChooseThenWheelAsync();else ShowWheel();e.Handled=true;}
    }
    private async Task PasteLinkAsync()
    {
        if(Clipboard is null)return;var text=(await Clipboard.GetTextAsync())?.Trim();
        if(string.IsNullOrEmpty(text)||WebMediaPayload.Parse(text) is null&&!Uri.TryCreate(text,UriKind.Absolute,out _)){SetStatus("Copy a public media link first.");return;}
        link.Text=WebMediaPayload.Parse(text)?.Url??text;ShowLibrary(false);link.Focus();SetStatus("Link ready. Choose a format, then Download.");
    }
    private Control BuildDownload()
    {
        var kinds=new[]{("video","Video"),("audio","Audio"),("thumbnail","Thumbnail"),("image","Image")};downloadKind.ItemsSource=kinds.Select(k=>L10n.T(k.Item2)).ToArray();downloadKind.SelectedIndex=0;
        downloadQuality.ItemsSource=new[]{L10n.T("Best"),"1080p","720p"};downloadQuality.SelectedIndex=0;
        var web=new StackPanel{Spacing=6};var head=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};head.Children.Add(Brand.Text("↓",28,"Gold",false));
        var caption=new StackPanel();caption.Children.Add(Brand.Text("Download a link",17));caption.Children.Add(Brand.Text("Video, audio, or an image. Straight into your tools.",12,"Muted"));head.Children.Add(caption);web.Children.Add(head);
        var row=new DockPanel{Margin=new(0,8,0,0)};var save=Brand.Primary("Download ↗",async()=>await DownloadAsync());save.Margin=new(8,0,0,0);DockPanel.SetDock(save,Dock.Right);row.Children.Add(save);
        downloadQuality.Margin=new(8,0,0,0);DockPanel.SetDock(downloadQuality,Dock.Right);row.Children.Add(downloadQuality);downloadKind.Margin=new(8,0,0,0);DockPanel.SetDock(downloadKind,Dock.Right);row.Children.Add(downloadKind);row.Children.Add(link);web.Children.Add(row);
        web.Children.Add(Brand.Text("Ctrl+Shift+D  Use a copied link  ·  Public, downloadable media",11,"Muted"));
        var card=Brand.Card(web);card.Margin=new(0,14,0,6);return card;
    }
    private async Task DownloadAsync()
    {
        if(busy)return;var kind=new[]{"video","audio","thumbnail","image"}[Math.Max(0,downloadKind.SelectedIndex)];var payload=WebMediaPayload.Parse(link.Text??"");
        string url;try{url=WebDownload.Validate(payload?.Url??link.Text??"").AbsoluteUri;}catch(ArgumentException error){await Dialogs.MessageAsync(this,"Check your link",error.Message);return;}
        var needsTools=kind!="image"&&payload?.Kind!="image"&&!(kind=="thumbnail"&&payload?.Kind=="youtube");
        if(needsTools&&!operations.Tools.Ready){await Dialogs.MessageAsync(this,"Media setup",L10n.T("Install the missing tools in a terminal:")+"\n"+SystemTools.InstallCommand(operations.Tools.Missing));return;}
        int? height=downloadQuality.SelectedIndex==1?1080:downloadQuality.SelectedIndex==2?720:null;
        await RunAsync((destination,p,t)=>new WebDownload(operations.Tools).DownloadAsync(url,kind,destination,p,t,height),"Download "+kind,preferences.Value.DownloadFolder,true);
    }
    private async Task ReadQrAsync(IReadOnlyList<string> inputs)
    {
        string[] results;SetBusy(true,"Reading QR codes…");
        try{results=await LinuxPdf.ReadQrAsync(inputs,CancellationToken.None);}
        catch(Exception error)when(error is not OutOfMemoryException){SetBusy(false);await Dialogs.MessageAsync(this,"Read QR Codes",error.Message);return;}
        SetBusy(false);await Dialogs.QrAsync(this,results,SetClipboardAsync);
    }
    private void RenderLibrary()
    {
        libraryRows.Children.Clear();recentRows.Children.Clear();
        IEnumerable<LibraryEntry> Find(string? query)=>library.Entries.Where(e=>string.IsNullOrWhiteSpace(query)||library.Search(query).Any(m=>m.Id==e.Id)||AppLocalization.Matches(e.Action,query,L10n.Language));
        var filtered=Find(search.Text).ToArray();
        if(filtered.Length==0)libraryRows.Children.Add(Brand.Card(Brand.Text(library.Entries.Count==0?"Nothing here yet. Paste something or make your first fresh copy.":"No matches. Try a file name or tool.",14,"Muted")));
        foreach(var entry in filtered)libraryRows.Children.Add(Row(entry,false));
        recentCount.Text=library.Entries.Count.ToString();foreach(var entry in Find(recentSearch.Text).Take(3))recentRows.Children.Add(Row(entry,true));
        if(library.Entries.Count==0)recentRows.Children.Add(Brand.Text("Your next fresh copy will land here.",13,"Muted"));
    }
    private Control Row(LibraryEntry entry,bool compact)
    {
        var available=entry.AvailablePaths;var row=new DockPanel();var buttons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
        var reuse=Brand.Icon("↖","Use in Nashi",()=>{if(busy)return;var existing=entry.AvailablePaths;if(existing.Length==0){SetStatus("These files have moved or were deleted.");RenderLibrary();return;}files.Clear();CloseTool();Add(existing);ShowLibrary(false);SetStatus(existing.Length==entry.Paths.Length?"Ready for another round.":"Available files selected. Some files have moved or were deleted.");});
        var open=Brand.Icon("◉","Open file",()=>{var existing=entry.AvailablePaths;if(existing.Length>0)Desktop.Open(existing[0]);});
        var reveal=Brand.Icon("▱","Open folder",()=>{var existing=entry.AvailablePaths;if(existing.Length>0)_=Task.Run(()=>Desktop.Reveal(existing[0]));else{SetStatus("This file has moved or was deleted.");RenderLibrary();}});
        foreach(var button in new[]{open,reuse,reveal}){button.IsEnabled=available.Length>0;buttons.Children.Add(button);}
        var text=new StackPanel{Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};var name=Brand.Text(entry.Name,12,null,false);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;ToolTip.SetTip(name,string.Join(Environment.NewLine,entry.Paths));text.Children.Add(name);
        var info=new StackPanel{Orientation=Orientation.Horizontal};info.Children.Add(Brand.Text(entry.Action,10,"Muted"));info.Children.Add(Brand.Text("  ·  "+entry.Created.ToLocalTime().ToString("g",L10n.Culture),10,"Muted",false));if(available.Length<entry.Paths.Length)info.Children.Add(Brand.Text("  ·  "+L10n.T("Files missing"),10,"Muted",false));text.Children.Add(info);row.Children.Add(text);
        var card=Brand.Card(row);card.Padding=new(14,10);if(compact){card.CornerRadius=new(12);card.BorderThickness=new(0);card.Margin=new(0,0,0,2);}return card;
    }
    private void ShowTools()=>_=new ToolsWindow(operations,()=>UpdateToolsButton()).ShowDialog(this);
    private async void ShowSettings()
    {
        var language=preferences.Value.Language;var window=new SettingsWindow(preferences,library);await window.ShowDialog(this);
        if(!window.Saved)return;L10n.Language=preferences.Value.Language;Brand.Apply(library.Appearance);
        // Labels are localized when built, so a language change rebuilds the window with the same selection.
        if(language!=preferences.Value.Language&&!busy&&Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {var replacement=new MainWindow(library,preferences,files.ToArray()){Width=Width,Height=Height,Position=Position,WindowStartupLocation=WindowStartupLocation.Manual};desktop.MainWindow=replacement;replacement.Show();Close();return;}
        Refresh();RenderLibrary();SetStatus("Settings saved.");
    }
}
