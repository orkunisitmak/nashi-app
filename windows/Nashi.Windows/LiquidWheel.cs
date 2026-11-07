using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nashi.Core;
namespace Nashi.Windows;

// The wheel as one frosted disc. Icon chips sit on a ring;
// the one aimed at fills and lifts while the others step back, and the hub says its name plainly. A soft beam leans
// toward it and a notch on the hub follows the pointer. The petals stay as invisible sectors: they are what the pointer
// (and the tests) hit. Only transforms and opacity ever move, and still layers are kept as bitmaps on the graphics card.
internal sealed class WheelFace
{
    internal const double Middle=270,Disc=470,Ring=160,HubSize=164,Inner=96,Outer=250;
    internal Canvas Canvas{get;}=new(){Width=540,Height=540};
    internal List<System.Windows.Shapes.Path> Petals{get;}=[];
    internal List<FrameworkElement> Captions{get;}=[];
    private readonly IReadOnlyList<ToolChoice> choices;private readonly string title,idle;
    private readonly List<Grid> faces=[];private readonly List<UIElement> lit=[];private readonly List<Vector> offsets=[];private readonly List<UIElement> glass=[];
    private readonly Grid hub=new(){Width=HubSize,Height=HubSize,RenderTransformOrigin=new(.5,.5),RenderTransform=new ScaleTransform(),IsHitTestVisible=false};
    private readonly StackPanel say=new(){VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,RenderTransform=new TranslateTransform()};
    private readonly TextBlock sayIcon=new(){FontSize=34,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,0,0,4)},sayName=new(){FontSize=23,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=HubSize-30},sayHint=new(){FontSize=17,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=HubSize-30,Margin=new(0,3,0,0)};
    private readonly WheelBeam beam=new();private readonly Ellipse notch=new(){Width=9,Height=9,Opacity=0,IsHitTestVisible=false};private readonly RotateTransform turn=new(0,4.5,4.5);
    private int hot=-1;private bool aiming;private readonly List<(double Lit,double Fade,double Grow)> looks=[];
    private static readonly IEasingFunction Out=Frozen(new ExponentialEase{EasingMode=EasingMode.EaseOut,Exponent=5}),In=Frozen(new CubicEase{EasingMode=EasingMode.EaseIn});
    private static IEasingFunction Frozen(EasingFunctionBase easing){easing.Freeze();return easing;}
    internal static bool Moving=>WheelPresentation.Preferences.Animations&&SystemParameters.ClientAreaAnimation;
    internal static double Angle(int index,int count)=>(WheelPresentation.StartAngle(count)+360d*index/count)*Math.PI/180;
    internal WheelFace(IReadOnlyList<ToolChoice> choices,string title,string idle,bool buttons)
    {
        this.choices=choices;this.title=title;this.idle=idle;var count=choices.Count;
        // The glass: a shadow under the disc, what is under the wheel (frosted hard, so it never competes), the tint, light from above.
        Ellipse Round(){var round=new Ellipse{Width=Disc,Height=Disc,IsHitTestVisible=false};Canvas.SetLeft(round,Middle-Disc/2);Canvas.SetTop(round,Middle-Disc/2);return round;}
        var shade=Round();shade.SetResourceReference(Shape.FillProperty,"Menu");shade.Effect=new DropShadowEffect{BlurRadius=46,ShadowDepth=12,Direction=270,Opacity=Theme.Dark?.55:.26};glass.Add(shade);
        if(WheelPresentation.Clear){var lens=new Lens{Preset="orb",Radius=Disc/2,Width=Disc,Height=Disc};Canvas.SetLeft(lens,Middle-Disc/2);Canvas.SetTop(lens,Middle-Disc/2);glass.Add(lens);}
        var disc=Round();disc.SetResourceReference(Shape.FillProperty,WheelPresentation.Clear?"GlassStrong":"Menu");disc.SetResourceReference(Shape.StrokeProperty,"Rim");disc.StrokeThickness=1.5;glass.Add(disc);
        var light=new RadialGradientBrush(Color.FromArgb((byte)(Theme.Dark?26:96),255,255,255),Color.FromArgb(0,255,255,255)){Center=new(.5,-.12),GradientOrigin=new(.5,-.12),RadiusX=.62,RadiusY=.7};light.Freeze();var sheen=Round();sheen.Fill=light;glass.Add(sheen);
        foreach(var layer in glass){layer.CacheMode=new BitmapCache();Canvas.Children.Add(layer);}
        Canvas.Children.Add(beam);
        for(var i=0;i<count;i++)
        {
            var angle=Angle(i,count);var half=(180d/count-1)*Math.PI/180;var a=angle-half;var b=angle+half;
            Point P(double r,double v)=>new(Middle+r*Math.Cos(v),Middle+r*Math.Sin(v));
            var geometry=new StreamGeometry();using(var g=geometry.Open()){g.BeginFigure(P(Inner,a),true,true);g.LineTo(P(Outer,a),true,false);g.ArcTo(P(Outer,b),new Size(Outer,Outer),0,b-a>Math.PI,SweepDirection.Clockwise,true,false);g.LineTo(P(Inner,b),true,false);g.ArcTo(P(Inner,a),new Size(Inner,Inner),0,b-a>Math.PI,SweepDirection.Counterclockwise,true,false);}geometry.Freeze();
            var petal=new System.Windows.Shapes.Path{Data=geometry,Fill=Brushes.Transparent,IsHitTestVisible=buttons};Petals.Add(petal);Canvas.Children.Add(petal);
        }
        // Chips: the icon alone (or a format's name); the hub says the rest.
        var size=count<=6?80d:count<=8?74:count<=10?62:count<=12?50:Math.Floor(600d/count);
        for(var i=0;i<count;i++)
        {
            var angle=Angle(i,count);var offset=new Vector(Ring*Math.Cos(angle),Ring*Math.Sin(angle));offsets.Add(offset);var face=Face(choices[i],size,out var glow);faces.Add(face);lit.Add(glow);looks.Add((0,1,1));
            FrameworkElement host=buttons?new Button{Style=(Style)Application.Current.FindResource("WheelLabelButton"),Content=face,Width=size,Height=size,Background=Brushes.Transparent,Padding=new(0),BorderThickness=new(0)}:new Border{Child=face,Width=size,Height=size,IsHitTestVisible=false};
            System.Windows.Automation.AutomationProperties.SetName(host,L10n.Text(choices[i].Title));Canvas.SetLeft(host,Middle+offset.X-size/2);Canvas.SetTop(host,Middle+offset.Y-size/2);Captions.Add(host);Canvas.Children.Add(host);
        }
        // The hub: what you will get, said plainly (icon and name); the wheel's name and a hint until something is aimed at.
        var plate=new Ellipse{StrokeThickness=1,Effect=new DropShadowEffect{BlurRadius=22,ShadowDepth=8,Direction=270,Opacity=Theme.Dark?.4:.14},CacheMode=new BitmapCache()};plate.SetResourceReference(Shape.FillProperty,"Menu");plate.SetResourceReference(Shape.StrokeProperty,"Line");hub.Children.Add(plate);
        sayIcon.SetResourceReference(TextBlock.FontFamilyProperty,"IconFont");sayName.SetResourceReference(TextBlock.FontFamilyProperty,"DisplayFont");foreach(var text in new[]{sayIcon,sayName})text.SetResourceReference(TextBlock.ForegroundProperty,"Ink");sayHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        say.Children.Add(sayIcon);say.Children.Add(sayName);say.Children.Add(sayHint);hub.Children.Add(say);Canvas.SetLeft(hub,Middle-HubSize/2);Canvas.SetTop(hub,Middle-HubSize/2);Canvas.Children.Add(hub);
        notch.SetResourceReference(Shape.FillProperty,"Ink");notch.RenderTransform=new TransformGroup{Children={new TranslateTransform(HubSize/2+12,0),turn}};Canvas.SetLeft(notch,Middle-4.5);Canvas.SetTop(notch,Middle-4.5);Canvas.Children.Add(notch);
        Say(-1,false);
    }
    // A chip: a light plate with the mark; lit, a solid plate with the same mark in reverse, lifted on a soft shadow.
    private static Grid Face(ToolChoice choice,double size,out UIElement glow)
    {
        var face=new Grid{Width=size,Height=size,RenderTransformOrigin=new(.5,.5),RenderTransform=new TransformGroup{Children={new ScaleTransform(),new TranslateTransform()}},CacheMode=new BitmapCache{RenderAtScale=1.5}};
        var plate=new Ellipse{StrokeThickness=1};plate.SetResourceReference(Shape.FillProperty,"Card");plate.SetResourceReference(Shape.StrokeProperty,"Line");face.Children.Add(plate);face.Children.Add(Mark(choice,size,"Ink"));
        var solid=new Ellipse();solid.SetResourceReference(Shape.FillProperty,"Solid");
        glow=new Grid{Opacity=0,Children={solid,Mark(choice,size,"OnSolid")},Effect=new DropShadowEffect{BlurRadius=26,ShadowDepth=8,Direction=270,Opacity=.26}};face.Children.Add(glow);
        // Named for screen readers and the tests; the hub shows the name to the eye.
        face.Children.Add(new TextBlock{Text=L10n.Text(choice.Title),Visibility=Visibility.Collapsed});
        return face;
    }
    private static UIElement Mark(ToolChoice choice,double size,string ink)
    {
        if(choice.Placement is {} placement)
        {
            // Where the window will go: the screen, and the part of it the window takes.
            double w=Math.Round(size*.46),h=Math.Round(size*.32);var glyph=new Canvas{Width=w,Height=h,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            var frame=new Rectangle{Width=w,Height=h,RadiusX=4,RadiusY=4,StrokeThickness=2};frame.SetResourceReference(Shape.StrokeProperty,ink);glyph.Children.Add(frame);
            var (start,end)=placement.Range();var part=new Rectangle{Width=(w-8)*(end-start),Height=h-8,RadiusX=2,RadiusY=2,Opacity=.75};part.SetResourceReference(Shape.FillProperty,ink);Canvas.SetLeft(part,4+(w-8)*start);Canvas.SetTop(part,4);glyph.Children.Add(part);return glyph;
        }
        if(WheelPresentation.Icon(choice.Id) is {} icon){var symbol=new TextBlock{Text=icon,FontSize=Math.Round(size*.4),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};symbol.SetResourceReference(TextBlock.FontFamilyProperty,"IconFont");symbol.SetResourceReference(TextBlock.ForegroundProperty,ink);return symbol;}
        // Formats (and anything without an icon) read as their name, made smaller only if it would not fit.
        var word=new TextBlock{Text=L10n.Text(choice.Title),FontSize=Math.Round(size*.27),FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center};word.SetResourceReference(TextBlock.FontFamilyProperty,"DisplayFont");word.SetResourceReference(TextBlock.ForegroundProperty,ink);
        return new Viewbox{Child=word,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,MaxWidth=size*.74,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    }
    private void Say(int index,bool animate=true)
    {
        var icon=index>=0&&choices[index].Placement is null?WheelPresentation.Icon(choices[index].Id):null;sayIcon.Text=icon??"";sayIcon.Visibility=icon is null?Visibility.Collapsed:Visibility.Visible;
        sayName.Text=L10n.Text(index>=0?choices[index].Title:title);sayHint.Text=index>=0?"":L10n.Text(idle);sayHint.Visibility=index>=0?Visibility.Collapsed:Visibility.Visible;
        if(!animate||!Moving)return;say.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(200)){EasingFunction=Out});((TranslateTransform)say.RenderTransform).BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(4,0,TimeSpan.FromMilliseconds(200)){EasingFunction=Out});
    }
    // The aimed chip (pointer or keyboard); -1 for none.
    internal void Hot(int index)
    {
        if(index==hot||index>=faces.Count)return;hot=index;
        // Only the chips whose look changes move: usually just the one left and the one arrived at.
        for(var i=0;i<faces.Count;i++)
        {
            var on=i==index;(double Lit,double Fade,double Grow) goal=(on?1:0,index<0||on?1:.72,on?1.16:1);if(goal==looks[i])continue;
            if(goal.Lit!=looks[i].Lit)Fade(lit[i],goal.Lit,140);if(goal.Fade!=looks[i].Fade)Fade(faces[i],goal.Fade,200);if(goal.Grow!=looks[i].Grow)Grow(Scale(faces[i]),goal.Grow,420);looks[i]=goal;
        }
        beam.Move(index,faces.Count);Say(index);if(index>=0&&!aiming)Point(Angle(index,faces.Count)*180/Math.PI);
    }
    // The notch follows the pointer around the hub; over the hub it rests.
    internal void Aim(Point at)
    {
        var d=at-new Point(Middle,Middle);if(d.Length<HubSize/2-4){aiming=false;Fade(notch,0,160);return;}
        aiming=true;Fade(notch,.55,160);Point(Math.Atan2(d.Y,d.X)*180/Math.PI);
    }
    private void Point(double degrees)
    {
        var goal=turn.Angle+((degrees-turn.Angle)%360+540)%360-180;
        if(!Moving){turn.BeginAnimation(RotateTransform.AngleProperty,null);turn.Angle=goal;return;}
        turn.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(goal,TimeSpan.FromMilliseconds(180)){EasingFunction=Out},HandoffBehavior.SnapshotAndReplace);
    }
    // Arriving: the glass fades in where it is (never zoomed), the hub settles, the chips spring out of it one after another.
    internal void Enter()
    {
        if(!Moving)return;
        foreach(var layer in glass){layer.Opacity=0;Fade(layer,1,200);}
        hub.Opacity=0;Fade(hub,1,200);var settle=(ScaleTransform)hub.RenderTransform;settle.ScaleX=settle.ScaleY=.82;Grow(settle,1,360);
        for(var i=0;i<faces.Count;i++)
        {
            var face=faces[i];var scale=Scale(face);var move=Move(face);var delay=i*18;face.Opacity=0;scale.ScaleX=scale.ScaleY=.55;move.X=-offsets[i].X*.65;move.Y=-offsets[i].Y*.65;
            Fade(face,1,220,delay);Grow(scale,1,460,delay);Glide(move,new(),460,delay,Spring.Shared);
        }
    }
    // Leaving with a choice: it lifts and dissolves while the rest fold into the hub. Without one, everything folds in.
    internal void Leave(bool picked,Action done)
    {
        var chosen=picked?hot:-1;if(!Moving){done();return;}
        for(var i=0;i<faces.Count;i++)
        {
            if(i==chosen){Ease(Scale(faces[i]),1.45,260,Out);Fade(faces[i],0,260);continue;}
            var pull=chosen>=0?.45:.6;Glide(Move(faces[i]),-offsets[i]*pull,180,0,In);Ease(Scale(faces[i]),chosen>=0?.7:.6,180,In);Fade(faces[i],0,160,0,In);
        }
        foreach(var layer in glass.Append(hub).Append(beam).Append(notch))Fade(layer,0,chosen>=0?220:160,chosen>=0?40:0);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(chosen>=0?280:180)};timer.Tick+=(_,_)=>{timer.Stop();done();};timer.Start();
    }
    private static ScaleTransform Scale(UIElement face)=>(ScaleTransform)((TransformGroup)face.RenderTransform).Children[0];
    private static TranslateTransform Move(UIElement face)=>(TranslateTransform)((TransformGroup)face.RenderTransform).Children[1];
    private static void Fade(UIElement element,double to,int milliseconds,int delay=0,IEasingFunction? easing=null)
    {
        if(!Moving){element.BeginAnimation(UIElement.OpacityProperty,null);element.Opacity=to;return;}
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(to,TimeSpan.FromMilliseconds(milliseconds)){BeginTime=TimeSpan.FromMilliseconds(delay),EasingFunction=easing??Out},HandoffBehavior.SnapshotAndReplace);
    }
    private static void Grow(ScaleTransform scale,double to,int milliseconds,int delay=0)=>Ease(scale,to,milliseconds,Spring.Shared,delay);
    private static void Ease(ScaleTransform scale,double to,int milliseconds,IEasingFunction easing,int delay=0)
    {
        if(!Moving){scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);scale.ScaleX=scale.ScaleY=to;return;}
        var grow=new DoubleAnimation(to,TimeSpan.FromMilliseconds(milliseconds)){BeginTime=TimeSpan.FromMilliseconds(delay),EasingFunction=easing};scale.BeginAnimation(ScaleTransform.ScaleXProperty,grow,HandoffBehavior.SnapshotAndReplace);scale.BeginAnimation(ScaleTransform.ScaleYProperty,grow,HandoffBehavior.SnapshotAndReplace);
    }
    private static void Glide(TranslateTransform move,Vector to,int milliseconds,int delay,IEasingFunction easing)
    {
        if(!Moving){move.BeginAnimation(TranslateTransform.XProperty,null);move.BeginAnimation(TranslateTransform.YProperty,null);move.X=to.X;move.Y=to.Y;return;}
        move.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(to.X,TimeSpan.FromMilliseconds(milliseconds)){BeginTime=TimeSpan.FromMilliseconds(delay),EasingFunction=easing},HandoffBehavior.SnapshotAndReplace);
        move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(to.Y,TimeSpan.FromMilliseconds(milliseconds)){BeginTime=TimeSpan.FromMilliseconds(delay),EasingFunction=easing},HandoffBehavior.SnapshotAndReplace);
    }
}

// The beam: a soft wedge of light from the hub toward the aimed chip. It flows between chips like liquid (the leading
// edge first, the trailing one a moment later) and is blurred so it reads as light, not as a shape.
internal sealed class WheelBeam:FrameworkElement
{
    public static readonly DependencyProperty StartProperty=DependencyProperty.Register(nameof(Start),typeof(double),typeof(WheelBeam),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EndProperty=DependencyProperty.Register(nameof(End),typeof(double),typeof(WheelBeam),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    public double Start{get=>(double)GetValue(StartProperty);set=>SetValue(StartProperty,value);}
    public double End{get=>(double)GetValue(EndProperty);set=>SetValue(EndProperty,value);}
    private int index=-1;private Brush? fill;
    public WheelBeam(){Width=540;Height=540;IsHitTestVisible=false;Opacity=0;Effect=new BlurEffect{Radius=18};}
    internal void Move(int target,int count)
    {
        if(target<0||count<1){index=-1;Fade(0);return;}
        var half=180d/count-1;var center=WheelPresentation.StartAngle(count)+360d*target/count;
        // From nothing it grows out of the chip's middle; otherwise it flows the short way round from where it is.
        if(index<0||Opacity<.05){Stop();Start=End=center;Edge(StartProperty,center-half,300);Edge(EndProperty,center+half,300);index=target;Fade(1);return;}
        if(target==index)return;
        var current=(Start+End)/2;var delta=((center-current)%360+540)%360-180;var goal=current+delta;var forward=delta>=0;
        Edge(forward?EndProperty:StartProperty,goal+(forward?half:-half),240);Edge(forward?StartProperty:EndProperty,goal+(forward?-half:half),400);index=target;Fade(1);
    }
    private void Edge(DependencyProperty edge,double to,int milliseconds)
    {
        if(!WheelFace.Moving){BeginAnimation(edge,null);SetValue(edge,to);return;}
        BeginAnimation(edge,new DoubleAnimation(to,TimeSpan.FromMilliseconds(milliseconds)){EasingFunction=Spring.Shared},HandoffBehavior.SnapshotAndReplace);
    }
    private void Fade(double to){if(!WheelFace.Moving){BeginAnimation(OpacityProperty,null);Opacity=to;return;}BeginAnimation(OpacityProperty,new DoubleAnimation(to,TimeSpan.FromMilliseconds(to>0?120:180)),HandoffBehavior.SnapshotAndReplace);}
    private void Stop(){BeginAnimation(StartProperty,null);BeginAnimation(EndProperty,null);}
    protected override void OnRender(DrawingContext dc)
    {
        double a=Start*Math.PI/180,b=End*Math.PI/180;if(b-a<.002)return;
        static Point P(double r,double t)=>new(270+r*Math.Cos(t),270+r*Math.Sin(t));
        var figure=new PathFigure{StartPoint=P(96,a),IsClosed=true};figure.Segments.Add(new LineSegment(P(236,a),true));figure.Segments.Add(new ArcSegment(P(236,b),new Size(236,236),0,b-a>Math.PI,SweepDirection.Clockwise,true));
        figure.Segments.Add(new LineSegment(P(96,b),true));figure.Segments.Add(new ArcSegment(P(96,a),new Size(96,96),0,b-a>Math.PI,SweepDirection.Counterclockwise,true));
        dc.DrawGeometry(fill??=Light(),null,new PathGeometry([figure]));
    }
    // Strongest just past the hub, gone by the rim.
    private static Brush Light()
    {
        var color=((SolidColorBrush)Theme.Brush("Sel")).Color;var light=new RadialGradientBrush{MappingMode=BrushMappingMode.Absolute,Center=new(270,270),GradientOrigin=new(270,270),RadiusX=240,RadiusY=240};
        light.GradientStops.Add(new(Color.FromArgb(0,color.R,color.G,color.B),.38));light.GradientStops.Add(new(color,.52));light.GradientStops.Add(new(Color.FromArgb((byte)(color.A*.6),color.R,color.G,color.B),.82));light.GradientStops.Add(new(Color.FromArgb(0,color.R,color.G,color.B),1));light.Freeze();return light;
    }
}
