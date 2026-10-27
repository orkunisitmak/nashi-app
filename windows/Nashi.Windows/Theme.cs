using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Nashi.Windows;

internal static class Theme
{
    internal static bool Dark{get;private set;}
    // Workbench palette: graphite neutrals and a single pear accent. Gold is the
    // accent token (focus, selection), Sage its tinted fill, Sand the neutral
    // thumbnail well. Primary stays the same lime in both modes with dark text.
    internal static Brush Primary=>new SolidColorBrush(Color.FromRgb(0xC8,0xE6,0x4C));
    internal static readonly Color OnPrimary=Color.FromRgb(0x1A,0x1F,0x0A);
    internal static Brush Brush(string name)=>(Brush)Application.Current.FindResource(name);
    internal static void Apply(string appearance)
    {
        Dark=appearance=="dark"||(appearance=="system"&&Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int value&&value==0);
        foreach(var (key,light,dark) in new[]{("Paper","#F3F4F6","#0F1012"),("Ink","#16181B","#ECEEF0"),("Surface","#FBFBFC","#17191C"),("Inset","#E9EBEE","#1F2226"),("Muted","#5A616B","#9AA0A8"),("Line","#D9DCE1","#2A2E33"),("Sage","#E6F0C4","#28311A"),("Sand","#E1E4E8","#25282C"),("Gold","#5C7F0E","#C8E64C"),("Desk","#ECEEF1","#141619"),("Dot","#CDD1D7","#262A2F")})
            Application.Current.Resources[key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(Dark?dark:light));
        foreach(Window window in Application.Current.Windows)ApplyTitleBar(window);
    }
    internal static void Attach(Window window)
    {
        window.SetResourceReference(Window.BackgroundProperty,"Paper");window.SetResourceReference(Window.ForegroundProperty,"Ink");
        window.Loaded+=(_,_)=>{if(window.WindowStyle!=WindowStyle.None)DesktopGeometry.FitToWorkArea(window);L10n.Bind(window,Window.TitleProperty,window.Title);L10n.Attach(window);};
        window.SourceInitialized+=(_,_)=>ApplyTitleBar(window);
        window.Icon??=AppIcon.Value;
        // Hand focus back to the owner before an owned window goes away; otherwise Windows
        // activates whatever app was behind and Nashi seems to vanish.
        window.Closing+=(_,e)=>{if(!e.Cancel&&window.IsActive&&window.Owner is {IsVisible:true} owner)owner.Activate();};
    }
    private static readonly Lazy<ImageSource> AppIcon=new(()=>{var icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/NashiIcon.png"));icon.Freeze();return icon;});
    internal static void Entrance(UIElement element,double fromX=0)=>Motion.Enter(element,fromX,fromX==0?8:0);
    private static void ApplyTitleBar(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;var dark=Dark?1:0;_ = DwmSetWindowAttribute(handle,20,ref dark,sizeof(int));
    }
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
}
