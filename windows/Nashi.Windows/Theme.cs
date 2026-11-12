using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
namespace Nashi.Windows;

internal static class Theme
{
    internal static bool Dark{get;private set;}
    internal static Brush Brush(string name)=>(Brush)Application.Current.FindResource(name);
    // Ink & Coral. Ink and paper carry the interface; coral marks only the one primary action, the active wheel petal, trim handles and focus.
    internal static void Apply(string appearance)
    {
        Dark=appearance=="dark"||(appearance=="system"&&Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int value&&value==0);
        var resources=Application.Current.Resources;
        foreach(var (key,light,dark) in new[]{("Paper","#F4F3F0","#1C1C1F"),("Ink","#111214","#F7F6F3"),("Muted","#B8111214","#BDF7F6F3"),("Faint","#80111214","#80F7F6F3"),("Card","#9EFFFFFF","#0EFFFFFF"),("Well","#7AFFFFFF","#11FFFFFF"),("WellHover","#ADFFFFFF","#1CFFFFFF"),("Raised","#DBFFFFFF","#2BFFFFFF"),("Sel","#13111214","#1FFFFFFF"),("Hover","#0D111214","#0FFFFFFF"),("Line","#1A111214","#1AFFFFFF"),("LineStrong","#29111214","#29FFFFFF"),("Coral","#F2553A","#FF6E52"),("OnCoral","#111214","#111214"),("Solid","#111214","#F7F6F3"),("OnSolid","#F7F6F3","#111214"),("Menu","#FBFAF8","#262628"),("Media","#E7E6E2","#0E0E10"),("Keycap","#9EFFFFFF","#1CFFFFFF"),("Danger","#D93036","#FF6369"),("Glass","#38FFFFFF","#14FFFFFF"),("GlassStrong","#D6FBFAF8","#E018181B"),("Slab","#9EF7F6F3","#A8101012"),("GlassDock","#99FBFAF8","#99121214"),("Petal","#80FFFFFF","#8C1E1E21"),("PetalStroke","#E6FFFFFF","#1AFFFFFF"),("Clear","#1AFFFFFF","#0AFFFFFF"),("Focus","#66111214","#73F7F6F3"),("Panel","#B8FBFAF8","#B8161618")})
        {
            var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(Dark?dark:light));brush.Freeze();resources[key]=brush;
        }
        // One light from above: glass edges are brightest at the top. Coral keeps a thin highlight on top and a shade below.
        resources["Rim"]=Vertical(Dark?["#57FFFFFF","#1AFFFFFF","#0FFFFFFF"]:["#E6FFFFFF","#57FFFFFF","#38FFFFFF"],[0,.38,1]);
        resources["ThumbRim"]=Vertical(Dark?["#33FFFFFF","#00FFFFFF"]:["#B3FFFFFF","#00FFFFFF"],[0,.5]);
        resources["SignatureRim"]=Vertical(["#6BFFFFFF","#00FFFFFF","#00000000","#24000000"],[0,.35,.65,1]);
        // The segment droplet's shadow: soft in light, deeper in dark (design: 0 4px 12px .08 / 0 2px 10px .35).
        var drop=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=Dark?12:12,ShadowDepth=Dark?2:2.5,Direction=270,Opacity=Dark?.4:.16,Color=Colors.Black};drop.Freeze();resources["ThumbShadow"]=drop;
        foreach(Window window in Application.Current.Windows)ApplyTitleBar(window);
        Lens.RefreshAll();
    }
    private static LinearGradientBrush Vertical(string[] colors,double[] offsets)
    {
        var brush=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};for(var n=0;n<colors.Length;n++)brush.GradientStops.Add(new((Color)ColorConverter.ConvertFromString(colors[n]),offsets[n]));brush.Freeze();return brush;
    }
    // Windows are glass; tests and high contrast stay solid paper.
    internal static bool Glass=>!SystemParameters.HighContrast&&!App.IsSmokeTest;
    // toolbar: the window draws its own toolbar row under the caption buttons; otherwise a small title row is added.
    // glass: false keeps a solid backdrop, for canvases where the work itself should not sit on the desktop.
    internal static void Attach(Window window,bool toolbar=false,bool glass=true)
    {
        window.SetResourceReference(Window.BackgroundProperty,"Paper");window.SetResourceReference(Window.ForegroundProperty,"Ink");window.SetResourceReference(Control.FontFamilyProperty,"UiFont");window.FontSize=13;Backdrop.SetEnabled(window,glass);
        window.Loaded+=(_,_)=>{if(window.WindowStyle!=WindowStyle.None)DesktopGeometry.FitToWorkArea(window);L10n.Bind(window,Window.TitleProperty,window.Title);L10n.Attach(window);};
        window.SourceInitialized+=(_,_)=>{if(window.WindowStyle!=WindowStyle.None)Frame(window,toolbar);ApplyTitleBar(window);};
    }
    private static void Frame(Window window,bool toolbar)
    {
        WindowChrome.SetWindowChrome(window,new WindowChrome{CaptionHeight=toolbar?52:34,ResizeBorderThickness=new(6),CornerRadius=new(0),GlassFrameThickness=new(0),UseAeroCaptionButtons=false});
        window.Template=(ControlTemplate)Application.Current.FindResource(toolbar?"ToolbarFrame":"TitledFrame");
        window.CommandBindings.Add(new(SystemCommands.MinimizeWindowCommand,(_,_)=>SystemCommands.MinimizeWindow(window)));
        window.CommandBindings.Add(new(SystemCommands.MaximizeWindowCommand,(_,_)=>SystemCommands.MaximizeWindow(window)));
        window.CommandBindings.Add(new(SystemCommands.RestoreWindowCommand,(_,_)=>SystemCommands.RestoreWindow(window)));
        window.CommandBindings.Add(new(SystemCommands.CloseWindowCommand,(_,_)=>SystemCommands.CloseWindow(window)));
    }
    // Content arrives without movement: a short fade is enough to notice the change.
    internal static void Entrance(UIElement element)
    {
        if(!WheelPresentation.Preferences.Animations||!SystemParameters.ClientAreaAnimation)return;
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
    }
    private static void ApplyTitleBar(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;var dark=Dark?1:0;_ = DwmSetWindowAttribute(handle,20,ref dark,sizeof(int));
        if(window.WindowStyle!=WindowStyle.None){var round=2;_ = DwmSetWindowAttribute(handle,33,ref round,sizeof(int));}
    }
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
}
