using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Nashi.Windows;

/// One icon family for the whole app. Callers pass Phosphor names ("crop",
/// "share-network"); anything else (a legacy glyph from a test fixture) is not
/// an icon and is drawn as text by the caller.
internal static partial class Icons
{
    /// Marks icon paths so checks can tell them from drawn shapes.
    internal const string Tag="icon";
    private static readonly Dictionary<string,Geometry> parsed=new(StringComparer.Ordinal);
    internal static IEnumerable<string> Names=>Data.Keys;
    internal static bool Has(string? name)=>name is not null&&Data.ContainsKey(name);
    internal static Geometry Geometry(string name)
    {
        if(!parsed.TryGetValue(name,out var geometry)){geometry=System.Windows.Media.Geometry.Parse(Data[name]);geometry.Freeze();parsed[name]=geometry;}
        return geometry;
    }
    /// A square icon filled with a theme brush, so it follows light and dark mode.
    internal static FrameworkElement Create(string name,double size=16,string brush="Ink")
    {
        var path=new System.Windows.Shapes.Path{Data=Geometry(name),Tag=Icons.Tag};path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,brush);
        var box=new Canvas{Width=256,Height=256};box.Children.Add(path);
        return new Viewbox{Width=size,Height=size,Child=box,IsHitTestVisible=false,SnapsToDevicePixels=true};
    }
    /// Icon followed by a label, for buttons that need both.
    internal static StackPanel Labeled(string name,string text,double size=16)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};var icon=Create(name,size);icon.Margin=new(0,0,8,0);icon.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(icon);
        row.Children.Add(L10n.Label(new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center}));return row;
    }
}
