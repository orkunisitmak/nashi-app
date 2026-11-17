using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nashi.Windows;

/// App-wide Fluent UI System Icons renderer. All geometries come from the
/// official Regular 24 SVG set; see Resources/FluentIcons/SOURCE.json.
internal static class WheelIcon
{
    internal static Viewbox? Create(string id,double size=32,bool wheel=true)
    {
        var geometry=FluentIcons.GeometryFor(id);
        if(geometry is null)return null;
        var path=new System.Windows.Shapes.Path
        {
            Data=geometry,Stretch=Stretch.None,Stroke=null,
            IsHitTestVisible=false
        };
        path.SetResourceReference(Shape.FillProperty,"Ink");
        var canvas=new Canvas{Width=NashiDesignTokens.IconGrid,
            Height=NashiDesignTokens.IconGrid,IsHitTestVisible=false};
        canvas.Children.Add(path);
        return new Viewbox
        {
            Width=size,Height=size,Stretch=Stretch.Uniform,
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center,
            Margin=wheel?new Thickness(0,0,0,6):new Thickness(0),Child=canvas
        };
    }
}
