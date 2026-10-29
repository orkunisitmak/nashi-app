using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
namespace Nashi.Linux;

/// The same pear palette as the Windows and Mac apps.
internal static class NashiTheme
{
    private static readonly (string Key,string Light,string Dark)[] Palette=[("Paper","#F9F6ED","#111A14"),("Ink","#243B2B","#E7EBE6"),("Surface","#FFFEF7","#1C281F"),("Inset","#F1EDE0","#151F18"),("Muted","#596650","#A9B5A8"),("Line","#DCDDD1","#344237"),("Sage","#E0EBD2","#243928"),("Sand","#F6E7BE","#3C3421")];
    public static IBrush Primary=>new LinearGradientBrush{StartPoint=new(0,0,RelativeUnit.Relative),EndPoint=new(1,0,RelativeUnit.Relative),GradientStops={new(Color.FromRgb(227,184,89),0),new(Color.FromRgb(245,209,125),1)}};
    public static void Apply(Application app,string appearance)
    {
        app.RequestedThemeVariant=appearance switch{"light"=>ThemeVariant.Light,"dark"=>ThemeVariant.Dark,_=>ThemeVariant.Default};
        var light=new ResourceDictionary();var dark=new ResourceDictionary();
        foreach(var (key,l,d) in Palette){light[key]=new SolidColorBrush(Color.Parse(l));dark[key]=new SolidColorBrush(Color.Parse(d));}
        app.Resources.ThemeDictionaries[ThemeVariant.Light]=light;app.Resources.ThemeDictionaries[ThemeVariant.Dark]=dark;
    }
}
