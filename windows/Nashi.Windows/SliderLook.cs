using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Nashi.Windows;

/// Replaces a playback slider's rectangular thumb with a round one.
internal static class SliderLook
{
    internal static void RoundThumb(Slider slider)
    {
        void Apply()
        {
            slider.ApplyTemplate();
            if(slider.Template?.FindName("Thumb",slider) is not Thumb thumb)return;
            var ball=new FrameworkElementFactory(typeof(Ellipse));
            ball.SetValue(Shape.FillProperty,Brushes.White);
            ball.SetValue(Shape.StrokeProperty,new SolidColorBrush(Color.FromArgb(160,0,0,0)));
            ball.SetValue(Shape.StrokeThicknessProperty,2.0);
            thumb.Template=new ControlTemplate(typeof(Thumb)){VisualTree=ball};
            thumb.Width=16;thumb.Height=16;
        }
        if(slider.IsLoaded)Apply();else slider.Loaded+=(_,_)=>Apply();
    }
}