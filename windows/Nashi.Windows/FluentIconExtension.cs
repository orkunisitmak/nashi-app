using System.Windows.Markup;
using System.Windows.Media;

namespace Nashi.Windows;

/// Makes pinned Fluent SVG geometry available to WPF control templates.
[MarkupExtensionReturnType(typeof(Geometry))]
public sealed class FluentIconExtension(string id):MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)=>
        FluentIcons.GeometryFor(id)??throw new InvalidOperationException(
            "Missing Fluent UI System Icon: "+id);
}
