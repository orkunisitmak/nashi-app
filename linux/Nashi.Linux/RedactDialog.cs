using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
namespace Nashi.Linux;

/// Redact Photo and Redact Video: drag boxes over what to hide, click one to select, move or resize it,
/// Delete removes it. The preview shows the chosen cover style drawn by the same code that saves the file.
internal sealed class RedactDialog:Window
{
    private static readonly (string Label,string Style)[] Covers=[("Black box","solid"),("Blur","blur"),("Pixelate","pixelate")];
    private readonly RegionCanvas canvas;
    private readonly Bitmap original;
    private readonly byte[] source;
    private readonly ComboBox style=new(){ItemsSource=Covers.Select(s=>s.Label).ToArray(),SelectedIndex=0,MinWidth=120};
    private readonly TextBlock count=new(){VerticalAlignment=VerticalAlignment.Center,MinWidth=90};
    private readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(80)};
    private int generation;
    public Redaction? Result{get;private set;}

    private RedactDialog(string title,string note,Bitmap preview,byte[] png,PixelSize pixels)
    {
        original=preview;source=png;Title=title;Width=880;Height=660;MinWidth=560;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        canvas=new(preview,pixels){Focusable=true,Margin=new(16)};canvas.Changed+=Changed;
        style.SelectionChanged+=(_,_)=>{Changed();canvas.Focus();};
        var undo=new Button{Content="Undo"};undo.Click+=(_,_)=>{canvas.RemoveLast();canvas.Focus();};
        var clear=new Button{Content="Clear"};clear.Click+=(_,_)=>{canvas.Clear();canvas.Focus();};
        var ok=new Button{Content="Redact",IsDefault=true,FontWeight=Avalonia.Media.FontWeight.SemiBold,Background=NashiTheme.Primary,Foreground=new SolidColorBrush(Color.Parse("#243B2B"))};ok.Click+=(_,_)=>Accept();
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();
        var bar=new DockPanel{Margin=new(16,10),Children=
        {
            new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,Children={cancel,ok}},
            new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=10,Children={new TextBlock{Text="Cover",VerticalAlignment=VerticalAlignment.Center},style,undo,clear,count}}
        }};
        DockPanel.SetDock(bar.Children[0],Dock.Right);
        var root=new DockPanel{Children={bar,new TextBlock{Text=note+" · drag over what to hide, click a box to move or resize it, Delete removes it",Margin=new(16,10,16,0),FontSize=12,Opacity=.75,TextTrimming=TextTrimming.CharacterEllipsis},canvas}};
        DockPanel.SetDock(bar,Dock.Bottom);DockPanel.SetDock(root.Children[1],Dock.Top);
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");Content=root;
        debounce.Tick+=async(_,_)=>{debounce.Stop();await Render();};
        Opened+=(_,_)=>{canvas.Focus();Changed();};
        Closed+=(_,_)=>{debounce.Stop();generation++;if(canvas.Preview!=original)canvas.Preview.Dispose();original.Dispose();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Accept();e.Handled=true;}};
    }

    public static Task<RedactDialog> CreateAsync(string title,string input)=>Load(title,$"{Path.GetFileName(input)}",()=>PhotoCodec.Read(input));
    /// Video boxes cover the whole clip; the frame only helps to place them.
    public static Task<RedactDialog> CreateAsync(string title,string input,byte[] frame)=>Load(title,Path.GetFileName(input)+Localizer.Pick(" · boxes cover the whole video"," · kutular videonun tamamını kapatır"),()=>new MagickImage(frame));
    private static async Task<RedactDialog> Load(string title,string note,Func<MagickImage> load)
    {
        var (preview,pixels)=await Picture.LoadAsync(load);
        // The editor redraws from the bounded preview, never from the full-size file.
        using var stream=new MemoryStream();preview.Save(stream);
        return new(title,$"{note} · {pixels.Width} × {pixels.Height}",preview,stream.ToArray(),pixels);
    }

    private Redaction Current()=>new(canvas.Areas.Select(a=>new ImageEdit(a.X/canvas.Pixels.Width,a.Y/canvas.Pixels.Height,a.Width/canvas.Pixels.Width,a.Height/canvas.Pixels.Height)).ToArray(),Covers[Math.Max(0,style.SelectedIndex)].Style);
    private void Accept(){if(canvas.Areas.Count==0)return;Result=Current();Close();}
    private void Changed(){count.Text=canvas.Areas.Count switch{0=>Localizer.Pick("No boxes yet","Henüz kutu yok"),1=>Localizer.Pick("1 box","1 kutu"),var n=>Localizer.Pick($"{n} boxes",$"{n} kutu")};debounce.Stop();debounce.Start();}

    /// Only the newest request is shown, so dragging never flickers back to an older state.
    private async Task Render()
    {
        var current=++generation;var redaction=Current();
        if(redaction.Areas.Length==0){Show(original);return;}
        var scale=original.PixelSize.Width/(double)canvas.Pixels.Width;
        byte[] png;
        try{png=await Task.Run(()=>{using var image=new MagickImage(source);Compositions.Redact(image,redaction,scale);return Picture.Encode(image);});}
        catch(Exception error)when(error is ArgumentException or MagickException){return;}
        if(current!=generation||!IsVisible)return;
        using var stream=new MemoryStream(png);Show(new Bitmap(stream));
    }
    private void Show(Bitmap bitmap){var previous=canvas.Preview;canvas.Preview=bitmap;if(previous!=original&&previous!=bitmap)previous.Dispose();}
}

internal sealed class RegionCanvas(Bitmap preview,PixelSize pixels):PictureCanvas(preview,pixels)
{
    private enum Mode{None,Create,Move,Resize}
    public List<Rect> Areas{get;}=[];
    public event Action? Changed;
    private int selected=-1;
    private Mode mode;
    private Point anchor,origin;
    private Rect start;

    public void RemoveLast(){if(Areas.Count==0)return;Areas.RemoveAt(Areas.Count-1);selected=Areas.Count-1;Update();}
    public void Clear(){Areas.Clear();selected=-1;Update();}
    private void Update(){Changed?.Invoke();InvalidateVisual();}

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        Focus();var p=e.GetPosition(this);origin=ToPixels(p);
        var corner=selected>=0?Corner(ToScreen(Areas[selected]),p):-1;
        // The newest box is on top, so hit-test from the end.
        var hit=Areas.FindLastIndex(a=>ToScreen(a).Contains(p));
        if(corner>=0){mode=Mode.Resize;start=Areas[selected];anchor=Corners(start)[(corner+2)%4];}
        else if(hit>=0){selected=hit;start=Areas[hit];mode=Mode.Move;InvalidateVisual();}
        else{mode=Mode.Create;anchor=origin;selected=-1;InvalidateVisual();}
        e.Pointer.Capture(this);e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);var p=e.GetPosition(this);
        if(mode==Mode.None)
        {
            Cursor=new(selected>=0&&Corner(ToScreen(Areas[selected]),p)>=0?StandardCursorType.SizeAll:Areas.Any(a=>ToScreen(a).Contains(p))?StandardCursorType.Hand:StandardCursorType.Cross);return;
        }
        var point=ToPixels(p);
        if(mode==Mode.Move){Areas[selected]=Clamp(start.Translate(new Vector(point.X-origin.X,point.Y-origin.Y)));Update();return;}
        if(Math.Abs(point.X-anchor.X)<Minimum||Math.Abs(point.Y-anchor.Y)<Minimum)return;
        var area=Clamp(new Rect(anchor,point).Normalize());
        if(mode==Mode.Create){Areas.Add(area);selected=Areas.Count-1;mode=Mode.Resize;}else Areas[selected]=area;
        Update();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);mode=Mode.None;e.Pointer.Capture(null);}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);if(selected<0)return;
        if(e.Key is Key.Delete or Key.Back){Areas.RemoveAt(selected);selected=-1;Update();e.Handled=true;return;}
        var step=(e.KeyModifiers&KeyModifiers.Shift)!=0?10:1;
        var move=e.Key switch{Key.Left=>new Vector(-step,0),Key.Right=>new Vector(step,0),Key.Up=>new Vector(0,-step),Key.Down=>new Vector(0,step),_=>(Vector?)null};
        if(move is {} v){Areas[selected]=Clamp(Areas[selected].Translate(v));Update();e.Handled=true;}
    }

    public override void Render(DrawingContext context)
    {
        context.DrawImage(Preview,new Rect(Preview.Size),Image);
        for(var n=0;n<Areas.Count;n++)
        {
            var s=ToScreen(Areas[n]);
            context.DrawRectangle(new Pen(n==selected?Gold:Brushes.White,n==selected?2:1.5,n==selected?null:new DashStyle([4,3],0)),s);
            if(n==selected)Handles(context,s);
        }
    }
}
