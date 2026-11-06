using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;

internal sealed record ScreenFrame(BitmapSource Image,Int32Rect Bounds);
internal static class ScreenCapture
{
    [DllImport("user32.dll")]private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32.dll")]private static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern bool BitBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sx,int sy,uint operation);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(IntPtr dc);
    internal static ScreenFrame Capture(Int32Rect? area=null)
    {
        var bounds=area??new Int32Rect(GetSystemMetrics(76),GetSystemMetrics(77),GetSystemMetrics(78),GetSystemMetrics(79));
        if(bounds.Width<1||bounds.Height<1||(long)bounds.Width*bounds.Height>100_000_000)throw new InvalidOperationException("This screen is too large to capture.");
        var screen=GetDC(IntPtr.Zero);if(screen==IntPtr.Zero)throw new Win32Exception();
        var memory=CreateCompatibleDC(screen);var bitmap=CreateCompatibleBitmap(screen,bounds.Width,bounds.Height);var previous=IntPtr.Zero;
        try
        {
            if(memory==IntPtr.Zero||bitmap==IntPtr.Zero)throw new Win32Exception();previous=SelectObject(memory,bitmap);
            if(!BitBlt(memory,0,0,bounds.Width,bounds.Height,screen,bounds.X,bounds.Y,0x40cc0020))throw new Win32Exception(Marshal.GetLastWin32Error());
            var image=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());image.Freeze();return new(image,bounds);
        }
        finally{if(previous!=IntPtr.Zero)SelectObject(memory,previous);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(memory!=IntPtr.Zero)DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen);}
    }
    internal static Int32Rect Selection(Point first,Point last,Int32Rect screen)
    {
        var left=Math.Clamp((int)Math.Floor(Math.Min(first.X,last.X))-screen.X,0,screen.Width);var top=Math.Clamp((int)Math.Floor(Math.Min(first.Y,last.Y))-screen.Y,0,screen.Height);
        var right=Math.Clamp((int)Math.Ceiling(Math.Max(first.X,last.X))-screen.X,0,screen.Width);var bottom=Math.Clamp((int)Math.Ceiling(Math.Max(first.Y,last.Y))-screen.Y,0,screen.Height);
        return new(left,top,Math.Max(0,right-left),Math.Max(0,bottom-top));
    }
}
