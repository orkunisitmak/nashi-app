using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Nashi.Core;

namespace Nashi.Linux;

public sealed class MainWindow : Window
{
    private readonly ObservableCollection<string> files=[];
    private readonly ListBox fileList=new(){MinHeight=180,SelectionMode=SelectionMode.Multiple};
    private readonly ComboBox action=new(){HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ComboBox format=new(){HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly StackPanel fields=new(){Spacing=8};
    private readonly Dictionary<string,TextBox> inputs=[];
    private readonly CheckBox metadata=new(){Content="Keep metadata",IsChecked=true};
    private readonly TextBox output=new(){PlaceholderText="Choose an output folder"};
    private readonly TextBlock status=new(){Text="Drop local files here or choose files to begin.",TextWrapping=TextWrapping.Wrap};
    private readonly ProgressBar progress=new(){Minimum=0,Maximum=1,Height=6};
    private readonly Button run=new(){Content="Export",HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly Button cancel=new(){Content="Cancel",IsEnabled=false};
    private readonly StackPanel selection=new(){Spacing=12};
    private readonly StackPanel libraryRows=new(){Spacing=8};
    private readonly LibraryStore library=new(Operations.DataDirectory);
    private readonly PreferencesStore preferences=new(Operations.DataDirectory);
    private readonly TabControl tabs=new();
    private readonly TextBox link=new(){PlaceholderText="https://…"};
    private readonly ComboBox downloadKind=new(){ItemsSource=new[]{"video","audio","thumbnail","photos","image"},SelectedIndex=0};
    private readonly Button download=new(){Content="Download"};
    private CancellationTokenSource? job;
    private bool closeWhenDone;
    private string[] lastOutputs=[];

    public MainWindow(string[] args)
    {
        Title="Nashi · Linux";Width=1060;Height=760;MinWidth=800;MinHeight=600;
        var iconStream=AssetLoader.Open(new Uri("avares://Nashi/Assets/NashiIcon.png"));
        using(iconStream)Icon=new WindowIcon(iconStream);
        RequestedThemeVariant=library.Appearance switch{"dark"=>ThemeVariant.Dark,"light"=>ThemeVariant.Light,_=>ThemeVariant.Default};
        Styles.Add(new Style(s=>s.OfType<Button>()){Setters={new Setter(Button.PaddingProperty,new Thickness(16,10)),new Setter(Button.CornerRadiusProperty,new CornerRadius(10))}});
        run.Background=new SolidColorBrush(Color.Parse("#E4BD59"));run.Foreground=Brushes.Black;
        fileList.ItemsSource=files;
        AutomationProperties.SetName(fileList,"Selected files");
        AutomationProperties.SetName(output,"Output folder");
        AutomationProperties.SetName(progress,"Export progress");
        AutomationProperties.SetName(link,"Public URL");
        fileList.ItemTemplate=new FuncDataTemplate<string>((path,_)=>new TextBlock
        {Text=Path.GetFileName(path),TextTrimming=TextTrimming.CharacterEllipsis});
        var title=new StackPanel{Orientation=Orientation.Horizontal,Spacing=16,Children={
            new Image{Source=new Bitmap(AssetLoader.Open(new Uri("avares://Nashi/Assets/NashiIcon.png"))),Width=56,Height=56},
            new StackPanel{Children={new TextBlock{Text="Nashi",FontSize=30,FontWeight=FontWeight.Bold},new TextBlock{Text="Your files, a little sweeter. · Linux preview",Opacity=.7}}}}};
        var home=new Grid{ColumnDefinitions=new ColumnDefinitions("*,20,*")};
        selection.Children.Add(new TextBlock{Text="Your files",FontSize=21,FontWeight=FontWeight.SemiBold});
        selection.Children.Add(new TextBlock{Text="Drop files into this window. Selection order is export order.",TextWrapping=TextWrapping.Wrap,Opacity=.7});
        selection.Children.Add(Row(Btn("Choose files…",ChooseFiles),Btn("Add folder…",ChooseFolder)));
        selection.Children.Add(fileList);
        selection.Children.Add(Row(Btn("Remove",()=>{foreach(var f in fileList.SelectedItems!.Cast<string>().ToArray())files.Remove(f);Refresh();}),
            Btn("Clear",()=>{files.Clear();Refresh();}),Btn("↑",()=>Move(-1)),Btn("↓",()=>Move(1))));
        selection.Children.Add(Btn("Preview selected",Preview));
        home.Children.Add(selection);
        var tools=new StackPanel{Spacing=12};Grid.SetColumn(tools,2);home.Children.Add(tools);
        tools.Children.Add(new TextBlock{Text="Make something useful",FontSize=21,FontWeight=FontWeight.SemiBold});
        tools.Children.Add(Label("Action",action));tools.Children.Add(Label("Output format",format));
        tools.Children.Add(fields);tools.Children.Add(metadata);tools.Children.Add(run);
        action.SelectionChanged+=(_,_)=>UpdateFields();
        run.Click+=async(_,_)=>await Export();
        tabs.ItemsSource=new[]{new TabItem{Header="Home",Content=new ScrollViewer{Content=home}},
            new TabItem{Header="Library",Content=BuildLibrary()},new TabItem{Header="Web download",Content=BuildDownloads()},
            new TabItem{Header="Settings & tools",Content=BuildSettings()}};
        var root=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto"),Margin=new Thickness(28),RowSpacing=20};
        root.Children.Add(title);Grid.SetRow(tabs,1);root.Children.Add(tabs);
        var footer=new StackPanel{Spacing=10};
        footer.Children.Add(Row(output,Btn("Output folder…",ChooseOutput)));
        footer.Children.Add(progress);footer.Children.Add(Row(cancel,Btn("Open output folder",OpenOutput)));
        footer.Children.Add(status);Grid.SetRow(footer,2);root.Children.Add(footer);Content=root;
        output.MinWidth=460;output.Text=preferences.Value.OutputFolder;
        cancel.Click+=(_,_)=>job?.Cancel();
        DragDrop.SetAllowDrop(this,true);
        AddHandler(DragDrop.DragOverEvent,(_,e)=>e.DragEffects=job is null&&e.DataTransfer.Contains(DataFormat.File)?DragDropEffects.Copy:DragDropEffects.None);
        AddHandler(DragDrop.DropEvent,(_,e)=>{if(job is null)AddFiles(e.DataTransfer.TryGetFiles()?.Select(f=>f.TryGetLocalPath()).OfType<string>()??[]);});
        KeyDown+=async(_,e)=>
        {
            if(e.KeyModifiers==KeyModifiers.Control&&e.Key==Key.O){e.Handled=true;await ChooseFiles();}
            else if(e.KeyModifiers==KeyModifiers.Control&&e.Key==Key.D1){tabs.SelectedIndex=0;e.Handled=true;}
            else if(e.KeyModifiers==KeyModifiers.Control&&e.Key==Key.D2){tabs.SelectedIndex=1;e.Handled=true;}
            else if(e.Key==Key.Escape&&job is not null){job.Cancel();e.Handled=true;}
        };
        Closing+=(_,e)=>{if(job is not null){e.Cancel=true;closeWhenDone=true;job.Cancel();status.Text="Cancelling before closing…";}};
        AddFiles(args.Where(a=>!a.StartsWith("--",StringComparison.Ordinal)));
        if(library.RecoveryNotice is {} notice)status.Text=notice;
        RefreshLibrary();
        if(args.Contains("--smoke-test"))Opened+=(_,_)=>Dispatcher.UIThread.Post(async()=>await Smoke());
    }
    private static StackPanel Row(params Control[] controls)=>new StackPanel(){Orientation=Orientation.Horizontal,Spacing=8,Children={}}.WithChildren(controls);
    private static StackPanel Label(string label,Control control)
    {
        AutomationProperties.SetName(control,label);
        return new(){Spacing=5,Children={new TextBlock{Text=label,Opacity=.75},control}};
    }
    private Button Btn(string label,Action action)
    {
        var button=new Button{Content=label};
        AutomationProperties.SetName(button,label switch{"↑"=>"Move selected file up","↓"=>"Move selected file down",_=>label});
        button.Click+=(_,_)=>{try{action();}catch(Exception e){status.Text=e.Message;}};return button;
    }
    private Button Btn(string label,Func<Task> action)
    {
        var button=new Button{Content=label};button.Click+=async(_,_)=>{try{await action();}catch(Exception e){status.Text=e.Message;}};return button;
    }
    private async Task ChooseFiles()
    {
        if(job is not null)return;
        var picked=await StorageProvider.OpenFilePickerAsync(new(){Title="Choose files",AllowMultiple=true});
        AddFiles(picked.Select(f=>f.TryGetLocalPath()).OfType<string>());
    }
    private async Task ChooseFolder()
    {
        if(job is not null)return;
        var picked=await StorageProvider.OpenFolderPickerAsync(new(){Title="Add a folder to ZIP"});
        AddFiles(picked.Select(f=>f.TryGetLocalPath()).OfType<string>());
    }
    private async Task ChooseOutput()
    {
        var picked=await StorageProvider.OpenFolderPickerAsync(new(){Title="Choose output folder"});
        if(picked.FirstOrDefault()?.TryGetLocalPath() is {} path)
        {
            output.Text=path;
            preferences.Save(preferences.Value with{OutputFolder=path,OutputMode="folder"});
        }
    }
    private void AddFiles(IEnumerable<string> paths)
    {
        foreach(var path in paths)
        {
            var full=Path.GetFullPath(path);
            if((File.Exists(full)||Directory.Exists(full))&&!files.Contains(full))files.Add(full);
        }
        Refresh();tabs.SelectedIndex=0;
    }
    private void Move(int offset)
    {
        if(fileList.SelectedItem is not string file)return;
        var index=files.IndexOf(file);if(index+offset<0||index+offset>=files.Count)return;
        files.Move(index,index+offset);fileList.SelectedItem=file;
    }
    private void Refresh()
    {
        var old=(action.SelectedItem as ActionChoice)?.Id;
        var choices=Operations.Choices(files);action.ItemsSource=choices;
        action.SelectedItem=choices.FirstOrDefault(c=>c.Id==old)??choices.FirstOrDefault();
        format.ItemsSource=Operations.Formats(files);format.SelectedIndex=0;
        run.IsEnabled=choices.Length>0;UpdateFields();
    }
    private void Field(string key,string caption,string value)
    {
        var box=new TextBox{Text=value};inputs[key]=box;fields.Children.Add(Label(caption,box));
    }
    private void UpdateFields()
    {
        fields.Children.Clear();inputs.Clear();var id=(action.SelectedItem as ActionChoice)?.Id??"";
        var images=files.Count>0&&files.All(FeatureCatalog.IsImage);
        var previous=format.SelectedItem as string;
        var formats=images&&id!="convert"?RasterEngine.Formats:Operations.Formats(files);
        format.ItemsSource=formats;format.SelectedItem=formats.Contains(previous)?previous:formats.FirstOrDefault();
        format.IsVisible=id=="convert"||images&&id is not ("zip" or "createPDF");
        var media=files.Any(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p));
        metadata.IsVisible=id is not ("removeMetadata" or "zip")&&(images||media);
        if((images||media)&&id is "convert" or "editImage" or "compress")Field("quality","Quality (1–100)","85");
        if(id is "resize" or "crop")
        {
            Field("width",id=="crop"?"Width in pixels":"Width in pixels (blank keeps aspect ratio)","");Field("height","Height in pixels","");
            if(id=="crop"){Field("x","Left (pixels)","0");Field("y","Top (pixels)","0");}
        }
        if(id=="rotate")Field("rotation","Clockwise degrees (90, 180, 270)","90");
        if(id is "trimVideo" or "trimAudio" or "videoSnapshots")
        {
            Field("start","Start / snapshot time (seconds)","0");
            if(id!="videoSnapshots")Field("end","End (seconds, blank = full duration)","");
        }
        if(id=="changeVideoSpeed")Field("speed","Speed multiplier", "1");
        if(id=="splitVideo")Field("speed","Split interval (seconds)","30");
        if(id=="audioChannels")Field("channels","Channels (1 = mono, 2 = stereo)","2");
        if(id is "splitPDF" or "organizePDF")
        {
            Field("pages","Pages in order (e.g. 3, 1-2; blank = all)","");
            if(id=="splitPDF")Field("count","Pages per output file","1");
        }
        if(files.Any(p=>FeatureCatalog.Extension(p)=="pdf"||FeatureCatalog.IsArchive(p)))
        {Field("password","Password (if needed)","");inputs["password"].PasswordChar='●';}
        if(id=="editImage")
        {
            Field("exposure","Exposure (stops, −5 to 5)","0");Field("brightness","Brightness (−1 to 1)","0");
            Field("contrast","Contrast (0 to 3)","1");Field("saturation","Saturation (0 to 3)","1");
        }
    }
    private string Text(string key,string fallback="")=>inputs.TryGetValue(key,out var box)?box.Text??"":fallback;
    private double Number(string key,double fallback)
    {
        var text=Text(key);if(string.IsNullOrWhiteSpace(text))return fallback;
        if(!double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value)&&!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!double.IsFinite(value))
            throw new ArgumentException("Enter a valid number for "+key+".");return value;
    }
    private int Integer(string key,int fallback)
    {
        var n=Number(key,fallback);if(n!=Math.Truncate(n)||n<int.MinValue||n>int.MaxValue)throw new ArgumentException("Enter a whole number for "+key+".");return (int)n;
    }
    private ExportOptions Options()=>new()
    {
        Format=format.SelectedItem as string??"png",Width=Text("width").Length==0?null:Integer("width",0),Height=Text("height").Length==0?null:Integer("height",0),
        X=Integer("x",0),Y=Integer("y",0),Rotation=Integer("rotation",90),Quality=Integer("quality",85),PreserveMetadata=metadata.IsChecked==true,
        Start=Number("start",0),End=Text("end").Length==0?null:Number("end",0),Speed=Number("speed",1),Channels=Integer("channels",2),
        Pages=Text("pages"),PagesPerFile=Integer("count",1),Password=Text("password"),
        Adjustments=new(){Exposure=Bound("exposure",0,-5,5),Brightness=Bound("brightness",0,-1,1),Contrast=Bound("contrast",1,0,3),Saturation=Bound("saturation",1,0,3)}
    };
    private double Bound(string key,double fallback,double min,double max)
    {var n=Number(key,fallback);if(n<min||n>max)throw new ArgumentException($"{key}: choose {min} to {max}.");return n;}
    private string Destination()
    {
        if(string.IsNullOrWhiteSpace(output.Text)||!Path.IsPathFullyQualified(output.Text))throw new ArgumentException("Choose an absolute output folder first.");
        return output.Text;
    }
    private async Task Export()
    {
        if(job is not null)return;
        try
        {
            var id=(action.SelectedItem as ActionChoice)?.Id??throw new ArgumentException("Choose files and an action.");
            var options=Options();var destination=Destination();var snapshot=files.ToArray();
            await Execute(id,(p,t)=>Operations.RunAsync(id,snapshot,destination,options,p,t));
        }
        catch(Exception e){status.Text=e.Message;}
    }
    private async Task Execute(string name,Func<IProgress<OperationProgress>,CancellationToken,Task<IReadOnlyList<string>>> operation)
    {
        if(job is not null)return;
        job=new();var token=job.Token;selection.IsEnabled=false;action.IsEnabled=false;fields.IsEnabled=false;format.IsEnabled=false;
        run.IsEnabled=false;download.IsEnabled=false;cancel.IsEnabled=true;progress.IsIndeterminate=true;status.Text="Working…";
        var report=new Progress<OperationProgress>(p=>{progress.IsIndeterminate=p.Fraction is null;progress.Value=p.Fraction??0;status.Text=p.Label;});
        try
        {
            lastOutputs=(await Task.Run(()=>operation(report,token),token)).ToArray();
            status.Text=$"Saved {lastOutputs.Length} output(s): "+string.Join(", ",lastOutputs.Select(Path.GetFileName));
            try{library.Record(name,lastOutputs);RefreshLibrary();}catch(Exception e){status.Text+=" · History could not be saved: "+e.Message;}
        }
        catch(OperationCanceledException){status.Text="Cancelled. No partial export was kept.";}
        catch(Exception e){status.Text=e.Message;}
        finally
        {
            job.Dispose();job=null;selection.IsEnabled=true;action.IsEnabled=true;fields.IsEnabled=true;format.IsEnabled=true;
            run.IsEnabled=files.Count>0;download.IsEnabled=true;cancel.IsEnabled=false;progress.IsIndeterminate=false;progress.Value=0;
            if(closeWhenDone)Close();
        }
    }
    private Control BuildLibrary()
    {
        var search=new TextBox{PlaceholderText="Search recent exports"};
        AutomationProperties.SetName(search,"Search recent exports");
        search.TextChanged+=(_,_)=>RefreshLibrary(search.Text??"");
        return new StackPanel{Spacing=12,Children={new TextBlock{Text="Your last 50 exports",FontSize=22},search,new ScrollViewer{Content=libraryRows,MaxHeight=420}}};
    }
    private void RefreshLibrary(string query="")
    {
        libraryRows.Children.Clear();
        foreach(var entry in library.Search(query))
        {
            var copy=entry;
            libraryRows.Children.Add(new Border{Padding=new Thickness(12),CornerRadius=new CornerRadius(10),BorderBrush=Brushes.Gray,BorderThickness=new Thickness(.5),Child=
                new StackPanel{Spacing=6,Children={new TextBlock{Text=entry.Name,TextWrapping=TextWrapping.Wrap},
                    new TextBlock{Text=$"{entry.Action} · {entry.Created.LocalDateTime:g} · {entry.AvailablePaths.Length}/{entry.Paths.Length} available",Opacity=.6},
                    Btn("Use files",()=>{if(job is null){AddFiles(copy.AvailablePaths);}})}}});
        }
        if(libraryRows.Children.Count==0)libraryRows.Children.Add(new TextBlock{Text="No exports yet.",Opacity=.6});
    }
    private Control BuildDownloads()
    {
        download.Click+=async(_,_)=>
        {
            try
            {
                var destination=Destination();var url=link.Text??"";var kind=downloadKind.SelectedItem as string??"video";
                WebDownload.Validate(url);
                await Execute("Web download",(p,t)=>new WebDownload(new Toolchain(Path.Combine(Operations.DataDirectory,"tools"))).DownloadAsync(url,kind,destination,p,t));
            }
            catch(Exception e){status.Text=e.Message;}
        };
        return new StackPanel{Spacing=16,MaxWidth=640,HorizontalAlignment=HorizontalAlignment.Left,Children={
            new TextBlock{Text="Public web media",FontSize=22},
            new TextBlock{Text="Download public video, audio, thumbnails or images. Some sites restrict access. No browser cookies are imported.",TextWrapping=TextWrapping.Wrap},
            Label("Public URL",link),Label("Download type",downloadKind),download}};
    }
    private Control BuildSettings()
    {
        var appearance=new ComboBox{ItemsSource=new[]{"system","light","dark"},SelectedItem=library.Appearance};
        appearance.SelectionChanged+=(_,_)=>
        {
            try{var value=appearance.SelectedItem as string??"system";library.SetAppearance(value);RequestedThemeVariant=value switch{"light"=>ThemeVariant.Light,"dark"=>ThemeVariant.Dark,_=>ThemeVariant.Default};}
            catch(Exception e){status.Text=e.Message;}
        };
        var text=new TextBlock{TextWrapping=TextWrapping.Wrap};
        void Tools()=>text.Text=string.Join("\n",new[]{"ffmpeg","ffprobe","yt-dlp","deno","libreoffice"}.Select(n=>$"{n}: {SystemTools.Find(n)??"not installed"}"));
        Tools();
        return new StackPanel{Spacing=16,MaxWidth=680,HorizontalAlignment=HorizontalAlignment.Left,Children={
            new TextBlock{Text="Settings & tools",FontSize=22},Label("Appearance",appearance),text,Btn("Refresh tools",()=>{Tools();Refresh();}),
            new TextBlock{Text="Install optional tools through your distribution’s package manager. Image, PDF, archive and subtitle tools work without them. FFmpeg enables media exports; LibreOffice enables Office conversions; yt-dlp and Deno enable web media downloads.",TextWrapping=TextWrapping.Wrap},
            new TextBlock{Text="Ctrl+O: choose files · Ctrl+1/2: Home/Library · Escape: cancel\nLinux preview uses in-app file drop and native file dialogs. Global drag wheels, system window control, tray, automatic updates and full graphical editors are not included yet.",TextWrapping=TextWrapping.Wrap,Opacity=.7}}};
    }
    private async Task Preview()
    {
        if(fileList.SelectedItem is not string path||!File.Exists(path))return;
        if(!FeatureCatalog.IsImage(path)){status.Text="In-app preview is available for images.";return;}
        var pixels=await Task.Run(()=>PhotoCodec.Png(path));using var memory=new MemoryStream(pixels);using var bitmap=new Bitmap(memory);
        var preview=new Window{Title=Path.GetFileName(path),Width=700,Height=520,Content=new Image{Source=bitmap,Stretch=Stretch.Uniform}};
        await preview.ShowDialog(this);
    }
    private async Task OpenOutput()
    {
        var path=lastOutputs.FirstOrDefault() is {} last?Path.GetDirectoryName(last)!:Destination();
        await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }
    private async Task Smoke()
    {
        try
        {
            var directory=Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY")??throw new InvalidOperationException("Set NASHI_SMOKE_DIRECTORY.");
            Directory.CreateDirectory(directory);File.Delete(Path.Combine(directory,"smoke.json"));output.Text=Path.Combine(directory,"exports");
            var fixture=Path.Combine(directory,"synthetic.png");
            using(var image=new ImageMagick.MagickImage(ImageMagick.MagickColors.Gold,64,32))image.Write(fixture);
            AddFiles([fixture]);action.SelectedItem=((IEnumerable<ActionChoice>)action.ItemsSource!).Single(a=>a.Id=="resize");
            inputs["width"].Text="16";format.SelectedItem="png";
            await Export();
            if(lastOutputs.Length!=1)throw new InvalidOperationException("UI export failed: "+status.Text);
            using(var check=PhotoCodec.Read(lastOutputs[0]))if(check.Width!=16||check.Height!=8)throw new InvalidOperationException("UI dimensions were not applied.");
            foreach(var theme in new[]{ThemeVariant.Light,ThemeVariant.Dark})
            {
                RequestedThemeVariant=theme;await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Render);
                using var bitmap=new RenderTargetBitmap(new PixelSize((int)Bounds.Width,(int)Bounds.Height));bitmap.Render(this);
                bitmap.Save(Path.Combine(directory,$"home-{theme.Key}.png"), new PngBitmapEncoderOptions());
            }
            await File.WriteAllTextAsync(Path.Combine(directory,"smoke.json"),"{\"passed\":true,\"checks\":[\"native-window\",\"file-selection\",\"resize-controls\",\"export\",\"light-dark-render\"]}");
            Close();
        }
        catch(Exception e)
        {
            Console.Error.WriteLine(e);
            if(Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)desktop.Shutdown(1);
            else Close();
        }
    }
}
internal static class PanelExtensions
{
    public static StackPanel WithChildren(this StackPanel panel,IEnumerable<Control> children)
    {foreach(var child in children)panel.Children.Add(child);return panel;}
}
