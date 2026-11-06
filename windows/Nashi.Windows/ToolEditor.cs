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
    private readonly MediaElement player=new(){LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,Stretch=Stretch.Uniform,Height=260};private bool playing;private Uri? openedSource;
    private readonly Slider scrubber=new(){Minimum=0,Margin=new(4,12,4,8),ToolTip="Preview position"};
    private readonly StackPanel previewStack=new(),dock=new(){Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};private readonly Grid stage=new();private readonly TextBlock panelTitle=new(){Text="Options"};
    private readonly RangeTimeline timeline=new();private readonly TextBlock time=new(){Margin=new(4,6,4,10)};private double duration=1;
    internal double PreviewSeconds=>player.Position.TotalSeconds;
    internal bool Playing=>playing;
    internal RangeTimeline Timeline=>timeline;
    internal TextBox FieldForTest(string name)=>fields[name];
    internal bool AcceptForTest()=>ReadSettings(false);
    internal void ChooseAnnotationImageForTest(string file)=>ChooseAnnotationImage(file);
    private readonly string input;private readonly MediaInfo? media;private readonly StackPanel options=new();
    public ToolEditor(Window owner,string tool,string title,string input,MediaInfo? media=null,BitmapSource? preview=null,IReadOnlyList<string>? inputs=null,string[]? outputFormats=null)
    {
        // The canvas fills the window. The title, the dock and the options float over it as glass, as in the design.
        Theme.Attach(this,toolbar:true,glass:false);SetResourceReference(BackgroundProperty,"Media");
        Owner=owner;this.tool=tool;this.input=input;this.media=media;bitmap=preview;Title=title+" · Nashi";Width=1060;Height=780;MinWidth=800;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=grid;
        Grid.SetRowSpan(stage,3);grid.Children.Add(stage);var previewArea=new DockPanel();stage.Children.Add(previewArea);
        var top=new DockPanel{Height=38,Margin=new(18,12,146,0),VerticalAlignment=VerticalAlignment.Top};grid.Children.Add(top);
        var brand=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,10,0)};brand.Children.Add(Ui.Pear(16));var appName=new TextBlock{Text="Nashi",FontSize=12,Margin=new(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};appName.SetResourceReference(TextBlock.ForegroundProperty,"Muted");brand.Children.Add(appName);DockPanel.SetDock(brand,Dock.Left);top.Children.Add(brand);
        var caption=new DockPanel{VerticalAlignment=VerticalAlignment.Center};var heading=L10n.Label(new TextBlock{Text=title,FontSize=13,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});DockPanel.SetDock(heading,Dock.Left);caption.Children.Add(heading);
        var details=Path.GetFileName(input)+(bitmap is not null?$" · {bitmap.PixelWidth} × {bitmap.PixelHeight}":media is not null&&media.Duration>0?" · "+TimeSpan.FromSeconds(media.Duration).ToString(@"mm\:ss\.fff"):"");var file=new TextBlock{Text=details,FontSize=12,MaxWidth=360,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};file.SetResourceReference(TextBlock.ForegroundProperty,"Muted");caption.Children.Add(file);
        var capsule=Ui.GlassCapsule(caption,"GlassDock");capsule.Height=38;capsule.HorizontalAlignment=HorizontalAlignment.Left;capsule.Margin=new(0,0,12,0);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(actions,true);DockPanel.SetDock(actions,Dock.Right);top.Children.Add(actions);top.Children.Add(capsule);
        // The options: a small glass panel at the lower right, above the dock. One rhythm for every editor:
        // a quiet label 6 px above its control, 14 px between settings, 34 px controls.
        OptionStyles(options.Resources);
        panelTitle.FontSize=12;panelTitle.FontWeight=FontWeights.SemiBold;panelTitle.Margin=new(2,0,0,2);panelTitle.SetResourceReference(TextBlock.ForegroundProperty,"Muted");L10n.Label(panelTitle);options.Children.Add(panelTitle);
        var panel=Ui.GlassPanel(new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});panel.Width=tool=="crop"?250:296;panel.HorizontalAlignment=HorizontalAlignment.Right;panel.VerticalAlignment=VerticalAlignment.Bottom;panel.Margin=new(0,12,22,14);Grid.SetRow(panel,1);grid.Children.Add(panel);optionsPanel=panel;
        // Adjust Photo: the dials sit centered above the dock, as wide as the section needs.
        if(tool=="editImage"){panel.Width=double.NaN;panel.MinWidth=430;panel.HorizontalAlignment=HorizontalAlignment.Center;panel.Margin=new(0,12,0,14);}
        if(bitmap is not null)
        {
            drawing.Width=bitmap.PixelWidth;drawing.Height=bitmap.PixelHeight;
            drawing.Children.Add(new Image{Source=bitmap,Width=bitmap.PixelWidth,Height=bitmap.PixelHeight});
            UIElement visual=drawing;if(media is not null){var layered=new Grid{Width=bitmap.PixelWidth,Height=bitmap.PixelHeight};player.Width=bitmap.PixelWidth;player.Height=bitmap.PixelHeight;player.Source=new Uri(input);layered.Children.Add(player);layered.Children.Add(drawing);visual=layered;((Image)drawing.Children[0]).Opacity=0;}
            previewArea.Children.Add(ZoomablePreview(visual));
            drawing.MouseLeftButtonDown+=BeginDraw;drawing.MouseMove+=MoveDraw;drawing.MouseLeftButtonUp+=EndDraw;
        }
        else if(media is not null)
        {
            player.Source=new Uri(input);player.Height=double.NaN;if(media.Video is not null)previewArea.Children.Add(player);player.MediaOpened+=(_,_)=>UsePlayerDuration();
        }
        else{var note=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=360};note.Children.Add(Icons.Make("doc",48,"Faint"));note.Children.Add(L10n.Label(new TextBlock{Text="Your files will be saved as new copies.\nChoose options, then save.",FontSize=17,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new(0,16,0,0)}));previewArea.Children.Add(note);}
        duration=media?.Duration??1;
        if(media is not null)
        {
            // Scrubbing and the play-pause that shows the first frame are for pictures: together they leave a sound stuck at 0.
            player.ScrubbingEnabled=media.Video is not null;player.Loaded+=(_,_)=>{if(media.Video is not null){player.Play();player.Pause();}};player.MediaFailed+=async(_,_)=>await CreatePreviewProxy();
            timeline.Set(duration,0,duration);timeline.ShowsRange=tool is "trimVideo" or "trimAudio";
            if(tool is "trimVideo" or "trimAudio" or "redactAudio" or "redactVideo")
                timeline.Changed+=(a,b)=>{var valid=double.TryParse(fields["Start"].Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var previousStart)&&double.IsFinite(previousStart);fields["Start"].Text=Number(a);fields["End"].Text=Number(b);SeekSoon(!valid||Math.Abs(a-previousStart)>.0001?a:b);};
            BuildMediaBar(previewArea);
            // Hearing the result needs it made first: loudness, a mono mix, the parts joined, the covered video, the visualization.
            if((media.Video is null&&tool is "normalizeAudio" or "audioChannels")||tool is "redactVideo" or "splitVideo")
            {
                var comparison=new WrapPanel();comparison.Children.Add(MainWindow.Button("Original",PlayOriginalPreview));processedButton=MainWindow.Button(tool=="splitVideo"?"Play Result":tool=="audioToVideo"?"Preview Visualization":"Preview Processed",async()=>await PreviewProcessed());comparison.Children.Add(processedButton);options.Children.Add(comparison);
            }
            scrubber.Maximum=duration;scrubber.ValueChanged+=(_,_)=>{if(scrubber.IsMouseCaptureWithin||scrubber.IsKeyboardFocusWithin)Seek(scrubber.Value);};
            if(tool is "trimAudio" or "trimVideo" or "redactAudio"){var range=MainWindow.Button(tool=="redactAudio"?"Play selected part":"Play selected range",PlayRange);range.MinHeight=36;range.Margin=new(6,0,0,0);Ui.SetIcon(range,"play",14);barActions.Children.Add(range);}
        }
        if(tool=="convert")
        {
            var formats=outputFormats?.ToHashSet()??(media is not null?(media.Video is null?MediaEngine.AudioFormats:MediaEngine.VideoFormats.Concat(MediaEngine.AudioFormats).ToHashSet()):ImageEngine.Formats.ToHashSet());
            options.Children.Add(L10n.Label(new TextBlock{Text="Output format",FontWeight=FontWeights.SemiBold}));format.ItemsSource=formats.Order().ToArray();format.SelectedIndex=0;options.Children.Add(format);
        }
        if(tool is "trimVideo" or "trimAudio" or "redactAudio"){Field("Start",0);Field("End",duration);fields["Start"].TextChanged+=(_,_)=>ShowRange();fields["End"].TextChanged+=(_,_)=>ShowRange();ShowRange();}
        if(tool=="resize"){Field("Width",bitmap?.PixelWidth??1280);Field("Height",bitmap?.PixelHeight??720);}
        if(tool=="compress")Field("Quality (%)",80);
        if(tool=="changeVideoSpeed"){Field("Speed",1);fields["Speed"].TextChanged+=(_,_)=>{try{player.SpeedRatio=Math.Clamp(Value("Speed",1),.05,32);}catch(ArgumentException){}};}
        if(tool=="splitVideo")Field("Number of clips",2);
        if(tool=="videoSnapshots")TextField("Times (seconds)","");
        if(tool=="normalizeAudio"){Field("Target LUFS",-16);Field("True peak dB",-1.5);}
        if(tool=="audioChannels"){Field("Channels",2);Field("Left volume",1);Field("Right volume",1);}
        if(tool=="audioToVideo"){Field("Width",1280);Field("Height",720);}
        if(tool=="organizePDF")TextField("Page numbers","1");
        if(tool=="rotate")Field("Degrees",90);
        if(tool is "crop" or "cropVideo" or "redactVideo" or "redactImage")
        {
            if(tool=="redactVideo")options.Children.Add(new TextBlock{Text=L10n.Text("Drag a region to cover with a solid block."),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)});
            var w=bitmap?.PixelWidth??media?.Video?.Width??1280;var h=bitmap?.PixelHeight??media?.Video?.Height??720;
            Field("X",0);Field("Y",0);Field("Width",w);Field("Height",h);
            // Size and position as two pairs of fields with a pixel unit, as in the design.
            if(tool is "crop" or "cropVideo"){L10n.SetText(panelTitle,"Size and Position");Pairs(["Width","Height","X","Y"],"px");}
            if(tool=="redactVideo"){Field("Start",0);Field("End",duration);}
        }
        if(tool=="annotate")
        {
            drawingTool.ItemsSource=new[]{"Marker","Rectangle","Arrow","Line","Ellipse","Text","Number","Callout","Magnifier","Image","Freehand","Select / Move"};drawingTool.SelectedIndex=0;
            // The drawing tools sit first in the dock as a row of icons; it stays a ComboBox for the keyboard and the tests.
            drawingTool.Style=Ui.Style("SegmentedCombo");drawingTool.DropDownOpened+=(_,_)=>drawingTool.IsDropDownOpen=false;drawingTool.VerticalAlignment=VerticalAlignment.Center;
            var glyph=new FrameworkElementFactory(typeof(ContentControl));glyph.SetBinding(ContentControl.ContentProperty,new System.Windows.Data.Binding{Converter=new AnnotationGlyph()});drawingTool.ItemTemplate=new DataTemplate{VisualTree=glyph};
            // Color, size and undo sit in the dock too, so the picture keeps the whole window; sizes follow the picture's size.
            var extent=Math.Max(bitmap?.PixelWidth??1000,bitmap?.PixelHeight??1000);
            TextField("Annotation text",L10n.Text("Note"));TextField("Ink color","#FF5200");Field("Stroke width",Math.Max(2,Math.Round(extent/260d)));Field("Font size",Math.Max(14,Math.Round(extent/26d)));
            var undo=DockIcon("reuse","Undo (Ctrl+Z)",UndoAnnotation);
            foreach(var item in new UIElement[]{drawingTool,DockDivider(),InkPicker(),SizePicker(extent),DockDivider(),undo})dock.Children.Add(item);
            panel.Visibility=Visibility.Collapsed;
            drawingTool.SelectionChanged+=(_,e)=>{if(drawingTool.SelectedIndex is not (8 or 11))ShowLoupeHandles(null);if(drawingTool.SelectedIndex==9&&annotationImage is null&&!ChooseAnnotationImage())drawingTool.SelectedItem=e.RemovedItems.Count>0?e.RemovedItems[0]:"Marker";};
        }
        BuildPhotoOptions(inputs??[input]);if(coverPanel)panel.Visibility=Visibility.Collapsed;BuildInputOrder(inputs??[input]);BuildCropOptions();BuildCompressionOptions();BuildDocumentOptions();BuildMediaOptions();Loaded+=(_,_)=>ShapeMedia();if(tool=="splitPDF")ArrangePdfSplit(previewArea);
        if(options.Children.Count==1)options.Children.Add(L10n.Label(new TextBlock{Text="Ready to make a fresh copy. No extra settings needed.",TextWrapping=TextWrapping.Wrap,Margin=new(4,20,4,0)}));
        // Cancel stays for Esc-less closing by keyboard and tests; the window's close button is the visible way out.
        var cancel=MainWindow.Button("Cancel",()=>DialogResult=false);cancel.Visibility=Visibility.Collapsed;actions.Children.Add(cancel);
        var reset=MainWindow.Button("↶ Reset",Reset);reset.Style=Ui.Style("GlassRound");Ui.SetIcon(reset,"rotL",17);Ui.SetIconOnly(reset,true);L10n.Bind(reset,ToolTipProperty,"Reset");reset.Margin=new(0,0,10,0);actions.Children.Add(reset);
        var save=MainWindow.Button("Save Copy",Save);save.Style=Ui.Style("Signature");Ui.SetHint(save,"Ctrl+S");save.Margin=new(0);actions.Children.Add(save);
        if(previewStack.Children.Count>0){previewStack.Orientation=Orientation.Horizontal;previewStack.VerticalAlignment=VerticalAlignment.Center;if(dock.Children.Count>0)dock.Children.Add(DockDivider());dock.Children.Add(previewStack);}
        var footer=new DockPanel{Margin=new(0,10,0,16)};Grid.SetRow(footer,2);grid.Children.Add(footer);var dockCapsule=Ui.GlassCapsule(dock,"GlassDock",26,"dock");dockCapsule.Padding=new(8);dockCapsule.HorizontalAlignment=HorizontalAlignment.Center;footer.Children.Add(dockCapsule);if(dock.Children.Count==0||media is not null)dockCapsule.Visibility=Visibility.Collapsed;if(media is not null)footer.Children.Add(MediaBarCapsule());
        // The glass over the canvas bends the picture under it: read the canvas again shortly after it may have changed, and
        // bend anew only when it did (every layout pass in the window, a tooltip or a typed digit, asks; few change the canvas).
        Lens.Supply? bent=null;var bend=glassRefresh;bend.Tick+=(_,_)=>{bend.Stop();if(!IsVisible)return;var view=Lens.Snapshot(stage,this);if(view is not null&&bent is not null&&view.Bounds==bent.Bounds&&view.Pixels.AsSpan().SequenceEqual(bent.Pixels))return;bent=view;Lens.SetSupply(this,view);};if(media is null)stage.LayoutUpdated+=(_,_)=>{bend.Stop();bend.Start();};else ContentRendered+=(_,_)=>bend.Start();Closed+=(_,_)=>bend.Stop();
        var clock=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};
        // While a source opens, reading where it is waits on the player (10-25 ms a read, several a tick): the playhead keeps
        // its place until the source is open.
        player.MediaOpened+=(_,_)=>openedSource=player.Source;
        clock.Tick+=(_,_)=>{var open=player.Source==openedSource;if(open&&!scrubber.IsMouseCaptureWithin&&!scrubber.IsKeyboardFocusWithin)scrubber.Value=Math.Clamp(player.Position.TotalSeconds,0,duration);if(playing&&open){time.Text=Position();if(playingRange&&player.Position.TotalSeconds>=rangePlaybackEnd){player.Pause();playing=false;playingRange=false;}}MediaTick(open);};
        player.MediaEnded+=(_,_)=>{playing=false;player.Pause();};if(media is not null)clock.Start();
        PreviewKeyDown+=OnKey;Closed+=(_,_)=>{clock.Stop();player.Close();previewLifetime.Cancel();DeletePreviewFiles();};
    }
    private static string Number(double n)=>n.ToString("0.###",CultureInfo.CurrentCulture);
    private readonly TextBlock rangeText=new(),selectionText=new();
    private void ShowRange()
    {
        try{var start=Value("Start");var end=RangeEnd();if(end<=start){rangeText.Text=selectionText.Text="";return;}
            rangeText.Text=$"{Stamp(start)} – {Stamp(end)}";selectionText.Text=string.Format(L10n.Text("{0} selected"),Stamp(end-start));}
        catch(Exception error)when(error is ArgumentException or OverflowException){rangeText.Text=selectionText.Text="";}
    }
    // Implicit styles for an options panel, based on the app's own.
    internal static void OptionStyles(ResourceDictionary resources)
    {
        Style Based(Type type,params (DependencyProperty Property,object Value)[] setters)
        {
            var style=Application.Current.TryFindResource(type) is Style app?new Style(type,app):new Style(type);foreach(var (property,value) in setters)style.Setters.Add(new Setter(property,value));return style;
        }
        resources[typeof(TextBlock)]=Based(typeof(TextBlock),(TextBlock.FontSizeProperty,11.5),(TextBlock.ForegroundProperty,new DynamicResourceExtension("Muted")),(FrameworkElement.MarginProperty,new Thickness(2,14,0,6)),(TextBlock.TextWrappingProperty,TextWrapping.Wrap));
        resources[typeof(TextBox)]=Based(typeof(TextBox),(FrameworkElement.MarginProperty,new Thickness(0)),(FrameworkElement.MinHeightProperty,34d));
        resources[typeof(ComboBox)]=Based(typeof(ComboBox),(FrameworkElement.MarginProperty,new Thickness(0)),(FrameworkElement.MinHeightProperty,34d),(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Stretch));
        resources[typeof(CheckBox)]=Based(typeof(CheckBox),(FrameworkElement.MarginProperty,new Thickness(2,14,0,0)));
        resources[typeof(Button)]=Based(typeof(Button),(FrameworkElement.MarginProperty,new Thickness(0,14,8,0)),(FrameworkElement.MinHeightProperty,34d),(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left));
        resources[typeof(ListBox)]=Based(typeof(ListBox),(FrameworkElement.MarginProperty,new Thickness(0,2,0,0)));
    }
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
    private static Button DockIcon(string icon,string tip,Action action){var button=MainWindow.Button("",action);button.Style=Ui.Style("IconPlain");button.Width=button.Height=36;button.Content=Icons.Make(icon,17);L10n.Bind(button,ToolTipProperty,tip);System.Windows.Automation.AutomationProperties.SetName(button,L10n.Text(tip));return button;}
    // The ink: a dot showing the color in use; a click offers the palette above it.
    private FrameworkElement InkPicker()
    {
        var dot=new Ellipse{Width=18,Height=18,StrokeThickness=2};dot.SetResourceReference(Shape.StrokeProperty,"Rim");
        void Show(){try{dot.Fill=ImageEngine.ColorBrush(fields["Ink color"].Text);}catch(ArgumentException){}}
        var button=DockIcon("pencil","Ink color",()=>{});button.Content=dot;
        var row=new WrapPanel{Width=7*32};var popup=new System.Windows.Controls.Primitives.Popup{PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Top,VerticalOffset=-12,AllowsTransparency=true,StaysOpen=false};
        foreach(var hex in new[]{"#FF5200","#FFD60A","#30D158","#0A84FF","#BF5AF2","#111214","#FFFFFF"})
        {
            var swatch=new Ellipse{Width=22,Height=22,StrokeThickness=1,Fill=(Brush)new BrushConverter().ConvertFromString(hex)!};swatch.SetResourceReference(Shape.StrokeProperty,"LineStrong");
            var pick=new Button{Style=Ui.Style("WheelLabelButton"),Width=32,Height=32,ToolTip=hex,Content=swatch};pick.Click+=(_,_)=>{fields["Ink color"].Text=hex;popup.IsOpen=false;};row.Children.Add(pick);
        }
        var card=new Border{CornerRadius=new(16),Padding=new(6),BorderThickness=new(1),Child=row,Margin=new(10,6,10,16)};card.SetResourceReference(Border.BackgroundProperty,"Menu");card.SetResourceReference(Border.BorderBrushProperty,"LineStrong");card.SetResourceReference(EffectProperty,"PanelShadow");popup.Child=card;
        button.Click+=(_,_)=>popup.IsOpen=!popup.IsOpen;fields["Ink color"].TextChanged+=(_,_)=>Show();Show();return button;
    }
    // Three sizes for lines and text, scaled to the picture.
    private FrameworkElement SizePicker(double extent)
    {
        var sizes=new Segmented(small:true);
        foreach(var (title,help,stroke,font) in new[]{("S","Thin",400d,40d),("M","Medium",260d,26d),("L","Thick",150d,16d)})
        {var item=sizes.Add(title,()=>{fields["Stroke width"].Text=Number(Math.Max(1,Math.Round(extent/stroke)));fields["Font size"].Text=Number(Math.Max(10,Math.Round(extent/font)));});L10n.Bind(item,ToolTipProperty,help);}
        sizes.Select(1);sizes.Margin=new(2,0,2,0);return sizes;
    }
    private bool ChooseAnnotationImage()
    {
        var picker=new Microsoft.Win32.OpenFileDialog{Filter="Images|*.png;*.jpg;*.jpeg;*.webp;*.heic;*.avif;*.bmp"};if(picker.ShowDialog(this)!=true)return false;ChooseAnnotationImage(picker.FileName);return annotationImage is not null;
    }
    private sealed class AnnotationGlyph:System.Windows.Data.IValueConverter
    {
        public object Convert(object value,Type type,object parameter,CultureInfo culture)
        {
            var name=value as string??"";var icon=Icons.Make(name switch{"Marker"=>"pencil","Rectangle"=>"rect","Arrow"=>"arrow","Line"=>"line","Ellipse"=>"ellipse","Text"=>"text","Number"=>"number","Callout"=>"callout","Magnifier"=>"search","Image"=>"image","Freehand"=>"freehand",_=>"pointer"},16);
            icon.ToolTip=L10n.Text(name);System.Windows.Automation.AutomationProperties.SetName(icon,L10n.Text(name));return icon;
        }
        public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
    private static Border DockDivider(){var line=new Border{Width=1,Margin=new(4,6,4,6)};line.SetResourceReference(Border.BackgroundProperty,"LineStrong");return line;}
    // Lays the named fields (each a label and its text box) out two per row, each box showing a unit.
    private void Pairs(string[] names,string unit)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new(8)});grid.ColumnDefinitions.Add(new());
        for(var n=0;n<names.Length;n++)
        {
            var box=fields[names[n]];var at=options.Children.IndexOf(box);var label=(UIElement)options.Children[at-1];options.Children.Remove(box);options.Children.Remove(label);
            if(n%2==0)grid.RowDefinitions.Add(new());var cell=new StackPanel{Margin=new(0,0,0,4)};if(label is FrameworkElement caption)caption.Margin=new(2,n<2?10:8,0,6);cell.Children.Add(label);box.Style=Ui.Style("Unit");box.Tag=unit;box.Margin=new(0);cell.Children.Add(box);Grid.SetRow(cell,n/2);Grid.SetColumn(cell,n%2*2);grid.Children.Add(cell);
        }
        options.Children.Add(grid);
    }
    private void TextField(string name,string value){options.Children.Add(L10n.Label(new TextBlock{Text=name}));var box=new TextBox{Text=value};System.Windows.Automation.AutomationProperties.SetAutomationId(box,name);if(name is "Start" or "End")box.LostKeyboardFocus+=(_,_)=>{try{SyncTimeline();}catch(ArgumentException){}};fields.Add(name,box);defaults.Add(name,value);options.Children.Add(box);}
    private Action? resetCompression,resetPhotoView,resetPreviewZoom,applyExportProfile;
    private void Reset()
    {
        if(processingPreview){time.Text=L10n.Text("Wait for the processed preview to finish before resetting.");return;}
        if(media is not null){if(processedPlayback)PlayOriginalPreview();player.Pause();playing=false;Seek(0);}
        resetCompression?.Invoke();resetPhotoView?.Invoke();resetPreviewZoom?.Invoke();
        foreach(var pair in defaults)fields[pair.Key].Text=pair.Value;
        foreach(var choice in new[]{drawingTool,backdrop,collageLayout,redactionStyle,visualization,redactMode,bleepMode,sampleDepth,documentLayout,epubLayout,aspectRatio})choice.SelectedIndex=0;
        flipHorizontal.IsChecked=flipVertical.IsChecked=trimSilence.IsChecked=joinClips.IsChecked=false;fitCollage.IsChecked=preserveMetadata.IsChecked=true;
        backgroundImage=visualImage=null;annotationImage=null;archivePassword.Clear();
        if(inputOrder.Items.Count>0){inputOrder.Items.Clear();foreach(var path in collageInputs)inputOrder.Items.Add(new ListBoxItem{Content=Path.GetFileName(path),Tag=path});OrderedInputs=collageInputs.ToArray();}
        var originalOrder=imageOrder.Items.Cast<ListBoxItem>().OrderBy(item=>(int)item.Tag).ToArray();imageOrder.Items.Clear();foreach(var item in originalOrder)imageOrder.Items.Add(item);
        while(drawing.Children.Count>1)drawing.Children.RemoveAt(drawing.Children.Count-1);
        drawing.ReleaseMouseCapture();dragging=false;cropGesture=null;selection=activeRectangle=null;activeStroke=null;annotationShape=selectedAnnotation=null;ShowLoupeHandles(null);
        photoRegions.Clear();videoRegions.Clear();bleepRanges.Clear();ReloadRanges();markNumber=1;applyExportProfile?.Invoke();timeline.Set(duration,0,duration);afterReset?.Invoke();QueuePhotoPreview();
    }
    private double Value(string key,double fallback=0)
    {
        if(!fields.TryGetValue(key,out var box))return fallback;
        if(media is not null&&key is "Start" or "End"&&Clocked(box.Text) is {} clocked)return clocked;
        if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value)&&!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!double.IsFinite(value))throw new ArgumentException("Enter a valid number for "+key+".");return value;
    }
    // "1:23.5" or "1:02:03" as seconds; anything else is left to the number parser.
    private static double? Clocked(string text)
    {
        var parts=text.Trim().Split(':');if(parts.Length is < 2 or > 3)return null;double total=0;
        for(var n=0;n<parts.Length;n++){if(!double.TryParse(parts[n],NumberStyles.Float,CultureInfo.CurrentCulture,out var part)&&!double.TryParse(parts[n],NumberStyles.Float,CultureInfo.InvariantCulture,out part)||!double.IsFinite(part)||part<0||n<parts.Length-1&&part!=Math.Floor(part))return null;total=total*60+part;}
        return total;
    }
    private void BeginDraw(object sender,MouseButtonEventArgs e)
    {
        if(tool is not ("crop" or "cropVideo" or "redactVideo" or "redactImage" or "annotate"))return;
        drawing.Focus();origin=e.GetPosition(drawing);dragging=drawing.CaptureMouse();
        if(BeginCropGesture(origin)){e.Handled=true;return;}
        // A cover already placed: take it to move it (Delete removes the one last touched).
        if(tool=="redactImage"&&photoRegions.FindLastIndex(r=>r.Contains(origin)) is var hit&&hit>=0){movingCover=selectedCover=hit;coverGrab=origin;MarkCover();e.Handled=true;return;}
        if(tool=="annotate"&&e.ClickCount==2&&ReopenText(origin)){dragging=false;drawing.ReleaseMouseCapture();e.Handled=true;return;}
        if(tool=="annotate"){try{AnnotationStart();}catch(ArgumentException error){dragging=false;drawing.ReleaseMouseCapture();if(App.IsSmokeTest)throw;MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Check drawing settings"));}e.Handled=true;return;}
        if(tool=="annotate"&&drawingTool.SelectedIndex==0){activeStroke=new Polyline{Stroke=new SolidColorBrush(Color.FromArgb(160,234,181,40)),StrokeThickness=Math.Max(3,drawing.Width/100),StrokeLineJoin=PenLineJoin.Round};activeStroke.Points.Add(origin);drawing.Children.Add(activeStroke);}
        else
        {
            if(tool is not ("annotate" or "redactImage" or "redactVideo")&&selection is not null)drawing.Children.Remove(selection);
            activeRectangle=new Rectangle{Stroke=tool=="annotate"?Brushes.OrangeRed:Brushes.White,StrokeThickness=Math.Max(2,drawing.Width/350),Fill=tool is "redactVideo" or "redactImage"?Brushes.Black:new SolidColorBrush(Color.FromArgb(26,255,255,255)),IsHitTestVisible=false};drawing.Children.Add(activeRectangle);Canvas.SetLeft(activeRectangle,origin.X);Canvas.SetTop(activeRectangle,origin.Y);if(tool!="annotate")selection=activeRectangle;
        }
        e.Handled=true;
    }
    private void MoveDraw(object sender,MouseEventArgs e)
    {
        if(!dragging){if(tool=="annotate")HoverLoupe(e.GetPosition(drawing));if(tool is "crop" or "cropVideo")drawing.Cursor=CropCursor(CropHit(e.GetPosition(drawing)));return;}var p=e.GetPosition(drawing);p.X=Math.Clamp(p.X,0,drawing.Width);p.Y=Math.Clamp(p.Y,0,drawing.Height);
        if(MoveCropGesture(p))return;
        if(movingCover>=0&&movingCover<photoRegions.Count)
        {
            var r=photoRegions[movingCover];var moved=new Rect(Math.Clamp(r.X+p.X-coverGrab.X,0,Math.Max(0,drawing.Width-r.Width)),Math.Clamp(r.Y+p.Y-coverGrab.Y,0,Math.Max(0,drawing.Height-r.Height)),r.Width,r.Height);
            coverGrab=new(coverGrab.X+moved.X-r.X,coverGrab.Y+moved.Y-r.Y);photoRegions[movingCover]=moved;var cover=drawing.Children.OfType<Rectangle>().ElementAt(movingCover);Canvas.SetLeft(cover,moved.X);Canvas.SetTop(cover,moved.Y);return;
        }
        if(tool=="annotate"){AnnotationMove(p);return;}
        if(activeStroke is not null){activeStroke.Points.Add(p);return;}
        if(activeRectangle is null)return;var dx=p.X-origin.X;var dy=p.Y-origin.Y;
        if(tool is "crop" or "cropVideo" && (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)||CropRatio() is not null))
        {
            var ratio=CropRatio()??1d;var width=Math.Min(Math.Abs(dx),Math.Abs(dy)*ratio);var height=width/ratio;p=new(origin.X+Math.CopySign(width,dx),origin.Y+Math.CopySign(height,dy));
        }
        var x=Math.Min(origin.X,p.X);var y=Math.Min(origin.Y,p.Y);var w=Math.Abs(p.X-origin.X);var h=Math.Abs(p.Y-origin.Y);
        Canvas.SetLeft(activeRectangle,x);Canvas.SetTop(activeRectangle,y);activeRectangle.Width=w;activeRectangle.Height=h;CropDecor(new Rect(x,y,w,h));
        if(tool!="annotate"){fields["X"].Text=Number(Math.Floor(x));fields["Y"].Text=Number(Math.Floor(y));fields["Width"].Text=Number(Math.Max(1,Math.Floor(w)));fields["Height"].Text=Number(Math.Max(1,Math.Floor(h)));}
    }
    private void EndDraw(object sender,MouseButtonEventArgs e)
    {
        var committed=false;
        if(movingCover>=0&&movingCover<photoRegions.Count){drawing.Children.OfType<Rectangle>().ElementAt(movingCover).Fill=CoverFill(photoRegions[movingCover]);movingCover=-1;dragging=false;drawing.ReleaseMouseCapture();e.Handled=true;return;}
        try
        {
            if(tool=="annotate")EndLoupe();
            if(tool=="redactImage"&&activeRectangle is {Width:>=1,Height:>=1} r){var area=new Rect(Canvas.GetLeft(r),Canvas.GetTop(r),r.Width,r.Height);photoRegions.Add(area);r.Fill=CoverFill(area);committed=true;}
            if(tool=="redactVideo"&&activeRectangle is {Width:>=2,Height:>=2} v){videoRegions.Add(new(Canvas.GetLeft(v),Canvas.GetTop(v),v.Width,v.Height,0,duration));committed=true;ReloadRanges();newArea?.Invoke(videoRegions.Count-1);}
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
    private void Seek(double seconds){playingRange=false;player.Position=TimeSpan.FromSeconds(Math.Clamp(seconds,0,duration));time.Text=Position();timeline.Position=processedPlayback?double.NaN:player.Position.TotalSeconds;}
    private string Position()=>$"{Stamp(player.Position.TotalSeconds)} / {Stamp(duration)}";
    private void TogglePlayback(){playingRange=false;if(playing){player.Pause();playing=false;}else{if(player.Position.TotalSeconds>=duration-.001)Seek(0);player.Play();playing=true;}}
    private void OnKey(object sender,KeyEventArgs e)
    {
        var modifiers=Keyboard.Modifiers;if(Keyboard.FocusedElement is TextBox{Tag:"annotation"})return;
        if(e.Key==Key.Escape){Close();e.Handled=true;return;}
        if(e.Key==Key.S&&modifiers==ModifierKeys.Control){Save();e.Handled=true;return;}
        // Space over a still picture is for moving around it (held while dragging), never for pressing a focused button.
        if(e.Key==Key.Space&&media is null&&previewViewport is {IsMouseOver:true}&&!ClipboardImport.IsEditing(Keyboard.FocusedElement as DependencyObject)){e.Handled=true;return;}
        if(Keyboard.FocusedElement is Button&&modifiers==ModifierKeys.None&&e.Key is Key.Space or Key.Enter)return;
        if(e.Key==Key.Enter&&modifiers==ModifierKeys.None){Save();e.Handled=true;return;}
        if(ClipboardImport.IsEditing(Keyboard.FocusedElement as DependencyObject))return;if(modifiers==ModifierKeys.None&&NudgeDial(e.Key)){e.Handled=true;return;}if(tool=="annotate"&&e.Key==Key.Z&&modifiers==ModifierKeys.Control){UndoAnnotation();e.Handled=true;return;}if(tool=="redactImage"&&e.Key==Key.Delete&&selectedCover>=0&&selectedCover<photoRegions.Count){photoRegions.RemoveAt(selectedCover);drawing.Children.Remove(drawing.Children.OfType<Rectangle>().ElementAt(selectedCover));selectedCover=-1;e.Handled=true;return;}if(tool=="annotate"&&e.Key==Key.Delete&&selectedAnnotation is not null){drawing.Children.Remove(selectedAnnotation);selectedAnnotation=null;ShowLoupeHandles(null);e.Handled=true;return;}if(e.Key==Key.Enter){Save();e.Handled=true;return;}
        if(media is null||modifiers is not (ModifierKeys.None or ModifierKeys.Shift)||Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.RangeBase)return;
        try
        {
            switch(e.Key){case Key.Space:if(modifiers.HasFlag(ModifierKeys.Shift)){PlayRange();break;}TogglePlayback();break;case Key.Left:Seek(player.Position.TotalSeconds-(modifiers.HasFlag(ModifierKeys.Shift)?FineSeekStep:1));break;case Key.Right:Seek(player.Position.TotalSeconds+(modifiers.HasFlag(ModifierKeys.Shift)?FineSeekStep:1));break;case Key.I:if(markHere is not null){markHere(true);break;}if(fields.TryGetValue("Start",out var start)&&player.Position.TotalSeconds<Value("End",duration))start.Text=Number(player.Position.TotalSeconds);SyncTimeline();break;case Key.O:if(markHere is not null){markHere(false);break;}if(fields.TryGetValue("End",out var end)&&player.Position.TotalSeconds>Value("Start"))end.Text=Number(player.Position.TotalSeconds);SyncTimeline();break;default:return;}
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
            MediaOptions=new(){Start=Value("Start"),End=fields.ContainsKey("End")?RangeEnd():null,Quality=Value("Quality (%)",80)/100,Speed=Value("Speed",1),Width=fields.ContainsKey("Width")?(int)w:null,Height=fields.ContainsKey("Height")?(int)h:null,Channels=(int)Value("Channels",2),LeftVolume=Value("Left volume",1),RightVolume=Value("Right volume",1),EqualClips=(int)Value("Number of clips",2),TargetLoudness=Value("Target LUFS",-16),TruePeak=Value("True peak dB",-1.5),Regions=tool is "cropVideo" or "redactVideo"?[new(x,y,w,h,Value("Start"),fields.ContainsKey("End")?RangeEnd():null)]:[],AudioRedactions=tool=="redactAudio"?[new(Value("Start"),RangeEnd())]:[],SnapshotTimes=tool=="videoSnapshots"?ChosenFrames():[0]};
            BitmapSource? annotated=null;if(tool=="annotate"){drawing.Focus();var target=new RenderTargetBitmap(bitmap!.PixelWidth,bitmap.PixelHeight,96,96,PixelFormats.Pbgra32);target.Render(drawing);target.Freeze();annotated=target;}
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
