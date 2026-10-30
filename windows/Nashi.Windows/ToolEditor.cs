using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Nashi.Core;
namespace Nashi.Windows;

public sealed partial class ToolEditor:Window
{
    public MediaOptions MediaOptions{get;private set;}=new();public ImageOptions ImageOptions{get;private set;}=new();public string Format{get;private set;}="png";public int[] Pages{get;private set;}=[];
    private readonly string tool;private readonly BitmapSource? bitmap;private readonly Dictionary<string,TextBox> fields=[];private readonly Dictionary<string,string> defaults=[];
    private readonly ComboBox format=new();private readonly ComboBox drawingTool=new(){ItemsSource=new[]{"Marker","Rectangle"},SelectedIndex=0};
    private readonly Canvas drawing=new(){Background=Brushes.Transparent,Focusable=true};private Rectangle? selection,activeRectangle;private Polyline? activeStroke;private Point origin;private bool dragging;
    private readonly MediaElement player=new(){LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,Stretch=Stretch.Uniform,Height=260};private bool playing;
    private readonly Slider scrubber=new(){Minimum=0,Margin=new(4,12,4,8),ToolTip="Preview position"};
    private readonly StackPanel previewStack=new();
    private readonly RangeTimeline timeline=new();private readonly TextBlock time=new(){Margin=new(4,6,4,10)};private double duration=1;
    internal double PreviewSeconds=>player.Position.TotalSeconds;
    internal bool Playing=>playing;
    internal RangeTimeline Timeline=>timeline;
    internal TextBox FieldForTest(string name)=>fields[name];
    internal string SplitSummaryForTest=>splitSummary?.Text??"";
    internal bool AcceptForTest()=>ReadSettings(false);
    internal string UIAuditInputPath=>input;
    private readonly string input;private readonly MediaInfo? media;private readonly StackPanel options=new(){Margin=new(18,0,0,0)};
    public ToolEditor(Window owner,string tool,string title,string input,MediaInfo? media=null,BitmapSource? preview=null,IReadOnlyList<string>? inputs=null,string[]? outputFormats=null)
    {
        Theme.Attach(this);
        Owner=owner;this.tool=tool;this.input=input;this.media=media;bitmap=preview;Title=title+" · Nashi";Width=1060;Height=780;MinWidth=800;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var grid=new Grid{Margin=new(28)};grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=grid;
        var heading=new StackPanel();heading.Children.Add(L10n.Label(new TextBlock{Text=title,FontSize=30,FontWeight=FontWeights.Bold}));heading.Children.Add(new TextBlock{Text=Path.GetFileName(input)+" · "+L10n.Text("Your original stays safe. Save creates new files."),Foreground=Theme.Brush("Muted"),Margin=new(0,5,0,22)});grid.Children.Add(heading);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new(1.6,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});Grid.SetRow(body,1);grid.Children.Add(body);
        previewStack.Children.Add(L10n.Label(new TextBlock{Text="PREVIEW",FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Theme.Brush("Muted"),Margin=new(4,0,0,16)}));options.Children.Add(L10n.Label(new TextBlock{Text="MAKE IT YOURS",FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Theme.Brush("Muted"),Margin=new(4,0,0,16)}));body.Children.Add(new ScrollViewer{Content=previewStack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Grid.SetColumn(options,1);body.Children.Add(new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Grid.SetColumn(body.Children[^1],1);
        if(bitmap is not null)
        {
            drawing.Width=bitmap.PixelWidth;drawing.Height=bitmap.PixelHeight;
            drawing.Children.Add(new Image{Source=bitmap,Width=bitmap.PixelWidth,Height=bitmap.PixelHeight});
            UIElement visual=drawing;if(media is not null){var layered=new Grid{Width=bitmap.PixelWidth,Height=bitmap.PixelHeight};player.Width=bitmap.PixelWidth;player.Height=bitmap.PixelHeight;player.Source=new Uri(input);layered.Children.Add(player);layered.Children.Add(drawing);visual=layered;((Image)drawing.Children[0]).Opacity=0;}
            previewStack.Children.Add(ZoomablePreview(visual));
            drawing.MouseLeftButtonDown+=BeginDraw;drawing.MouseMove+=MoveDraw;drawing.MouseLeftButtonUp+=EndDraw;
        }
        else if(media is not null)
        {
            player.Source=new Uri(input);previewStack.Children.Add(player);player.MediaOpened+=(_,_)=>UsePlayerDuration();
        }
        else previewStack.Children.Add(L10n.Label(new TextBlock{Text="Your files will be saved as new copies.\nChoose options, then save.",FontSize=21,TextWrapping=TextWrapping.Wrap,Margin=new(0,50,20,0)}));
        duration=media?.Duration??1;
        if(media is not null)
        {
            player.ScrubbingEnabled=true;player.Loaded+=(_,_)=>{player.Play();player.Pause();};player.MediaFailed+=async(_,_)=>await CreatePreviewProxy();
            if(tool is "trimVideo" or "trimAudio" or "redactAudio")
            {
                timeline.Set(duration,0,duration);previewStack.Children.Add(timeline);timeline.Changed+=(a,b)=>{var valid=double.TryParse(fields["Start"].Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var previousStart)&&double.IsFinite(previousStart);fields["Start"].Text=Number(a);fields["End"].Text=Number(b);Seek(!valid||Math.Abs(a-previousStart)>.0001?a:b);};
            }
            var controls=new StackPanel{Orientation=Orientation.Horizontal};var play=MainWindow.Button("▶ / Ⅱ",TogglePlayback);controls.Children.Add(MainWindow.Button("−1s",()=>Seek(player.Position.TotalSeconds-1)));controls.Children.Add(play);controls.Children.Add(MainWindow.Button("+1s",()=>Seek(player.Position.TotalSeconds+1)));previewStack.Children.Add(controls);previewStack.Children.Add(time);
            if((media.Video is null&&tool is "normalizeAudio" or "audioChannels" or "redactAudio")||tool is "redactVideo" or "splitVideo" or "audioToVideo")
            {
                var comparison=new StackPanel{Orientation=Orientation.Horizontal};comparison.Children.Add(MainWindow.Button("Original",PlayOriginalPreview));processedButton=MainWindow.Button(tool=="splitVideo"?"Play Result":tool=="audioToVideo"?"Preview Visualization":"Preview Processed",async()=>await PreviewProcessed());comparison.Children.Add(processedButton);previewStack.Children.Add(comparison);
            }
            scrubber.Maximum=duration;scrubber.ValueChanged+=(_,_)=>{if(scrubber.IsMouseCaptureWithin||scrubber.IsKeyboardFocusWithin)Seek(scrubber.Value);};previewStack.Children.Add(scrubber);
            if(tool is "trimAudio" or "trimVideo" or "redactAudio")previewStack.Children.Add(MainWindow.Button("Play selected range",PlayRange));
            previewStack.Children.Add(L10n.Label(new TextBlock{Text="Space play/pause · ←/→ 1s · Shift+←/→ 1 frame (video), 0.1s (audio) · I/O set range · Enter save",TextWrapping=TextWrapping.Wrap,Foreground=Theme.Brush("Muted")}));
        }
        if(tool=="convert")
        {
            var formats=outputFormats?.ToHashSet()??(media is not null?(media.Video is null?MediaEngine.AudioFormats:MediaEngine.VideoFormats.Concat(MediaEngine.AudioFormats).ToHashSet()):ImageEngine.Formats.ToHashSet());
            options.Children.Add(L10n.Label(new TextBlock{Text="Output format",FontWeight=FontWeights.SemiBold}));format.ItemsSource=formats.Order().ToArray();format.SelectedIndex=0;options.Children.Add(format);
        }
        if(tool is "trimVideo" or "trimAudio" or "redactAudio"){Field("Start",0);Field("End",duration);}
        if(tool=="resize"){Field("Width",bitmap?.PixelWidth??1280);Field("Height",bitmap?.PixelHeight??720);}
        if(tool=="compress")Field("Quality (%)",80);
        if(tool=="changeVideoSpeed"){Field("Speed",1);fields["Speed"].TextChanged+=(_,_)=>{try{player.SpeedRatio=Math.Clamp(Value("Speed",1),.05,32);}catch(ArgumentException){}};}
        if(tool=="splitVideo")Field("Number of clips",2);
        if(tool=="videoSnapshots")TextField("Times (seconds)","0");
        if(tool=="normalizeAudio"){Field("Target LUFS",-16);Field("True peak dB",-1.5);}
        if(tool=="audioChannels"){Field("Channels",2);Field("Left volume",1);Field("Right volume",1);}
        if(tool=="audioToVideo"){Field("Width",1280);Field("Height",720);}
        if(tool=="organizePDF")TextField("Page numbers","1");
        if(tool=="rotate")Field("Degrees",90);
        if(tool is "crop" or "cropVideo" or "redactVideo" or "redactImage")
        {
            options.Children.Add(new TextBlock{Text=L10n.Text(tool=="redactVideo"?"Drag a region to cover with a solid block.":"Drag to select a crop. Hold Shift for a square; existing selections keep their ratio. Press Enter to save."),TextWrapping=TextWrapping.Wrap,Margin=new(4,4,4,16)});
            var w=bitmap?.PixelWidth??media?.Video?.Width??1280;var h=bitmap?.PixelHeight??media?.Video?.Height??720;
            Field("X",0);Field("Y",0);Field("Width",w);Field("Height",h);
            if(tool=="redactVideo"){Field("Start",0);Field("End",duration);}
        }
        if(tool=="annotate")
        {
            drawingTool.ItemsSource=new[]{"Marker","Rectangle","Arrow","Line","Ellipse","Text","Number","Callout","Magnifier","Image","Freehand","Select / Move"};drawingTool.SelectedIndex=0;
            options.Children.Add(L10n.Label(new TextBlock{Text="Drawing tool"}));options.Children.Add(drawingTool);TextField("Annotation text",L10n.Text("Note"));TextField("Ink color","#FF5200");Field("Stroke width",3);Field("Font size",24);options.Children.Add(L10n.Label(new TextBlock{Text="Drag to draw. One active tool per stroke.",TextWrapping=TextWrapping.Wrap,Margin=new(4,12,4,12)}));
            options.Children.Add(MainWindow.Button("Undo last mark",()=>{UndoAnnotation();}));
        }
        BuildPhotoOptions(inputs??[input]);BuildInputOrder(inputs??[input]);BuildCropOptions();BuildCompressionOptions();BuildDocumentOptions();BuildMediaOptions();
        if(options.Children.Count==1)options.Children.Add(L10n.Label(new TextBlock{Text="Ready to make a fresh copy. No extra settings needed.",TextWrapping=TextWrapping.Wrap,Margin=new(4,20,4,0)}));
        var footer=new DockPanel{Margin=new(0,20,0,0),LastChildFill=false};Grid.SetRow(footer,2);grid.Children.Add(footer);var save=MainWindow.Button("Save new copy  ↗",Save);save.Background=Theme.Primary;save.Foreground=new SolidColorBrush(Color.FromRgb(43,54,28));DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);var cancel=MainWindow.Button("Cancel",()=>DialogResult=false);DockPanel.SetDock(cancel,Dock.Left);footer.Children.Add(cancel);var reset=MainWindow.Button("↶ Reset",Reset);DockPanel.SetDock(reset,Dock.Left);footer.Children.Add(reset);var safe=L10n.Label(new TextBlock{Text="Original stays safe",Foreground=Theme.Brush("Muted"),FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,16,0)});DockPanel.SetDock(safe,Dock.Right);footer.Children.Add(safe);
        var clock=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        clock.Tick+=(_,_)=>{if(!scrubber.IsMouseCaptureWithin&&!scrubber.IsKeyboardFocusWithin)scrubber.Value=Math.Clamp(player.Position.TotalSeconds,0,duration);if(playing){time.Text=$"{player.Position:mm\\:ss\\.fff} / {TimeSpan.FromSeconds(duration):mm\\:ss\\.fff}";if(playingRange&&player.Position.TotalSeconds>=rangePlaybackEnd){player.Pause();playing=false;playingRange=false;}}};
        player.MediaEnded+=(_,_)=>{playing=false;player.Pause();};clock.Start();
        PreviewKeyDown+=OnKey;Closed+=(_,_)=>{clock.Stop();player.Close();previewLifetime.Cancel();DeletePreviewFiles();};
    }
    private static string Number(double n)=>n.ToString("0.###",CultureInfo.CurrentCulture);
    // Fields show milliseconds, so the default End can round up to half a millisecond
    // past ffprobe's duration. MediaEngine allows the same slack; it gets the clamped End.
    private const double RangeSlack=.001;
    // MediaEngine.TimeRange also refuses ranges shorter than 50 ms. Both round the length, so
    // 1.1 to 1.15 (0.04999… in binary) still counts as 50 ms.
    private const double MinRange=.05;
    private bool Untouched(string key)=>fields.TryGetValue(key,out var box)&&box.Text==defaults[key];
    private bool SizeUntouched=>Untouched("Width")&&Untouched("Height");
    private double RangeEnd(){var end=Value("End",duration);return duration>0?Math.Min(end,duration):end;}
    // MediaEngine checks ranges against ffprobe's duration, so the player's value only
    // stands in when ffprobe reported none, and the range controls follow it.
    private void UsePlayerDuration()
    {
        if(media is not {Duration:<=0}||!player.NaturalDuration.HasTimeSpan)return;
        duration=player.NaturalDuration.TimeSpan.TotalSeconds;scrubber.Maximum=duration;
        if(fields.TryGetValue("End",out var end)&&end.Text==defaults["End"])end.Text=defaults["End"]=Number(duration);
        try{SyncTimeline();}catch(ArgumentException){}
    }
    private void Field(string name,double value)=>TextField(name,Number(value));
    private void TextField(string name,string value){options.Children.Add(L10n.Label(new TextBlock{Text=name,Margin=new(4,12,4,0)}));var box=new TextBox{Text=value};System.Windows.Automation.AutomationProperties.SetAutomationId(box,name);if(name is "Start" or "End")box.LostKeyboardFocus+=(_,_)=>{try{SyncTimeline();}catch(ArgumentException){}};fields.Add(name,box);defaults.Add(name,value);options.Children.Add(box);}
    private Action? resetCompression,resetPhotoView,resetPreviewZoom,applyExportProfile;
    private void Reset()
    {
        if(processingPreview){time.Text=L10n.Text("Wait for the processed preview to finish before resetting.");return;}
        if(media is not null){if(processedPlayback)PlayOriginalPreview();player.Pause();playing=false;Seek(0);}
        resetCompression?.Invoke();resetPhotoView?.Invoke();resetPreviewZoom?.Invoke();
        foreach(var pair in defaults)fields[pair.Key].Text=pair.Value;
        foreach(var choice in new[]{drawingTool,backdrop,collageLayout,redactionStyle,visualization,redactMode,bleepMode,sampleDepth,documentLayout,epubLayout,aspectRatio})choice.SelectedIndex=0;
        flipHorizontal.IsChecked=flipVertical.IsChecked=trimSilence.IsChecked=joinClips.IsChecked=false;fitCollage.IsChecked=preserveMetadata.IsChecked=true;
        backgroundImage=annotationImage=visualImage=null;archivePassword.Clear();
        if(inputOrder.Items.Count>0){inputOrder.Items.Clear();foreach(var path in collageInputs)inputOrder.Items.Add(new ListBoxItem{Content=Path.GetFileName(path),Tag=path});OrderedInputs=collageInputs.ToArray();}
        var originalOrder=imageOrder.Items.Cast<ListBoxItem>().OrderBy(item=>(int)item.Tag).ToArray();imageOrder.Items.Clear();foreach(var item in originalOrder)imageOrder.Items.Add(item);
        while(drawing.Children.Count>1)drawing.Children.RemoveAt(drawing.Children.Count-1);
        drawing.ReleaseMouseCapture();dragging=false;cropGesture=null;selection=activeRectangle=null;activeStroke=null;annotationShape=selectedAnnotation=null;
        photoRegions.Clear();videoRegions.Clear();bleepRanges.Clear();ReloadRanges();markNumber=1;applyExportProfile?.Invoke();timeline.Set(duration,0,duration);QueuePhotoPreview();
    }
    private double Value(string key,double fallback=0)
    {
        if(!fields.TryGetValue(key,out var box))return fallback;
        if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value)&&!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!double.IsFinite(value))throw new ArgumentException("Enter a valid number for "+key+".");return value;
    }
    private void BeginDraw(object sender,MouseButtonEventArgs e)
    {
        if(tool is not ("crop" or "cropVideo" or "redactVideo" or "redactImage" or "annotate"))return;
        drawing.Focus();origin=e.GetPosition(drawing);dragging=drawing.CaptureMouse();
        if(BeginCropGesture(origin)){e.Handled=true;return;}
        if(tool=="annotate"){try{AnnotationStart();}catch(ArgumentException error){dragging=false;drawing.ReleaseMouseCapture();if(App.IsSmokeTest)throw;MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Check drawing settings"));}e.Handled=true;return;}
        if(tool=="annotate"&&drawingTool.SelectedIndex==0){activeStroke=new Polyline{Stroke=new SolidColorBrush(Color.FromArgb(160,234,181,40)),StrokeThickness=Math.Max(3,drawing.Width/100),StrokeLineJoin=PenLineJoin.Round};activeStroke.Points.Add(origin);drawing.Children.Add(activeStroke);}
        else
        {
            if(tool is not ("annotate" or "redactImage" or "redactVideo")&&selection is not null)drawing.Children.Remove(selection);
            activeRectangle=new Rectangle{Stroke=tool=="annotate"?Brushes.OrangeRed:Brushes.Gold,StrokeThickness=Math.Max(2,drawing.Width/350),Fill=tool is "redactVideo" or "redactImage"?Brushes.Black:new SolidColorBrush(Color.FromArgb(35,224,189,82)),IsHitTestVisible=false};drawing.Children.Add(activeRectangle);Canvas.SetLeft(activeRectangle,origin.X);Canvas.SetTop(activeRectangle,origin.Y);if(tool!="annotate")selection=activeRectangle;
        }
        e.Handled=true;
    }
    private void MoveDraw(object sender,MouseEventArgs e)
    {
        if(!dragging)return;var p=e.GetPosition(drawing);p.X=Math.Clamp(p.X,0,drawing.Width);p.Y=Math.Clamp(p.Y,0,drawing.Height);
        if(MoveCropGesture(p))return;
        if(tool=="annotate"){AnnotationMove(p);return;}
        if(activeStroke is not null){activeStroke.Points.Add(p);return;}
        if(activeRectangle is null)return;var dx=p.X-origin.X;var dy=p.Y-origin.Y;
        if(tool is "crop" or "cropVideo" && (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)||CropRatio() is not null))
        {
            var ratio=CropRatio()??1d;var width=Math.Min(Math.Abs(dx),Math.Abs(dy)*ratio);var height=width/ratio;p=new(origin.X+Math.CopySign(width,dx),origin.Y+Math.CopySign(height,dy));
        }
        var x=Math.Min(origin.X,p.X);var y=Math.Min(origin.Y,p.Y);var w=Math.Abs(p.X-origin.X);var h=Math.Abs(p.Y-origin.Y);
        Canvas.SetLeft(activeRectangle,x);Canvas.SetTop(activeRectangle,y);activeRectangle.Width=w;activeRectangle.Height=h;
        if(tool!="annotate"){fields["X"].Text=Number(Math.Floor(x));fields["Y"].Text=Number(Math.Floor(y));fields["Width"].Text=Number(Math.Max(1,Math.Floor(w)));fields["Height"].Text=Number(Math.Max(1,Math.Floor(h)));}
    }
    private void EndDraw(object sender,MouseButtonEventArgs e)
    {
        var committed=false;
        try
        {
            if(tool=="redactImage"&&activeRectangle is {Width:>=1,Height:>=1} r){photoRegions.Add(new Rect(Canvas.GetLeft(r),Canvas.GetTop(r),r.Width,r.Height));committed=true;}
            if(tool=="redactVideo"&&activeRectangle is {Width:>=2,Height:>=2} v){var (start,end)=ReadPlaybackRange();videoRegions.Add(new(Canvas.GetLeft(v),Canvas.GetTop(v),v.Width,v.Height,start,end));committed=true;ReloadRanges();}
        }
        catch(ArgumentException error){time.Text=error.Message;}
        finally
        {
            // A click or a too-small drag must not leave a cover that is never exported;
            // Undo and Remove also pair canvas boxes with regions by position.
            if(!committed&&tool is "redactImage" or "redactVideo"&&activeRectangle is not null)drawing.Children.Remove(activeRectangle);
            dragging=false;cropGesture=null;activeStroke=null;activeRectangle=null;annotationShape=null;drawing.ReleaseMouseCapture();e.Handled=true;
        }
    }
    private void Seek(double seconds){playingRange=false;player.Position=TimeSpan.FromSeconds(Math.Clamp(seconds,0,duration));time.Text=$"{player.Position:mm\\:ss\\.fff} / {TimeSpan.FromSeconds(duration):mm\\:ss\\.fff}";}
    private void TogglePlayback(){playingRange=false;if(playing){player.Pause();playing=false;}else{if(player.Position.TotalSeconds>=duration-.001)Seek(0);player.Play();playing=true;}}
    private void OnKey(object sender,KeyEventArgs e)
    {
        var modifiers=Keyboard.Modifiers;
        if(e.Key==Key.Escape){Close();e.Handled=true;return;}
        if(Keyboard.FocusedElement is Button&&modifiers==ModifierKeys.None&&e.Key is Key.Space or Key.Enter)return;
        if(e.Key==Key.Enter&&modifiers==ModifierKeys.None){Save();e.Handled=true;return;}
        if(ClipboardImport.IsEditing(Keyboard.FocusedElement as DependencyObject))return;if(tool=="annotate"&&e.Key==Key.Z&&modifiers==ModifierKeys.Control){UndoAnnotation();e.Handled=true;return;}if(tool=="annotate"&&e.Key==Key.Delete&&selectedAnnotation is not null){drawing.Children.Remove(selectedAnnotation);selectedAnnotation=null;e.Handled=true;return;}if(e.Key==Key.Enter){Save();e.Handled=true;return;}
        if(media is null||modifiers is not (ModifierKeys.None or ModifierKeys.Shift)||Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.RangeBase)return;
        try
        {
            switch(e.Key){case Key.Space:if(modifiers.HasFlag(ModifierKeys.Shift)){PlayRange();break;}TogglePlayback();break;case Key.Left:Seek(player.Position.TotalSeconds-(modifiers.HasFlag(ModifierKeys.Shift)?FineSeekStep:1));break;case Key.Right:Seek(player.Position.TotalSeconds+(modifiers.HasFlag(ModifierKeys.Shift)?FineSeekStep:1));break;case Key.I:if(fields.TryGetValue("Start",out var start)&&player.Position.TotalSeconds<Value("End",duration))start.Text=Number(player.Position.TotalSeconds);SyncTimeline();break;case Key.O:if(fields.TryGetValue("End",out var end)&&player.Position.TotalSeconds>Value("Start"))end.Text=Number(player.Position.TotalSeconds);SyncTimeline();break;default:return;}
        }
        catch(ArgumentException error){time.Text=error.Message;}
        e.Handled=true;
    }
    private void SyncTimeline(){if(fields.ContainsKey("Start")&&fields.ContainsKey("End"))timeline.Set(duration,Value("Start"),Value("End",duration));}
    private double FineSeekStep=>media?.Video is {} video?1/Math.Max(1,video.FrameRate):.1;
    private void Save()=>ReadSettings(true);
    private bool ReadSettings(bool accept)
    {
        try
        {
            if(tool=="compress"&&(Value("Target size (KB; 0 = auto)")<0||Value("Reduction (%; 0 = auto)")<0||Value("Reduction (%; 0 = auto)")>=100))throw new ArgumentException("Choose a positive size or a reduction below 100%. Zero uses automatic compression.");
            if(tool is "trimVideo" or "trimAudio"||tool=="redactAudio"&&bleepRanges.Count==0)ReadPlaybackRange();
            var x=Value("X");var y=Value("Y");var w=Value("Width");var h=Value("Height");
            if(tool is "crop" or "cropVideo" or "redactVideo"&& (w<1||h<1)||tool=="redactVideo"&&bitmap is not null&&videoRegions.Count==0)throw new ArgumentException("Drag an area first.");
            MediaOptions=new(){Start=Value("Start"),End=fields.ContainsKey("End")?RangeEnd():null,Quality=Value("Quality (%)",80)/100,Speed=Value("Speed",1),Width=fields.ContainsKey("Width")?(int)w:null,Height=fields.ContainsKey("Height")?(int)h:null,Channels=(int)Value("Channels",2),LeftVolume=Value("Left volume",1),RightVolume=Value("Right volume",1),EqualClips=(int)Value("Number of clips",2),TargetLoudness=Value("Target LUFS",-16),TruePeak=Value("True peak dB",-1.5),Regions=tool is "cropVideo" or "redactVideo"?[new(x,y,w,h,Value("Start"),fields.ContainsKey("End")?RangeEnd():null)]:[],AudioRedactions=tool=="redactAudio"?[new(Value("Start"),RangeEnd())]:[],SnapshotTimes=tool=="videoSnapshots"?fields["Times (seconds)"].Text.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(s=>NumericInput.ParseFinite(s)).ToArray():[0]};
            BitmapSource? annotated=null;if(tool=="annotate"){var target=new RenderTargetBitmap(bitmap!.PixelWidth,bitmap.PixelHeight,96,96,PixelFormats.Pbgra32);target.Render(drawing);target.Freeze();annotated=target;}
            // The size fields start from the first image. Untouched fields and presets are left to each
            // image; in a batch an untouched side follows each image's proportions. A single image gets
            // exactly the size typed.
            var preset=tool=="compress"?CompressionPreset():null;var sized=tool is "resize" or "compress" or "editImage";var batch=collageInputs.Length>1;
            int? Side(string key,double value)=>!fields.ContainsKey(key)||sized&&(preset is not null||SizeUntouched||batch&&Untouched(key))?null:(int)value;
            ImageOptions=PhotoSettings(new(Side("Width",w),Side("Height",h),(int)Value("Degrees",tool=="rotate"?90:0),tool=="crop"?new Int32Rect((int)x,(int)y,(int)w,(int)h):null,(int)Value("Quality (%)",85),true,annotated))with{Scale=preset?.Scale,LongestEdge=preset?.LongestEdge};
            MediaOptions=MoreMediaSettings(MediaOptions);
            Format=format.SelectedItem as string??"png";
            if(tool=="splitPDF"&&(!int.TryParse(fields["Pages per file"].Text,NumberStyles.AllowLeadingWhite|NumberStyles.AllowTrailingWhite,CultureInfo.CurrentCulture,out var perFile)||perFile is <1 or >1000))throw new ArgumentException("Choose between 1 and 1,000 pages per file."); // validate while the editor is still open
            if(tool=="organizePDF")Pages=fields["Page numbers"].Text.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(s=>int.Parse(s.Trim(),CultureInfo.InvariantCulture)).ToArray();
            DocumentOptions=new((int)Value("DPI",144),documentLayout.SelectedItem as string??"editable",epubLayout.SelectedItem as string??"reflowable",archivePassword.Password);if(accept)DialogResult=true;return true;
        }
        catch(Exception error)when(error is ArgumentException or FormatException or OverflowException){if(App.IsSmokeTest)throw;MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Check your settings"),MessageBoxButton.OK,MessageBoxImage.Information);return false;}
    }
}
