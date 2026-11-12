using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Nashi.Core;
using Microsoft.Win32;
namespace Nashi.Windows;
public sealed partial class ToolEditor
{
    private readonly List<Rect> photoRegions=[];
    private readonly ComboBox backdrop=new(){ItemsSource=new[]{"solid","gradient","image"},SelectedIndex=0},collageLayout=new(){ItemsSource=new[]{"grid","row","column","featured"},SelectedIndex=0},redactionStyle=new(){ItemsSource=new[]{"solid","blur","pixelate"},SelectedIndex=0};
    private string? backgroundImage;private BitmapSource? annotationImage;private string[] collageInputs=[];private readonly ListBox imageOrder=new(){MaxHeight=140};
    private UIElement? annotationShape,selectedAnnotation;private Vector annotationOffset;private int strokeKind;private Point endPoint;private int markNumber=1;
    private System.Windows.Threading.DispatcherTimer? photoPreview;private bool showingOriginal;private readonly SemaphoreSlim previewGate=new(1,1);private long photoGeneration;private CancellationTokenSource? collageRender;
    private readonly CheckBox fitCollage=new(){Content="Fill cells (crop to fit)",IsChecked=true},flipHorizontal=new(){Content="Flip horizontally"},flipVertical=new(){Content="Flip vertically"};
    private void BuildPhotoOptions(IReadOnlyList<string> inputs)
    {
        collageInputs=inputs.ToArray();
        if(tool=="editImage")
        {
            Field("Width",bitmap?.PixelWidth??1280);Field("Height",bitmap?.PixelHeight??720);Field("Degrees",0);flipHorizontal.Click+=(_,_)=>QueuePhotoPreview();flipVertical.Click+=(_,_)=>QueuePhotoPreview();
            BuildAdjust();
        }
        if(tool=="frameImage")
        {
            TextField("Background color","#FFFFFF");TextField("Gradient end","#FFD85C");
            Field("Padding",80);Field("Corner radius",24);Field("Shadow radius",ShadowRadius(.3));Field("Shadow opacity",ShadowOpacity(.3));Field("Background blur",0);Field("Image scale",1);Field("Position X",.5);Field("Position Y",.5);Field("Canvas width",(bitmap?.PixelWidth??1000)+160);Field("Canvas height",(bitmap?.PixelHeight??1000)+160);Field("Shadow X",0);Field("Shadow Y",0);BuildFrame();
        }
        if(tool=="createCollage")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Layout"}));options.Children.Add(collageLayout);Field("Columns",2);Field("Canvas width",2048);Field("Canvas height",2048);Field("Spacing",12);Field("Padding",16);Field("Corner radius",0);TextField("Background color","#FFFFFF");
            for(var n=0;n<inputs.Count;n++)imageOrder.Items.Add(new ListBoxItem{Content=System.IO.Path.GetFileName(inputs[n]),Tag=n});options.Children.Add(imageOrder);options.Children.Add(fitCollage);fitCollage.Click+=(_,_)=>QueuePhotoPreview();
            var buttons=new WrapPanel();buttons.Children.Add(MainWindow.Button("Move up",()=>MoveImage(-1)));buttons.Children.Add(MainWindow.Button("Move down",()=>MoveImage(1)));options.Children.Add(buttons);
        }
        if(tool=="redactImage")
        {
            // The cover style, detection and undo sit in the dock; the picture keeps the window and shows the cover as it will be saved.
            redactionStyle.Style=Ui.Style("SegmentedCombo");redactionStyle.DropDownOpened+=(_,_)=>redactionStyle.IsDropDownOpen=false;redactionStyle.VerticalAlignment=VerticalAlignment.Center;redactionStyle.SelectionChanged+=(_,_)=>RefreshCovers();
            Button Tool(string title,string icon,Action action){var button=MainWindow.Button(title,action);button.Style=Ui.Style("Plain");button.MinHeight=34;button.Margin=new(0);Ui.SetIcon(button,icon,15);return button;}
            dock.Children.Add(redactionStyle);dock.Children.Add(DockDivider());dock.Children.Add(Tool("Detect text","text",async()=>await DetectPhoto(true,false)));dock.Children.Add(Tool("Detect faces","eye",async()=>await DetectPhoto(false,true)));dock.Children.Add(DockDivider());
            dock.Children.Add(DockIcon("reuse","Undo area",()=>{if(photoRegions.Count==0)return;photoRegions.RemoveAt(photoRegions.Count-1);if(drawing.Children.Count>1)drawing.Children.RemoveAt(drawing.Children.Count-1);}));coverPanel=true;

        }
        if(tool is "editImage" or "frameImage" or "createCollage")
        {
            photoPreview=new(){Interval=TimeSpan.FromMilliseconds(300)};photoPreview.Tick+=async(_,_)=>{photoPreview.Stop();await PreviewPhoto();};
            foreach(var box in fields.Values)box.TextChanged+=(_,_)=>QueuePhotoPreview();backdrop.SelectionChanged+=(_,_)=>QueuePhotoPreview();collageLayout.SelectionChanged+=(_,_)=>QueuePhotoPreview();Closed+=(_,_)=>{photoPreview.Stop();collageRender?.Cancel();};
            // Adjust Photo has Before / After in its dock; Frame and Collage keep Show original.
            if(tool!="editImage"){var original=L10n.Content(new CheckBox{Content="Show original",Margin=new(4,14,4,4)});original.Checked+=(_,_)=>{showingOriginal=true;photoPreview.Stop();photoGeneration++;SetPhotoPreview(bitmap!);};original.Unchecked+=(_,_)=>{showingOriginal=false;QueuePhotoPreview();};resetPhotoView=()=>original.IsChecked=false;previewStack.Children.Add(original);}
            Loaded+=(_,_)=>QueuePhotoPreview();
        }
        if(tool=="annotate")options.Children.Add(MainWindow.Button("Choose annotation image…",()=>{var picker=new OpenFileDialog{Filter="Images|*.png;*.jpg;*.webp;*.heic;*.avif;*.bmp"};if(picker.ShowDialog(this)==true)ChooseAnnotationImage(picker.FileName);}));
    }
    private void ChooseAnnotationImage(string file)
    {
        try{annotationImage=ImageEngine.Load(file);drawingTool.SelectedIndex=9;}
        catch(Exception error)when(error is ArgumentException or FormatException or InvalidDataException or ImageMagick.MagickException or IOException or NotSupportedException or UnauthorizedAccessException){if(App.IsSmokeTest)throw;MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Check drawing settings"));}
    }
    private void RotatePhoto(int degrees)
    {
        try{fields["Degrees"].Text=Number(Value("Degrees")+degrees);sizing=true;var width=fields["Width"].Text;fields["Width"].Text=fields["Height"].Text;fields["Height"].Text=width;QueuePhotoPreview();}
        catch(ArgumentException error){ToolTip=error.Message;}finally{sizing=false;}
    }
    // The picture fills the canvas; zoom lives in the dock.
    private UIElement ZoomablePreview(UIElement visual)
    {
        var zoom=1d;var viewbox=new Viewbox{Child=new System.Windows.Documents.AdornerDecorator{Child=visual},Stretch=Stretch.Uniform};var scroll=new ScrollViewer{Content=viewbox,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        void SetZoom(double value){zoom=Math.Clamp(value,.25,4);viewbox.Width=Math.Max(100,scroll.ViewportWidth)*zoom;viewbox.Height=Math.Max(100,scroll.ViewportHeight)*zoom;}
        var percent=L10n.Label(new TextBlock{Text="100%",Width=44,TextAlignment=TextAlignment.Center,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Foreground=Theme.Brush("Muted")});
        void Apply(double value){SetZoom(value);percent.Text=$"{zoom*100:0}%";}
        resetPreviewZoom=()=>Apply(1);
        Button Zoom(string label,Action action){var button=MainWindow.Button(label,action);button.Style=Ui.Style("Plain");button.MinWidth=34;return button;}
        if(tool is not ("crop" or "cropVideo" or "editImage" or "annotate" or "redactImage")){dock.Children.Add(Zoom("−",()=>Apply(zoom/1.25)));dock.Children.Add(percent);dock.Children.Add(Zoom("+",()=>Apply(zoom*1.25)));dock.Children.Add(Zoom("Fit",()=>Apply(1)));}
        // Ctrl + wheel zooms toward the pointer; hold Space (or the middle button) and drag to move around.
        scroll.PreviewMouseWheel+=(_,e)=>
        {
            if(Keyboard.Modifiers!=ModifierKeys.Control)return;var p=e.GetPosition(scroll);var at=new Point((scroll.HorizontalOffset+p.X)/Math.Max(1,viewbox.Width),(scroll.VerticalOffset+p.Y)/Math.Max(1,viewbox.Height));
            Apply(e.Delta>0?zoom*1.25:zoom/1.25);scroll.UpdateLayout();scroll.ScrollToHorizontalOffset(at.X*viewbox.Width-p.X);scroll.ScrollToVerticalOffset(at.Y*viewbox.Height-p.Y);e.Handled=true;
        };
        Point? grab=null;var grabOffset=new Point();previewViewport=scroll;
        scroll.PreviewMouseDown+=(_,e)=>
        {
            if(e.ChangedButton!=MouseButton.Middle&&!(e.ChangedButton==MouseButton.Left&&Keyboard.IsKeyDown(Key.Space)))return;
            grab=e.GetPosition(scroll);grabOffset=new(scroll.HorizontalOffset,scroll.VerticalOffset);scroll.CaptureMouse();scroll.Cursor=Cursors.SizeAll;e.Handled=true;
        };
        scroll.PreviewMouseMove+=(_,e)=>{if(grab is not {} start)return;var p=e.GetPosition(scroll);scroll.ScrollToHorizontalOffset(grabOffset.X-(p.X-start.X));scroll.ScrollToVerticalOffset(grabOffset.Y-(p.Y-start.Y));e.Handled=true;};
        scroll.PreviewMouseUp+=(_,e)=>{if(grab is null)return;grab=null;scroll.ReleaseMouseCapture();scroll.ClearValue(CursorProperty);e.Handled=true;};
        scroll.LostMouseCapture+=(_,_)=>{grab=null;scroll.ClearValue(CursorProperty);};System.Windows.Automation.AutomationProperties.SetAutomationId(scroll,"ImagePreviewViewport");scroll.SizeChanged+=(_,_)=>SetZoom(zoom);scroll.ScrollChanged+=(_,e)=>{if(Math.Abs(e.ViewportWidthChange)>.1||Math.Abs(e.ViewportHeightChange)>.1)SetZoom(zoom);};return scroll;
    }
    private ScrollViewer? previewViewport;private BitmapSource? frameProxy;private readonly System.Windows.Threading.DispatcherTimer glassRefresh=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private void MoveImage(int delta){var index=imageOrder.SelectedIndex;var next=index+delta;if(index<0||next<0||next>=imageOrder.Items.Count)return;var item=imageOrder.Items[index];imageOrder.Items.RemoveAt(index);imageOrder.Items.Insert(next,item);imageOrder.SelectedIndex=next;QueuePhotoPreview();}
    // Adjust Photo previews as fast as it can, always with the latest values (no waiting for input to stop).
    private bool photoBusy,photoPending;
    private void QueuePhotoPreview()
    {
        if(photoPreview is null||showingOriginal)return;
        if(tool is not ("editImage" or "frameImage")){photoPreview.Stop();photoPreview.Start();return;}
        if(photoBusy){photoPending=true;return;}_ = RunPhotoPreview();
    }
    private async Task RunPhotoPreview()
    {
        photoBusy=true;
        try{do{photoPending=false;await PreviewPhoto();}while(photoPending&&IsVisible&&!showingOriginal);}
        finally{photoBusy=false;}
    }
    // The picture the adjustments run on: small (640 px while a dial is dragged, 1200 px at rest), each read once and kept.
    private readonly Dictionary<int,(byte[] Pixels,int Width,int Height,double Factor)> photoProxies=[];
    // Where the adjusted pixels go: one buffer and one surface per size, rewritten for every preview instead of new ones.
    private readonly Dictionary<int,(byte[] Pixels,WriteableBitmap Surface)> photoSurfaces=[];
    private (byte[] Pixels,int Width,int Height,double Factor) PhotoProxy(int limit)
    {
        if(photoProxies.TryGetValue(limit,out var known))return known;
        var factor=Math.Min(1,limit/(double)Math.Max(bitmap!.PixelWidth,bitmap.PixelHeight));BitmapSource scaled=factor<1?new TransformedBitmap(bitmap,new ScaleTransform(factor,factor)):bitmap;
        var straight=new FormatConvertedBitmap(scaled,PixelFormats.Bgra32,null,0);int width=straight.PixelWidth,height=straight.PixelHeight;var pixels=new byte[width*height*4];straight.CopyPixels(pixels,width*4,0);
        photoProxies[limit]=(pixels,width,height,factor);return(pixels,width,height,factor);
    }
    private ImageOptions PhotoSettings(ImageOptions original)
    {
        var changes=new Dictionary<string,double>();foreach(var property in typeof(PhotoAdjustments).GetProperties())changes[property.Name]=Value(property.Name,(double)property.GetValue(new PhotoAdjustments())!);
        var adjustments=System.Text.Json.JsonSerializer.Deserialize<PhotoAdjustments>(System.Text.Json.JsonSerializer.Serialize(changes))!;
        return original with{TargetBytes=Value("Target size (KB; 0 = auto)")>0?(long)(Value("Target size (KB; 0 = auto)")*1024):null,ReductionPercent=Value("Reduction (%; 0 = auto)")>0?Value("Reduction (%; 0 = auto)"):null,FlipHorizontal=flipHorizontal.IsChecked==true,FlipVertical=flipVertical.IsChecked==true,Adjustments=adjustments,Frame=new((int)Value("Padding",80),Value("Corner radius",24),fields.GetValueOrDefault("Background color")?.Text??"#FFFFFF",backdrop.SelectedItem as string??"solid",fields.GetValueOrDefault("Gradient end")?.Text??"#FFD85C",backgroundImage,Value("Background blur"),Value("Shadow radius",24),Value("Shadow opacity",.25),fields.ContainsKey("Canvas width")?(int)Value("Canvas width"):null,fields.ContainsKey("Canvas height")?(int)Value("Canvas height"):null,Value("Image scale",1),Value("Position X",.5),Value("Position Y",.5),Value("Shadow X"),Value("Shadow Y")),Collage=new(collageLayout.SelectedItem as string??"grid",(int)Value("Columns",2),(int)Value("Canvas width",2048),(int)Value("Canvas height",2048),(int)Value("Spacing",12),(int)Value("Padding",16),Value("Corner radius"),fields.GetValueOrDefault("Background color")?.Text??"#FFFFFF",fitCollage.IsChecked==true,imageOrder.Items.Count>0?imageOrder.Items.Cast<ListBoxItem>().Select(i=>(int)i.Tag).ToArray():null),Redactions=photoRegions.ToArray(),RedactionStyle=redactionStyle.SelectedItem as string??"solid"};
    }
    private async Task PreviewPhoto()
    {
        if(bitmap is null||!IsVisible||showingOriginal)return;var generation=++photoGeneration;
        if(!await previewGate.WaitAsync(0)){collageRender?.Cancel();QueuePhotoPreview();return;}
        try
        {
            var settings=PhotoSettings(new((int)Value("Width",bitmap.PixelWidth),(int)Value("Height",bitmap.PixelHeight),(int)Value("Degrees",0)));BitmapSource result;
            if(tool=="editImage")
            {
                // Shrinking a large photo the first time takes a moment, so it happens on the preview thread.
                var limit=adjusting?640:1200;var (pixels,width,height,factor)=bitmap.IsFrozen&&!photoProxies.ContainsKey(limit)?await PreviewWorker.Run(()=>PhotoProxy(limit)):PhotoProxy(limit);var adjustments=settings.Adjustments;
                if(!photoSurfaces.TryGetValue(width,out var made)){made=(new byte[pixels.Length],new WriteableBitmap(width,height,96,96,PixelFormats.Bgra32,null));photoSurfaces[width]=made;}var adjusted=made.Pixels;
                await Task.Run(()=>PhotoCodec.AdjustPixels(pixels,adjusted,width,height,adjustments,factor));
                if(!settings.FlipHorizontal&&!settings.FlipVertical&&settings.Rotation%360==0&&SizeUntouched){made.Surface.WritePixels(new Int32Rect(0,0,width,height),adjusted,width*4,0);result=made.Surface;glassRefresh.Stop();glassRefresh.Start();}
                else{result=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,adjusted,width*4);result.Freeze();result=ImageEngine.TransformPhoto(result,SizeUntouched?settings with{Width=null,Height=null}:settings with{Width=Math.Max(1,(int)Math.Round((settings.Width??bitmap.PixelWidth)*factor)),Height=Math.Max(1,(int)Math.Round((settings.Height??bitmap.PixelHeight)*factor))});}
            }
            else if(tool=="frameImage")
            {
                // The preview frames a smaller copy with every length scaled to match; the saved copy is full size.
                var f=Math.Min(1,1100d/Math.Max(bitmap.PixelWidth,bitmap.PixelHeight));var o=settings.Frame;
                if(f<1){if(frameProxy is null){var original=bitmap;BitmapSource Shrink(){var small=new WriteableBitmap(new TransformedBitmap(original,new ScaleTransform(f,f)));small.Freeze();return small;}frameProxy=original.IsFrozen?await PreviewWorker.Run(Shrink):Shrink();}o=o with{Padding=(int)Math.Round(o.Padding*f),CornerRadius=o.CornerRadius*f,Blur=o.Blur*f,Shadow=o.Shadow*f,Width=o.Width is {} cw?(int)Math.Max(1,Math.Round(cw*f)):null,Height=o.Height is {} ch?(int)Math.Max(1,Math.Round(ch*f)):null,ShadowX=o.ShadowX*f,ShadowY=o.ShadowY*f};}
                var source=f<1?frameProxy!:bitmap;
                // While values move, the graphics card draws the frame live; once they rest, the exact pixels (as saved) replace it.
                if(source.IsFrozen){var (live,lw,lh)=await PreviewWorker.Run(()=>ImageEngine.FrameLive(source,o));if(!IsVisible||showingOriginal||generation!=photoGeneration)return;ShowFrame(live,lw,lh);exactFrame=(source,o);framePixels.Stop();framePixels.Start();return;}
                result=ImageEngine.Frame(source,o);
            }
            else
            {
                // A collage reads every picture and draws the whole canvas: that happens on the preview thread, and a newer request stops it.
                var inputs=collageInputs;var collage=settings.Collage;var render=collageRender=new CancellationTokenSource();
                try{result=await PreviewWorker.Run(()=>ImageEngine.Collage(inputs,collage,render.Token));}
                catch(OperationCanceledException){return;}
                finally{if(ReferenceEquals(collageRender,render))collageRender=null;render.Dispose();}
            }
            if(!IsVisible||showingOriginal||generation!=photoGeneration)return;SetPhotoPreview(result);
        }
        catch(Exception error)when(error is ArgumentException or FormatException or InvalidDataException or ImageMagick.MagickException or IOException or NotSupportedException){ToolTip=error.Message;}finally{previewGate.Release();}
    }
    private void ShowFrame(ImageSource live,int width,int height){drawing.Width=width;drawing.Height=height;if(drawing.Children[0] is Image image){image.Source=live;image.Width=width;image.Height=height;}}
    private (BitmapSource Source,FrameOptions Options)? exactFrame;private readonly System.Windows.Threading.DispatcherTimer framePixels=new(){Interval=TimeSpan.FromMilliseconds(180)};
    private void SetPhotoPreview(BitmapSource result){drawing.Width=result.PixelWidth;drawing.Height=result.PixelHeight;if(drawing.Children[0] is Image image){image.Source=result;image.Width=result.PixelWidth;image.Height=result.PixelHeight;}}
    private async Task DetectPhoto(bool text,bool faces)
    {
        if(bitmap is null)return;try{var regions=await NativePdf.DetectAsync(bitmap,text,faces);foreach(var r in regions){photoRegions.Add(r);var shape=new Rectangle{Width=r.Width,Height=r.Height,Fill=CoverFill(r),Stroke=Brushes.White,StrokeThickness=2,IsHitTestVisible=false};Canvas.SetLeft(shape,r.X);Canvas.SetTop(shape,r.Y);drawing.Children.Add(shape);}if(regions.Length==0)MessageBox.Show(this,L10n.Text("No matching regions were detected. You can still draw areas by hand."),L10n.Text("Redact Photo"));}catch(Exception error){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Detection unavailable"));}
    }
    private void AnnotationStart()
    {
        strokeKind=drawingTool.SelectedIndex;
        if(strokeKind==11){selectedAnnotation=drawing.Children.Cast<UIElement>().Skip(1).Reverse().FirstOrDefault(element=>{var bounds=VisualTreeHelper.GetDescendantBounds(element);bounds.Offset(Canvas.GetLeft(element) is var x&&double.IsFinite(x)?x:0,Canvas.GetTop(element) is var y&&double.IsFinite(y)?y:0);if(element.RenderTransform is TranslateTransform t)bounds.Offset(t.X,t.Y);bounds.Inflate(8,8);return bounds.Contains(origin);});annotationShape=selectedAnnotation;annotationOffset=selectedAnnotation?.RenderTransform is TranslateTransform transform?new(transform.X,transform.Y):new();if(IsLoupe(selectedAnnotation)){if(LoupeAt(origin) is {} grip)GrabLoupe(grip.Mark,grip.Part);else{selectedAnnotation=annotationShape=null;ShowLoupeHandles(null);}}else ShowLoupeHandles(null);return;}
        ShowLoupeHandles(null);
        var ink=ImageEngine.ColorBrush(fields["Ink color"].Text);var thickness=Math.Clamp(Value("Stroke width",3),1,100);
        if(strokeKind is 0 or 10){activeStroke=new Polyline{Stroke=ink,Opacity=strokeKind==0?.55:1,StrokeThickness=strokeKind==0?thickness*5:thickness,StrokeLineJoin=PenLineJoin.Round,IsHitTestVisible=false};activeStroke.Points.Add(origin);annotationShape=activeStroke;}
        else if(strokeKind is 2 or 3)annotationShape=new Polyline{Stroke=ink,StrokeThickness=thickness,StrokeLineJoin=PenLineJoin.Round,IsHitTestVisible=false};
        else if(strokeKind==4)annotationShape=new Ellipse{Stroke=ink,StrokeThickness=thickness,IsHitTestVisible=false};
        else if(strokeKind is 5 or 6 or 7)
        {
            // Numbers are placed as they are; text and callouts open right where you clicked, ready to type.
            var size=Math.Clamp(Value("Font size",24),6,500);var mark=new Border{Background=strokeKind is 6 or 7?ink:Brushes.Transparent,CornerRadius=new(strokeKind==6?size:size*.35),Padding=strokeKind==5?new(2):new(size*.45,size*.22,size*.45,size*.22)};annotationShape=mark;
            if(strokeKind==6)mark.Child=new TextBlock{Text=(markNumber++).ToString(),Foreground=Brushes.White,FontSize=size,FontWeight=FontWeights.Bold};else Dispatcher.BeginInvoke(()=>EditText(mark,""),System.Windows.Threading.DispatcherPriority.Input);
        }
        else if(strokeKind is 8 or 9)
        {
            if(strokeKind==8){if(LoupeAt(origin) is {} grip){GrabLoupe(grip.Mark,grip.Part);return;}annotationShape=loupe=NewLoupe();loupeCorner=-1;}
            else{if(annotationImage is null){dragging=false;drawing.ReleaseMouseCapture();return;}annotationShape=new Image{Source=annotationImage,Stretch=Stretch.Fill,IsHitTestVisible=false};}
        }
        else annotationShape=new Rectangle{Stroke=ink,StrokeThickness=thickness,IsHitTestVisible=false};
        drawing.Children.Add(annotationShape);Canvas.SetLeft(annotationShape,annotationShape is Polyline?0:origin.X);Canvas.SetTop(annotationShape,annotationShape is Polyline?0:origin.Y);
    }
    // Text goes on the picture as you type: a normal-size box opens under the mark, and the mark shows the words live.
    // Enter places it, Esc cancels, a click elsewhere keeps it; double-click a text mark to change it.
    private void EditText(Border mark,string text)
    {
        var ink=mark.Child is TextBlock old?old.Foreground:mark.Background==Brushes.Transparent?ImageEngine.ColorBrush(fields["Ink color"].Text):Brushes.White;var size=mark.Child is TextBlock shown?shown.FontSize:Math.Clamp(Value("Font size",24),6,500);
        var live=new TextBlock{Text=text.Length>0?text:"…",Foreground=ink,FontSize=size,Opacity=text.Length>0?1:.6};mark.Child=live;
        var box=new TextBox{Text=text,Tag="annotation",MinWidth=260,FontSize=14,Margin=new(0)};var hint=L10n.Label(new TextBlock{Text="Enter to place · Esc to cancel",FontSize=11,Margin=new(2,6,0,0)});hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var card=new Border{CornerRadius=new(14),Padding=new(10),BorderThickness=new(1),Margin=new(10,6,10,16),Child=new StackPanel{Children={box,hint}}};card.SetResourceReference(Border.BackgroundProperty,"Menu");card.SetResourceReference(Border.BorderBrushProperty,"LineStrong");card.SetResourceReference(EffectProperty,"PanelShadow");
        var popup=new System.Windows.Controls.Primitives.Popup{PlacementTarget=mark,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,AllowsTransparency=true,StaysOpen=true,Child=card};var done=false;
        void Finish(bool keep)
        {
            if(done)return;done=true;popup.IsOpen=false;var value=box.Text;
            if(keep&&!string.IsNullOrWhiteSpace(value))mark.Child=new TextBlock{Text=value,Foreground=ink,FontSize=size};else if(text.Length>0)mark.Child=new TextBlock{Text=text,Foreground=ink,FontSize=size};else drawing.Children.Remove(mark);
        }
        box.TextChanged+=(_,_)=>{live.Text=box.Text.Length>0?box.Text:"…";live.Opacity=box.Text.Length>0?1:.6;};
        box.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){Finish(true);drawing.Focus();e.Handled=true;}else if(e.Key==Key.Escape){Finish(false);drawing.Focus();e.Handled=true;}};
        box.LostKeyboardFocus+=(_,_)=>Finish(true);Closed+=(_,_)=>popup.IsOpen=false;popup.Opened+=(_,_)=>{box.Focus();box.SelectAll();};popup.IsOpen=true;
    }
    private bool ReopenText(Point point)
    {
        foreach(var mark in drawing.Children.OfType<Border>().Reverse())
        {
            if(mark.Child is not TextBlock text)continue;var x=Canvas.GetLeft(mark);var y=Canvas.GetTop(mark);if(mark.RenderTransform is TranslateTransform shift){x+=shift.X;y+=shift.Y;}
            if(new Rect(x,y,mark.ActualWidth,mark.ActualHeight).Contains(point)&&!(mark.Background is SolidColorBrush&&text.FontWeight==FontWeights.Bold)){EditText(mark,text.Text);return true;}
        }
        return false;
    }
    private bool coverPanel;private int movingCover=-1,selectedCover=-1;private Point coverGrab;
    // The cover last touched shows a dashed edge.
    private void MarkCover(){var covers=drawing.Children.OfType<Rectangle>().ToArray();for(var n=0;n<covers.Length;n++)covers[n].StrokeDashArray=n==selectedCover?new DoubleCollection{4,3}:null;}
    private Brush CoverFill(Rect area)
    {
        var style=redactionStyle.SelectedItem as string??"solid";if(style=="solid"||bitmap is null||area.Width<1||area.Height<1)return Brushes.Black;
        try{var clamped=Rect.Intersect(area,new Rect(0,0,bitmap.PixelWidth,bitmap.PixelHeight));return clamped.IsEmpty?Brushes.Black:new ImageBrush(ImageEngine.RedactionPatch(bitmap,clamped,style));}
        catch(Exception error)when(error is ArgumentException or ImageMagick.MagickException){return Brushes.Black;}
    }
    // The canvas pairs each cover with its area by order (Undo removes the last of both).
    private void RefreshCovers(){var covers=drawing.Children.OfType<Rectangle>().ToArray();for(var n=0;n<photoRegions.Count&&n<covers.Length;n++)covers[n].Fill=CoverFill(photoRegions[n]);}
    private void UndoAnnotation(){if(drawing.Children.Count>1)drawing.Children.RemoveAt(drawing.Children.Count-1);selectedAnnotation=null;ShowLoupeHandles(null);}
    private void AnnotationMove(Point point)
    {
        if(loupe is not null&&loupeCorner>=0){DragLoupe(point);return;}
        if(annotationShape is null)return;if(strokeKind==11){var delta=point-origin;annotationShape.RenderTransform=new TranslateTransform(annotationOffset.X+delta.X,annotationOffset.Y+delta.Y);return;}if(strokeKind is 5 or 6 or 7)return;endPoint=point;if(activeStroke is not null){activeStroke.Points.Add(point);return;}
        if(annotationShape is Polyline line)
        {
            Canvas.SetLeft(line,0);Canvas.SetTop(line,0);line.Points=new(){origin,point};if(strokeKind==2){var a=Math.Atan2(point.Y-origin.Y,point.X-origin.X);var size=Math.Max(12,line.StrokeThickness*4);line.Points.Add(new(point.X-size*Math.Cos(a-.5),point.Y-size*Math.Sin(a-.5)));line.Points.Add(point);line.Points.Add(new(point.X-size*Math.Cos(a+.5),point.Y-size*Math.Sin(a+.5)));}return;
        }
        var bounds=new Rect(origin,point);if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){var side=Math.Min(bounds.Width,bounds.Height);bounds.Width=side;bounds.Height=side;}
        if(annotationShape is Border{Tag:"loupe"} mark){PlaceLoupe(mark,bounds);return;}
        if(annotationShape is FrameworkElement element){Canvas.SetLeft(element,bounds.X);Canvas.SetTop(element,bounds.Y);element.Width=Math.Max(1,bounds.Width);element.Height=Math.Max(1,bounds.Height);}
    }
}
