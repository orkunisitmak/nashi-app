using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
namespace Nashi.Linux.UI;

/// The Nashi wheel as on macOS (Sources/NashiApp/WheelGeometry.swift): rounded petals around a
/// centre disc, the first one at the top and the rest clockwise, a small gap between petals, the
/// chosen petal filled with gold and its name in the middle. Colours come from the theme, so the
/// wheel follows Classic, Glass, Frosted and Brutalist. One control serves the in-window wheel and
/// the gesture layer, where the hovered petal is found from the drag position.
public sealed class PetalWheel:Control
{
    private IReadOnlyList<ToolChoice> items=[];private int selected=-1;
    public IReadOnlyList<ToolChoice> Items{get=>items;set{items=value;selected=-1;InvalidateVisual();}}
    public int Selected{get=>selected;set{var next=value>=0&&value<items.Count?value:-1;if(next==selected)return;selected=next;if(next>=0)Sound.Tick();SelectionChanged?.Invoke(SelectedItem);InvalidateVisual();}}
    /// Settings → Appearance: the bloom when a wheel appears.
    public static bool Animate{get;set;}=true;
    /// Settings → Appearance: wheel size (the shared WheelScale preference, 0.85–1.4).
    public static double Scale{get;set;}=1;
    public ToolChoice? SelectedItem=>selected>=0?items[selected]:null;
    /// Shown in the centre when nothing is highlighted.
    public string CenterTitle{get;set;}="";
    public event Action<ToolChoice>? Chosen;
    public event Action<ToolChoice?>? SelectionChanged;

    public PetalWheel(double size=360)
    {
        Width=Height=size*Scale;Focusable=false;Cursor=new Cursor(StandardCursorType.Hand);
        ActualThemeVariantChanged+=(_,_)=>InvalidateVisual();
        PointerMoved+=(_,e)=>Selected=HitTest(e.GetPosition(this));
        PointerExited+=(_,_)=>Selected=-1;
        PointerReleased+=(_,e)=>{if(e.InitialPressMouseButton==MouseButton.Left&&HitTest(e.GetPosition(this)) is var n and >=0)Chosen?.Invoke(items[n]);};
        // A short bloom when the wheel appears (skipped when animations are off in Settings).
        if(!Animate)return;
        Opacity=0;RenderTransform=new ScaleTransform(.9,.9);RenderTransformOrigin=RelativePoint.Center;
        Transitions=[new DoubleTransition{Property=OpacityProperty,Duration=TimeSpan.FromMilliseconds(140)},new TransformOperationsTransition{Property=RenderTransformProperty,Duration=TimeSpan.FromMilliseconds(170),Easing=new Avalonia.Animation.Easings.CubicEaseOut()}];
        AttachedToVisualTree+=(_,_)=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>{Opacity=1;RenderTransform=Avalonia.Media.Transformation.TransformOperations.Parse("scale(1)");},Avalonia.Threading.DispatcherPriority.Render);
    }

    private double Size=>Math.Min(Bounds.Width>0?Bounds.Width:Width,Bounds.Height>0?Bounds.Height:Height);
    private Point Center=>new(Size/2,Size/2);
    private double Radius=>Size*.44;
    private double Inner=>Radius*.407;
    private double Delta=>360d/Math.Max(1,items.Count);
    // Screen angles grow clockwise (y points down). The first petal sits at the top, like macOS.
    private double Middle(int index)=>-90+index*Delta;

    /// The petal under a point in this control's coordinates, or -1 for the centre and outside.
    public int HitTest(Point point)
    {
        if(items.Count==0)return -1;var d=point-Center;var distance=Math.Sqrt(d.X*d.X+d.Y*d.Y);
        if(distance<Inner||distance>Radius+6)return -1;
        var angle=Math.Atan2(d.Y,d.X)*180/Math.PI;var offset=((angle+90+Delta/2)%360+360)%360;
        return Math.Min(items.Count-1,(int)(offset/Delta));
    }
    public void Choose(){if(SelectedItem is {} item)Chosen?.Invoke(item);}
    /// Where a petal's label sits, in this control's coordinates (used by tests and drag targeting).
    public Point PetalCenter(int index)=>Label(index);

    private IBrush Brush(string key,string fallback)=>this.FindResource(ActualThemeVariant,key) as IBrush??new SolidColorBrush(Color.Parse(fallback));
    public override void Render(DrawingContext context)
    {
        var surface=Brush("Surface","#FFFEF7");var line=Brush("WheelLine","#33C9962E");var ink=Brush("Ink","#243B2B");var gold=Brush("Gold","#E3B859");var goldInk=Brush("GoldInk","#243B2B");var muted=Brush("Muted","#596650");var inset=Brush("Inset","#F1EDE0");
        var brutal=line is ISolidColorBrush s&&s.Color.A==255&&s.Color.R<40;var stroke=new Pen(line,brutal?2.5:1.2);
        // Backdrop ring: a soft disc behind the petals so the wheel reads on any background.
        context.DrawEllipse(inset,new Pen(line,brutal?3:1),Center,Radius+8,Radius+8);
        for(var n=0;n<items.Count;n++)
        {
            var chosen=n==selected;context.DrawGeometry(chosen?gold:surface,stroke,Petal(n));
            var (symbol,label)=(items[n].Symbol,L10n.T(items[n].Title));var at=Label(n);var color=chosen?goldInk:ink;
            var compact=items.Count>10;var width=Math.Max(44,Math.Min(96,(Radius-Inner)*1.05*(items.Count>6?.9:1)));
            var text=new FormattedText(label,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(FontFamily.Default,FontStyle.Normal,chosen?FontWeight.Bold:FontWeight.SemiBold),compact?10.5:12,color){MaxTextWidth=width,MaxLineCount=2,TextAlignment=TextAlignment.Center,Trimming=TextTrimming.CharacterEllipsis};
            var showSymbol=!string.IsNullOrEmpty(symbol)&&symbol!="↗";
            var icon=showSymbol?new FormattedText(symbol,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(FontFamily.Default),compact?13:15,color){TextAlignment=TextAlignment.Center,MaxTextWidth=width}:null;
            var total=text.Height+(icon is null?0:icon.Height+1);var top=at.Y-total/2;
            if(icon is not null){context.DrawText(icon,new Point(at.X-width/2,top));top+=icon.Height+1;}
            context.DrawText(text,new Point(at.X-width/2,top));
        }
        // Centre disc with the highlighted name (or the wheel's own title).
        context.DrawEllipse(surface,stroke,Center,Inner-4,Inner-4);
        var title=SelectedItem is {} item?L10n.T(item.Title):CenterTitle;
        if(title.Length>0)
        {
            var center=new FormattedText(title,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(FontFamily.Default,FontStyle.Normal,FontWeight.Bold),13,SelectedItem is null?muted:ink){MaxTextWidth=(Inner-10)*1.8,MaxLineCount=3,TextAlignment=TextAlignment.Center,Trimming=TextTrimming.CharacterEllipsis};
            context.DrawText(center,new Point(Center.X-center.MaxTextWidth/2,Center.Y-center.Height/2));
        }
    }
    /// The label's spot: the centroid of the annular sector, as macOS computes it.
    private Point Label(int index)
    {
        var sweep=(Delta-1.8)*Math.PI/180;
        var centroid=items.Count>1?4*Math.Sin(sweep/2)*(Math.Pow(Radius,3)-Math.Pow(Inner,3))/(3*sweep*(Radius*Radius-Inner*Inner)):(Radius+Inner)/2;
        centroid=Math.Clamp(centroid,Inner+(Radius-Inner)*.38,Radius*.8);
        var angle=Middle(index)*Math.PI/180;return new(Center.X+centroid*Math.Cos(angle),Center.Y+centroid*Math.Sin(angle));
    }
    private Geometry Petal(int index)
    {
        // One item: a full ring.
        if(items.Count==1)return new CombinedGeometry(GeometryCombineMode.Exclude,new EllipseGeometry(new Rect(Center.X-Radius,Center.Y-Radius,2*Radius,2*Radius)),new EllipseGeometry(new Rect(Center.X-Inner,Center.Y-Inner,2*Inner,2*Inner)));
        const double gap=.9;var mid=Middle(index);var a=mid-Delta/2+gap;var b=mid+Delta/2-gap;
        var corner=Math.Min(10*Size/320,Inner*Delta*Math.PI/180/4);var outerInset=corner/Radius*180/Math.PI;var innerInset=corner/Inner*180/Math.PI;
        Point P(double r,double degrees)=>new(Center.X+r*Math.Cos(degrees*Math.PI/180),Center.Y+r*Math.Sin(degrees*Math.PI/180));
        var geometry=new StreamGeometry();
        using(var g=geometry.Open())
        {
            g.BeginFigure(P(Radius-corner,a),true);
            g.QuadraticBezierTo(P(Radius,a),P(Radius,a+outerInset));
            g.ArcTo(P(Radius,b-outerInset),new Size(Radius,Radius),0,b-a-2*outerInset>180,SweepDirection.Clockwise);
            g.QuadraticBezierTo(P(Radius,b),P(Radius-corner,b));
            g.LineTo(P(Inner+corner,b));
            g.QuadraticBezierTo(P(Inner,b),P(Inner,b-innerInset));
            g.ArcTo(P(Inner,a+innerInset),new Size(Inner,Inner),0,b-a-2*innerInset>180,SweepDirection.CounterClockwise);
            g.QuadraticBezierTo(P(Inner,a),P(Inner+corner,a));
            g.EndFigure(true);
        }
        return geometry;
    }
}
