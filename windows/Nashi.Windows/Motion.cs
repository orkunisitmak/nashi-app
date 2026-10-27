using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Nashi.Windows;

/// Shared timing for every animation in Nashi: one switch and one speed, both from Settings.
internal static class Motion
{
    internal static bool On=>WheelPresentation.Preferences.Animations&&SystemParameters.ClientAreaAnimation;
    /// A duration at the user's speed (2 = twice as fast, .5 = half speed).
    internal static TimeSpan Time(double milliseconds)=>TimeSpan.FromMilliseconds(milliseconds/Math.Clamp(WheelPresentation.Preferences.AnimationSpeed,.5,2));
    /// A page slides in from the side it comes from and fades up.
    internal static void Enter(UIElement element,double fromX=0,double fromY=8)
    {
        if(!On)return;var time=Time(fromX==0?180:240);var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,time));
        var offset=new TranslateTransform();element.RenderTransform=offset;
        if(fromX!=0)offset.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(fromX,0,time){EasingFunction=ease});
        if(fromY!=0)offset.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(fromY,0,time){EasingFunction=ease});
    }
    /// A short shake, like a file being worked on.
    internal static void Shake(UIElement element)
    {
        if(!On)return;var move=new TranslateTransform();var turn=new RotateTransform();var group=new TransformGroup();group.Children.Add(turn);group.Children.Add(move);
        element.RenderTransformOrigin=new(.5,.5);element.RenderTransform=group;var total=Time(460);
        DoubleAnimationUsingKeyFrames Frames(params double[] values)
        {
            var frames=new DoubleAnimationUsingKeyFrames{Duration=total};for(var n=0;n<values.Length;n++)frames.KeyFrames.Add(new EasingDoubleKeyFrame(values[n],KeyTime.FromPercent(n/(double)(values.Length-1)),new SineEase()));return frames;
        }
        move.BeginAnimation(TranslateTransform.XProperty,Frames(0,-7,6,-5,4,-2,1,0));turn.BeginAnimation(RotateTransform.AngleProperty,Frames(0,-2.5,2,-1.5,1,-.5,0,0));
    }
    /// A gentle pop for something that just appeared.
    internal static void Pop(UIElement element)
    {
        if(!On)return;var scale=new ScaleTransform(.6,.6);element.RenderTransformOrigin=new(.5,.5);element.RenderTransform=scale;var time=Time(320);var ease=new BackEase{Amplitude=.45,EasingMode=EasingMode.EaseOut};
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.6,1,time){EasingFunction=ease});scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.6,1,time){EasingFunction=ease});
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,Time(160)));
    }
    /// Keeps an icon turning until it leaves the screen.
    internal static void Spin(FrameworkElement element)
    {
        var turn=new RotateTransform();element.RenderTransformOrigin=new(.5,.5);element.RenderTransform=turn;
        if(On)turn.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,Time(900)){RepeatBehavior=RepeatBehavior.Forever});
    }
}
