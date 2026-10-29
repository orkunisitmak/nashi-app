using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

public sealed class MainWindow:Window
{
    private readonly LibraryStore library;
    private readonly List<string> files=[];
    private readonly ListBox fileList=new(){SelectionMode=SelectionMode.Multiple,MaxHeight=220,ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<string>((path,_)=>{var row=new TextBlock{Text=Path.GetFileName(path),TextTrimming=TextTrimming.CharacterEllipsis};ToolTip.SetTip(row,path);return row;})};
    private readonly TextBlock selection=new(){FontSize=22,FontWeight=FontWeight.SemiBold};
    private readonly TextBlock hint=new(){TextWrapping=TextWrapping.Wrap};
    private readonly WrapPanel formats=new(),tools=new();
    private readonly TextBlock formatsLabel=Label("Convert to"),toolsLabel=Label("Tools");
    private readonly ProgressBar progress=new(){Minimum=0,Maximum=1,Height=6,IsVisible=false};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Button cancel=new(){Content="Cancel",IsVisible=false},reveal=new(){Content="Show in folder",IsVisible=false};
    private readonly TextBox search=new(){Watermark="Search Library"};
    private readonly StackPanel libraryItems=new(){Spacing=6};
    private CancellationTokenSource? running;
    private string? lastFolder;

    public MainWindow(LibraryStore library,IReadOnlyList<string> args)
    {
        this.library=library;Title="Nashi";Width=760;Height=720;MinWidth=520;MinHeight=520;
        Icon=new WindowIcon(AssetLoader.Open(new Uri("avares://Nashi/Assets/NashiIcon.png")));
        this[!BackgroundProperty]=Resource("Paper");this[!ForegroundProperty]=Resource("Ink");
        Content=Layout();
        cancel.Click+=(_,_)=>running?.Cancel();reveal.Click+=(_,_)=>{if(lastFolder is not null)Open(lastFolder);};
        search.TextChanged+=(_,_)=>RefreshLibrary();
        KeyDown+=OnKey;
        DragDrop.SetAllowDrop(this,true);AddHandler(DragDrop.DragOverEvent,(_,e)=>e.DragEffects=e.DataTransfer.Contains(DataFormat.File)?DragDropEffects.Copy:DragDropEffects.None);
        AddHandler(DragDrop.DropEvent,(_,e)=>Add(e.DataTransfer.TryGetFiles()?.Select(f=>f.TryGetLocalPath()).OfType<string>()??[]));
        if(library.RecoveryNotice is {} notice)status.Text=notice;
        var capture=args.SkipWhile(a=>a!="--capture").Skip(1).FirstOrDefault();
        Add(args.Where(a=>!a.StartsWith('-')&&a!=capture).Select(Path.GetFullPath));Refresh();RefreshLibrary();
        if(capture is not null)Opened+=async(_,_)=>await Capture(Path.GetFullPath(capture),args.Contains("--dark")?"dark":args.Contains("--light")?"light":null);
    }

    /// Render check without a visible desktop (a locked session or CI): saves the window and exits.
    private async Task Capture(string file,string? appearance)
    {
        if(appearance is not null)NashiTheme.Apply(Application.Current!,appearance);
        await Task.Delay(800);
        var size=new PixelSize((int)Bounds.Width,(int)Bounds.Height);using var bitmap=new RenderTargetBitmap(size);bitmap.Render(this);bitmap.Save(file);
        Close();
    }

    private Control Layout()
    {
        var choose=Primary("Choose files");choose.Click+=async(_,_)=>await Choose();
        var clear=new Button{Content="Clear"};clear.Click+=(_,_)=>{files.Clear();Refresh();};
        var remove=new Button{Content="Remove selected"};remove.Click+=(_,_)=>{foreach(var item in fileList.SelectedItems?.OfType<string>().ToArray()??[])files.Remove(item);Refresh();};
        var drop=Card(new StackPanel{Spacing=10,HorizontalAlignment=HorizontalAlignment.Center,Children=
        {
            new Image{Source=new Bitmap(AssetLoader.Open(new Uri("avares://Nashi/Assets/NashiMark.png"))),Width=88,Height=88},
            selection,hint,new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Center,Children={choose,remove,clear}}
        }});
        selection.HorizontalAlignment=hint.HorizontalAlignment=HorizontalAlignment.Center;hint.TextAlignment=TextAlignment.Center;
        var home=new StackPanel{Spacing=14,Margin=new(20),Children={drop,fileList,formatsLabel,formats,toolsLabel,tools,progress,
            new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={cancel,reveal}},status}};
        var libraryPage=new StackPanel{Spacing=12,Margin=new(20),Children={search,libraryItems}};
        var appearance=new ComboBox{ItemsSource=new[]{"System","Light","Dark"},SelectedIndex=library.Appearance switch{"light"=>1,"dark"=>2,_=>0},MinWidth=120};
        appearance.SelectionChanged+=(_,_)=>{var value=new[]{"system","light","dark"}[appearance.SelectedIndex];library.SetAppearance(value);NashiTheme.Apply(Application.Current!,value);};
        var settings=new StackPanel{Spacing=12,Margin=new(20),Children={Label("Appearance"),appearance,
            new TextBlock{TextWrapping=TextWrapping.Wrap,Text=$"Media tools: {(Operations.Media is null?"install ffmpeg with your package manager":"ffmpeg found")}.\nLibrary and settings: {LinuxPaths.DataDirectory}\nNashi {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)} · Linux preview"}}};
        var tabs=new TabControl{Items={Tab("Home",home),Tab("Library",libraryPage),Tab("Settings",settings)}};
        tabs.SelectionChanged+=(_,_)=>{if(tabs.SelectedIndex==1)RefreshLibrary();};
        return tabs;
    }

    private void OnKey(object? sender,KeyEventArgs e)
    {
        if(e.KeyModifiers==KeyModifiers.Control&&e.Key==Key.O){_=Choose();e.Handled=true;}
        else if(e.KeyModifiers==KeyModifiers.Control&&e.Key==Key.V&&FocusManager?.GetFocusedElement() is not TextBox){_=Paste();e.Handled=true;}
        else if(e.Key==Key.Delete&&fileList.IsFocused){foreach(var item in fileList.SelectedItems?.OfType<string>().ToArray()??[])files.Remove(item);Refresh();e.Handled=true;}
    }
    private async Task Choose()
    {
        var picked=await StorageProvider.OpenFilePickerAsync(new(){AllowMultiple=true,Title="Choose files"});
        Add(picked.Select(f=>f.TryGetLocalPath()).OfType<string>());
    }
    private async Task Paste()
    {
        if(Clipboard is null)return;
        using var data=await Clipboard.TryGetDataAsync();if(data is null)return;
        if(await data.TryGetFilesAsync() is {Length:>0} items){Add(items.Select(i=>i.TryGetLocalPath()).OfType<string>());return;}
        // File managers such as Nautilus put file:// URIs on the clipboard as text.
        if(await data.TryGetTextAsync() is {} text)Add(text.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(line=>Uri.TryCreate(line,UriKind.Absolute,out var uri)&&uri.IsFile?uri.LocalPath:line).Where(Path.IsPathFullyQualified));
    }
    private void Add(IEnumerable<string> paths)
    {
        if(running is not null)return;
        foreach(var path in paths.Select(Path.GetFullPath))if((File.Exists(path)||Directory.Exists(path))&&!files.Contains(path))files.Add(path);
        Refresh();
    }

    private void Refresh()
    {
        fileList.ItemsSource=files.ToArray();fileList.IsVisible=files.Count>0;
        selection.Text=files.Count switch{0=>"Drop something good.",1=>Path.GetFileName(files[0]),var n=>$"{n} files, ready to go."};
        hint.Text=files.Count==0?"Images, videos, audio, PDFs and archives. Drop files here, choose them or paste with Ctrl+V.":"Pick a format or a tool below. Originals are never overwritten.";
        var ready=running is null&&files.Count>0;
        var formatChoices=ready?Operations.Formats(files):[];var toolChoices=ready?Operations.Tools(files):[];
        formats.Children.Clear();foreach(var format in formatChoices)formats.Children.Add(Action(format.ToUpperInvariant(),new("convert",format.ToUpperInvariant(),format)));
        tools.Children.Clear();foreach(var tool in toolChoices)tools.Children.Add(Action(tool.Title,tool));
        formatsLabel.IsVisible=formats.IsVisible=formatChoices.Length>0;toolsLabel.IsVisible=tools.IsVisible=toolChoices.Length>0;
        if(ready&&formatChoices.Length==0&&toolChoices.Length==0)status.Text="These files have no Linux preview action yet. Try one kind of file at a time.";
    }
    private Button Action(string title,Choice choice){var button=new Button{Content=title,Margin=new(0,0,8,8),MinWidth=72,HorizontalContentAlignment=HorizontalAlignment.Center};button.Click+=async(_,_)=>await Run(choice);return button;}

    private async Task Run(Choice choice)
    {
        if(running is not null||files.Count==0)return;
        var inputs=files.ToArray();
        if(choice.Id=="readQRCode")
        {
            try{var codes=await Task.Run(()=>Operations.ReadQr(inputs));status.Text=codes.Length==0?"No QR code found.":string.Join("\n",codes);if(codes.Length>0&&Clipboard is not null){await Clipboard.SetTextAsync(string.Join("\n",codes));status.Text+="\n(Copied)";}}
            catch(Exception error){status.Text=error.Message;}
            return;
        }
        MediaOptions? media=null;
        if(choice.Id=="changeVideoSpeed"){if(await Ask(choice.Title,[("0.5×",.5),("1.5×",1.5),("2×",2d)],1) is not {} speed)return;media=new(){Speed=speed};}
        else if(choice.Id=="audioChannels"){if(await Ask(choice.Title,[("Mono",1),("Stereo",2)],0) is not {} channels)return;media=new(){Channels=channels};}
        var destination=Path.GetDirectoryName(inputs[0])!;
        running=new CancellationTokenSource();progress.IsVisible=cancel.IsVisible=true;progress.IsIndeterminate=true;reveal.IsVisible=false;status.Text=choice.Title+"…";Refresh();
        var report=new Progress<OperationProgress>(p=>{if(p.Fraction is {} f){progress.IsIndeterminate=false;progress.Value=f;}status.Text=p.Label;});
        try
        {
            var outputs=await Operations.RunAsync(choice,inputs,destination,report,running.Token,media);
            library.Record(choice.Id=="convert"?"Convert to "+choice.Format!.ToUpperInvariant():choice.Title,outputs);
            lastFolder=destination;reveal.IsVisible=true;
            status.Text=outputs.Count==1?$"Saved {Path.GetFileName(outputs[0])}":$"Saved {outputs.Count} files";
        }
        catch(OperationCanceledException){status.Text="Cancelled. Nothing was saved.";}
        catch(Exception error){status.Text=error.Message;}
        finally{running.Dispose();running=null;progress.IsVisible=cancel.IsVisible=false;Refresh();}
    }

    /// One-choice dialog for tools whose only option is a small preset list. Null means cancelled.
    private async Task<T?> Ask<T>(string title,(string Label,T Value)[] options,int selected)where T:struct
    {
        T? result=null;var dialog=new Window{Title=title,SizeToContent=SizeToContent.WidthAndHeight,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var list=new ComboBox{ItemsSource=options.Select(o=>o.Label).ToArray(),SelectedIndex=selected,MinWidth=200};
        var ok=Primary("OK");ok.IsDefault=true;ok.Click+=(_,_)=>{result=options[list.SelectedIndex].Value;dialog.Close();};
        var close=new Button{Content="Cancel",IsCancel=true};close.Click+=(_,_)=>dialog.Close();
        dialog.Content=new StackPanel{Margin=new(20),Spacing=14,Children={Label(title),list,new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right,Children={close,ok}}}};
        dialog[!BackgroundProperty]=Resource("Paper");
        await dialog.ShowDialog(this);return result;
    }

    private void RefreshLibrary()
    {
        libraryItems.Children.Clear();
        foreach(var entry in library.Search(search.Text??""))
        {
            var available=entry.AvailablePaths;
            var use=new Button{Content="Use",IsEnabled=available.Length>0&&running is null};use.Click+=(_,_)=>{files.Clear();Add(available);};
            var folder=new Button{Content="Folder",IsEnabled=available.Length>0};folder.Click+=(_,_)=>Open(Path.GetDirectoryName(available[0])!);
            libraryItems.Children.Add(Card(new DockPanel{Children=
            {
                Dock(new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,VerticalAlignment=VerticalAlignment.Center,Children={use,folder}},Avalonia.Controls.Dock.Right),
                new StackPanel{Children={new TextBlock{Text=entry.Name,FontWeight=FontWeight.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis},Muted($"{entry.Action} · {entry.Created.LocalDateTime:g}{(available.Length==0?" · missing":"")}")}}
            }}));
        }
        if(libraryItems.Children.Count==0)libraryItems.Children.Add(Muted("Finished files appear here."));
    }

    private static void Open(string folder)
    {
        if(!Directory.Exists(folder))return;
        try{var start=new ProcessStartInfo("xdg-open"){UseShellExecute=false};start.ArgumentList.Add(folder);Process.Start(start)?.Dispose();}catch(Exception error)when(error is System.ComponentModel.Win32Exception or InvalidOperationException){}
    }

    private static TabItem Tab(string header,Control content)=>new(){Header=header,Content=new ScrollViewer{Content=content}};
    private static TextBlock Label(string text)=>new(){Text=text,FontWeight=FontWeight.SemiBold,FontSize=15};
    private static TextBlock Muted(string text){var block=new TextBlock{Text=text,FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis};block[!TextBlock.ForegroundProperty]=Resource("Muted");return block;}
    private static Button Primary(string text){var button=new Button{Content=text,Background=NashiTheme.Primary,Foreground=new SolidColorBrush(Color.Parse("#243B2B")),FontWeight=FontWeight.SemiBold};return button;}
    private static Border Card(Control child){var border=new Border{CornerRadius=new(16),Padding=new(18),BorderThickness=new(1),Child=child};border[!Border.BackgroundProperty]=Resource("Surface");border[!Border.BorderBrushProperty]=Resource("Line");return border;}
    private static T Dock<T>(T control,Dock side)where T:Control{DockPanel.SetDock(control,side);return control;}
    private static Avalonia.Data.IBinding Resource(string key)=>new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key);
}
