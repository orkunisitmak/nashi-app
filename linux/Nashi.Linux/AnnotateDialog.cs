using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
namespace Nashi.Linux;

/// Annotate Photo: pick a tool and drag on the picture. Marks are kept in image pixels and the
/// preview is drawn by the same Annotations.Apply that saves the file, on a bounded copy.
/// Select / Move picks the topmost mark; Delete removes it, Ctrl+Z undoes the last mark.
internal sealed class AnnotateDialog:Window
{
    public static readonly (string Label,string Kind)[] Tools=[("Marker","marker"),("Rectangle","rectangle"),("Arrow","arrow"),("Line","line"),("Ellipse","ellipse"),("Text","text"),("Number","number"),("Callout","callout"),("Magnifier","magnifier"),("Image","image"),("Freehand","freehand"),("Select / Move","select")];
    private static readonly (string Label,string Hex)[] Colors=[("Orange","#FF5200"),("Red","#E5322D"),("Yellow","#F5C400"),("Green","#2E9E4F"),("Blue","#2F6FEB"),("Black","#111111"),("White","#FFFFFF")];
    private readonly AnnotateCanvas canvas;
    internal AnnotateCanvas Canvas=>canvas;
    private readonly Bitmap original;private readonly byte[] source;
    private readonly ComboBox tool=new(){ItemsSource=Tools.Select(t=>t.Label).ToArray(),SelectedIndex=0,MinWidth=140};
    private readonly ComboBox color=new(){ItemsSource=Colors.Select(c=>c.Label).ToArray(),SelectedIndex=0,MinWidth=100};
    private readonly NumericUpDown stroke=new(){Minimum=1,Maximum=200,Increment=1,FormatString="0",Width=110};
    private readonly NumericUpDown font=new(){Minimum=6,Maximum=600,Increment=2,FormatString="0",Width=110};
    private readonly TextBox text=new(){Text="Note",Width=160};
    private readonly Button picture=new(){Content="Choose image…"};
    private readonly TextBlock count=new(){VerticalAlignment=VerticalAlignment.Center,MinWidth=80};
    private readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(60)};
    private int generation;private bool rendering;
    public Annotation? Result{get;private set;}
    /// True once the preview shows the current marks, for the editor checks.
    /// Renders now instead of after the debounce timer, which does not run on the headless platform.
    internal Task Flush(){debounce.Stop();return Render();}
    internal bool Settled=>!rendering&&!debounce.IsEnabled&&shownGeneration==generation;
    private int shownGeneration;

    private AnnotateDialog(string title,string note,Bitmap preview,byte[] png,PixelSize pixels)
    {
        original=preview;source=png;Title=title;Width=1040;Height=740;MinWidth=760;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        canvas=new(preview,pixels){Focusable=true,Margin=new(16)};canvas.Changed+=Changed;canvas.Settings=Settings;
        // Defaults follow the picture size, so a mark looks the same on a phone photo and a screenshot.
        var edge=Math.Max(pixels.Width,pixels.Height);stroke.Value=(decimal)Math.Clamp(Math.Round(edge/220d),2,200);font.Value=(decimal)Math.Clamp(Math.Round(edge/22d),12,600);
        tool.SelectionChanged+=(_,_)=>{canvas.Kind=Tools[Math.Max(0,tool.SelectedIndex)].Kind;Visibility();canvas.Focus();};
        picture.Click+=async(_,_)=>await ChoosePicture();
        var undo=new Button{Content="Undo"};undo.Click+=(_,_)=>{canvas.Undo();canvas.Focus();};
        var clear=new Button{Content="Clear"};clear.Click+=(_,_)=>{canvas.Clear();canvas.Focus();};
        var ok=new Button{Content="Save",IsDefault=true,FontWeight=Avalonia.Media.FontWeight.SemiBold,Background=NashiTheme.Primary,Foreground=new SolidColorBrush(Color.Parse("#243B2B"))};ok.Click+=(_,_)=>Accept();
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();
        static Control Field(string label,Control control)=>new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=6,Children={new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center},control}};
        var options=new WrapPanel{Margin=new(16,10,16,0),Children={Field("Tool",tool),Field("Color",color),Field("Stroke",stroke),Field("Font size",font),Field("Text",text),picture}};
        foreach(var child in options.Children.OfType<Control>())child.Margin=new(0,0,14,8);
        var bar=new DockPanel{Margin=new(16,10),Children=
        {
            new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,Children={cancel,ok}},
            new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=10,Children={undo,clear,count,new TextBlock{Text=note,FontSize=12,Opacity=.75,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis}}}
        }};
        DockPanel.SetDock(bar.Children[0],Dock.Right);
        var root=new DockPanel{Children={bar,options,canvas}};DockPanel.SetDock(bar,Dock.Bottom);DockPanel.SetDock(options,Dock.Top);
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");Content=root;
        debounce.Tick+=async(_,_)=>{debounce.Stop();await Render();};
        Opened+=(_,_)=>{canvas.Focus();Visibility();Changed();};
        Closed+=(_,_)=>{debounce.Stop();generation++;if(canvas.Preview!=original)canvas.Preview.Dispose();original.Dispose();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Enter&&FocusManager?.GetFocusedElement() is not TextBox){Accept();e.Handled=true;}};
    }

    public static async Task<AnnotateDialog> CreateAsync(string title,string input)
    {
        var (preview,pixels)=await Picture.LoadAsync(()=>PhotoCodec.Read(input));
        using var stream=new MemoryStream();preview.Save(stream);
        return new(title,$"{Path.GetFileName(input)} · {pixels.Width} × {pixels.Height}",preview,stream.ToArray(),pixels);
    }

    /// Current tool settings for a new mark, in image pixels.
    private (string Color,double Width,double FontSize,string Text,byte[]? Picture) Settings()=>(Colors[Math.Max(0,color.SelectedIndex)].Hex,(double)(stroke.Value??4),(double)(font.Value??48),text.Text??"",canvas.Picture);
    private void Visibility()
    {
        var kind=canvas.Kind;text.IsEnabled=kind is "text" or "callout";font.IsEnabled=kind is "text" or "number" or "callout";
        stroke.IsEnabled=kind is not ("text" or "number" or "callout" or "image" or "select");color.IsEnabled=kind is not ("image" or "select");
        picture.IsVisible=kind=="image";
    }
    private async Task ChoosePicture()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Choose an image to place",AllowMultiple=false,FileTypeFilter=[new("Images"){Patterns=["*.png","*.jpg","*.jpeg","*.webp","*.heic","*.avif","*.bmp","*.tiff"]}]});
        if(files.FirstOrDefault()?.TryGetLocalPath() is not {} path)return;
        try{canvas.Picture=await Task.Run(()=>{using var image=PhotoCodec.Read(path);return image.ToByteArray(MagickFormat.Png);});picture.Content=Path.GetFileName(path);tool.SelectedIndex=Array.FindIndex(Tools,t=>t.Kind=="image");}
        catch(Exception error)when(error is IOException or InvalidDataException or MagickException){count.Text=error.Message;}
    }
    // For the render check.
    public void Add(Mark mark){canvas.Marks.Add(mark);Changed();}
    public void Choose(string kind)=>tool.SelectedIndex=Array.FindIndex(Tools,t=>t.Kind==kind);

    private void Accept(){if(canvas.Marks.Count==0){count.Text="Draw a mark first.";return;}Result=new(canvas.Marks.ToArray());Close();}
    private void Changed(){count.Text=canvas.Marks.Count switch{0=>"No marks yet",1=>"1 mark",var n=>$"{n} marks"};debounce.Stop();debounce.Start();}

    /// Only the newest request is shown, so drawing never flickers back to an older state.
    private async Task Render()
    {
        if(rendering){debounce.Start();return;}
        var current=++generation;var marks=canvas.Marks.ToArray();
        if(marks.Length==0){Show(original);shownGeneration=current;return;}
        var scale=original.PixelSize.Width/(double)canvas.Pixels.Width;rendering=true;
        try
        {
            var png=await Task.Run(()=>{var clock=System.Diagnostics.Stopwatch.StartNew();using var image=new MagickImage(source);var read=clock.ElapsedMilliseconds;Annotations.Apply(image,MarkGeometry.Scaled(new(marks),scale));var drawn=clock.ElapsedMilliseconds;var bytes=Picture.Encode(image);if(Environment.GetEnvironmentVariable("NASHI_TRACE") is not null)Console.Error.WriteLine($"annotate preview {image.Width}x{image.Height}: read {read} ms, draw {drawn-read} ms, encode {clock.ElapsedMilliseconds-drawn} ms");return bytes;});
            if(current!=generation||!IsVisible)return;using var stream=new MemoryStream(png);Show(new Bitmap(stream));shownGeneration=current;
        }
        catch(Exception error)when(error is ArgumentException or MagickException or IOException or InvalidOperationException){count.Text=error.Message;}
        finally{rendering=false;}
    }
    private void Show(Bitmap bitmap){var previous=canvas.Preview;canvas.Preview=bitmap;if(previous!=original&&previous!=bitmap)previous.Dispose();}
}

internal sealed class AnnotateCanvas(Bitmap preview,PixelSize pixels):PictureCanvas(preview,pixels)
{
    public List<Mark> Marks{get;}=[];
    public event Action? Changed;
    public string Kind{get;set;}="marker";
    public byte[]? Picture{get;set;}
    public Func<(string Color,double Width,double FontSize,string Text,byte[]? Picture)> Settings{get;set;}=()=>("#FF5200",6,48,"",null);
    private int active=-1,selected=-1;private Point origin;private Mark? start;
    private int Next=>Marks.Count(m=>m.Kind=="number")+1;

    public void Undo(){if(Marks.Count==0)return;Marks.RemoveAt(Marks.Count-1);selected=-1;Update();}
    public void Clear(){Marks.Clear();selected=-1;Update();}
    private void Update(){Changed?.Invoke();InvalidateVisual();}

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        Focus();var p=ToPixels(e.GetPosition(this));origin=p;e.Pointer.Capture(this);e.Handled=true;
        if(Kind=="select"){selected=Marks.FindLastIndex(m=>MarkGeometry.Bounds(m).Inflate(8/Scale).Contains(p));start=selected>=0?Marks[selected]:null;InvalidateVisual();return;}
        var (color,width,size,text,picture)=Settings();
        if(Kind=="image"&&picture is null){e.Pointer.Capture(null);return;}
        Mark mark=Kind switch
        {
            "marker" or "freehand"=>new(Kind,p.X,p.Y,p.X,p.Y,color,width,Points:[(p.X,p.Y)]),
            "number"=>new(Kind,p.X,p.Y,p.X,p.Y,color,width,Next.ToString(System.Globalization.CultureInfo.InvariantCulture),size),
            // Text is placed by a click; its box follows the text, so there is nothing to drag.
            "text"=>new(Kind,p.X,p.Y,p.X,p.Y,color,width,text.Length==0?"Note":text,size),
            _=>new(Kind,p.X,p.Y,p.X,p.Y,color,width,text.Length==0?"Note":text,size,Picture:picture)
        };
        Marks.Add(mark);active=Marks.Count-1;selected=-1;
        if(Kind is "text" or "number"){active=-1;Update();}
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);var p=ToPixels(e.GetPosition(this));
        if(Kind=="select"&&selected>=0&&start is not null&&e.GetCurrentPoint(this).Properties.IsLeftButtonPressed){Marks[selected]=MarkGeometry.Moved(start,p.X-origin.X,p.Y-origin.Y);Update();return;}
        if(active<0){Cursor=new(Kind=="select"?(Marks.Any(m=>MarkGeometry.Bounds(m).Contains(p))?StandardCursorType.SizeAll:StandardCursorType.Arrow):StandardCursorType.Cross);return;}
        var mark=Marks[active];
        if(mark.Kind is "marker" or "freehand"){var last=mark.Points![^1];if(Math.Abs(last.X-p.X)+Math.Abs(last.Y-p.Y)<2/Scale)return;Marks[active]=mark with{Points=[..mark.Points,(p.X,p.Y)],X2=p.X,Y2=p.Y};}
        else{
            // Shift keeps shapes square and lines at 45° steps.
            if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)){var dx=p.X-origin.X;var dy=p.Y-origin.Y;
                if(mark.Kind is "line" or "arrow"){var angle=Math.Round(Math.Atan2(dy,dx)/(Math.PI/4))*(Math.PI/4);var length=Math.Sqrt(dx*dx+dy*dy);p=new(origin.X+Math.Cos(angle)*length,origin.Y+Math.Sin(angle)*length);}
                else{var side=Math.Max(Math.Abs(dx),Math.Abs(dy));p=new(origin.X+Math.Sign(dx)*side,origin.Y+Math.Sign(dy)*side);}}
            Marks[active]=mark with{X2=p.X,Y2=p.Y};
        }
        Update();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);e.Pointer.Capture(null);start=null;
        if(active>=0)
        {
            // A click without a drag leaves no empty shape behind.
            var m=Marks[active];var tiny=m.Kind is not ("marker" or "freehand")&&Math.Abs(m.X2-m.X1)*Scale<3&&Math.Abs(m.Y2-m.Y1)*Scale<3;
            if(tiny){Marks.RemoveAt(active);Update();}active=-1;
        }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.Key==Key.Z&&e.KeyModifiers==KeyModifiers.Control){Undo();e.Handled=true;return;}
        if(selected<0)return;
        if(e.Key is Key.Delete or Key.Back){Marks.RemoveAt(selected);selected=-1;Update();e.Handled=true;return;}
        var step=(e.KeyModifiers&KeyModifiers.Shift)!=0?10:1;
        var move=e.Key switch{Key.Left=>(-step,0),Key.Right=>(step,0),Key.Up=>(0,-step),Key.Down=>(0,step),_=>((int,int)?)null};
        if(move is var (dx,dy)){Marks[selected]=MarkGeometry.Moved(Marks[selected],dx,dy);Update();e.Handled=true;}
    }
    public override void Render(DrawingContext context)
    {
        context.DrawImage(Preview,new Rect(Preview.Size),Image);
        // The mark being drawn is shown at once as a thin outline; the rendered preview follows.
        if(active>=0&&active<Marks.Count)
        {
            var m=Marks[active];var pen=new Pen(new SolidColorBrush(Color.Parse(m.Color),m.Kind=="marker"?.55:1),Math.Max(1.5,m.Width*Scale*(m.Kind=="marker"?4:1)),lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round);
            Point S(double x,double y)=>new(Image.X+x*Scale,Image.Y+y*Scale);
            var box=new Rect(S(Math.Min(m.X1,m.X2),Math.Min(m.Y1,m.Y2)),S(Math.Max(m.X1,m.X2),Math.Max(m.Y1,m.Y2)));
            switch(m.Kind)
            {
                case "marker" or "freehand" when m.Points is {Count:>1} points:for(var i=1;i<points.Count;i++)context.DrawLine(pen,S(points[i-1].X,points[i-1].Y),S(points[i].X,points[i].Y));break;
                case "line" or "arrow":context.DrawLine(pen,S(m.X1,m.Y1),S(m.X2,m.Y2));break;
                case "ellipse" or "magnifier":context.DrawEllipse(null,pen,box);break;
                case "rectangle" or "callout" or "image":context.DrawRectangle(new Pen(pen.Brush,Math.Max(1.5,Math.Min(pen.Thickness,4))),box);break;
            }
        }
        if(selected>=0&&selected<Marks.Count){var r=MarkGeometry.Bounds(Marks[selected]);context.DrawRectangle(new Pen(Gold,2,new DashStyle([4,3],0)),ToScreen(r).Inflate(4));}
    }
}
