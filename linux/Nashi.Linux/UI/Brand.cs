using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
namespace Nashi.Linux.UI;

/// The Windows/macOS palette: paper, sage and gold, with matching dark values.
internal static class Brand
{
    private static readonly (string Key,string Light,string Dark)[] Palette=[("Paper","#F9F6ED","#111A14"),("Ink","#243B2B","#E7EBE6"),("Surface","#FFFEF7","#1C281F"),("Inset","#F1EDE0","#151F18"),("Muted","#596650","#A9B5A8"),("Line","#DCDDD1","#344237"),("Sage","#E0EBD2","#243928"),("Sand","#F6E7BE","#3C3421"),("Gold","#B8862B","#E3B859")];
    internal static readonly IBrush PrimaryBrush=new LinearGradientBrush{StartPoint=new(0,0,RelativeUnit.Relative),EndPoint=new(1,0,RelativeUnit.Relative),GradientStops={new(Color.FromRgb(227,184,89),0),new(Color.FromRgb(245,209,125),1)}};
    internal static readonly IBrush OnPrimary=new SolidColorBrush(Color.FromRgb(43,54,28));
    internal static void Install(Application app)
    {
        var light=new ResourceDictionary();var dark=new ResourceDictionary();
        foreach(var (key,l,d) in Palette){light[key]=new SolidColorBrush(Color.Parse(l));dark[key]=new SolidColorBrush(Color.Parse(d));}
        app.Resources.ThemeDictionaries[ThemeVariant.Light]=light;app.Resources.ThemeDictionaries[ThemeVariant.Dark]=dark;
    }
    internal static void Apply(string appearance)
    {
        if(Application.Current is {} app)app.RequestedThemeVariant=appearance switch{"light"=>ThemeVariant.Light,"dark"=>ThemeVariant.Dark,_=>ThemeVariant.Default};
    }
    internal static T Res<T>(this T control,AvaloniaProperty property,string key)where T:AvaloniaObject{control.Bind(property,new DynamicResourceExtension(key));return control;}
    internal static Bitmap Asset(string name)=>new(AssetLoader.Open(new Uri("avares://nashi/Assets/"+name+".png")));

    internal static TextBlock Text(string value,double size,string? color=null,bool localize=true)
    {
        var text=new TextBlock{Text=localize?L10n.T(value):value,FontSize=size,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,FontWeight=size>=17?FontWeight.SemiBold:FontWeight.Normal};
        return color is null?text.Res(TextBlock.ForegroundProperty,"Ink"):text.Res(TextBlock.ForegroundProperty,color);
    }
    internal static Border Card(Control content,double padding=22)
    {
        return new Border{CornerRadius=new(24),Padding=new(padding),Margin=new(0,0,0,6),Child=content,BorderThickness=new(1)}.Res(Border.BackgroundProperty,"Surface").Res(Border.BorderBrushProperty,"Line");
    }
    internal static Button Button(string title,Action action,bool localize=true)
    {
        var button=new Button{Content=localize?L10n.T(title):title,Padding=new(12,7),Margin=new(0,0,8,8),CornerRadius=new(12),VerticalAlignment=VerticalAlignment.Center};
        button.Click+=(_,_)=>action();return button;
    }
    internal static Button Primary(string title,Action action,bool localize=true)
    {
        var button=Button(title,action,localize);button.Background=PrimaryBrush;button.Foreground=OnPrimary;button.FontWeight=FontWeight.SemiBold;return button;
    }
    internal static Button Icon(string symbol,string tip,Action action)
    {
        var button=Button(symbol,action,false);button.Padding=new(9,5);button.Margin=new(4,0,0,0);ToolTip.SetTip(button,L10n.T(tip));
        Avalonia.Automation.AutomationProperties.SetName(button,L10n.T(tip));return button;
    }
}
