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
    private string? backgroundImage,annotationImage;private string[] collageInputs=[];private readonly ListBox imageOrder=new(){MaxHeight=140};
    private UIElement? annotationShape,selectedAnnotation;private Vector annotationOffset;private int strokeKind;private Point endPoint;private int markNumber=1;
    private System.Windows.Threading.DispatcherTimer? photoPreview;private bool showingOriginal;private readonly SemaphoreSlim previewGate=new(1,1);private long photoGeneration;
    private readonly CheckBox fitCollage=new(){Content="Fill cells (crop to fit)",IsChecked=true},flipHorizontal=new(){Content="Flip horizontally"},flipVertical=new(){Content="Flip vertically"};
    private void BuildPhotoOptions(IReadOnlyList<string> inputs)
    {
        collageInputs=inputs.ToArray();
        if(tool=="editImage")
        {
            Field("Width",bitmap?.PixelWidth??1280);Field("Height",bitmap?.PixelHeight??720);Field("Degrees",0);options.Children.Add(flipHorizontal);options.Children.Add(flipVertical);flipHorizontal.Click+=(_,_)=>QueuePhotoPreview();flipVertical.Click+=(_,_)=>QueuePhotoPreview();
            var rotation=new WrapPanel();rotation.Children.Add(MainWindow.Button("Rotate Left",()=>RotatePhoto(-90)));rotation.Children.Add(MainWindow.Button("Rotate Right",()=>RotatePhoto(90)));options.Children.Add(rotation);
            PhotoGroup("Light",[("Exposure",-3,3),("Brightness",-1,1),("Contrast",0,2),("Highlights",-1,1),("Shadows",-1,1),("Whites",-1,1),("Blacks",-1,1),("Gamma",.2,3)]);
            PhotoGroup("Color",[("Temperature",-1,1),("Tint",-1,1),("Saturation",0,2),("Vibrance",-1,1),("Hue",-180,180)]);
            PhotoGroup("Detail",[("Clarity",0,1),("Texture",-1,1),("Sharpen",0,2),("Dehaze",0,1),("Grain",0,1),("NoiseReduction",0,1),("Vignette",0,2),("Fade",0,1),("Blur",0,30)]);
        }
        if(tool=="frameImage")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Background"}));options.Children.Add(backdrop);TextField("Background color","#FFFFFF");TextField("Gradient end","#FFD85C");
            options.Children.Add(MainWindow.Button("Choose background image…",()=>{var picker=new OpenFileDialog{Filter="Images|*.png;*.jpg;*.webp;*.heic;*.avif;*.tiff;*.bmp"};if(picker.ShowDialog(this)==true){backgroundImage=picker.FileName;backdrop.SelectedItem="image";QueuePhotoPreview();}}));
            Field("Padding",80);Field("Corner radius",24);Field("Shadow radius",24);Field("Shadow opacity",.25);Field("Background blur",0);Field("Image scale",1);Field("Position X",.5);Field("Position Y",.5);Field("Canvas width",(bitmap?.PixelWidth??1000)+160);Field("Canvas height",(bitmap?.PixelHeight??1000)+160);
        }
        if(tool=="createCollage")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Layout"}));options.Children.Add(collageLayout);Field("Columns",2);Field("Canvas width",2048);Field("Canvas height",2048);Field("Spacing",12);Field("Padding",16);Field("Corner radius",0);TextField("Background color","#FFFFFF");
            for(var n=0;n<inputs.Count;n++)imageOrder.Items.Add(new ListBoxItem{Content=System.IO.Path.GetFileName(inputs[n]),Tag=n});options.Children.Add(imageOrder);options.Children.Add(fitCollage);fitCollage.Click+=(_,_)=>QueuePhotoPreview();
            var buttons=new WrapPanel();buttons.Children.Add(MainWindow.Button("Move up",()=>MoveImage(-1)));buttons.Children.Add(MainWindow.Button("Move down",()=>MoveImage(1)));options.Children.Add(buttons);
        }
        if(tool=="redactImage")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Cover style"}));options.Children.Add(redactionStyle);
            options.Children.Add(MainWindow.Button("Detect text",async()=>await DetectPhoto(true,false)));options.Children.Add(MainWindow.Button("Detect faces",async()=>await DetectPhoto(false,true)));
            options.Children.Add(MainWindow.Button("Undo area",()=>{if(photoRegions.Count==0)return;photoRegions.RemoveAt(photoRegions.Count-1);if(drawing.Children.Count>1)drawing.Children.RemoveAt(drawing.Children.Count-1);}));
        }
        if(tool is "editImage" or "frameImage" or "createCollage")
        {
            photoPreview=new(){Interval=TimeSpan.FromMilliseconds(300)};photoPreview.Tick+=async(_,_)=>{photoPreview.Stop();await PreviewPhoto();};
            foreach(var box in fields.Values)box.TextChanged+=(_,_)=>QueuePhotoPreview();backdrop.SelectionChanged+=(_,_)=>QueuePhotoPreview();collageLayout.SelectionChanged+=(_,_)=>QueuePhotoPreview();Closed+=(_,_)=>photoPreview.Stop();
            var original=L10n.Content(new CheckBox{Content="Show original",Margin=new(4,14,4,4)});original.Checked+=(_,_)=>{showingOriginal=true;photoPreview.Stop();photoGeneration++;SetPhotoPreview(bitmap!);};original.Unchecked+=(_,_)=>{showingOriginal=false;QueuePhotoPreview();};resetPhotoView=()=>original.IsChecked=false;previewStack.Children.Add(original);Loaded+=(_,_)=>QueuePhotoPreview();
        }
        if(tool=="annotate")options.Children.Add(MainWindow.Button("Choose annotation image…",()=>{var picker=new OpenFileDialog{Filter="Images|*.png;*.jpg;*.webp;*.heic;*.avif;*.bmp"};if(picker.ShowDialog(this)==true){annotationImage=picker.FileName;drawingTool.SelectedIndex=9;}}));
    }
    private void RotatePhoto(int degrees)
    {
        try{fields["Degrees"].Text=Number(Value("Degrees")+degrees);var width=fields["Width"].Text;fields["Width"].Text=fields["Height"].Text;fields["Height"].Text=width;QueuePhotoPreview();}
        catch(ArgumentException error){ToolTip=error.Message;}
    }
    private void PhotoGroup(string title,(string Name,double Minimum,double Maximum)[] controls)
    {
        options.Children.Add(L10n.Label(new TextBlock{Text=title,FontSize=15,FontWeight=FontWeights.SemiBold,Foreground=Theme.Brush("Muted"),Margin=new(4,16,4,4)}));
        foreach(var setting in controls)
        {
            var property=typeof(PhotoAdjustments).GetProperty(setting.Name)!;var value=(double)property.GetValue(new PhotoAdjustments())!;Field(setting.Name,value);var field=fields[setting.Name];options.Children.Remove(field);var label=(UIElement)options.Children[options.Children.Count-1];options.Children.Remove(label);
            var row=new DockPanel{Margin=new(4,8,4,0)};field.Width=72;field.Margin=new(4,0,0,0);field.Padding=new(7,3,7,3);DockPanel.SetDock(field,Dock.Right);row.Children.Add(field);row.Children.Add(L10n.Label(new TextBlock{Text=setting.Name=="NoiseReduction"?"Noise reduction":setting.Name,VerticalAlignment=VerticalAlignment.Center}));options.Children.Add(row);
            var slider=new Slider{Minimum=setting.Minimum,Maximum=setting.Maximum,Value=value,Margin=new(4,7,4,7),ToolTip=setting.Name};var syncing=false;
            slider.ValueChanged+=(_,_)=>{if(syncing)return;syncing=true;field.Text=Number(slider.Value);syncing=false;};field.TextChanged+=(_,_)=>{if(syncing||!double.TryParse(field.Text,System.Globalization.CultureInfo.CurrentCulture,out var number)||!double.IsFinite(number))return;syncing=true;slider.Value=number;syncing=false;};options.Children.Add(slider);
        }
    }
    private UIElement ZoomablePreview(UIElement visual)
    {
        var zoom=1d;var result=new StackPanel();var controls=new StackPanel{Orientation=Orientation.Horizontal};var viewbox=new Viewbox{Child=visual,Stretch=Stretch.Uniform,Width=600,Height=420};var scroll=new ScrollViewer{Content=viewbox,Height=420,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        void SetZoom(double value){zoom=Math.Clamp(value,.25,4);var height=Math.Clamp(ActualHeight-360,180,420);scroll.Height=height;viewbox.Width=Math.Max(100,scroll.ViewportWidth)*zoom;viewbox.Height=(scroll.ViewportHeight>0?scroll.ViewportHeight:height)*zoom;}
        var percent=L10n.Label(new TextBlock{Text="100%",VerticalAlignment=VerticalAlignment.Center,Foreground=Theme.Brush("Muted"),Margin=new(8,0,8,0)});
        void Apply(double value){SetZoom(value);percent.Text=$"{zoom*100:0}%";}
        resetPreviewZoom=()=>Apply(1);
        controls.Children.Add(MainWindow.Button("−",()=>Apply(zoom/1.25)));controls.Children.Add(MainWindow.Button("Fit",()=>Apply(1)));controls.Children.Add(MainWindow.Button("+",()=>Apply(zoom*1.25)));controls.Children.Add(percent);result.Children.Add(controls);result.Children.Add(scroll);System.Windows.Automation.AutomationProperties.SetAutomationId(scroll,"ImagePreviewViewport");scroll.SizeChanged+=(_,_)=>SetZoom(zoom);scroll.ScrollChanged+=(_,e)=>{if(Math.Abs(e.ViewportWidthChange)>.1||Math.Abs(e.ViewportHeightChange)>.1)SetZoom(zoom);};SizeChanged+=(_,_)=>SetZoom(zoom);return result;
    }
    private void MoveImage(int delta){var index=imageOrder.SelectedIndex;var next=index+delta;if(index<0||next<0||next>=imageOrder.Items.Count)return;var item=imageOrder.Items[index];imageOrder.Items.RemoveAt(index);imageOrder.Items.Insert(next,item);imageOrder.SelectedIndex=next;QueuePhotoPreview();}
    private void QueuePhotoPreview(){if(photoPreview is null||showingOriginal)return;photoPreview.Stop();photoPreview.Start();}
    private ImageOptions PhotoSettings(ImageOptions original)
    {
        var changes=new Dictionary<string,double>();foreach(var property in typeof(PhotoAdjustments).GetProperties())changes[property.Name]=Value(property.Name,(double)property.GetValue(new PhotoAdjustments())!);
        var adjustments=System.Text.Json.JsonSerializer.Deserialize<PhotoAdjustments>(System.Text.Json.JsonSerializer.Serialize(changes))!;
        return original with{TargetBytes=Value("Target size (KB; 0 = auto)")>0?(long)(Value("Target size (KB; 0 = auto)")*1024):null,ReductionPercent=Value("Reduction (%; 0 = auto)")>0?Value("Reduction (%; 0 = auto)"):null,FlipHorizontal=flipHorizontal.IsChecked==true,FlipVertical=flipVertical.IsChecked==true,Adjustments=adjustments,Frame=new((int)Value("Padding",80),Value("Corner radius",24),fields.GetValueOrDefault("Background color")?.Text??"#FFFFFF",backdrop.SelectedItem as string??"solid",fields.GetValueOrDefault("Gradient end")?.Text??"#FFD85C",backgroundImage,Value("Background blur"),Value("Shadow radius",24),Value("Shadow opacity",.25),fields.ContainsKey("Canvas width")?(int)Value("Canvas width"):null,fields.ContainsKey("Canvas height")?(int)Value("Canvas height"):null,Value("Image scale",1),Value("Position X",.5),Value("Position Y",.5)),Collage=new(collageLayout.SelectedItem as string??"grid",(int)Value("Columns",2),(int)Value("Canvas width",2048),(int)Value("Canvas height",2048),(int)Value("Spacing",12),(int)Value("Padding",16),Value("Corner radius"),fields.GetValueOrDefault("Background color")?.Text??"#FFFFFF",fitCollage.IsChecked==true,imageOrder.Items.Count>0?imageOrder.Items.Cast<ListBoxItem>().Select(i=>(int)i.Tag).ToArray():null),Redactions=photoRegions.ToArray(),RedactionStyle=redactionStyle.SelectedItem as string??"solid"};
    }
    private async Task PreviewPhoto()
    {
        if(bitmap is null||!IsVisible||showingOriginal)return;var generation=++photoGeneration;
        if(!await previewGate.WaitAsync(0)){QueuePhotoPreview();return;}
        try
        {
            var settings=PhotoSettings(new((int)Value("Width",bitmap.PixelWidth),(int)Value("Height",bitmap.PixelHeight),(int)Value("Degrees",0)));BitmapSource result;
            if(tool=="editImage"){var factor=Math.Min(1,1200d/Math.Max(bitmap.PixelWidth,bitmap.PixelHeight));var source=factor<1?new TransformedBitmap(bitmap,new ScaleTransform(factor,factor)):(BitmapSource)bitmap;var png=ImageEngine.PngBytes(source);result=ImageEngine.FromPng(await Task.Run(()=>PhotoCodec.Adjust(png,settings.Adjustments)));result=ImageEngine.TransformPhoto(result,SizeUntouched?settings with{Width=null,Height=null}:settings with{Width=Math.Max(1,(int)Math.Round((settings.Width??bitmap.PixelWidth)*factor)),Height=Math.Max(1,(int)Math.Round((settings.Height??bitmap.PixelHeight)*factor))});}
            else if(tool=="frameImage")result=ImageEngine.Frame(bitmap,settings.Frame);
            else result=ImageEngine.Collage(collageInputs,settings.Collage,CancellationToken.None);
            if(!IsVisible||showingOriginal||generation!=photoGeneration)return;SetPhotoPreview(result);
        }
        catch(Exception error)when(error is ArgumentException or FormatException or InvalidDataException or ImageMagick.MagickException or IOException or NotSupportedException){ToolTip=error.Message;}finally{previewGate.Release();}
    }
    private void SetPhotoPreview(BitmapSource result){drawing.Width=result.PixelWidth;drawing.Height=result.PixelHeight;if(drawing.Children[0] is Image image){image.Source=result;image.Width=result.PixelWidth;image.Height=result.PixelHeight;}}
    private async Task DetectPhoto(bool text,bool faces)
    {
        if(bitmap is null)return;try{var regions=await NativePdf.DetectAsync(bitmap,text,faces);foreach(var r in regions){photoRegions.Add(r);var shape=new Rectangle{Width=r.Width,Height=r.Height,Fill=Brushes.Black,Stroke=Brushes.Gold,StrokeThickness=2,IsHitTestVisible=false};Canvas.SetLeft(shape,r.X);Canvas.SetTop(shape,r.Y);drawing.Children.Add(shape);}if(regions.Length==0)MessageBox.Show(this,L10n.Text("No matching regions were detected. You can still draw areas by hand."),L10n.Text("Redact Photo"));}catch(Exception error){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Detection unavailable"));}
    }
    private void AnnotationStart()
    {
        strokeKind=drawingTool.SelectedIndex;
        if(strokeKind==11){selectedAnnotation=drawing.Children.Cast<UIElement>().Skip(1).Reverse().FirstOrDefault(element=>{var bounds=VisualTreeHelper.GetDescendantBounds(element);bounds.Offset(Canvas.GetLeft(element) is var x&&double.IsFinite(x)?x:0,Canvas.GetTop(element) is var y&&double.IsFinite(y)?y:0);if(element.RenderTransform is TranslateTransform t)bounds.Offset(t.X,t.Y);bounds.Inflate(8,8);return bounds.Contains(origin);});annotationShape=selectedAnnotation;annotationOffset=selectedAnnotation?.RenderTransform is TranslateTransform transform?new(transform.X,transform.Y):new();return;}
        var ink=ImageEngine.ColorBrush(fields["Ink color"].Text);var thickness=Math.Clamp(Value("Stroke width",3),1,100);
        if(strokeKind is 0 or 10){activeStroke=new Polyline{Stroke=ink,Opacity=strokeKind==0?.55:1,StrokeThickness=strokeKind==0?thickness*5:thickness,StrokeLineJoin=PenLineJoin.Round,IsHitTestVisible=false};activeStroke.Points.Add(origin);annotationShape=activeStroke;}
        else if(strokeKind is 2 or 3)annotationShape=new Polyline{Stroke=ink,StrokeThickness=thickness,StrokeLineJoin=PenLineJoin.Round,IsHitTestVisible=false};
        else if(strokeKind==4)annotationShape=new Ellipse{Stroke=ink,StrokeThickness=thickness,IsHitTestVisible=false};
        else if(strokeKind is 5 or 6 or 7)
        {
            var text=strokeKind==6?(markNumber++).ToString():fields["Annotation text"].Text;annotationShape=new Border{Background=strokeKind is 6 or 7?ink:Brushes.Transparent,CornerRadius=new(strokeKind==6?100:8),Padding=new(strokeKind==6?10:4),Child=new TextBlock{Text=text,Foreground=strokeKind is 6 or 7?Brushes.White:ink,FontSize=Math.Clamp(Value("Font size",24),6,500),TextWrapping=TextWrapping.Wrap}};
        }
        else if(strokeKind is 8 or 9)
        {
            if(strokeKind==9&&annotationImage is null){dragging=false;drawing.ReleaseMouseCapture();return;}annotationShape=new Image{Source=strokeKind==9?ImageEngine.Load(annotationImage!):bitmap,Stretch=Stretch.Fill,IsHitTestVisible=false};
        }
        else annotationShape=new Rectangle{Stroke=ink,StrokeThickness=thickness,IsHitTestVisible=false};
        drawing.Children.Add(annotationShape);Canvas.SetLeft(annotationShape,annotationShape is Polyline?0:origin.X);Canvas.SetTop(annotationShape,annotationShape is Polyline?0:origin.Y);
    }
    private void UndoAnnotation(){if(drawing.Children.Count>1)drawing.Children.RemoveAt(drawing.Children.Count-1);selectedAnnotation=null;}
    private void AnnotationMove(Point point)
    {
        if(annotationShape is null)return;if(strokeKind==11){var delta=point-origin;annotationShape.RenderTransform=new TranslateTransform(annotationOffset.X+delta.X,annotationOffset.Y+delta.Y);return;}endPoint=point;if(activeStroke is not null){activeStroke.Points.Add(point);return;}
        if(annotationShape is Polyline line)
        {
            Canvas.SetLeft(line,0);Canvas.SetTop(line,0);line.Points=new(){origin,point};if(strokeKind==2){var a=Math.Atan2(point.Y-origin.Y,point.X-origin.X);var size=Math.Max(12,line.StrokeThickness*4);line.Points.Add(new(point.X-size*Math.Cos(a-.5),point.Y-size*Math.Sin(a-.5)));line.Points.Add(point);line.Points.Add(new(point.X-size*Math.Cos(a+.5),point.Y-size*Math.Sin(a+.5)));}return;
        }
        var bounds=new Rect(origin,point);if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){var side=Math.Min(bounds.Width,bounds.Height);bounds.Width=side;bounds.Height=side;}
        if(annotationShape is FrameworkElement element){Canvas.SetLeft(element,bounds.X);Canvas.SetTop(element,bounds.Y);element.Width=Math.Max(1,bounds.Width);element.Height=Math.Max(1,bounds.Height);if(strokeKind==8&&element is Image magnifier&&bitmap is not null){var width=Math.Max(1,(int)bounds.Width/2);var height=Math.Max(1,(int)bounds.Height/2);var crop=new Int32Rect(Math.Clamp((int)bounds.X,0,bitmap.PixelWidth-width),Math.Clamp((int)bounds.Y,0,bitmap.PixelHeight-height),width,height);magnifier.Source=new CroppedBitmap(bitmap,crop);}}
    }
}
