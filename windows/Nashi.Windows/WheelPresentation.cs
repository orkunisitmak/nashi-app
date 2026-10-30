using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nashi.Core;
namespace Nashi.Windows;
internal static class WheelPresentation
{
    internal const double Diameter=320;
    internal static double StartAngle(int count)=>count==3?-180:-90;
    internal static Preferences Preferences{get;set;}=new();
    internal static Brush Backdrop{get{var brush=Theme.Brush("Surface").Clone();brush.Opacity=Preferences.WheelFinish=="glass"?.95:1;return brush;}}
    internal static double LabelRadius(int count,double half)=>count<=2?180:4*Math.Sin(half)*(Math.Pow(250,3)-Math.Pow(108,3))/(6*half*(250*250-108*108));
    internal static (StackPanel Label,Point Center) Caption(ToolChoice choice,Geometry petal,double angle,double half,int count)
    {
        var preferred=LabelRadius(count,half);
        for(var font=16d;font>=10;font-=.5)for(var width=142d;width>=72;width-=7)
        {
            var label=new StackPanel{Width=width,IsHitTestVisible=false};
            if(choice.Placement is {} placement){var glyph=new System.Windows.Controls.Canvas{Width=36,Height=25,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,0,0,7)};glyph.Children.Add(new System.Windows.Shapes.Rectangle{Width=35,Height=24,RadiusX=3,RadiusY=3,Stroke=Theme.Brush("Ink"),StrokeThickness=1.7});var (start,end)=placement.Range();var fill=new System.Windows.Shapes.Rectangle{Width=29*(end-start),Height=18,Fill=Theme.Brush("Ink"),Opacity=.65};System.Windows.Controls.Canvas.SetLeft(fill,3+29*start);System.Windows.Controls.Canvas.SetTop(fill,3);glyph.Children.Add(fill);label.Children.Add(glyph);}
            else label.Children.Add(new System.Windows.Controls.TextBlock{Text=choice.Symbol,FontSize=32,TextAlignment=TextAlignment.Center,Foreground=Theme.Brush("Ink"),Margin=new(0,0,0,6)});
            label.Children.Add(new System.Windows.Controls.TextBlock{Text=L10n.Uppercase(choice.Title),FontSize=font,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Foreground=Theme.Brush("Ink")});
            label.Measure(new Size(width,double.PositiveInfinity));var h=label.DesiredSize.Height;
            for(var step=0;step<=20;step++)
            {
                var radial=preferred+(step==0?0:(step%2==1?1:-1)*((step+1)/2)*5);var center=new Point(270+radial*Math.Cos(angle),270+radial*Math.Sin(angle));var r=new Rect(center.X-width/2-3,center.Y-h/2-3,width+6,h+6);
                if(new[]{r.TopLeft,r.TopRight,r.BottomLeft,r.BottomRight,new Point(r.Left,r.Top+r.Height/2),new Point(r.Right,r.Top+r.Height/2)}.All(petal.FillContains))return(label,center);
            }
        }
        var fallback=new StackPanel{Width=72};fallback.Children.Add(new System.Windows.Controls.TextBlock{Text=L10n.Uppercase(choice.Title),FontSize=10,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Foreground=Theme.Brush("Ink")});fallback.Measure(new Size(72,double.PositiveInfinity));return(fallback,new Point(270+preferred*Math.Cos(angle),270+preferred*Math.Sin(angle)));
    }
    internal static void Animate(Window window,double from=0){if(Preferences.Animations&&SystemParameters.ClientAreaAnimation)window.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(from,1,TimeSpan.FromMilliseconds(140)));}
}
