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
    private readonly PreferencesStore preferences=new(LinuxPaths.DataDirectory);
    private readonly WrapPanel actions=new();
    private readonly TextBlock actionsLabel=Label("File actions");
    private readonly WebPanel web=new();
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
        string? Value(string flag)=>args.SkipWhile(a=>a!=flag).Skip(1).FirstOrDefault();
        var capture=Value("--capture");var dialog=Value("--dialog");var tab=Value("--tab");
        Add(args.Where(a=>!a.StartsWith('-')&&a!=capture&&a!=dialog&&a!=tab).Select(Path.GetFullPath));
        if(tab is not null&&Content is TabControl pages)pages.SelectedIndex=Math.Max(0,Array.IndexOf(["home","web","library","settings"],tab));Refresh();RefreshLibrary();
        if(capture is not null)Opened+=async(_,_)=>await Capture(Path.GetFullPath(capture),args.Contains("--dark")?"dark":args.Contains("--light")?"light":null,dialog,args.ToArray());
    }

    /// Render check without a visible desktop (a locked session or CI): saves the window and exits.
    /// With --dialog, an editor is opened on the first file with a sample edit and captured instead.
    private async Task Capture(string file,string? appearance,string? dialog,string[] args)
    {
        if(appearance is not null)NashiTheme.Apply(Application.Current!,appearance);
        await Task.Delay(800);
        try{if(dialog is null)Snap(this,file);else if(await CaptureDialog.OpenAsync(dialog,files) is {} editor){editor.Show(this);await Task.Delay(args.Contains("--slow")?5000:1500);Snap(editor,file);editor.Close();}}
        catch(Exception error){Console.Error.WriteLine(error);}
        Close();
    }
    private static void Snap(Window window,string file){var size=new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height);using var bitmap=new RenderTargetBitmap(size);bitmap.Render(window);bitmap.Save(file);}

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
        Button Act(string text,Func<Task> action){var b=new Button{Content=text,Margin=new(0,0,8,8)};b.Click+=async(_,_)=>await action();return b;}
        actions.Children.Add(Act("Copy",CopyFiles));actions.Children.Add(Act("Move to…",MoveFiles));actions.Children.Add(Act("Copy path",async()=>{if(Clipboard is not null){await Clipboard.SetTextAsync(FileActions.PathList(files));status.Text=files.Count==1?"Path copied.":$"{files.Count} paths copied.";}}));
        actions.Children.Add(Act("Open terminal here",()=>{status.Text=FileActions.Terminal(files[0])?"":"No terminal app was found.";return Task.CompletedTask;}));
        var home=new StackPanel{Spacing=14,Margin=new(20),Children={drop,fileList,formatsLabel,formats,toolsLabel,tools,actionsLabel,actions,progress,
            new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={cancel,reveal}},status}};
        web.Run=(title,work)=>RunWork(title,work,preferences.Value.DownloadFolder);
        var libraryPage=new StackPanel{Spacing=12,Margin=new(20),Children={search,libraryItems}};
        var appearance=new ComboBox{ItemsSource=new[]{"System","Light","Dark"},SelectedIndex=library.Appearance switch{"light"=>1,"dark"=>2,_=>0},MinWidth=120};
        appearance.SelectionChanged+=(_,_)=>{var value=new[]{"system","light","dark"}[appearance.SelectedIndex];library.SetAppearance(value);NashiTheme.Apply(Application.Current!,value);};
        var settings=new StackPanel{Spacing=12,Margin=new(20),Children={Label("Appearance"),appearance}};
        Settings(settings);
        var tabs=new TabControl{Items={Tab("Home",home),Tab("Web",web),Tab("Library",libraryPage),Tab("Settings",settings)}};
        tabs.SelectionChanged+=(_,_)=>{if(tabs.SelectedIndex==2)RefreshLibrary();};
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
        hint.Text=files.Count==0?"Images, videos, audio, PDFs, documents, spreadsheets, slides, subtitles and archives. Drop files here, choose them or paste with Ctrl+V.":"Pick a format or a tool below. Originals are never overwritten.";
        var ready=running is null&&files.Count>0;
        var formatChoices=ready?Operations.Formats(files):[];var toolChoices=ready?Operations.Tools(files):[];
        formats.Children.Clear();foreach(var format in formatChoices)formats.Children.Add(Action(format.ToUpperInvariant(),new("convert",format.ToUpperInvariant(),format)));
        tools.Children.Clear();foreach(var tool in toolChoices)tools.Children.Add(Action(tool.Title,tool));
        formatsLabel.IsVisible=formats.IsVisible=formatChoices.Length>0;toolsLabel.IsVisible=tools.IsVisible=toolChoices.Length>0;actionsLabel.IsVisible=actions.IsVisible=ready;
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
        MediaOptions? media=null;ImageEdit? edit=null;PhotoEdit? photo=null;FrameOptions? frame=null;CollageOptions? collage=null;Redaction? redact=null;PdfOrganization? organize=null;Annotation? annotate=null;DocumentOptions? document=null;MetadataEdit? metadata=null;
        if(choice.Id=="removeMetadata")
        {
            try{if(await EditMetadata(inputs) is not {} picked)return;metadata=picked.RemoveAll&&inputs.Length>1?null:picked;}
            catch(Exception error)when(error is IOException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException){status.Text=error.Message;return;}
        }
        if(choice.Id=="convert"&&choice.Format is "docx" or "pages" or "epub"&&inputs.All(f=>FeatureCatalog.Extension(f)=="pdf"))
        {
            // As on Windows: rebuild editable text, or keep each page's exact look as a picture.
            if(await Ask("PDF to "+choice.Title,[("Editable text and images",0),("Keep the exact look (page pictures)",1)],0) is not {} layout)return;
            document=new(Layout:layout==1?"preserve":"editable");
        }
        if(choice.Id=="convert"&&choice.Format=="epub")
        {
            if(await Ask("EPUB layout",[("Reflowable (text adapts to the reader)",0),("Fixed pages",1)],0) is not {} epub)return;
            document=(document??new())with{EpubLayout=epub==1?"fixed":"reflowable"};
        }
        if(choice.Id=="annotateImage")
        {
            AnnotateDialog editor;try{editor=await AnnotateDialog.CreateAsync(choice.Title,inputs[0]);}catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or ImageMagick.MagickException){status.Text=error.Message;return;}
            await editor.ShowDialog(this);if(editor.Result is not {} marks)return;annotate=marks;
        }
        if(choice.Id=="organizePDF")
        {
            PdfOrganizeDialog editor;status.Text="Reading pages…";try{editor=await PdfOrganizeDialog.CreateAsync(choice.Title,inputs[0]);}catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or DllNotFoundException){status.Text=error.Message;return;}
            status.Text="";await editor.ShowDialog(this);if(editor.Result is not {} result)return;organize=result;
        }
        if(choice.Id=="frameImage")
        {
            FrameDialog editor;try{editor=await FrameDialog.CreateAsync(choice.Title,inputs[0]);}catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or ImageMagick.MagickException){status.Text=error.Message;return;}
            await editor.ShowDialog(this);if(!editor.Accepted)return;frame=editor.Result;
        }
        else if(choice.Id=="createCollage")
        {
            CollageDialog editor;try{editor=await CollageDialog.CreateAsync(choice.Title,inputs);}catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or ImageMagick.MagickException){status.Text=error.Message;return;}
            await editor.ShowDialog(this);if(!editor.Accepted)return;collage=editor.Result;
        }
        if(choice.Id=="editImage")
        {
            PhotoDialog editor;try{editor=await PhotoDialog.CreateAsync(choice.Title,inputs[0]);}catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or ImageMagick.MagickException){status.Text=error.Message;return;}
            await editor.ShowDialog(this);if(editor.Result is not {} result)return;photo=result;
        }
        if(choice.Id=="cropImage")
        {
            CropDialog crop;try{crop=await CropDialog.CreateAsync(choice.Title,inputs[0]);}catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or ImageMagick.MagickException){status.Text=error.Message;return;}
            await crop.ShowDialog(this);if(crop.Result is not {} selection)return;edit=selection;
        }
        if(choice.Id=="redactImage")
        {
            RedactDialog editor;try{editor=await RedactDialog.CreateAsync(choice.Title,inputs[0]);}catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or ImageMagick.MagickException){status.Text=error.Message;return;}
            await editor.ShowDialog(this);if(editor.Result is not {} areas)return;redact=areas;
        }
        else if(choice.Id is "cropVideo" or "redactVideo")
        {
            // Boxes are drawn on a frame at 10% of the clip and sent to FFmpeg in video pixels.
            MediaInfo info;byte[] picture;status.Text="Reading a frame…";
            try{info=await Operations.Media!.InspectAsync(inputs[0]);if(info.Video is null)throw new ArgumentException("This file has no video stream.");picture=await Operations.VideoFrameAsync(inputs[0],info.Duration*.1);}
            catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or InvalidOperationException){status.Text=error.Message;return;}
            status.Text="";double w=info.Video.Width??0,h=info.Video.Height??0;if(w<2||h<2){status.Text="Could not read the video size.";return;}
            MediaRegion Region(ImageEdit a){var x=Math.Floor(a.X*w);var y=Math.Floor(a.Y*h);return new(x,y,Math.Clamp(Math.Round(a.Width*w),2,w-x),Math.Clamp(Math.Round(a.Height*h),2,h-y));}
            if(choice.Id=="cropVideo")
            {
                CropDialog crop;try{crop=await CropDialog.CreateAsync(choice.Title,inputs[0],picture);}catch(Exception error)when(error is ArgumentException or ImageMagick.MagickException){status.Text=error.Message;return;}
                await crop.ShowDialog(this);if(crop.Result is not {} selection)return;media=new(){Regions=[Region(selection)]};
            }
            else
            {
                RedactDialog editor;try{editor=await RedactDialog.CreateAsync(choice.Title,inputs[0],picture);}catch(Exception error)when(error is ArgumentException or ImageMagick.MagickException){status.Text=error.Message;return;}
                await editor.ShowDialog(this);if(editor.Result is not {} areas)return;media=new(){Regions=areas.Areas.Select(Region).ToArray(),RedactionMode=areas.Style};
            }
        }
        else if(choice.Id=="changeVideoSpeed"){if(await Ask(choice.Title,[("0.5×",.5),("1.5×",1.5),("2×",2d)],1) is not {} speed)return;media=new(){Speed=speed};}
        else if(choice.Id=="audioChannels"){if(await Ask(choice.Title,[("Mono",1),("Stereo",2)],0) is not {} channels)return;media=new(){Channels=channels};}
        else if(choice.Id=="splitVideo"){if(await Ask(choice.Title,[("2 equal parts",2),("3 equal parts",3),("4 equal parts",4)],0) is not {} parts)return;media=new(){EqualClips=parts};}
        else if(choice.Id=="audioToVideo"){string[] styles=["waveform","spectrum","bars"];if(await Ask(choice.Title,[("Waveform",0),("Spectrum",1),("Bars",2)],0) is not {} style)return;media=new(){Visualization=styles[style]};}
        else if(choice.Id is "videoSnapshots" or "trimVideo" or "trimAudio" or "redactAudio")
        {
            MediaInfo info;try{info=await Operations.Media!.InspectAsync(inputs[0]);}catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException){status.Text=error.Message;return;}
            if(choice.Id=="videoSnapshots")
            {
                if(await Ask(choice.Title,[("First frame",0),("Middle frame",1),("5 frames across the video",2)],0) is not {} pick)return;var d=info.Duration;
                media=new(){SnapshotTimes=pick switch{1=>[d/2],2=>[d*.1,d*.3,d*.5,d*.7,d*.9],_=>[0]}};
            }
            else if(choice.Id=="redactAudio"){var bleep=new BleepDialog(choice.Title,inputs[0],info);await bleep.ShowDialog(this);if(bleep.Result is not {} picked)return;media=new(){AudioRedactions=picked.Ranges,RedactionMode=picked.Mode};}
            else{var trim=new TrimDialog(choice.Title,inputs[0],info);await trim.ShowDialog(this);if(trim.Result is not {} range)return;media=new(){Start=range.Start,End=range.End};}
        }
        var settings=new ToolSettings(media,edit,photo,frame,collage,redact,organize,annotate,document,metadata);
        await RunWork(choice.Id=="convert"?"Convert to "+choice.Format!.ToUpperInvariant():choice.Title,(destination,report,token)=>Operations.RunAsync(choice,inputs,destination,report,token,settings),Destination(inputs));
    }
    /// Settings → Output: next to the original (default) or one chosen folder.
    private string Destination(IReadOnlyList<string> inputs)=>preferences.Value.OutputMode=="folder"&&Directory.Exists(preferences.Value.OutputFolder)?preferences.Value.OutputFolder:Path.GetDirectoryName(inputs[0])!;

    /// Shared by tools, conversions, moves and downloads: progress, Cancel, Library and "Show in folder".
    private async Task RunWork(string title,Func<string,IProgress<OperationProgress>,CancellationToken,Task<IReadOnlyList<string>>> work,string destination)
    {
        if(running is not null)return;
        running=new CancellationTokenSource();progress.IsVisible=cancel.IsVisible=true;progress.IsIndeterminate=true;reveal.IsVisible=false;status.Text=title+"…";web.Status=title+"…";Refresh();
        var report=new Progress<OperationProgress>(p=>{if(p.Fraction is {} f){progress.IsIndeterminate=false;progress.Value=f;}status.Text=p.Label;web.Status=p.Label;});
        try
        {
            Directory.CreateDirectory(destination);
            var outputs=await work(destination,report,running.Token);
            library.Record(title,outputs);
            lastFolder=destination;reveal.IsVisible=true;
            status.Text=outputs.Count==1?$"Saved {Path.GetFileName(outputs[0])}":$"Saved {outputs.Count} files";web.Status=status.Text+" in "+destination;
        }
        catch(OperationCanceledException){status.Text="Cancelled. Nothing was saved.";web.Status=status.Text;}
        catch(Exception error){status.Text=error.Message;web.Status=error.Message;}
        finally{running.Dispose();running=null;progress.IsVisible=cancel.IsVisible=false;Refresh();}
    }

    private async Task CopyFiles()
    {
        if(Clipboard is null||files.Count==0)return;
        // GNOME Files, Dolphin and Thunar paste these as file copies; other apps get the paths as text.
        var transfer=new DataTransfer();
        foreach(var path in files)if((Directory.Exists(path)?await StorageProvider.TryGetFolderFromPathAsync(path) as IStorageItem:await StorageProvider.TryGetFileFromPathAsync(path)) is {} item)transfer.Add(DataTransferItem.CreateFile(item));
        var extra=new DataTransferItem();extra.Set(DataFormat.CreateStringPlatformFormat("x-special/gnome-copied-files"),"copy\n"+FileActions.UriList(files).Replace("\r\n","\n"));extra.SetText(FileActions.PathList(files));transfer.Add(extra);
        await Clipboard.SetDataAsync(transfer);status.Text=files.Count==1?"Copied. Paste it in your file manager.":$"{files.Count} files copied. Paste them in your file manager.";
    }
    private async Task MoveFiles()
    {
        if(files.Count==0)return;
        var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title="Move to…",AllowMultiple=false});
        if(folders.FirstOrDefault()?.TryGetLocalPath() is not {} target)return;
        var inputs=files.ToArray();
        await RunWork("Move",async(destination,report,token)=>{var moved=await FileActions.MoveAsync(inputs,destination,report,token);files.Clear();files.AddRange(moved);return moved;},target);
    }

    private void Settings(StackPanel page)
    {
        var output=new ComboBox{ItemsSource=new[]{"Next to the original","In one folder"},SelectedIndex=preferences.Value.OutputMode=="folder"?1:0,MinWidth=200};
        var outputFolder=Muted(preferences.Value.OutputFolder.Length>0?preferences.Value.OutputFolder:"No folder chosen");
        async Task<string?> PickFolder(string title)=>(await StorageProvider.OpenFolderPickerAsync(new(){Title=title,AllowMultiple=false})).FirstOrDefault()?.TryGetLocalPath();
        var chooseOutput=new Button{Content="Choose folder…"};
        chooseOutput.Click+=async(_,_)=>{if(await PickFolder("Save results to…") is {} folder){Save(preferences.Value with{OutputFolder=folder,OutputMode="folder"});outputFolder.Text=folder;output.SelectedIndex=1;}};
        output.SelectionChanged+=async(_,_)=>
        {
            if(output.SelectedIndex==0){Save(preferences.Value with{OutputMode="beside"});return;}
            if(!Directory.Exists(preferences.Value.OutputFolder)){if(await PickFolder("Save results to…") is {} folder){Save(preferences.Value with{OutputFolder=folder});outputFolder.Text=folder;}else{output.SelectedIndex=0;return;}}
            Save(preferences.Value with{OutputMode="folder"});
        };
        var downloads=Muted(preferences.Value.DownloadFolder);var chooseDownloads=new Button{Content="Change…"};
        chooseDownloads.Click+=async(_,_)=>{if(await PickFolder("Save downloaded media to…") is {} folder){Save(preferences.Value with{DownloadFolder=folder});downloads.Text=folder;}};
        var toolsState=new TextBlock{TextWrapping=TextWrapping.Wrap};
        var setup=new Button{Content="Set up media tools"};
        void Describe()
        {
            string Found(bool ok,string yes,string no)=>ok?"✓ "+yes:"– "+no;
            var tools=Operations.Toolset;
            toolsState.Text=string.Join("\n",
                Found(Operations.Media is not null,"FFmpeg (media tools)","FFmpeg: choose Set up media tools, or install ffmpeg"),
                Found(tools.Ready,"yt-dlp and Deno (web downloads, Pages)","yt-dlp and Deno: choose Set up media tools"),
                Found(Operations.Office is not null,"LibreOffice (documents, spreadsheets, slides)","LibreOffice: install libreoffice with your package manager"),
                Found(Operations.Exif.Executable is not null,"ExifTool (metadata fields)",Operations.Executable("perl") is null?"ExifTool needs perl":"ExifTool: offered the first time you edit metadata"),
                Found(Operations.Executable("tesseract") is not null,"Tesseract (text in scanned PDFs)","Tesseract: install tesseract and a language pack for scanned PDFs"));
            setup.IsVisible=!tools.Ready;
        }
        setup.Click+=async(_,_)=>
        {
            if(await Ask("Set up media tools",[("Download FFmpeg, yt-dlp and Deno from their official releases (SHA-256 verified)",1),("Not now",0)],0) is not 1)return;
            await RunWork("Set up media tools",async(_,report,token)=>{await Operations.Toolset.InstallAsync(report,token);return [];},Path.Combine(LinuxPaths.DataDirectory,"tools"));Describe();
        };
        Describe();
        page.Children.Add(Label("Output"));page.Children.Add(new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={output,chooseOutput}});page.Children.Add(outputFolder);
        page.Children.Add(Label("Downloads"));page.Children.Add(new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={chooseDownloads,downloads}});
        page.Children.Add(Label("Tools"));page.Children.Add(toolsState);page.Children.Add(setup);
        page.Children.Add(Muted($"Library and settings: {LinuxPaths.DataDirectory}"));
        page.Children.Add(Muted($"Nashi {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)} · Linux preview"));
    }
    private void Save(Preferences value){try{preferences.Save(value);}catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException){status.Text=error.Message;}}

    /// As on Windows: one file opens the editor, several files have all metadata removed.
    /// Images and PDFs use ExifTool, offered once as a verified download; without it only removal is possible.
    private async Task<MetadataEdit?> EditMetadata(string[] inputs)
    {
        var all=new MetadataEdit(true,new Dictionary<string,string>());
        if(Operations.IsMediaOnly(inputs))
        {
            if(inputs.Length>1)return all;
            status.Text="Reading tags…";var values=await MediaMetadata.Read(Operations.Toolset,inputs[0]);status.Text="";
            var editor=new MetadataDialog(values,true);await editor.ShowDialog(this);return editor.Result;
        }
        var exif=Operations.Exif;
        if(exif.Executable is null)
        {
            if(Operations.Executable("perl") is null)return all;
            if(await Ask("Metadata",[("Download ExifTool (~11 MB) to edit fields",1),("Only remove all metadata",0)],0) is not {} choice)return null;
            if(choice==0)return all;
            running=new CancellationTokenSource();progress.IsVisible=cancel.IsVisible=true;progress.IsIndeterminate=true;
            try{await exif.InstallAsync(new Progress<OperationProgress>(p=>{if(p.Fraction is {} f){progress.IsIndeterminate=false;progress.Value=f;}status.Text=p.Label;}),running.Token);}
            catch(OperationCanceledException){status.Text="Cancelled.";return null;}
            finally{running.Dispose();running=null;progress.IsVisible=cancel.IsVisible=false;}
        }
        if(inputs.Length>1)return all;
        status.Text="Reading tags…";var tags=await exif.ReadAsync(inputs[0]);status.Text="";
        var dialog=new MetadataDialog(tags,false);await dialog.ShowDialog(this);return dialog.Result;
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
