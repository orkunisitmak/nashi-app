using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace Nashi.Windows;
internal static class DesktopGeometry
{
    [StructLayout(LayoutKind.Sequential)]internal struct Point{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]internal struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromPoint(Point point,uint flags);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll")]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("shcore.dll")]private static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(IntPtr window,System.Text.StringBuilder name,int count);
    /// True while a borderless foreground window covers its whole monitor: a game, a video or an F11 browser.
    /// The desktop and Explorer can fill a monitor without a caption too, but file drags start there.
    internal static bool FullscreenForeground()
    {
        var window=GetForegroundWindow();if(window==IntPtr.Zero||(GetWindowLong(window,-16)&0x00C00000)==0x00C00000)return false; // WS_CAPTION: normal and maximized windows
        var type=new System.Text.StringBuilder(32);GetClassName(window,type,type.Capacity);if(type.ToString() is "Progman" or "WorkerW" or "CabinetWClass")return false;
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};return GetWindowRect(window,out var r)&&GetMonitorInfo(MonitorFromWindow(window,2),ref info)&&r.Left<=info.Monitor.Left&&r.Top<=info.Monitor.Top&&r.Right>=info.Monitor.Right&&r.Bottom>=info.Monitor.Bottom;
    }
    internal static void FitToWorkArea(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(!GetMonitorInfo(MonitorFromWindow(handle,2),ref info)||!GetWindowRect(handle,out var bounds))return;
        var dpi=96u;try{if(GetDpiForMonitor(MonitorFromWindow(handle,2),0,out var actual,out _)==0)dpi=actual;}catch(DllNotFoundException){}var scale=dpi/96d;var margin=(int)Math.Round(16*scale);var availableWidth=Math.Max(100,info.Work.Right-info.Work.Left-margin*2);var availableHeight=Math.Max(100,info.Work.Bottom-info.Work.Top-margin*2);
        window.MinWidth=Math.Min(window.MinWidth,availableWidth/scale);window.MinHeight=Math.Min(window.MinHeight,availableHeight/scale);
        var width=Math.Min(bounds.Right-bounds.Left,availableWidth);var height=Math.Min(bounds.Bottom-bounds.Top,availableHeight);var x=Math.Clamp(bounds.Left,info.Work.Left+margin,info.Work.Right-margin-width);var y=Math.Clamp(bounds.Top,info.Work.Top+margin,info.Work.Bottom-margin-height);SetWindowPos(handle,IntPtr.Zero,x,y,width,height,0x14);
    }
    internal static System.Windows.Rect WorkArea(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;var monitor=MonitorFromWindow(handle,2);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(monitor,ref info))return SystemParameters.WorkArea;
        var dpi=96u;try{if(GetDpiForMonitor(monitor,0,out var actual,out _)==0)dpi=actual;}catch(DllNotFoundException){}
        var scale=dpi/96d;return new(info.Work.Left/scale,info.Work.Top/scale,(info.Work.Right-info.Work.Left)/scale,(info.Work.Bottom-info.Work.Top)/scale);
    }
    internal static bool FitsWorkArea(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};return GetMonitorInfo(MonitorFromWindow(handle,2),ref info)&&GetWindowRect(handle,out var r)&&r.Left>=info.Work.Left&&r.Top>=info.Work.Top&&r.Right<=info.Work.Right&&r.Bottom<=info.Work.Bottom;
    }
    internal static bool IsInteractive(Window window){var handle=new WindowInteropHelper(window).Handle;return window.IsEnabled&&(handle==IntPtr.Zero||IsWindowEnabled(handle));}
    internal static void PlaceWheel(Window window,int x,int y,double logicalSize)
    {
        var monitor=MonitorFromPoint(new(){X=x,Y=y},2);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(!GetMonitorInfo(monitor,ref info))return;
        var dpi=96u;try{if(GetDpiForMonitor(monitor,0,out var actual,out _)==0)dpi=actual;}catch(DllNotFoundException){}var size=(int)Math.Round(logicalSize*dpi/96d);size=Math.Min(size,Math.Min(info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top));
        var left=Math.Clamp(x-size/2,info.Work.Left,info.Work.Right-size);var top=Math.Clamp(y-size/2,info.Work.Top,info.Work.Bottom-size);SetWindowPos(new WindowInteropHelper(window).Handle,IntPtr.Zero,left,top,size,size,0x14);
    }
    internal static string? Snap(Window window,string? edge,bool detect=false)
    {
        var handle=new WindowInteropHelper(window).Handle;var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(!GetMonitorInfo(MonitorFromWindow(handle,2),ref info)||!GetWindowRect(handle,out var r))return edge;
        if(detect)edge=Math.Abs(r.Left-info.Work.Left)<24?"Left":Math.Abs(r.Right-info.Work.Right)<24?"Right":Math.Abs(r.Top-info.Work.Top)<24?"Top":Math.Abs(r.Bottom-info.Work.Bottom)<24?"Bottom":null;
        var x=edge=="Left"?info.Work.Left:edge=="Right"?info.Work.Right-(r.Right-r.Left):r.Left;var y=edge=="Top"?info.Work.Top:edge=="Bottom"?info.Work.Bottom-(r.Bottom-r.Top):r.Top;SetWindowPos(handle,IntPtr.Zero,x,y,0,0,0x15);return edge;
    }
}
