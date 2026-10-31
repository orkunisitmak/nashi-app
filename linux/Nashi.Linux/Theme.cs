using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Platform;
using Nashi.Core;
namespace Nashi.Linux;
internal static class NashiTheme
{
    public static string Language{get;set;}="en";
    public static bool Dark{get;private set;}
    public static IBrush Paper=>Brush(Dark?"#111A14":"#F9F6ED");
    public static IBrush Ink=>Brush(Dark?"#E7EBE6":"#243B2B");
    public static IBrush Surface=>Brush(Dark?"#1C281F":"#FFFEF7");
    public static IBrush Muted=>Brush(Dark?"#A9B5A8":"#596650");
    public static IBrush Gold=>Brush("#E3B859");
    public static IBrush Brush(string hex)=>new SolidColorBrush(Color.Parse(hex));
    public static void Apply(string appearance)
    {
        Dark=appearance=="dark"||appearance=="system"&&Application.Current!.PlatformSettings?.GetColorValues().ThemeVariant==PlatformThemeVariant.Dark;
        Application.Current!.RequestedThemeVariant=Dark?ThemeVariant.Dark:ThemeVariant.Light;
        foreach(var suffix in new[]{"PointerOver","Pressed"}){
            Application.Current.Resources["ButtonBackground"+suffix]=Surface;
            Application.Current.Resources["ButtonForeground"+suffix]=Ink;
        }
    }
    public static TextBlock Text(string value,double size=14,bool muted=false)=>new(){Text=AppLocalization.Text(value,Language),FontFamily=new FontFamily("DejaVu Sans"),FontSize=size,FontWeight=size>=20?FontWeight.SemiBold:FontWeight.Normal,Foreground=muted?Muted:Ink,TextWrapping=TextWrapping.Wrap};
    public static Border Card(Control content)=>new(){Background=Surface,CornerRadius=new(24),Padding=new(22),Child=content};
    public static Button Button(string label,Action action,bool primary=false)
    {
        var button=new Button{Content=AppLocalization.Text(label,Language),Padding=new(16,10),CornerRadius=new(14),Background=primary?Gold:Surface,Foreground=primary?Brush("#243B2B"):Ink};
        button.Click+=(_,_)=>action();return button;
    }
}
