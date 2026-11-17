using System.Windows.Controls;
using System.Windows.Shapes;
using Nashi.Core;
namespace Nashi.Windows;

/// Isolated assertions for the wheel's cross-theme, font-independent vector artwork.
internal static class WheelIconSmoke
{
    internal static void Run(Action<bool,string> check)
    {
        var ids=FeatureCatalog.ToolNames.Keys.Concat(new[]
        {
            "convert","convert:png","crop","annotate","mergePDFs",
            "resize","share","float","copy","move","copyPath","terminal"
        }).Distinct(StringComparer.Ordinal).ToArray();
        foreach(var id in ids)
        {
            var icon=WheelIcon.Create(id);
            check(icon is Viewbox { Width: 32, Height: 32, Child: Canvas { Width: 24, Height: 24 } },
                "Wheel icon has an identical 24-unit vector viewport: "+id);
            var canvas=(Canvas)icon!.Child;
            check(FluentIcons.HasIcon(id),"Every wheel tool resolves to an official Fluent icon: "+id);
            check(canvas.Children.OfType<System.Windows.Shapes.Path>().Any(path=>path.Data is not null && !path.Data.Bounds.IsEmpty &&
                path.Fill is not null && path.Stroke is null),
                "Wheel icon renders Fluent filled geometry, not font glyphs: "+id);
        }
        check(WheelIcon.Create("future-unmapped-tool") is null,
            "Unknown tool should retain the existing symbol fallback");
    }
}
