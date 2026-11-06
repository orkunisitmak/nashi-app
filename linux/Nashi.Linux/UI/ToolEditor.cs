using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageMagick;
using ImageMagick.Drawing;
using Nashi.Core;
using Nashi.Linux.Engine;
using PixelRect = Nashi.Linux.Engine.PixelRect;
namespace Nashi.Linux.UI;

/// Options for one tool or conversion, with the same fields, ranges and remembered per-format
/// export defaults as the Windows editors. Accept collects and validates everything; an invalid
/// value keeps the window open with the reason shown.
public sealed class ToolEditor:Window
{
    private readonly string tool;private readonly IReadOnlyList<string> inputs;private readonly PreferencesStore preferences;private readonly MediaInfo? info;
    private readonly byte[]? preview;private readonly int sourceWidth,sourceHeight;private readonly bool metadataReady;
    private readonly StackPanel form=new(){Spacing=2};private readonly TextBlock problem=new(){Foreground=Brushes.IndianRed,TextWrapping=TextWrapping.Wrap,IsVisible=false};
    private Func<Task>? collect;private List<string> order;
    public string Format{get;private set;}="";
    public ImageOptions ImageOptions{get;private set;}=new();
    public MediaOptions MediaOptions{get;private set;}=new();
    public DocumentOptions DocumentOptions{get;private set;}=new();
    public string[] OrderedInputs=>order.ToArray();
    public int PagesPerFile{get;private set;}=1;public string PageRanges{get;private set;}="";
    public int[]? Pages{get;private set;}public int[]? PageRotations{get;private set;}public bool UniformWidth{get;private set;}
    public bool PreserveMetadata{get;private set;}

    public ToolEditor(string tool,string title,IReadOnlyList<string> inputs,string[]? formats,PreferencesStore preferences,string? requestedFormat=null,MediaInfo? info=null,byte[]? preview=null,int sourceWidth=0,int sourceHeight=0,bool metadataReady=false)
    {
        this.tool=tool;this.inputs=inputs;this.preferences=preferences;this.info=info;this.preview=preview;this.sourceWidth=sourceWidth;this.sourceHeight=sourceHeight;this.metadataReady=metadataReady;order=inputs.ToList();
        Title=L10n.T(title)+" · Nashi";Width=preview is not null?720:560;MinWidth=480;SizeToContent=SizeToContent.Height;MaxHeight=900;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var head=Ui.Stack(4,Ui.Title(title,24),Ui.Text(inputs.Count==1?Path.GetFileName(inputs[0]):L10n.T($"{inputs.Count} files")+" · "+Path.GetFileName(inputs[0])+(info is {Duration:>0}?"  ·  "+Clock(info.Duration):""),12,"muted",false));
        head.Margin=new(0,0,0,14);
        var ok=Ui.Primary(tool=="convert"?"Convert":"Save",async()=>await AcceptAsync());ok.IsDefault=true;var cancel=Ui.Button("Cancel",()=>Close(false));cancel.IsCancel=true;
        var footer=Ui.Stack(8,problem,Ui.Row(0,ok,cancel),Ui.Text("Original stays safe",11,"muted"));footer.Margin=new(0,10,0,0);
        Content=new ScrollViewer{Content=new StackPanel{Margin=new(24),Children={head,form,footer}}};
        Build(formats??[],requestedFormat);
    }
    private bool AllImages=>inputs.All(FeatureCatalog.IsImage);
    private bool AllMedia=>inputs.All(p=>FeatureCatalog.IsVideo(p)||FeatureCatalog.IsAudio(p));
    private bool AllPdf=>inputs.All(p=>FeatureCatalog.Extension(p)=="pdf");
    private async Task AcceptAsync()
    {
        try{if(collect is not null)await collect();Close(true);}
        catch(Exception error)when(error is ArgumentException or FormatException or InvalidDataException or OverflowException){problem.Text=L10n.T(error.Message);problem.IsVisible=true;}
    }
    private void Add(Control control)=>form.Children.Add(control);
    private void Add(string label,Control control,string? hint=null)=>form.Children.Add(Ui.Field(label,control,hint));

    private void Build(string[] formats,string? requestedFormat)
    {
        switch(tool)
        {
            case "convert":BuildConvert(formats,requestedFormat);break;
            case "compress" when AllImages:BuildImageCompress();break;
            case "compress" when AllMedia:BuildMediaCompress();break;
            case "compress":BuildPdfCompress();break;
            case "crop":BuildCrop();break;
            case "annotate":BuildAnnotate();break;
            case "editImage":BuildPhoto();break;
            case "frameImage":BuildFrame();break;
            case "redactImage":BuildImageRedact();break;
            case "createPDF":Add(Ui.Text("Pages follow this order.",12,"muted"));AddOrder();break;
            case "createCollage":BuildCollage();break;
            case "trimVideo" or "trimAudio":BuildTrim();break;
            case "cropVideo":BuildVideoCrop();break;
            case "redactVideo":BuildVideoRedact();break;
            case "changeVideoSpeed":BuildSpeed();break;
            case "joinVideos":Add(Ui.Text("Videos are joined in this order.",12,"muted"));AddOrder();break;
            case "videoSnapshots":BuildSnapshots();break;
            case "splitVideo":BuildSplit();break;
            case "audioToVideo":BuildVisualizer();break;
            case "audioChannels":BuildChannels();break;
            case "normalizeAudio":BuildNormalize();break;
            case "redactAudio":BuildAudioRedact();break;
            case "muteVideo":Add(Ui.Text("A copy without its audio track. The video is not re-encoded.",13,"muted"));break;
            case "splitPDF":BuildSplitPdf();break;
            case "mergePDFs":Add(Ui.Text("PDFs are merged in this order.",12,"muted"));AddOrder();collect=()=>Task.CompletedTask;break;
            case "organizePDF":BuildOrganize();break;
            case "extractArchive" or "unzip":{var password=Password();Add("Password",password,"Only needed for encrypted archives.");collect=()=>{DocumentOptions=new(Password:password.Text??"");return Task.CompletedTask;};break;}
            default:Add(Ui.Text("This tool has no options.",13,"muted"));break;
        }
    }

    // ── Convert ───────────────────────────────────────────────────────────────────────
    private void BuildConvert(string[] formats,string? requested)
    {
        if(formats.Length==0)throw new ArgumentException("This conversion is not available for the selected files.");
        var initial=requested is not null&&formats.Contains(requested)?requested:formats[0];
        var combo=Ui.Choice(formats.Select(f=>(f,f.ToUpperInvariant())),initial);var options=new StackPanel();Add("Format",combo);Add(options);
        Func<OutputProfile>? read=null;
        void Render()
        {
            options.Children.Clear();var format=Ui.Value(combo);var profile=preferences.Profile(format);
            if(AllImages)read=ImageFormatOptions(options,format,profile);
            else if(AllMedia)read=MediaFormatOptions(options,format,profile);
            else read=DocumentFormatOptions(options,format,profile);
        }
        combo.SelectionChanged+=(_,_)=>Render();Render();
        collect=()=>
        {
            Format=Ui.Value(combo);var profile=read?.Invoke()??new();
            ImageOptions=new(Quality:(int)Math.Round(profile.Quality*100));PreserveMetadata=profile.PreserveMetadata;
            MediaOptions=new MediaOptions{Quality=profile.Quality,AudioBitrate=profile.AudioBitrate,SampleDepth=profile.SampleDepth,GifFrameRate=profile.GifFrameRate,GifWidth=profile.GifWidth,Height=profile.MaxVideoHeight>0?profile.MaxVideoHeight:null,PreserveMetadata=profile.PreserveMetadata};
            DocumentOptions=new(profile.Dpi,profile.Layout,profile.EpubLayout,passwordBox?.Text??"");
            preferences.SetProfile(Format,profile);return Task.CompletedTask;
        };
    }
    private TextBox? passwordBox;
    private Func<OutputProfile> ImageFormatOptions(StackPanel panel,string format,OutputProfile profile)
    {
        Slider? quality=null;CheckBox? keep=null;
        if(format is "jpg" or "webp" or "heic" or "avif"){quality=Ui.Slider(10,100,Math.Round(profile.Quality*100));panel.Children.Add(Ui.Field("Quality (%)",Ui.Labeled(quality,v=>$"{v:0}%")));}
        if(format is not ("pdf" or "bmp" or "gif"))
        {
            keep=new CheckBox{Content=L10n.T("Keep photo metadata (EXIF, location)"),IsChecked=profile.PreserveMetadata&&metadataReady,IsEnabled=metadataReady};
            panel.Children.Add(Ui.Field("Metadata",keep,metadataReady?null:"Set up ExifTool in Finish setup to keep metadata. Without it, exports carry no metadata."));
        }
        if(format=="heic")panel.Children.Add(Ui.Text("HEIC is encoded with the media tools (FFmpeg).",11,"muted"));
        return()=>profile with{Quality=(quality?.Value??profile.Quality*100)/100,PreserveMetadata=keep?.IsChecked==true};
    }
    private Func<OutputProfile> MediaFormatOptions(StackPanel panel,string format,OutputProfile profile)
    {
        var audioOnly=MediaEngine.AudioFormats.Contains(format);Slider? quality=null;NumericUpDown? bitrate=null,height=null,fps=null,gifWidth=null;ComboBox? depth=null;
        if(audioOnly&&format is "mp3" or "m4a" or "ogg" or "opus" or "aac" or "wma"){bitrate=Ui.Number(profile.AudioBitrate,8,512,16);panel.Children.Add(Ui.Field("Audio bitrate (kbps)",bitrate));}
        if(audioOnly&&format is "wav" or "flac" or "aiff"){depth=Ui.Choice([("matchSource","Match source"),("pcm16","16-bit"),("pcm24","24-bit"),("pcm32","32-bit"),("float32","32-bit float")],profile.SampleDepth);panel.Children.Add(Ui.Field("Sample depth",depth));}
        if(!audioOnly&&format!="gif"){quality=Ui.Slider(10,100,Math.Round(profile.Quality*100));panel.Children.Add(Ui.Field("Video quality",Ui.Labeled(quality,v=>$"{v:0}%")));height=Ui.Number(profile.MaxVideoHeight,0,8192,120);panel.Children.Add(Ui.Field("Maximum video height (0 = original)",height));}
        if(format=="gif"){fps=Ui.Number(profile.GifFrameRate,1,60);gifWidth=Ui.Number(profile.GifWidth,16,8192,16);panel.Children.Add(Ui.Field("GIF frames per second",fps));panel.Children.Add(Ui.Field("GIF width",gifWidth));}
        var keep=new CheckBox{Content=L10n.T("Keep metadata"),IsChecked=profile.PreserveMetadata};panel.Children.Add(keep);
        return()=>profile with{Quality=(quality?.Value??profile.Quality*100)/100,AudioBitrate=bitrate is null?profile.AudioBitrate:(int)Ui.Read(bitrate),SampleDepth=depth is null?profile.SampleDepth:Ui.Value(depth),MaxVideoHeight=height is null?profile.MaxVideoHeight:(int)Ui.Read(height),GifFrameRate=fps is null?profile.GifFrameRate:(int)Ui.Read(fps),GifWidth=gifWidth is null?profile.GifWidth:(int)Ui.Read(gifWidth),PreserveMetadata=keep.IsChecked==true};
    }
    private Func<OutputProfile> DocumentFormatOptions(StackPanel panel,string format,OutputProfile profile)
    {
        var fromPdf=AllPdf;NumericUpDown? dpi=null;ComboBox? layout=null,epub=null;
        if(fromPdf&&format is "docx" or "rtf" or "html" or "odt" or "pages" or "md"){layout=Ui.Choice([("editable","Editable text"),("preserve","Preserve page appearance")],profile.Layout);panel.Children.Add(Ui.Field("Layout",layout));}
        if(format=="epub"){epub=Ui.Choice([("reflowable","Reflowable"),("fixed","Fixed layout")],profile.EpubLayout);panel.Children.Add(Ui.Field("EPUB layout",epub));}
        if(format is "png" or "jpg" or "epub"||fromPdf&&format is "pptx" or "docx"||layout is not null){dpi=Ui.Number(profile.Dpi,36,600,12);panel.Children.Add(Ui.Field("DPI",dpi,"Used when pages are rendered as images."));}
        if(fromPdf||inputs.Any(FeatureCatalog.IsArchive)){passwordBox=Password();panel.Children.Add(Ui.Field("Password",passwordBox,"Only needed for protected files."));}
        if(inputs.Any(p=>FeatureCatalog.Extension(p)=="pdf")&&format is "txt" or "md")panel.Children.Add(Ui.Text("Scanned pages are read with Tesseract OCR when it is installed.",11,"muted"));
        return()=>profile with{Dpi=dpi is null?profile.Dpi:(int)Ui.Read(dpi),Layout=layout is null?profile.Layout:Ui.Value(layout),EpubLayout=epub is null?profile.EpubLayout:Ui.Value(epub)};
    }
    private static TextBox Password()=>new(){PasswordChar='•',PlaceholderText=L10n.T("Password")};

    // ── Image tools ───────────────────────────────────────────────────────────────────
    private (NumericUpDown Reduction,NumericUpDown Target) SizeTargets()
    {
        var reduction=Ui.Number(0,0,99);var target=Ui.Number(0,0,10_000_000,50);
        Add("Reduction (%; 0 = auto)",reduction);Add("Target size (KB; 0 = auto)",target,"A target size wins over the reduction.");return(reduction,target);
    }
    private (long? Bytes,double? Percent) ReadTargets((NumericUpDown Reduction,NumericUpDown Target) fields)
    {
        var target=Ui.Read(fields.Target);var reduction=Ui.Read(fields.Reduction);return(target>0?(long)(target*1024):null,target>0||reduction<=0?null:reduction);
    }
    private void BuildImageCompress()
    {
        var quality=Ui.Slider(10,100,80);Add("Quality (%)",Ui.Labeled(quality,v=>$"{v:0}%"));var targets=SizeTargets();var edge=Ui.Number(0,0,30000,100);Add("Longest edge (px; 0 = original)",edge);
        collect=()=>{var (bytes,percent)=ReadTargets(targets);ImageOptions=new(Quality:(int)quality.Value){TargetBytes=bytes,ReductionPercent=percent,LongestEdge=Ui.Read(edge)>0?(int)Ui.Read(edge):null};return Task.CompletedTask;};
    }
    private void BuildCrop()
    {
        var canvas=Region(RegionMode.Crop);if(canvas is null)return;
        var ratio=Ui.Choice([("free","Free"),("1","1:1"),("1.3333","4:3"),("1.5","3:2"),("1.7778","16:9"),("0.5625","9:16"),("0.8","4:5")],"free");
        ratio.SelectionChanged+=(_,_)=>{canvas.Ratio=Ui.Value(ratio)=="free"?null:double.Parse(Ui.Value(ratio),CultureInfo.InvariantCulture);canvas.ApplyRatio();};
        Add("Aspect ratio",ratio,"Drag to draw a new crop; drag inside it to move.");
        collect=()=>{ImageOptions=new(Crop:canvas.Crop??throw new ArgumentException("Drag a crop inside the image."));return Task.CompletedTask;};
    }
    private RegionCanvas? Region(RegionMode mode)
    {
        if(preview is null||sourceWidth<1){Add(Ui.Text("A preview could not be made for this file.",13,"muted"));return null;}
        var canvas=new RegionCanvas(preview,sourceWidth,sourceHeight,mode);Add(new Border{Classes={"inset"},HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,0,0,12),Child=canvas});return canvas;
    }
    private void BuildImageRedact()
    {
        var canvas=Region(RegionMode.Regions);if(canvas is null)return;
        var style=Ui.Choice([("solid","Solid"),("pixelate","Pixelate"),("blur","Blur")],"solid");Add("Style",style,"Drag over every area to hide. Solid is the safest choice for text.");
        Add(Ui.Row(0,Ui.Button("Undo",canvas.Undo,"chip"),Ui.Button("Clear",canvas.Clear,"chip")));
        collect=()=>{var regions=canvas.Regions;if(regions.Length==0)throw new ArgumentException("Draw at least one area to redact.");ImageOptions=new(){Redactions=regions,RedactionStyle=Ui.Value(style)};return Task.CompletedTask;};
    }
    private void BuildAnnotate()
    {
        var canvas=Region(RegionMode.Draw);if(canvas is null)return;
        var kind=Ui.Choice([("pen","Pen"),("box","Box"),("text","Text")],"pen");var color=Ui.Choice([("#E5484D","Red"),("#E3B859","Gold"),("#30A46C","Green"),("#0090FF","Blue"),("#000000","Black"),("#FFFFFF","White")],"#E5484D");
        var width=Ui.Slider(1,40,6);var text=new TextBox{PlaceholderText=L10n.T("Annotation text")};var size=Ui.Number(36,8,400,4);
        void Sync(){canvas.Tool=Ui.Value(kind);canvas.Color=Ui.Value(color);canvas.StrokeWidth=width.Value/canvas.Scale*Math.Max(1,sourceWidth/1200d);canvas.Text=text.Text??"";canvas.FontSize=Ui.Read(size);}
        kind.SelectionChanged+=(_,_)=>Sync();color.SelectionChanged+=(_,_)=>Sync();width.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty)Sync();};text.TextChanged+=(_,_)=>Sync();size.ValueChanged+=(_,_)=>Sync();Sync();
        var grid=Ui.Wrap(Ui.Field("Tool",kind),new Control{Width=12},Ui.Field("Ink color",color));Add(grid);Add("Stroke width",Ui.Labeled(width,v=>$"{v:0}"));
        Add(Ui.Wrap(Ui.Field("Annotation text",text).Also(f=>f.MinWidth=260),new Control{Width=12},Ui.Field("Font size",size)));
        Add(Ui.Row(0,Ui.Button("Undo",canvas.Undo,"chip"),Ui.Button("Clear",canvas.Clear,"chip")));
        collect=async()=>
        {
            var strokes=canvas.Strokes;if(strokes.Count==0)throw new ArgumentException("Draw at least one mark.");var input=inputs[0];var fontSize=Ui.Read(size);
            var png=await Task.Run(()=>Annotate(input,strokes,fontSize));ImageOptions=new(Annotated:png);
        };
    }
    /// Draws the marks at full resolution, so the export is as sharp as the source.
    private static byte[] Annotate(string input,IReadOnlyList<Stroke> strokes,double fontSize)
    {
        using var image=ImageEngine.Load(input);
        foreach(var stroke in strokes)
        {
            var color=new MagickColor(stroke.Color);var draw=new Drawables().StrokeColor(color).StrokeWidth(stroke.Width).FillColor(MagickColors.Transparent).StrokeLineCap(LineCap.Round).StrokeLineJoin(LineJoin.Round);
            if(stroke.Kind=="pen"&&stroke.Points.Count>1)draw.Polyline(stroke.Points.Select(p=>new PointD(p.X,p.Y)).ToArray());
            else if(stroke.Kind=="pen")draw.FillColor(color).Circle(stroke.Points[0].X,stroke.Points[0].Y,stroke.Points[0].X+stroke.Width/2,stroke.Points[0].Y);
            else if(stroke.Kind=="box")draw.Rectangle(stroke.Points[0].X,stroke.Points[0].Y,stroke.Points[1].X,stroke.Points[1].Y);
            else draw=new Drawables().FillColor(color).StrokeColor(MagickColors.Transparent).FontPointSize(fontSize).Font("DejaVu Sans").Text(stroke.Points[0].X,stroke.Points[0].Y,stroke.Text);
            draw.Draw(image);
        }
        return image.ToByteArray(MagickFormat.Png);
    }
    private void BuildPhoto()
    {
        var image=new Image{MaxHeight=320,Stretch=Stretch.Uniform};if(preview is not null){image.Source=Ui.FromPng(preview);Add(new Border{Classes={"inset"},Margin=new(0,0,0,12),Child=image});}
        (string Name,string Label,double Min,double Max,double Default)[] specs=[("Exposure","Exposure",-2,2,0),("Brightness","Brightness",-.5,.5,0),("Contrast","Contrast",0,2,1),("Saturation","Saturation",0,2,1),("Vibrance","Vibrance",-1,1,0),("Temperature","Temperature",-1,1,0),("Tint","Tint",-1,1,0),("Highlights","Highlights",-1,1,0),("Shadows","Shadows",-1,1,0),("Hue","Hue",-180,180,0),("Fade","Fade",0,1,0),("Clarity","Clarity",0,1,0),("Sharpen","Sharpen",0,2,0),("Blur","Blur",0,20,0),("Grain","Grain",0,1,0),("Vignette","Vignette",0,1,0)];
        var sliders=new Dictionary<string,Slider>();var grid=new Grid{ColumnDefinitions=new("*,16,*")};var row=0;
        for(var n=0;n<specs.Length;n++)
        {
            var spec=specs[n];var slider=Ui.Slider(spec.Min,spec.Max,spec.Default,0);sliders[spec.Name]=slider;var field=Ui.Field(spec.Label,Ui.Labeled(slider,v=>v.ToString(Math.Abs(spec.Max)>=20?"0":"0.00",CultureInfo.CurrentCulture)));
            if(n%2==0)grid.RowDefinitions.Add(new(GridLength.Auto));Grid.SetRow(field,row);Grid.SetColumn(field,n%2==0?0:2);grid.Children.Add(field);if(n%2==1)row++;
        }
        Add(grid);
        var rotation=Ui.Choice([("0","0°"),("90","90°"),("180","180°"),("270","270°")],"0");var flipH=new CheckBox{Content=L10n.T("Flip horizontally")};var flipV=new CheckBox{Content=L10n.T("Flip vertically")};
        var width=Ui.Number(0,0,30000,10);var height=Ui.Number(0,0,30000,10);
        Add(Ui.Wrap(Ui.Field("Degrees",rotation),new Control{Width=16},Ui.Field("Width",width),new Control{Width=16},Ui.Field("Height",height)));Add(Ui.Row(16,flipH,flipV));
        Add(Ui.Button("Reset",()=>{foreach(var spec in specs)sliders[spec.Name].Value=spec.Default;},"chip"));
        PhotoAdjustments Read()=>new(){Exposure=sliders["Exposure"].Value,Brightness=sliders["Brightness"].Value,Contrast=sliders["Contrast"].Value,Saturation=sliders["Saturation"].Value,Vibrance=sliders["Vibrance"].Value,Temperature=sliders["Temperature"].Value,Tint=sliders["Tint"].Value,Highlights=sliders["Highlights"].Value,Shadows=sliders["Shadows"].Value,Hue=sliders["Hue"].Value,Fade=sliders["Fade"].Value,Clarity=sliders["Clarity"].Value,Sharpen=sliders["Sharpen"].Value,Blur=sliders["Blur"].Value,Grain=sliders["Grain"].Value,Vignette=sliders["Vignette"].Value};
        // Live preview on a small copy, debounced so dragging stays smooth.
        if(preview is not null)
        {
            var small=Magick.FromPng(preview).Also(m=>{if(Math.Max(m.Width,m.Height)>720)m.Resize(new MagickGeometry(720,720));}).ToByteArray(MagickFormat.Png);
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(220)};var version=0;
            timer.Tick+=async(_,_)=>{timer.Stop();var mine=++version;var adjustments=Read();try{var png=await Task.Run(()=>PhotoCodec.Adjust(small,adjustments));if(mine==version)image.Source=Ui.FromPng(png);}catch(ArgumentException){}};
            foreach(var slider in sliders.Values)slider.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty){timer.Stop();timer.Start();}};
        }
        collect=()=>{ImageOptions=new(Rotation:int.Parse(Ui.Value(rotation),CultureInfo.InvariantCulture)){Adjustments=Read(),FlipHorizontal=flipH.IsChecked==true,FlipVertical=flipV.IsChecked==true,Width=Ui.Read(width)>0?(int)Ui.Read(width):null,Height=Ui.Read(height)>0?(int)Ui.Read(height):null};return Task.CompletedTask;};
    }
    private void BuildFrame()
    {
        var o=new FrameOptions();var backdrop=Ui.Choice([("solid","Solid color"),("gradient","Gradient"),("image","Image")],o.Backdrop);var color=new TextBox{Text=o.Color};var end=new TextBox{Text=o.GradientEnd};
        string? background=null;var pick=Ui.Button("Choose background image",async()=>{var files=await StorageProvider.OpenFilePickerAsync(new(){AllowMultiple=false,Title=L10n.T("Choose background image")});background=files.FirstOrDefault()?.TryGetLocalPath()??background;},"chip");
        var padding=Ui.Number(o.Padding,0,4000,10);var radius=Ui.Number(o.CornerRadius,0,2000,2);var shadow=Ui.Number(o.Shadow,0,100,2);var opacity=Ui.Slider(0,1,o.ShadowOpacity,0);var blur=Ui.Number(o.Blur,0,50,1);
        Add("Backdrop",backdrop);Add(Ui.Wrap(Ui.Field("Background color",color),new Control{Width=12},Ui.Field("Gradient end",end)));Add(pick);Add("Background blur",blur);
        Add(Ui.Wrap(Ui.Field("Padding",padding),new Control{Width=12},Ui.Field("Corner radius",radius),new Control{Width=12},Ui.Field("Shadow radius",shadow)));Add("Shadow opacity",Ui.Labeled(opacity,v=>$"{v*100:0}%"));
        collect=()=>{ImageOptions=new(){Frame=o with{Backdrop=Ui.Value(backdrop),Color=color.Text??"#FFFFFF",GradientEnd=end.Text??"#FFD85C",BackgroundImage=background,Padding=(int)Ui.Read(padding),CornerRadius=Ui.Read(radius),Shadow=Ui.Read(shadow),ShadowOpacity=opacity.Value,Blur=Ui.Read(blur)}};ImageEngine.ParseColor(ImageOptions.Frame.Color);ImageEngine.ParseColor(ImageOptions.Frame.GradientEnd);return Task.CompletedTask;};
    }
    private void BuildCollage()
    {
        var o=new CollageOptions();var layout=Ui.Choice([("grid","Grid"),("row","Row"),("column","Column"),("featured","Featured")],o.Layout);
        var columns=Ui.Number(o.Columns,1,100);var width=Ui.Number(o.Width,64,30000,64);var height=Ui.Number(o.Height,64,30000,64);var spacing=Ui.Number(o.Spacing,0,2000,2);var padding=Ui.Number(o.Padding,0,2000,2);var radius=Ui.Number(o.CornerRadius,0,2000,2);
        var color=new TextBox{Text=o.Color};var fill=new CheckBox{Content=L10n.T("Fill each cell"),IsChecked=o.Fill};
        Add(Ui.Wrap(Ui.Field("Layout",layout),new Control{Width=12},Ui.Field("Columns",columns)));Add(Ui.Wrap(Ui.Field("Canvas width",width),new Control{Width=12},Ui.Field("Canvas height",height)));
        Add(Ui.Wrap(Ui.Field("Spacing",spacing),new Control{Width=12},Ui.Field("Padding",padding),new Control{Width=12},Ui.Field("Corner radius",radius)));Add(Ui.Wrap(Ui.Field("Background color",color),new Control{Width=12},fill));
        AddOrder();
        collect=()=>{var options=o with{Layout=Ui.Value(layout),Columns=(int)Ui.Read(columns),Width=(int)Ui.Read(width),Height=(int)Ui.Read(height),Spacing=(int)Ui.Read(spacing),Padding=(int)Ui.Read(padding),CornerRadius=Ui.Read(radius),Color=color.Text??"#FFFFFF",Fill=fill.IsChecked==true};ImageEngine.ParseColor(options.Color);ImageOptions=new(){Collage=options};return Task.CompletedTask;};
    }
    /// Reorders the selected files (merge, join, collage and PDF pages follow this order).
    private void AddOrder()
    {
        if(inputs.Count<2)return;var list=new ListBox{MaxHeight=200};void Refresh(int keep){list.ItemsSource=order.Select((p,n)=>$"{n+1}.  {Path.GetFileName(p)}").ToList();list.SelectedIndex=keep;}
        void Move(int by){var at=list.SelectedIndex;if(at<0||at+by<0||at+by>=order.Count)return;(order[at],order[at+by])=(order[at+by],order[at]);Refresh(at+by);}
        Refresh(0);Add("Order",new Border{Classes={"inset"},Child=list});Add(Ui.Row(0,Ui.Button("Move up",()=>Move(-1),"chip"),Ui.Button("Move down",()=>Move(1),"chip")));
    }

    // ── Media tools ───────────────────────────────────────────────────────────────────
    private double Duration=>info?.Duration??0;
    private static string Clock(double seconds){var t=TimeSpan.FromSeconds(Math.Max(0,seconds));return t.TotalHours>=1?t.ToString(@"h\:mm\:ss\.f",CultureInfo.InvariantCulture):t.ToString(@"m\:ss\.f",CultureInfo.InvariantCulture);}
    /// Seconds as "83.5", "1:23.5" or "0:01:23.5".
    internal static double Seconds(string? text)
    {
        var value=(text??"").Trim().Replace(',','.');if(value.Length==0)throw new FormatException("Enter a time.");
        var parts=value.Split(':');double total=0;foreach(var part in parts){if(!double.TryParse(part,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||n<0)throw new FormatException("Enter times like 83.5 or 1:23.5.");total=total*60+n;}return total;
    }
    private static double[] SecondsList(string? text)=>(text??"").Split([';','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(Seconds).ToArray();
    private MediaOptions Base=>new(){Quality=.8};
    private void BuildMediaCompress()
    {
        var targets=SizeTargets();collect=()=>{var (bytes,percent)=ReadTargets(targets);MediaOptions=Base with{TargetBytes=bytes,ReductionPercent=percent};return Task.CompletedTask;};
    }
    private void BuildTrim()
    {
        var start=new TextBox{Text="0"};var end=new TextBox{Text=Clock(Duration)};var silence=new CheckBox{Content=L10n.T("Trim silence at both ends")};
        Add(Ui.Wrap(Ui.Field("Start",start),new Control{Width=12},Ui.Field("End",end)));Add(Ui.Text("Times like 83.5 or 1:23.5.",11,"muted"));if(tool=="trimAudio")Add(silence);
        collect=()=>{var a=Seconds(start.Text);var b=Seconds(end.Text);if(b<=a)throw new ArgumentException("The end must come after the start.");MediaOptions=Base with{Start=a,End=b,TrimSilence=silence.IsChecked==true};return Task.CompletedTask;};
    }
    private void BuildVideoCrop()
    {
        var canvas=Region(RegionMode.Crop);if(canvas is null)return;var ratio=Ui.Choice([("free","Free"),("1","1:1"),("1.7778","16:9"),("0.5625","9:16"),("1.3333","4:3"),("0.8","4:5")],"free");
        ratio.SelectionChanged+=(_,_)=>{canvas.Ratio=Ui.Value(ratio)=="free"?null:double.Parse(Ui.Value(ratio),CultureInfo.InvariantCulture);canvas.ApplyRatio();};Add("Aspect ratio",ratio);
        collect=()=>{var crop=canvas.Crop??throw new ArgumentException("Select one crop rectangle.");MediaOptions=Base with{Regions=[new MediaRegion(crop.X,crop.Y,crop.Width,crop.Height)]};return Task.CompletedTask;};
    }
    private void BuildVideoRedact()
    {
        var canvas=Region(RegionMode.Regions);if(canvas is null)return;var style=Ui.Choice([("solid","Solid"),("blur","Blur"),("pixelate","Pixelate")],"solid");
        var start=new TextBox{Text="0"};var end=new TextBox{Text=Clock(Duration)};Add("Style",style,"Drag over every area to hide on the first frame.");Add(Ui.Wrap(Ui.Field("Start",start),new Control{Width=12},Ui.Field("End",end)));
        Add(Ui.Row(0,Ui.Button("Undo",canvas.Undo,"chip"),Ui.Button("Clear",canvas.Clear,"chip")));
        collect=()=>{var a=Seconds(start.Text);var b=Seconds(end.Text);var regions=canvas.Regions;if(regions.Length==0)throw new ArgumentException("Draw a region to redact.");MediaOptions=Base with{RedactionMode=Ui.Value(style),Regions=regions.Select(r=>new MediaRegion(r.X,r.Y,r.Width,r.Height,a,b)).ToArray()};return Task.CompletedTask;};
    }
    private void BuildSpeed()
    {
        var speed=Ui.Choice([("0.25","0.25×"),("0.5","0.5×"),("0.75","0.75×"),("1.25","1.25×"),("1.5","1.5×"),("2","2×"),("3","3×"),("4","4×")],"2");Add("Speed",speed,"Audio keeps its pitch.");
        collect=()=>{MediaOptions=Base with{Speed=double.Parse(Ui.Value(speed),CultureInfo.InvariantCulture)};return Task.CompletedTask;};
    }
    private void BuildSnapshots()
    {
        var times=new TextBox{Text=string.Join("; ",new[]{0,.25,.5,.75}.Select(f=>Clock(Duration*f)))};Add("Times (seconds)",times,"Separate times with semicolons, e.g. 0; 12.5; 1:03.");
        collect=()=>{var values=SecondsList(times.Text);if(values.Length==0)throw new ArgumentException("Choose at least one frame.");MediaOptions=Base with{SnapshotTimes=values};return Task.CompletedTask;};
    }
    private void BuildSplit()
    {
        var mode=Ui.Choice([("interval","Every few seconds"),("equal","Equal clips"),("points","At split points")],"interval");var interval=Ui.Number(30,1,86400);var clips=Ui.Number(2,2,500);var points=new TextBox();var discard=new TextBox();var join=new CheckBox{Content=L10n.T("Join the remaining clips")};
        Add("Split",mode);Add("Seconds per clip",interval);Add("Number of clips",clips);Add("Split points (seconds; separated by semicolons)",points);Add("Remove clips (numbers; separated by semicolons)",discard);Add(join);
        collect=()=>
        {
            var removed=(discard.Text??"").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(s=>int.Parse(s,CultureInfo.InvariantCulture)-1).ToArray();
            MediaOptions=Ui.Value(mode) switch
            {
                "equal"=>Base with{EqualClips=(int)Ui.Read(clips)},
                "points"=>Base with{SplitPoints=SecondsList(points.Text)},
                _=>Base with{SplitInterval=Ui.Read(interval)}
            } with{DiscardedClips=removed,JoinSplitClips=join.IsChecked==true};
            return Task.CompletedTask;
        };
    }
    private void BuildVisualizer()
    {
        var style=Ui.Choice([("waveform","Waveform"),("spectrum","Spectrum"),("bars","Bars"),("image","Still image")],"waveform");string? image=null;
        var pick=Ui.Button("Choose image",async()=>{var files=await StorageProvider.OpenFilePickerAsync(new(){AllowMultiple=false});image=files.FirstOrDefault()?.TryGetLocalPath()??image;},"chip");
        var size=Ui.Choice([("1280x720","1280 × 720"),("1920x1080","1920 × 1080"),("1080x1080","1080 × 1080"),("1080x1920","1080 × 1920")],"1280x720");
        Add("Visualization",style);Add(pick);Add("Size",size);
        collect=()=>{var parts=Ui.Value(size).Split('x');MediaOptions=Base with{Visualization=Ui.Value(style),VisualizationImage=image,Width=int.Parse(parts[0],CultureInfo.InvariantCulture),Height=int.Parse(parts[1],CultureInfo.InvariantCulture)};return Task.CompletedTask;};
    }
    private void BuildChannels()
    {
        var channels=Ui.Choice([("2","Stereo"),("1","Mono")],info?.Audio?.Channels==1?"2":"1");var left=Ui.Slider(0,2,1,0);var right=Ui.Slider(0,2,1,0);
        Add("Channels",channels);Add("Left volume",Ui.Labeled(left,v=>$"{v*100:0}%"));Add("Right volume",Ui.Labeled(right,v=>$"{v*100:0}%"));
        collect=()=>{MediaOptions=Base with{Channels=int.Parse(Ui.Value(channels),CultureInfo.InvariantCulture),LeftVolume=left.Value,RightVolume=right.Value};return Task.CompletedTask;};
    }
    private void BuildNormalize()
    {
        var lufs=Ui.Number(-16,-70,-5,.5,"0.0");var range=Ui.Number(11,1,50,.5,"0.0");var peak=Ui.Number(-1.5,-9,0,.1,"0.0");
        Add("Target LUFS",lufs,"−16 suits podcasts and music; −23 is the EBU broadcast level.");Add("Loudness range",range);Add("True peak dB",peak);
        collect=()=>{MediaOptions=Base with{TargetLoudness=Ui.Read(lufs),LoudnessRange=Ui.Read(range),TruePeak=Ui.Read(peak)};return Task.CompletedTask;};
    }
    private void BuildAudioRedact()
    {
        var mode=Ui.Choice([("bleep","Bleep"),("mute","Silence")],"bleep");var ranges=new TextBox{AcceptsReturn=true,Height=80,PlaceholderText="1.5-2.8; 0:42-0:45"};
        Add("Replace with",mode);Add("Ranges",ranges,"Start-end pairs, separated by semicolons.");
        collect=()=>
        {
            var list=(ranges.Text??"").Split([';','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(r=>{var parts=r.Split('-',2,StringSplitOptions.TrimEntries);if(parts.Length!=2)throw new FormatException("Write each range as start-end.");return new MediaRange(Seconds(parts[0]),Seconds(parts[1]));}).ToArray();
            if(list.Length==0)throw new ArgumentException("Add a bleep or silence range.");MediaOptions=Base with{AudioRedactions=list,RedactionMode=Ui.Value(mode)};return Task.CompletedTask;
        };
    }

    // ── PDF tools ─────────────────────────────────────────────────────────────────────
    private void BuildPdfCompress()
    {
        var targets=SizeTargets();var password=Password();Add("Password",password,"Only needed for protected PDFs.");
        Add(Ui.Text("PDFs with selectable text, links, forms or annotations are rewritten without rasterizing, so they stay searchable.",11,"muted"));
        collect=()=>{var (bytes,percent)=ReadTargets(targets);ImageOptions=new(Quality:75){TargetBytes=bytes,ReductionPercent=percent};DocumentOptions=new(Password:password.Text??"");return Task.CompletedTask;};
    }
    private void BuildSplitPdf()
    {
        var per=Ui.Number(1,1,1000);var ranges=new TextBox{PlaceholderText="1-3, 5, 8-"};var password=Password();
        Add("Pages per file",per);Add("Page ranges",ranges,"Leave empty for every page.");Add("Password",password,"Only needed for protected PDFs.");
        collect=()=>{PagesPerFile=(int)Ui.Read(per);PageRanges=ranges.Text??"";DocumentOptions=new(Password:password.Text??"");return Task.CompletedTask;};
    }
    private void BuildOrganize()
    {
        var pages=new List<(int Page,int Rotation)>();var list=new StackPanel{Spacing=4};var thumbs=new Dictionary<int,byte[]>();var uniform=new CheckBox{Content=L10n.T("Make every page the same width")};
        var loading=Ui.Text("Loading pages…",12,"muted");Add(loading);Add(new Border{Classes={"inset"},MaxHeight=420,Child=new ScrollViewer{Content=list}});Add(uniform);
        void Render()
        {
            list.Children.Clear();
            for(var n=0;n<pages.Count;n++)
            {
                var at=n;var (page,rotation)=pages[n];
                var thumb=new Image{Width=54,Height=70,Stretch=Stretch.Uniform,RenderTransform=new RotateTransform(rotation)};if(thumbs.TryGetValue(page,out var png))thumb.Source=Ui.FromPng(png);
                var row=new DockPanel{Margin=new(4)};var actions=Ui.Row(0,
                    Ui.Button("↑",()=>{if(at>0){(pages[at],pages[at-1])=(pages[at-1],pages[at]);Render();}},"chip","Move up"),
                    Ui.Button("↓",()=>{if(at<pages.Count-1){(pages[at],pages[at+1])=(pages[at+1],pages[at]);Render();}},"chip","Move down"),
                    Ui.Button("↻",()=>{pages[at]=(page,(rotation+90)%360);Render();},"chip","Rotate"),
                    Ui.Button("✕",()=>{if(pages.Count>1){pages.RemoveAt(at);Render();}},"chip","Remove page"));
                DockPanel.SetDock(actions,Dock.Right);row.Children.Add(actions);row.Children.Add(Ui.Row(12,thumb,Ui.Text(L10n.T($"Page {page}")+(rotation!=0?$"  ·  {rotation}°":""),13,null,false).Also(t=>t.VerticalAlignment=VerticalAlignment.Center)));
                list.Children.Add(row);
            }
        }
        Opened+=async(_,_)=>
        {
            try{var rendered=await LinuxPdf.RenderAsync(inputs[0],36);for(var n=0;n<rendered.Length;n++){thumbs[n+1]=rendered[n];pages.Add((n+1,0));}loading.IsVisible=false;Render();}
            catch(Exception error){loading.Text=L10n.T(error.Message);}
        };
        collect=()=>{if(pages.Count==0)throw new ArgumentException("Enter page numbers within this PDF, in the desired order.");Pages=pages.Select(p=>p.Page).ToArray();PageRotations=pages.Select(p=>p.Rotation).ToArray();UniformWidth=uniform.IsChecked==true;return Task.CompletedTask;};
    }
}
