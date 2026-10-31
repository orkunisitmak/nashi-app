using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;

internal static class Theme
{
    internal static bool Dark{get;private set;}
    internal static Brush Primary=>new LinearGradientBrush(Color.FromRgb(227,184,89),Color.FromRgb(245,209,125),0);
    internal static Brush Brush(string name)=>(Brush)Application.Current.FindResource(name);
    internal static void Apply(string appearance)
    {
        Dark=appearance=="dark"||(appearance=="system"&&Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int value&&value==0);
        foreach(var (key,light,dark) in new[]{("Paper","#F9F6ED","#111A14"),("Ink","#243B2B","#E7EBE6"),("Surface","#FFFEF7","#1C281F"),("Inset","#F1EDE0","#151F18"),("Muted","#596650","#A9B5A8"),("Line","#DCDDD1","#344237"),("Sage","#E0EBD2","#243928"),("Sand","#F6E7BE","#3C3421")})
            Application.Current.Resources[key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(Dark?dark:light));
        foreach(Window window in Application.Current.Windows)ApplyTitleBar(window);
    }
    internal static void Attach(Window window)
    {
        window.Icon ??= BitmapFrame.Create(new Uri("pack://application:,,,/Assets/NashiIcon.png"));
        window.SetResourceReference(Window.BackgroundProperty,"Paper");window.SetResourceReference(Window.ForegroundProperty,"Ink");
        window.Loaded+=(_,_)=>{if(window.WindowStyle!=WindowStyle.None)DesktopGeometry.FitToWorkArea(window);L10n.Bind(window,Window.TitleProperty,window.Title);L10n.Attach(window);};
        window.SourceInitialized+=(_,_)=>ApplyTitleBar(window);
    }
    internal static void Entrance(UIElement element)
    {
        if(!WheelPresentation.Preferences.Animations||!SystemParameters.ClientAreaAnimation)return;
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
        var offset=new TranslateTransform();element.RenderTransform=offset;
        offset.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(8,0,TimeSpan.FromMilliseconds(180)){DecelerationRatio=.8});
    }
    private static void ApplyTitleBar(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;var dark=Dark?1:0;_ = DwmSetWindowAttribute(handle,20,ref dark,sizeof(int));
    }
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
}
