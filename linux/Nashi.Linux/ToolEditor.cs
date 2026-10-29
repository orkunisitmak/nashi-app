using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Nashi.Core;
using Nashi.Windows;
namespace Nashi.Linux;

public sealed partial class ToolEditor:Window
{
    private readonly Dictionary<string,Func<object?>> values=[];
    private readonly List<string> inputOrder=[];
    public string[] OrderedInputs=>inputOrder.ToArray();
    private readonly TextBlock error=NashiTheme.Text("",13);
    private readonly string action;
    private ImageSurface? surface;
    private ComboBox? cropAspect;
    private ComboBox? annotationTool;
    private bool cropSync;
    private bool editorReady;
    private MediaPreview? mediaPreview;
    private readonly CancellationTokenSource lifetime=new();
    private Task? processedTask;
    private Task? mediaClosing;
    private bool mediaCloseReady;
    private LinuxOptions? pendingDialogResult;
    private readonly List<Task> photoTasks=[];
    private CancellationTokenSource? photoPreviewRun;
    private byte[]? originalPhoto;
    private CheckBox? originalView;
    internal bool PhotoPreviewBusy=>photoClock.IsEnabled||photoTasks.Any(t=>!t.IsCompleted);
    private readonly DispatcherTimer photoClock=new(){Interval=TimeSpan.FromMilliseconds(300)};
    private long photoGeneration;
    private bool showingOriginal;
    private string? processedDirectory;
    private readonly Dictionary<string,TextBox> textFields=[];
    private readonly StackPanel fields=new(){Spacing=10};
    public ToolEditor(string action,string? format,string[] inputs,LinuxEngine engine,PreferencesStore? preferences=null)
    {
        this.action=action;inputOrder.AddRange(inputs);Title=(action=="convert"?"Convert to "+format?.ToUpperInvariant():FeatureCatalog.ToolNames.GetValueOrDefault(action,action))+" · Nashi";
        Width=1000;Height=740;MinWidth=720;MinHeight=520;FontFamily=new Avalonia.Media.FontFamily("DejaVu Sans");Background=NashiTheme.Paper;Foreground=NashiTheme.Ink;
        var root=new DockPanel{Margin=new(28)};var heading=new StackPanel{Spacing=8,Margin=new(0,0,0,22)};heading.Children.Add(NashiTheme.Text(Title[..^8],26));heading.Children.Add(NashiTheme.Text(Path.GetFileName(inputs[0])+" · Original stays safe",13,true));DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var footer=new StackPanel{Spacing=8,Margin=new(0,18,0,0)};footer.Children.Add(error);var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};
        buttons.Children.Add(NashiTheme.Button("Save new copy",Save,true));buttons.Children.Add(NashiTheme.Button("Reset",()=>Reset()));buttons.Children.Add(NashiTheme.Button("Cancel",()=>Finish(null)));footer.Children.Add(buttons);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var body=new Grid{ColumnDefinitions=new("3*,2*")};var preview=new StackPanel{Spacing=14,Margin=new(0,0,24,0)};body.Children.Add(new ScrollViewer{Content=preview});
        if(FeatureCatalog.IsImage(inputs[0]))
        {
            try{originalPhoto=PhotoCodec.Png(inputs[0]);surface=new ImageSurface(originalPhoto,action,UpdateRegionFields);surface.DrawingError+=message=>error.Text=message;preview.Children.Add(surface);
                if(action=="cropImage"){
                    preview.Children.Add(NashiTheme.Text("Aspect ratio",13));var ratios=cropAspect=new ComboBox{ItemsSource=new[]{"Free","1:1","16:9","9:16","4:3","3:4"},SelectedIndex=0};
                    ratios.SelectionChanged+=(_,_)=>{var text=ratios.SelectedItem as string;if(text is null||text=="Free")surface.CropRatio=null;else{var parts=text.Split(':');surface.CropRatio=double.Parse(parts[0],CultureInfo.InvariantCulture)/double.Parse(parts[1],CultureInfo.InvariantCulture);}};preview.Children.Add(ratios);
                }
                if(action=="annotateImage"){
                    var toolsPanel=new StackPanel{Spacing=8};fields.Children.Add(toolsPanel);toolsPanel.Children.Add(NashiTheme.Text("Drawing tool",13));
                    var tool=annotationTool=new ComboBox{ItemsSource=new[]{"Marker","Rectangle","Arrow","Line","Ellipse","Text","Number","Callout","Magnifier","Image","Freehand","Select / Move"},SelectedIndex=0};tool.SelectionChanged+=(_,_)=>surface.DrawingTool=(string)tool.SelectedItem!;toolsPanel.Children.Add(tool);
                    var editing=new WrapPanel();editing.Children.Add(NashiTheme.Button("Undo",surface.Undo));editing.Children.Add(NashiTheme.Button("Redo",surface.Redo));editing.Children.Add(NashiTheme.Button("Delete selected",surface.DeleteSelected));editing.Children.Add(NashiTheme.Button("Clear",surface.ClearMarks));toolsPanel.Children.Add(editing);
                    toolsPanel.Children.Add(NashiTheme.Button("Choose annotation image",async()=>{var picked=await StorageProvider.OpenFilePickerAsync(new(){AllowMultiple=false});if(picked.FirstOrDefault()?.TryGetLocalPath() is {} path){surface.InsertImage=path;tool.SelectedItem="Image";}}));
                    var marks=new ListBox{Name="Annotations",MaxHeight=100};toolsPanel.Children.Add(marks);var updating=false;
                    void RefreshMarks(){updating=true;try{marks.ItemsSource=surface.Annotations.Select((mark,index)=>$"{index+1}. {mark.Kind}").ToArray();marks.SelectedIndex=surface.SelectedAnnotation;}finally{updating=false;}}
                    surface.SceneChanged+=RefreshMarks;marks.SelectionChanged+=(_,_)=>{if(updating||marks.SelectedIndex<0)return;var index=marks.SelectedIndex;tool.SelectedItem="Select / Move";surface.SelectAnnotation(index);};RefreshMarks();
                }
                if(action=="redactImage")BuildRedactionControls(inputs[0]);
                if(action is "cropImage" or "redactImage" or "annotateImage")preview.Children.Add(NashiTheme.Text(action=="cropImage"?"Drag inside to move, handles to resize, or outside to start again. Shift locks the ratio.":action=="annotateImage"?"Draw with the selected tool. Use Select / Move to drag marks or resize their handles. Ctrl+Z undoes; Ctrl+Shift+Z redoes.":"Drag outside existing areas to add. Drag an area to move it, or its handles to resize. Delete removes the selected area.",12,true));
            }catch(Exception ex){preview.Children.Add(NashiTheme.Text(ex.Message));}
        }
        else if(FeatureCatalog.IsVideo(inputs[0])||FeatureCatalog.IsAudio(inputs[0])){mediaPreview=new MediaPreview(inputs[0],GetNumber,Set);preview.Children.Add(mediaPreview);}
        else preview.Children.Add(NashiTheme.Text(inputs.Length==1?Path.GetFileName(inputs[0]):$"{inputs.Length} files",22));
        foreach(var input in inputs.Skip(1))preview.Children.Add(NashiTheme.Text(Path.GetFileName(input),13,true));
        preview.Children.Add(NashiTheme.Button("Open original preview",async()=>{try{await MainWindow.OpenPath(inputs[0]);}catch(Exception ex){error.Text=ex.Message;}}));
        var options=new ScrollViewer{Content=fields};Grid.SetColumn(options,1);body.Children.Add(options);root.Children.Add(body);Content=root;
        if(inputs.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p)))BuildMedia();else BuildFile(inputs,format);
        if(preferences is not null&&action=="convert"&&format is not null)ConfigureProfile(preferences,format,inputs);
        if(surface is not null){if(textFields.TryGetValue("Width",out var width)&&string.IsNullOrEmpty(width.Text))width.Text=surface.PixelWidth.ToString(CultureInfo.InvariantCulture);if(textFields.TryGetValue("Height",out var height)&&string.IsNullOrEmpty(height.Text))height.Text=surface.PixelHeight.ToString(CultureInfo.InvariantCulture);}
        if(mediaPreview is not null&&action is "normalizeAudio" or "audioChannels" or "redactAudio" or "redactVideo" or "splitVideo" or "audioToVideo"){
            preview.Children.Add(NashiTheme.Button("Original",()=>mediaPreview.Load(inputs[0])));
            var processed=NashiTheme.Button("Preview processed result",()=>{});processed.Click+=(_,_)=>processedTask=PreviewProcessed(inputs[0],engine,processed);preview.Children.Add(processed);
        }
        Closing+=async(_,e)=>{
            if(mediaPreview is null||mediaCloseReady)return;e.Cancel=true;if(mediaClosing is not null)return;
            IsEnabled=false;lifetime.Cancel();mediaClosing=mediaPreview.ShutdownAsync();
            try{await mediaClosing;await Task.Yield();mediaCloseReady=true;Close(pendingDialogResult);}
            catch(Exception failure){mediaClosing=null;IsEnabled=true;error.Text="Media player could not close: "+failure.Message;}
        };
        Closed+=async(_,_)=>{lifetime.Cancel();photoClock.Stop();try{if(processedTask is not null)await processedTask;if(photoTasks.Count>0)await Task.WhenAll(photoTasks.ToArray());if(detectionTask is not null)await detectionTask;if(processedDirectory is not null&&Directory.Exists(processedDirectory))Directory.Delete(processedDirectory,true);}finally{surface?.DisposeArtwork();}};
        if(inputs.Length>1){
            fields.Children.Add(NashiTheme.Text("File order",16));var order=new ListBox{ItemsSource=inputOrder.Select(Path.GetFileName).ToArray(),SelectedIndex=0,MaxHeight=150};fields.Children.Add(order);
            void Move(int delta){var from=order.SelectedIndex;var to=from+delta;if(from<0||to<0||to>=inputOrder.Count)return;var item=inputOrder[from];inputOrder.RemoveAt(from);inputOrder.Insert(to,item);order.ItemsSource=inputOrder.Select(Path.GetFileName).ToArray();order.SelectedIndex=to;QueuePhoto();}
            var sorting=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};sorting.Children.Add(NashiTheme.Button("Move up",()=>Move(-1)));sorting.Children.Add(NashiTheme.Button("Move down",()=>Move(1)));fields.Children.Add(sorting);
        }
        if(surface is not null&&action is "editImage" or "frameImage" or "createCollage" or "redactImage"){
            photoClock.Tick+=(_,_)=>{photoClock.Stop();photoTasks.RemoveAll(t=>t.IsCompletedSuccessfully);photoTasks.Add(RefreshPhoto(engine));};
            if(action=="redactImage")surface.SceneChanged+=QueuePhoto;
            foreach(var child in fields.Children){if(child is TextBox text)text.TextChanged+=(_,_)=>QueuePhoto();if(child is ComboBox choice)choice.SelectionChanged+=(_,_)=>QueuePhoto();if(child is CheckBox toggle)toggle.IsCheckedChanged+=(_,_)=>QueuePhoto();}
            var original=originalView=new CheckBox{Name="OriginalView",Content="Show original",Foreground=NashiTheme.Ink};original.IsCheckedChanged+=(_,_)=>{showingOriginal=original.IsChecked==true;surface.ShowOriginal(showingOriginal);if(showingOriginal){photoGeneration++;photoClock.Stop();photoPreviewRun?.Cancel();surface.SetPreview(originalPhoto!);}else QueuePhoto();};if(action=="redactImage")preview.Children.Insert(0,original);else preview.Children.Add(original);Opened+=(_,_)=>QueuePhoto();
        }
        if(fields.Children.Count==0)fields.Children.Add(NashiTheme.Text("Ready to make a fresh copy. No extra settings needed.",14,true));
        KeyDown+=(_,e)=>{if(mediaPreview?.Handle(e)==true){e.Handled=true;return;}if(e.Key==Key.Escape)Finish(null);if(e.Key==Key.Enter&&FocusManager?.GetFocusedElement() is not TextBox)Save();};
        editorReady=true;
    }
    private void QueuePhoto(){if(showingOriginal||lifetime.IsCancellationRequested)return;photoGeneration++;photoPreviewRun?.Cancel();photoClock.Stop();photoClock.Start();}
    private async Task RefreshPhoto(LinuxEngine engine)
    {
        var generation=photoGeneration;var work=Path.Combine(Path.GetTempPath(),"NashiPhotoPreview-"+Guid.NewGuid().ToString("N"));
        using var refresh=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);photoPreviewRun=refresh;var token=refresh.Token;
        try{
            var options=(LinuxOptions)Fill(new LinuxOptions());var inputs=OrderedInputs;byte[] pixels;
            if(action=="redactImage")options=options with{Regions=surface!.Regions};
            if(action=="redactImage"&&options.Regions.Length==0)pixels=originalPhoto!;
            else{
                if(action=="redactImage")inputs=inputs.Take(1).ToArray();
                var output=await Task.Run(()=>engine.RunAsync(action,inputs,work,options,token:token),token);
                pixels=await Task.Run(()=>PhotoCodec.Png(output[0]),token);
            }
            if(!token.IsCancellationRequested&&!showingOriginal&&generation==photoGeneration){surface?.SetPreview(pixels);error.Text="";}
        }catch(OperationCanceledException){}catch(Exception ex){if(generation==photoGeneration)error.Text="Preview: "+ex.Message;}
        finally{if(ReferenceEquals(photoPreviewRun,refresh))photoPreviewRun=null;if(Directory.Exists(work))Directory.Delete(work,true);}
    }
    private async Task PreviewProcessed(string input,LinuxEngine engine,Button button)
    {
        if(!button.IsEnabled)return;button.IsEnabled=false;
        try{
            var options=(LinuxOptions)Fill(new LinuxOptions());if(action=="splitVideo")options=options with{Media=options.Media with{JoinSplitClips=true}};
            processedDirectory??=Path.Combine(Path.GetTempPath(),"NashiProcessed-"+Guid.NewGuid().ToString("N"));
            var outputs=await Task.Run(()=>engine.RunAsync(action,[input],processedDirectory,options,token:lifetime.Token));if(!lifetime.IsCancellationRequested)mediaPreview?.Load(outputs[0]);
        }catch(OperationCanceledException){}catch(Exception ex){error.Text="Preview unavailable: "+ex.Message;}finally{button.IsEnabled=true;}
    }
    private void BuildFile(string[] inputs,string? format)
    {
        if(FeatureCatalog.IsImage(inputs[0]))
        {
            if(action is "editImage" or "cropImage" or "redactImage")
            {Number("Width",null);Number("Height",null);}
            if(action is "cropImage" or "redactImage"){Number("X",0);Number("Y",0);}
            if(action=="editImage")
            {Number("Rotation",0);Toggle("FlipHorizontal",false);Toggle("FlipVertical",false);BuildObject("Adjustments",new PhotoAdjustments());}
            if(action=="frameImage")BuildObject("Frame",new FrameOptions());
            if(action=="createCollage")BuildObject("Collage",new CollageOptions());
            if(action=="annotateImage"){Text("Text","Note");Text("Ink","#FF5200");Text("Font","system");Number("StrokeWidth",3);Number("FontSize",24);}
            if(action=="convert")Toggle("PreserveImageMetadata",false);
            if(action is "compress" or "convert"){Number("Quality",85);if(action=="compress"){Number("TargetBytes",null);Number("ReductionPercent",null);}}
        }
        if(FeatureCatalog.Extension(inputs[0])=="pdf"||action=="convert"&&!FeatureCatalog.IsImage(inputs[0]))
        {
            Text("Password","");
            if(action=="organizePDF"||action=="splitPDF")Text("PageRanges","");
            if(action=="splitPDF")Number("PagesPerFile",1);
            if(action=="convert")BuildObject("Document",new DocumentOptions());
            if(action=="compress"){Number("Quality",85);Number("TargetBytes",null);Number("ReductionPercent",null);}
        }
        if(action=="removeMetadata"){Toggle("RemoveAllMetadata",true);Text("Metadata","");}
        if(action=="extractArchive")Text("Password","");
    }
    private void BuildMedia()
    {
        // Bind the same options model used by Windows; changing the catalog cannot create a second engine.
        var names=action switch
        {
            "trimVideo" or "trimAudio"=>new[]{"Start","End"},
            "cropVideo"=>new[]{"Width","Height","Regions"},
            "changeVideoSpeed"=>new[]{"Speed"},
            "normalizeAudio"=>new[]{"TargetLoudness","LoudnessRange","TruePeak","TrimSilence"},
            "audioChannels"=>new[]{"Channels","LeftVolume","RightVolume"},
            "videoSnapshots"=>new[]{"SnapshotTimes"},
            "splitVideo"=>new[]{"SplitPoints","EqualClips","SplitInterval","DiscardedClips","JoinSplitClips"},
            "audioToVideo"=>new[]{"Width","Height","Visualization","VisualizationImage"},
            "redactVideo"=>new[]{"Regions","RedactionMode"},
            "redactAudio"=>new[]{"AudioRedactions"},
            "removeMetadata"=>new[]{"RemoveAllMetadata","Metadata"},
            "convert"=>new[]{"Quality","AudioBitrate","SampleDepth","GifFrameRate","GifWidth","Width","Height","PreserveMetadata"},
            "compress"=>new[]{"Quality","AudioBitrate","TargetBytes","ReductionPercent","Width","Height"},
            _=>Array.Empty<string>()
        };
        BuildObject("Media",new MediaOptions(),names);
    }
    private void BuildObject(string prefix,object defaults,string[]? only=null)
    {
        foreach(var property in defaults.GetType().GetProperties().Where(p=>(only is null||only.Contains(p.Name))&&!(prefix=="Collage"&&p.Name=="Order")))
        {
            var name=prefix+"."+property.Name;var value=property.GetValue(defaults);var type=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;
            if(type==typeof(bool))Toggle(name,value as bool? ?? false);
            else if(type==typeof(string)||type.IsArray||type.IsGenericType)Text(name,Display(value));
            else Number(name,value);
        }
    }
    private static string Display(object? value)=>value switch
    {
        double[] a=>string.Join(", ",a.Select(v=>v.ToString(CultureInfo.InvariantCulture))),int[] a=>string.Join(", ",a),
        MediaRegion[] a=>string.Join("; ",a.Select(v=>$"{v.X},{v.Y},{v.Width},{v.Height},{v.Start},{v.End}")),
        MediaRange[] a=>string.Join("; ",a.Select(v=>$"{v.Start},{v.End}")),Dictionary<string,string> a=>string.Join('\n',a.Select(k=>k.Key+"="+k.Value)),
        _=>Convert.ToString(value,CultureInfo.InvariantCulture)??""
    };
    private void Text(string name,string initial)
    {
        var choices=name.Split('.')[^1] switch{
            "Font"=>new[]{"system","rounded","serif","monospaced"},"Backdrop"=>new[]{"solid","gradient","image"},"Layout"=>name.StartsWith("Collage.",StringComparison.Ordinal)?new[]{"grid","row","column","featured"}:new[]{"editable","preserve"},
            "EpubLayout"=>new[]{"reflowable","fixed"},"RedactionStyle"=>new[]{"solid","blur","pixelate"},"RedactionMode"=>new[]{"solid","blur","pixelate"},
            "SampleDepth"=>new[]{"matchSource","pcm16","pcm24","pcm32","float32"},"Visualization"=>new[]{"waveform","spectrum","image"},_=>Array.Empty<string>()};
        if(choices.Length>0){fields.Children.Add(NashiTheme.Text(Label(name),13));var choice=new ComboBox{Name=name.Replace(".","_",StringComparison.Ordinal),ItemsSource=choices,SelectedItem=initial,Tag=initial};fields.Children.Add(choice);values[name]=()=>choice.SelectedItem as string??initial;if(name=="Font"&&surface is not null){surface.AnnotationFont=initial;choice.SelectionChanged+=(_,_)=>surface.AnnotationFont=choice.SelectedItem as string??"system";}if(name=="RedactionStyle"&&surface is not null){surface.RedactionStyle=initial;choice.SelectionChanged+=(_,_)=>surface.RedactionStyle=choice.SelectedItem as string??"solid";}return;}

        fields.Children.Add(NashiTheme.Text(Label(name),13));var input=new TextBox{Name=name.Replace(".","_",StringComparison.Ordinal),Text=initial,PlaceholderText=Hint(name)};fields.Children.Add(input);values[name]=()=>input.Text??"";input.Tag=initial;textFields[name]=input;
        if(name.EndsWith("BackgroundImage",StringComparison.Ordinal)||name.EndsWith("VisualizationImage",StringComparison.Ordinal))fields.Children.Add(NashiTheme.Button("Choose image",async()=>{var picked=await StorageProvider.OpenFilePickerAsync(new(){AllowMultiple=false});if(picked.FirstOrDefault()?.TryGetLocalPath()is {} path)input.Text=path;}));
        if(surface is not null){
            void UpdateStyle(){if(name=="Ink")surface.Ink=input.Text??"";if(name=="Text")surface.AnnotationText=input.Text??"";if(name=="FontSize")surface.FontSize=double.TryParse(input.Text,CultureInfo.InvariantCulture,out var size)?size:double.NaN;if(name=="StrokeWidth")surface.StrokeWidth=double.TryParse(input.Text,CultureInfo.InvariantCulture,out var width)?width:double.NaN;}
            UpdateStyle();input.PropertyChanged+=(_,e)=>{if(e.Property==TextBox.TextProperty)UpdateStyle();};
        }
        if((action is "cropImage" or "redactImage")&&(name is "X" or "Y" or "Width" or "Height"))input.PropertyChanged+=(_,e)=>{if(e.Property==TextBox.TextProperty&&editorReady&&!cropSync)UpdateCropFromFields(name);};
    }
    private void UpdateRegionFields(Rect rect)
    {
        cropSync=true;
        try{Set("X",Math.Floor(rect.X+1e-7));Set("Y",Math.Floor(rect.Y+1e-7));Set("Width",Math.Floor(rect.Width+1e-7));Set("Height",Math.Floor(rect.Height+1e-7));}
        finally{cropSync=false;}
    }
    private void UpdateCropFromFields(string name)
    {
        if(surface is null||GetNumber("X") is not {} x||GetNumber("Y") is not {} y||GetNumber("Width") is not {} w||GetNumber("Height") is not {} h)return;
        if(action=="cropImage"&&surface.CropRatio is {} ratio){if(name=="Width")h=w/ratio;if(name=="Height")w=h*ratio;}
        if(!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(w)||!double.IsFinite(h)||x<0||y<0||w<1||h<1||x+w>surface.PixelWidth||y+h>surface.PixelHeight){error.Text="Choose an area inside the image.";return;}
        error.Text="";
        if(action=="cropImage")surface.SetCrop(new(x,y,w,h));else surface.SetRegionBounds(new(x,y,w,h));
    }
    private double? GetNumber(string name)=>textFields.TryGetValue(name,out var input)&&double.TryParse(input.Text,CultureInfo.InvariantCulture,out var value)?value:null;
    private void Set(string name,double value){if(textFields.TryGetValue(name,out var input))input.Text=value.ToString(CultureInfo.InvariantCulture);}
    private void Number(string name,object? initial)
    {Text(name,Display(initial));}
    private void Toggle(string name,bool initial)
    {
        var input=new CheckBox{Name=name.Replace(".","_",StringComparison.Ordinal),Content=Label(name),IsChecked=initial,Foreground=NashiTheme.Ink,Tag=initial};fields.Children.Add(input);values[name]=()=>input.IsChecked??false;
    }
    private static string Label(string name)=>Regex.Replace(name.Split('.')[^1],"(?<=[a-z])([A-Z])"," $1") switch
    {"Target Bytes"=>"Target size (bytes)","Page Ranges"=>"Pages in order (e.g. 1-3, 5)","Quality"=>name.StartsWith("Media.",StringComparison.Ordinal)?"Quality (0–1)":"Quality (%)",var label=>label};
    private static string Hint(string name)=>name.Split('.')[^1] switch
    {"SnapshotTimes" or "SplitPoints" or "DiscardedClips"=>"Comma-separated values", "AudioRedactions"=>"Start,end; start,end (seconds)","Regions"=>"X,Y,width,height,start,end; …", "Metadata"=>"One tag=value per line",_=>""};
    private void Reset()
    {if(processedTask is {IsCompleted:false}){error.Text="Wait for the processed preview to finish before resetting.";return;}mediaPreview?.Reset();detectionRun?.Cancel();if(originalView is not null)originalView.IsChecked=false;foreach(var child in fields.Children){if(child is TextBox t)t.Text=t.Tag as string;if(child is CheckBox c)c.IsChecked=c.Tag as bool?;if(child is ComboBox choice)choice.SelectedItem=choice.Tag;}if(cropAspect is not null)cropAspect.SelectedIndex=0;if(annotationTool is not null)annotationTool.SelectedIndex=0;surface?.Reset();error.Text="";}
    private object Fill(object target,string prefix="")
    {
        foreach(var p in target.GetType().GetProperties())
        {
            var name=prefix+p.Name;
            if(p.PropertyType==typeof(MediaOptions)||p.PropertyType==typeof(DocumentOptions)||p.PropertyType==typeof(PhotoAdjustments)||p.PropertyType==typeof(FrameOptions)||p.PropertyType==typeof(CollageOptions))
            {p.SetValue(target,Fill(p.GetValue(target)!,name+"."));continue;}
            if(!values.TryGetValue(name,out var get))continue;var value=get();var text=value as string??"";var type=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;
            if(type==typeof(bool))p.SetValue(target,value);
            else if(type==typeof(string))p.SetValue(target,text);
            else if(type==typeof(Dictionary<string,string>))p.SetValue(target,text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(l=>l.Split('=',2)).ToDictionary(l=>l[0],l=>l.Length==2?l[1]:""));
            else if(type==typeof(MediaRegion[]))p.SetValue(target,Rows(text).Select(v=>{if(v.Length<4||v.Length>6)throw new FormatException("A region needs X,Y,width,height,start,end.");return new MediaRegion(v[0],v[1],v[2],v[3],v.Length>4?v[4]:0,v.Length>5?v[5]:null);}).ToArray());
            else if(type==typeof(MediaRange[]))p.SetValue(target,Rows(text).Select(v=>{if(v.Length!=2)throw new FormatException("A range needs start,end.");return new MediaRange(v[0],v[1]);}).ToArray());
            else if(type==typeof(double[]))p.SetValue(target,Numbers(text));
            else if(type==typeof(int[]))p.SetValue(target,text.Length==0&&p.GetValue(target)is null?null:Numbers(text).Select(v=>checked((int)v)).ToArray());
            else if(text.Length==0&&Nullable.GetUnderlyingType(p.PropertyType)is not null)p.SetValue(target,null);
            else p.SetValue(target,Convert.ChangeType(text,type,CultureInfo.InvariantCulture));
        }
        return target;
    }
    private static double[] Numbers(string text)=>text.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
    private static IEnumerable<double[]> Rows(string text)=>text.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(Numbers);
    private void Finish(LinuxOptions? result){pendingDialogResult=result;Close(result);}
    private void Save()
    {
        try{var result=(LinuxOptions)Fill(new LinuxOptions());if(action=="redactImage"&&surface is not null){if(surface.Regions.Length==0)throw new ArgumentException("Draw or detect at least one redaction area before saving.");if(result.X<0||result.Y<0||result.Width is null or <1||result.Height is null or <1||(long)result.X+result.Width>surface.PixelWidth||(long)result.Y+result.Height>surface.PixelHeight)throw new ArgumentException("Choose an area inside the image.");result=result with{Regions=surface.Regions};}if(action=="annotateImage"&&surface is not null)result=result with{AnnotatedPng=surface.Png()};Finish(result);}
        catch(Exception ex)when(ex is ArgumentException or FormatException or OverflowException or TargetInvocationException){error.Text="Check your options: "+ex.Message;}
    }
}
