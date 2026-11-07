using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Nashi.Core;
namespace Nashi.Windows;

// Building blocks shared by the windows. Button templates read Icon (a leading icon), Hint (a trailing
// shortcut), Radius, Lens (refracting glass), Lift (a shadow under the fill) and On, so Content stays the
// plain label. The Field text box style reads Icon, Lens, Hint (its placeholder) and Tag (a key hint).
public sealed class Ui
{
    private Ui(){}
    public static readonly DependencyProperty IconProperty=DependencyProperty.RegisterAttached("Icon",typeof(object),typeof(Ui),new(null));
    public static object? GetIcon(DependencyObject element)=>element.GetValue(IconProperty);
    public static void SetIcon(DependencyObject element,object? value)=>element.SetValue(IconProperty,value);
    // An icon (Icons) ahead of the label.
    internal static void SetIcon(DependencyObject element,string name,double size=15)=>element.SetValue(IconProperty,Icons.Make(name,size));
    // A dialog's own heading, shown after "Nashi" in its toolbar.
    public static readonly DependencyProperty HeadingProperty=DependencyProperty.RegisterAttached("Heading",typeof(string),typeof(Ui),new(""));
    public static string GetHeading(DependencyObject element)=>(string)element.GetValue(HeadingProperty);
    public static void SetHeading(DependencyObject element,string value)=>element.SetValue(HeadingProperty,value);
    public static readonly DependencyProperty IconOnlyProperty=DependencyProperty.RegisterAttached("IconOnly",typeof(bool),typeof(Ui),new(false));
    public static bool GetIconOnly(DependencyObject element)=>(bool)element.GetValue(IconOnlyProperty);
    public static void SetIconOnly(DependencyObject element,bool value)=>element.SetValue(IconOnlyProperty,value);
    public static readonly DependencyProperty LensProperty=DependencyProperty.RegisterAttached("Lens",typeof(string),typeof(Ui),new(null));
    public static string? GetLens(DependencyObject element)=>(string?)element.GetValue(LensProperty);
    public static void SetLens(DependencyObject element,string? value)=>element.SetValue(LensProperty,value);
    public static readonly DependencyProperty HintProperty=DependencyProperty.RegisterAttached("Hint",typeof(string),typeof(Ui),new(null));
    public static string? GetHint(DependencyObject element)=>(string?)element.GetValue(HintProperty);
    public static void SetHint(DependencyObject element,string? value)=>element.SetValue(HintProperty,value);
    public static readonly DependencyProperty RadiusProperty=DependencyProperty.RegisterAttached("Radius",typeof(double),typeof(Ui),new(10d));
    public static double GetRadius(DependencyObject element)=>(double)element.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject element,double value)=>element.SetValue(RadiusProperty,value);
    public static readonly DependencyProperty LiftProperty=DependencyProperty.RegisterAttached("Lift",typeof(bool),typeof(Ui),new(false));
    public static bool GetLift(DependencyObject element)=>(bool)element.GetValue(LiftProperty);
    public static void SetLift(DependencyObject element,bool value)=>element.SetValue(LiftProperty,value);
    public static readonly DependencyProperty OnProperty=DependencyProperty.RegisterAttached("On",typeof(bool),typeof(Ui),new(false));
    public static bool GetOn(DependencyObject element)=>(bool)element.GetValue(OnProperty);
    public static void SetOn(DependencyObject element,bool value)=>element.SetValue(OnProperty,value);

    internal static Style Style(string key)=>(Style)Application.Current.FindResource(key);
    internal static TextBlock Icon(string glyph,double size=16,string brush="Ink")
    {
        var icon=new TextBlock{Text=glyph,FontSize=size,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};icon.SetResourceReference(TextBlock.FontFamilyProperty,"IconFont");icon.SetResourceReference(TextBlock.ForegroundProperty,brush);return icon;
    }
    internal static Border Keycap(string text)
    {
        var label=L10n.Label(new TextBlock{Text=text,FontSize=11,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center});label.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
        var cap=new Border{MinWidth=21,Height=21,Padding=new(5,0,5,0),CornerRadius=new(6),BorderThickness=new(1,1,1,2),Child=label,VerticalAlignment=VerticalAlignment.Center};cap.SetResourceReference(Border.BackgroundProperty,"Keycap");cap.SetResourceReference(Border.BorderBrushProperty,"Line");return cap;
    }
    internal static Border Card(UIElement? child,double radius=20,Thickness? padding=null)
    {
        var card=new Border{CornerRadius=new(radius),Padding=padding??new(16),BorderThickness=new(1),Child=child};card.SetResourceReference(Border.BackgroundProperty,"Card");card.SetResourceReference(Border.BorderBrushProperty,"Line");return card;
    }
    // A floating glass capsule (title, dock): the refracted backdrop, a translucent fill and the lit rim.
    internal static Border GlassCapsule(UIElement child,string fill,double radius=99,string lens="control")
    {
        var face=new RoundBorder{Radius=radius,BorderThickness=new(1),Padding=new(16,0,16,0),Child=child};face.SetResourceReference(Border.BackgroundProperty,fill);face.SetResourceReference(Border.BorderBrushProperty,"Rim");
        var host=new Grid();host.Children.Add(new Lens{Preset=lens,Radius=radius});host.Children.Add(face);return new Border{Child=host,VerticalAlignment=VerticalAlignment.Center,Effect=null};
    }
    // A floating glass panel for options: what is under it frosted (blur 22, as in the design), a panel tint, lit rim, soft shadow underneath.
    internal static Border GlassPanel(UIElement child)
    {
        var shadow=new Border{CornerRadius=new(22),Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=40,ShadowDepth=12,Direction=270,Opacity=.2}};shadow.SetResourceReference(Border.BackgroundProperty,"Panel");
        var face=new Border{CornerRadius=new(22),BorderThickness=new(1),Padding=new(18,16,18,18),Child=child};face.SetResourceReference(Border.BackgroundProperty,"Panel");face.SetResourceReference(Border.BorderBrushProperty,"Rim");
        var host=new Grid();host.Children.Add(shadow);host.Children.Add(new Lens{Preset="panel",Radius=22});host.Children.Add(face);return new Border{Child=host};
    }
    internal static Border TileIcon(string icon)
    {
        var tile=new Border{Width=34,Height=34,CornerRadius=new(10),BorderThickness=new(1),Child=Icons.Make(icon,17),VerticalAlignment=VerticalAlignment.Center};tile.SetResourceReference(TextElement.ForegroundProperty,"Ink");tile.SetResourceReference(Border.BackgroundProperty,"Well");tile.SetResourceReference(Border.BorderBrushProperty,"Line");return tile;
    }
    // The Nashi icon, small, where the app names itself (toolbars, title rows).
    private static readonly System.Windows.Media.Imaging.BitmapImage icon=LoadIcon();
    private static System.Windows.Media.Imaging.BitmapImage LoadIcon()
    {
        var image=new System.Windows.Media.Imaging.BitmapImage();image.BeginInit();image.UriSource=new Uri("pack://application:,,,/Assets/NashiIcon.png");image.DecodePixelWidth=96;image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();return image;
    }
    internal static Viewbox Pear(double size)
    {
        var image=new Image{Source=icon,Width=96,Height=96};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
        return new Viewbox{Width=size,Height=size,Child=image,VerticalAlignment=VerticalAlignment.Center};
    }
    // A file as the design shows it: pictures and videos as rounded thumbnails (videos with a play mark),
    // other files as a page with a colored label for their type.
    internal static FrameworkElement Thumb(string path,double size)
    {
        var extension=System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if(FeatureCatalog.IsImage(path)||FeatureCatalog.IsVideo(path))
        {
            var image=new Image{Width=size,Height=size,Stretch=Stretch.UniformToFill,Clip=new RectangleGeometry(new Rect(0,0,size,size),size/4,size/4)};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);FileArtwork.BindThumbnail(image,path);
            var tile=new Grid{Width=size,Height=size,VerticalAlignment=VerticalAlignment.Center};tile.Children.Add(image);var ring=new Border{CornerRadius=new(size/4),BorderThickness=new(1)};ring.SetResourceReference(Border.BorderBrushProperty,"Line");tile.Children.Add(ring);
            if(FeatureCatalog.IsVideo(path)){var play=new System.Windows.Shapes.Path{Data=PlayMark,Fill=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new(2,0,0,0),Effect=PlayShadow};tile.Children.Add(play);}
            return tile;
        }
        var color=extension switch{"pdf"=>"#E5484D","doc" or "docx" or "rtf" or "odt" or "pages" or "epub"=>"#2F6FED","xls" or "xlsx" or "csv" or "numbers" or "ods"=>"#30A46C","ppt" or "pptx" or "key" or "odp"=>"#F76B15","mp3" or "m4a" or "wav" or "flac" or "ogg" or "opus" or "aac" or "aiff" or "aif" or "wma"=>"#7A5AF0","zip" or "rar" or "7z" or "tar" or "gz" or "bz2" or "xz"=>"#8B8D93",_=>"#5B5D63"};
        var page=new Grid{Width=30,Height=36};
        page.Children.Add(new System.Windows.Shapes.Path{Data=PageOutline,Fill=PageFill,Stroke=PageLine});
        page.Children.Add(new System.Windows.Shapes.Path{Data=PageFold,Fill=FoldFill,Stroke=PageLine});
        if(!LabelFills.TryGetValue(color,out var fill))LabelFills[color]=fill=Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
        var label=new Border{Width=22,Height=9,CornerRadius=new(2),Margin=new(4,21,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Background=fill,Child=new TextBlock{Text=extension.Length>4?extension[..4].ToUpperInvariant():extension.ToUpperInvariant(),FontSize=6.6,FontWeight=FontWeights.Bold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};page.Children.Add(label);
        return new Viewbox{Width=size,Height=size,Child=page,VerticalAlignment=VerticalAlignment.Center};
    }
    // The tiles' fixed shapes and paints, made once and shared by every row (Library and Float lists draw many).
    private static readonly Geometry PlayMark=Frozen(Geometry.Parse("M0,0 L9,6 L0,12 Z")),PageOutline=Frozen(Geometry.Parse("M3,1.5 H19 L27,9.5 V33 A1.5,1.5 0 0 1 25.5,34.5 H3.5 A1.5,1.5 0 0 1 2,33 V3 A1.5,1.5 0 0 1 3.5,1.5 Z")),PageFold=Frozen(Geometry.Parse("M19,1.5 V9.5 H27"));
    private static readonly Brush PageLine=Frozen(new SolidColorBrush(Color.FromArgb(41,0,0,0))),PageFill=Frozen(new SolidColorBrush(Color.FromRgb(251,251,250))),FoldFill=Frozen(new SolidColorBrush(Color.FromRgb(237,236,233)));
    private static readonly System.Windows.Media.Effects.DropShadowEffect PlayShadow=Frozen(new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=4,ShadowDepth=1,Direction=270,Opacity=.5});
    private static readonly Dictionary<string,Brush> LabelFills=[];
    private static T Frozen<T>(T value)where T:Freezable{value.Freeze();return value;}
    internal static TextBlock Text(string value,double size=13,string brush="Ink",FontWeight? weight=null,bool localize=true)
    {
        var text=new TextBlock{Text=value,FontSize=size,FontWeight=weight??FontWeights.Normal,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
        if(size>=20){text.SetResourceReference(TextBlock.FontFamilyProperty,"DisplayFont");}
        text.SetResourceReference(TextBlock.ForegroundProperty,brush);return localize?L10n.Label(text):text;
    }
}

// A capsule of segments; the chosen one rests on a glass droplet taller than the track (36 px from the top
// inset, as in the design), which glides with a spring. Segments are plain buttons so their labels stay searchable.
// In a toolbar it is glass with a lit rim; inside a group it sits on a quiet fill.
internal sealed class Segmented:RoundBorder
{
    private readonly RoundBorder thumb=new(){Radius=99,Height=36,Width=0,BorderThickness=new(1),IsHitTestVisible=false};
    private readonly Lens lens=new();
    private readonly StackPanel row=new(){Orientation=Orientation.Horizontal};
    private readonly bool small;
    internal readonly List<Button> Items=[];
    internal int Selected{get;private set;}=-1;
    internal Segmented(bool small=false,bool glass=false)
    {
        this.small=small;Radius=99;Padding=new(3);VerticalAlignment=VerticalAlignment.Center;if(!glass)SetResourceReference(BackgroundProperty,"Sel");
        thumb.SetResourceReference(BackgroundProperty,"Raised");thumb.SetResourceReference(BorderBrushProperty,"ThumbRim");thumb.SetResourceReference(EffectProperty,"ThumbShadow");
        // Glass: the refracted backdrop, then the translucent face and its lit rim, then the droplet (on a canvas, so it can hang below the track).
        var grid=new Grid();if(glass){lens.Preset="control";lens.Margin=new(-3);grid.Children.Add(lens);var face=new RoundBorder{Radius=99,Margin=new(-3),BorderThickness=new(1)};face.SetResourceReference(BackgroundProperty,"Glass");face.SetResourceReference(BorderBrushProperty,"Rim");grid.Children.Add(face);}
        grid.Children.Add(new Canvas{Children={thumb}});grid.Children.Add(row);Child=grid;
        row.SizeChanged+=(_,_)=>Place(false);
    }
    internal Button Add(string title,Action action)
    {
        var index=Items.Count;var button=MainWindow.Button(title,()=>{Select(index);action();});button.Style=Ui.Style(small?"SegmentSmall":"Segment");Items.Add(button);row.Children.Add(button);return button;
    }
    internal void Select(int index)
    {
        var animate=Selected>=0&&IsLoaded;Selected=index;for(var n=0;n<Items.Count;n++)Ui.SetOn(Items[n],n==index);
        if(IsLoaded)Dispatcher.BeginInvoke(()=>Place(animate),System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void Place(bool animate)
    {
        if(Selected<0||Selected>=Items.Count)return;var item=Items[Selected];if(item.ActualWidth<=0)return;
        Glide.Move(thumb,item.TranslatePoint(new Point(0,0),row).X,item.ActualWidth,animate);
    }
}

// The segment droplet's motion, shared by Segmented and the SegmentedCombo style.
internal static class Glide
{
    internal static void Place(ComboBox box,bool animate)
    {
        if(box.Template?.FindName("PART_Thumb",box) is not FrameworkElement thumb||thumb.Parent is not FrameworkElement host)return;
        if(box.SelectedIndex<0||box.ItemContainerGenerator.ContainerFromIndex(box.SelectedIndex) is not FrameworkElement item||item.ActualWidth<=0||!item.IsDescendantOf(box)){thumb.Visibility=Visibility.Hidden;return;}
        thumb.Visibility=Visibility.Visible;Move(thumb,item.TranslatePoint(new Point(0,0),host).X,item.ActualWidth,animate);
    }
    internal static void Move(FrameworkElement thumb,double x,double width,bool animate)
    {
        if(thumb.RenderTransform is not TranslateTransform offset){offset=new TranslateTransform();thumb.RenderTransform=offset;}
        if(!animate||thumb.Width<=0||!WheelPresentation.Preferences.Animations||!SystemParameters.ClientAreaAnimation){offset.BeginAnimation(TranslateTransform.XProperty,null);thumb.BeginAnimation(FrameworkElement.WidthProperty,null);offset.X=x;thumb.Width=width;return;}
        var time=TimeSpan.FromMilliseconds(420);
        offset.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(x,time){EasingFunction=Spring.Shared});thumb.BeginAnimation(FrameworkElement.WidthProperty,new DoubleAnimation(width,time){EasingFunction=Spring.Shared});
    }
}

// The design's spring (its CSS linear() points): quick, a 1.7% overshoot, then it settles.
internal sealed class Spring:EasingFunctionBase
{
    internal static readonly Spring Shared=Make();
    private static Spring Make(){var spring=new Spring();spring.Freeze();return spring;}
    private static readonly (double T,double V)[] Points=[(0,0),(.014,.006),(.028,.025),(.061,.101),(.189,.539),(.253,.721),(.315,.849),(.381,.937),(.418,.968),(.457,.991),(.501,1.006),(.55,1.015),(.639,1.017),(.854,1.001),(1,1)];
    public Spring(){EasingMode=EasingMode.EaseIn;}
    protected override double EaseInCore(double t)
    {
        for(var i=1;i<Points.Length;i++)if(t<=Points[i].T){var (t0,v0)=Points[i-1];var (t1,v1)=Points[i];return v0+(v1-v0)*(t-t0)/(t1-t0);}
        return 1;
    }
    protected override Freezable CreateInstanceCore()=>new Spring();
}
