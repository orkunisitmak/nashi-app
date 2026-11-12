using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Nashi.Windows;

// WPF stretches a corner radius larger than half the height into an ellipse. This border clamps it,
// so a large radius reads as a capsule at any height.
public class RoundBorder:Border
{
    public static readonly DependencyProperty RadiusProperty=DependencyProperty.Register(nameof(Radius),typeof(double),typeof(RoundBorder),new PropertyMetadata(0d,(d,_)=>((RoundBorder)d).Fit()));
    public double Radius{get=>(double)GetValue(RadiusProperty);set=>SetValue(RadiusProperty,value);}
    protected override void OnRenderSizeChanged(SizeChangedInfo info){base.OnRenderSizeChanged(info);Fit();}
    private void Fit(){var limit=Math.Min(ActualWidth,ActualHeight)/2;CornerRadius=new(limit>0?Math.Min(Radius,limit):Radius);}
}

// The window glass: what is really under the window (other apps, the desktop and its icons), blurred,
// with a paper or ink tint over it. It is read again when the window appears, stops moving or comes forward.
public sealed class Backdrop:Grid
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(Backdrop),new PropertyMetadata(true));
    public static bool GetEnabled(DependencyObject element)=>(bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element,bool value)=>element.SetValue(EnabledProperty,value);
    // The latest picture under a window; the window's lenses bend it too.
    private static readonly DependencyProperty SceneProperty=DependencyProperty.RegisterAttached("Scene",typeof(Behind.View),typeof(Backdrop),new PropertyMetadata(null));
    internal static Behind.View? Scene(Window window)=>(Behind.View?)window.GetValue(SceneProperty);
    private readonly Image picture=new(){Stretch=Stretch.Fill,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};private readonly Border tint=new();private Window? window;
    private readonly DispatcherTimer settle=new(){Interval=TimeSpan.FromMilliseconds(200)};
    // The tint over the picture (a palette key; window glass by default) and, for a panel inside a window, its corner radius.
    public string Tint{set=>tint.SetResourceReference(Border.BackgroundProperty,value);}
    public double Radius{get;init;}
    public Backdrop()
    {
        IsHitTestVisible=false;ClipToBounds=true;RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);
        Tint="Slab";Children.Add(picture);Children.Add(tint);
        settle.Tick+=(_,_)=>{settle.Stop();Capture();};
        SizeChanged+=(_,_)=>{if(Radius>0)Clip=new RectangleGeometry(new Rect(RenderSize),Radius,Radius);Place();};
        IsVisibleChanged+=(_,e)=>{if(e.NewValue is true){settle.Stop();Capture();}};
        Loaded+=(_,_)=>
        {
            window=Window.GetWindow(this);if(window is null)return;
            window.LocationChanged+=Moved;window.SizeChanged+=Moved;window.DpiChanged+=Moved;window.StateChanged+=Moved;window.Activated+=Forward;
            Visibility=On?Visibility.Visible:Visibility.Collapsed;Capture();
        };
        Unloaded+=(_,_)=>
        {
            settle.Stop();if(window is null)return;
            window.LocationChanged-=Moved;window.SizeChanged-=Moved;window.DpiChanged-=Moved;window.StateChanged-=Moved;window.Activated-=Forward;window=null;
        };
    }
    // The glass fills whatever holds it and never sizes it (a toast sizes to its text, not to the screen picture).
    protected override Size MeasureOverride(Size constraint){base.MeasureOverride(constraint);return new Size(0,0);}
    private bool On=>window is not null&&Theme.Glass&&GetEnabled(window);
    // While the window moves, the picture already read slides under it; once it rests, the screen is read again.
    private void Moved(object? sender,EventArgs e){Place();settle.Stop();settle.Start();}
    private void Forward(object? sender,EventArgs e){settle.Stop();Capture();}
    // The screen is read and frosted away from the UI thread, so bringing a window forward never stalls it.
    // One read at a time per glass; a request that arrives meanwhile reads again once it is done.
    private bool reading,again;
    private async void Capture()
    {
        if(!On||window is null||!IsVisible||window.WindowState==WindowState.Minimized)return;
        if(reading){again=true;return;}
        var owner=window;var handle=new WindowInteropHelper(owner).Handle;if(handle==IntPtr.Zero)return;
        reading=true;
        try
        {
            if(await Behind.SceneAsync(handle,VisualTreeHelper.GetDpi(this).DpiScaleX) is {} scene&&ReferenceEquals(window,owner)&&On){owner.SetValue(SceneProperty,scene);Lens.RefreshAll(owner);}
            Place();
        }
        finally{reading=false;}
        if(again){again=false;Capture();}
    }
    private void Place()
    {
        if(window is null||Visibility!=Visibility.Visible)return;var handle=new WindowInteropHelper(window).Handle;
        // Nothing read yet (or reading is not possible here): the window stays plain paper.
        if(Scene(window) is not {} view||handle==IntPtr.Zero||!Behind.WindowRect(handle,out var r)){picture.Source=null;tint.Visibility=Visibility.Collapsed;return;}
        var scale=VisualTreeHelper.GetDpi(this).DpiScaleX;tint.Visibility=Visibility.Visible;picture.Source=view.Image;
        // Placed by its margin (no canvas: tests count a window's canvases), relative to this element's spot in the window.
        var at=window.IsLoaded&&IsDescendantOf(window)?TranslatePoint(new Point(0,0),window):new Point();
        picture.Width=view.Bounds.Width/scale;picture.Height=view.Bounds.Height/scale;picture.Margin=new((view.Bounds.X-r.Left)/scale-at.X,(view.Bounds.Y-r.Top)/scale-at.Y,0,0);
    }
}

// What is on screen under one of Nashi's windows. For a single frame the window is left out of screen capture
// (SetWindowDisplayAffinity, Windows 10 2004 and later) while the screen is read; right after, it is back in
// every screenshot and recording. This works whether or not Windows transparency effects are on.
internal static class Behind
{
    // Bounds: the screen rectangle the pixels cover, in physical pixels. Pixels are BGRA.
    internal sealed record View(BitmapSource Image,Rect Bounds,byte[] Pixels,int PixelWidth,int PixelHeight);
    // The monitor under a window, kept small and frosted like the design's window glass (blur 30, saturate 1.35).
    // Only leaving the window out of capture happens on the UI thread; reading and frosting run on the pool.
    internal static async Task<View?> SceneAsync(IntPtr window,double dpi)
    {
        if(!GetWindowRect(window,out var frame))return null;var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromWindow(window,2),ref info))return null;var m=info.Monitor;
        var area=new Rect32{Left=Math.Min(m.Left,frame.Left),Top=Math.Min(m.Top,frame.Top),Right=Math.Max(m.Right,frame.Right),Bottom=Math.Max(m.Bottom,frame.Bottom)};
        int areaWidth=area.Right-area.Left,areaHeight=area.Bottom-area.Top;if(areaWidth<1||areaHeight<1)return null;
        var width=Math.Clamp(areaWidth/4,160,960);var factor=areaWidth/(double)width;var height=Math.Max(1,(int)Math.Round(areaHeight/factor));
        if(await ReadAsync(window,area,width,height) is not {} pixels)return null;
        return await Task.Run(()=>{Blur(pixels,width,height,30*dpi/factor);Saturate(pixels,1.35);return Make(pixels,width,height,area);});
    }
    // Just the window's own rectangle, sharp, for clear glass that bends what is under it (the wheel).
    internal static async Task<View?> UnderAsync(IntPtr window,double dpi)
    {
        if(!GetWindowRect(window,out var area))return null;int areaWidth=area.Right-area.Left,areaHeight=area.Bottom-area.Top;if(areaWidth<1||areaHeight<1)return null;
        var factor=Math.Max(1,dpi*.75);int width=Math.Max(1,(int)(areaWidth/factor)),height=Math.Max(1,(int)(areaHeight/factor));
        if(await ReadAsync(window,area,width,height) is not {} pixels)return null;
        return await Task.Run(()=>{Saturate(pixels,1);return Make(pixels,width,height,area);});
    }
    internal static bool WindowRect(IntPtr window,out Rect32 rect)=>GetWindowRect(window,out rect);
    private static View Make(byte[] pixels,int width,int height,Rect32 area)
    {
        var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);image.Freeze();
        return new(image,new Rect(area.Left,area.Top,area.Right-area.Left,area.Bottom-area.Top),pixels,width,height);
    }
    // Windows take turns, so each one's glass still shows the others.
    private static readonly SemaphoreSlim turn=new(1,1);
    private static async Task<byte[]?> ReadAsync(IntPtr window,Rect32 area,int width,int height)
    {
        await turn.WaitAsync();
        try
        {
            if(!SetWindowDisplayAffinity(window,0x11))return null; // WDA_EXCLUDEFROMCAPTURE
            try{return await Task.Run(()=>{_ = DwmFlush();return Grab(area,width,height);});}
            finally{SetWindowDisplayAffinity(window,0);}
        }
        finally{turn.Release();}
    }
    private static byte[]? Grab(Rect32 area,int width,int height)
    {
        var screen=GetDC(IntPtr.Zero);if(screen==IntPtr.Zero)return null;var memory=CreateCompatibleDC(screen);var bitmap=IntPtr.Zero;var old=IntPtr.Zero;
        try
        {
            var header=new BitmapHeader{Size=40,Width=width,Height=-height,Planes=1,BitCount=32};bitmap=CreateDIBSection(screen,ref header,0,out var bits,IntPtr.Zero,0);if(bitmap==IntPtr.Zero)return null;
            old=SelectObject(memory,bitmap);SetStretchBltMode(memory,4);SetBrushOrgEx(memory,0,0,IntPtr.Zero); // HALFTONE averages the pixels it drops
            if(!StretchBlt(memory,0,0,width,height,screen,area.Left,area.Top,area.Right-area.Left,area.Bottom-area.Top,0x00CC0020))return null;
            var pixels=new byte[width*height*4];Marshal.Copy(bits,pixels,0,pixels.Length);return pixels;
        }
        finally{if(old!=IntPtr.Zero)SelectObject(memory,old);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);DeleteDC(memory);_ = ReleaseDC(IntPtr.Zero,screen);}
    }
    // Three box blurs in a row come within a few percent of a Gaussian of this standard deviation.
    internal static void Blur(byte[] pixels,int width,int height,double sigma)=>Nashi.Core.PhotoPixels.Blur(pixels,width,height,sigma);
    private static void Saturate(byte[] pixels,double amount)
    {
        for(var i=0;i<pixels.Length;i+=4)
        {
            double b=pixels[i],g=pixels[i+1],r=pixels[i+2];var light=.0722*b+.7152*g+.2126*r;
            pixels[i]=(byte)Math.Clamp(light+(b-light)*amount,0,255);pixels[i+1]=(byte)Math.Clamp(light+(g-light)*amount,0,255);pixels[i+2]=(byte)Math.Clamp(light+(r-light)*amount,0,255);pixels[i+3]=255;
        }
    }
    [StructLayout(LayoutKind.Sequential)]internal struct Rect32{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public Rect32 Monitor,Work;public int Flags;}
    [StructLayout(LayoutKind.Sequential)]private struct BitmapHeader{public int Size,Width,Height;public short Planes,BitCount;public int Compression,ImageSize,XPerMeter,YPerMeter,Used,Important;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out Rect32 rect);
    [DllImport("user32.dll")]private static extern bool SetWindowDisplayAffinity(IntPtr window,uint affinity);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll")]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("dwmapi.dll")]private static extern int DwmFlush();
    [DllImport("gdi32.dll")]private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapHeader header,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")]private static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]private static extern int SetStretchBltMode(IntPtr dc,int mode);
    [DllImport("gdi32.dll")]private static extern bool SetBrushOrgEx(IntPtr dc,int x,int y,IntPtr previous);
    [DllImport("gdi32.dll")]private static extern bool StretchBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sx,int sy,int sw,int sh,uint operation);
}
