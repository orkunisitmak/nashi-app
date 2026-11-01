using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
namespace Nashi.Linux;

/// Button looks shared with the macOS and Windows apps: pill navigation, the gold primary button,
/// tool tiles and format chips. Hover colors come from the pear palette in NashiTheme.
internal static class NashiStyles
{
    private static DynamicResourceExtension R(string key)=>new(key);
    public static readonly IBrush Gold=new SolidColorBrush(Color.FromRgb(227,184,89));
    public static readonly IBrush OnGold=new SolidColorBrush(Color.Parse("#243B2B"));

    public static void Install(Application app)
    {
        Style Look(string name,params (AvaloniaProperty Property,object Value)[] setters)
        {
            var style=new Style(x=>x.OfType<Button>().Class(name));foreach(var (p,v) in setters)style.Setters.Add(new Setter(p,v));return style;
        }
        // Fluent paints the hover background on the template's presenter, so hover is set there.
        Style Hover(string name,string background,IBrush? border=null)
        {
            var style=new Style(x=>x.OfType<Button>().Class(name).Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty,R(background)));
            if(border is not null)style.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty,border));
            style.Setters.Add(new Setter(ContentPresenter.ForegroundProperty,R("Ink")));return style;
        }
        // Every plain button, box and list gets the soft pear look first; the classes below refine it.
        var plain=new Style(x=>x.OfType<Button>());
        foreach(var (p,v) in new (AvaloniaProperty,object)[]{(Button.CornerRadiusProperty,new CornerRadius(10)),(Button.BackgroundProperty,R("Inset")),(Button.BorderBrushProperty,R("Line")),(Button.BorderThicknessProperty,new Thickness(1)),(Button.ForegroundProperty,R("Ink")),(Button.PaddingProperty,new Thickness(14,7))})plain.Setters.Add(new Setter(p,v));
        app.Styles.Add(plain);
        var plainHover=new Style(x=>x.OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        plainHover.Setters.Add(new Setter(ContentPresenter.BackgroundProperty,R("Sage")));plainHover.Setters.Add(new Setter(ContentPresenter.ForegroundProperty,R("Ink")));app.Styles.Add(plainHover);
        foreach(var type in new[]{typeof(ComboBox),typeof(TextBox),typeof(NumericUpDown)})
        {
            var round=new Style(x=>x.OfType(type));round.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty,new CornerRadius(10)));app.Styles.Add(round);
        }
        app.Styles.Add(Look("pill",(Button.CornerRadiusProperty,new CornerRadius(16)),(Button.PaddingProperty,new Thickness(14,6)),(Button.BackgroundProperty,R("Surface")),(Button.BorderBrushProperty,R("Line")),(Button.BorderThicknessProperty,new Thickness(1)),(Button.FontSizeProperty,13d),(Button.ForegroundProperty,R("Ink"))));
        app.Styles.Add(Hover("pill","Sage"));
        app.Styles.Add(Look("on",(Button.BackgroundProperty,R("Sage")),(Button.FontWeightProperty,FontWeight.SemiBold)));
        app.Styles.Add(Look("primary",(Button.CornerRadiusProperty,new CornerRadius(14)),(Button.PaddingProperty,new Thickness(20,11)),(Button.BackgroundProperty,NashiTheme.Primary),(Button.ForegroundProperty,OnGold),(Button.FontWeightProperty,FontWeight.SemiBold),(Button.FontSizeProperty,14d)));
        var primaryHover=new Style(x=>x.OfType<Button>().Class("primary").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        primaryHover.Setters.Add(new Setter(ContentPresenter.BackgroundProperty,new SolidColorBrush(Color.FromRgb(240,201,110))));primaryHover.Setters.Add(new Setter(ContentPresenter.ForegroundProperty,OnGold));app.Styles.Add(primaryHover);
        app.Styles.Add(Look("ghost",(Button.CornerRadiusProperty,new CornerRadius(14)),(Button.PaddingProperty,new Thickness(18,10)),(Button.BackgroundProperty,Brushes.Transparent),(Button.BorderBrushProperty,R("Line")),(Button.BorderThicknessProperty,new Thickness(1)),(Button.FontSizeProperty,14d),(Button.ForegroundProperty,R("Ink"))));
        app.Styles.Add(Hover("ghost","Inset"));
        app.Styles.Add(Look("tile",(Button.WidthProperty,138d),(Button.HeightProperty,96d),(Button.CornerRadiusProperty,new CornerRadius(16)),(Button.BackgroundProperty,R("Inset")),(Button.BorderBrushProperty,R("Line")),(Button.BorderThicknessProperty,new Thickness(1)),(Button.PaddingProperty,new Thickness(8)),(Button.MarginProperty,new Thickness(0,0,10,10)),(Button.HorizontalContentAlignmentProperty,Avalonia.Layout.HorizontalAlignment.Center),(Button.VerticalContentAlignmentProperty,Avalonia.Layout.VerticalAlignment.Center),(Button.ForegroundProperty,R("Ink"))));
        app.Styles.Add(Hover("tile","Sage",Gold));
        app.Styles.Add(Look("chip",(Button.CornerRadiusProperty,new CornerRadius(12)),(Button.PaddingProperty,new Thickness(16,8)),(Button.BackgroundProperty,R("Inset")),(Button.BorderBrushProperty,R("Line")),(Button.BorderThicknessProperty,new Thickness(1)),(Button.FontWeightProperty,FontWeight.SemiBold),(Button.FontSizeProperty,13d),(Button.MarginProperty,new Thickness(0,0,8,8)),(Button.ForegroundProperty,R("Ink"))));
        app.Styles.Add(Hover("chip","Sage",Gold));
        app.Styles.Add(Look("link",(Button.BackgroundProperty,Brushes.Transparent),(Button.BorderThicknessProperty,new Thickness(0)),(Button.PaddingProperty,new Thickness(0)),(Button.HorizontalContentAlignmentProperty,Avalonia.Layout.HorizontalAlignment.Stretch),(Button.CornerRadiusProperty,new CornerRadius(20))));
        app.Styles.Add(Hover("link","Inset"));
        app.Styles.Add(Look("mini",(Button.CornerRadiusProperty,new CornerRadius(10)),(Button.PaddingProperty,new Thickness(10,4)),(Button.FontSizeProperty,12d),(Button.BackgroundProperty,R("Inset")),(Button.BorderBrushProperty,R("Line")),(Button.BorderThicknessProperty,new Thickness(1)),(Button.ForegroundProperty,R("Ink"))));
        app.Styles.Add(Hover("mini","Sage"));
    }
}
