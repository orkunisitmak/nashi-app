using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace Nashi.Windows;
/// <summary>
/// A frozen picture of one display on which the user drags an area. The window is placed in physical
/// pixels and the picture is stretched over it, so the selection maps to image pixels by proportion
/// whatever the display's scaling. Esc or a right click cancels.
/// </summary>
internal sealed class ScreenSelectionWindow:Window
{
    internal const string Hint="Drag over the text to copy · Esc to cancel";
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    private readonly Int32Rect monitor;private readonly BitmapSource picture;private readonly Canvas surface=new(){Background=Brushes.Transparent,Cursor=Cursors.Cross};
    private readonly Rectangle shade=new(){Fill=new SolidColorBrush(Color.FromArgb(90,0,0,0)),IsHitTestVisible=false};
    private readonly Rectangle box=new(){Stroke=new SolidColorBrush(Color.FromRgb(234,189,72)),StrokeThickness=2,Fill=new SolidColorBrush(Color.FromArgb(40,255,255,255)),Visibility=Visibility.Collapsed,IsHitTestVisible=false};
    private Point start;private bool dragging;
    /// <summary>The selected area in image pixels, or null when cancelled.</summary>
    internal Int32Rect? Selection{get;private set;}

    internal ScreenSelectionWindow(BitmapSource picture,Int32Rect monitor)
    {
        this.picture=picture;this.monitor=monitor;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Topmost=true;Background=Brushes.Black;Title="Nashi";
        WindowStartupLocation=WindowStartupLocation.Manual;Left=-32000;Top=-32000;Width=200;Height=200;
        var grid=new Grid();grid.Children.Add(new Image{Source=picture,Stretch=Stretch.Fill});grid.Children.Add(shade);
        var hint=L10n.Label(new TextBlock{Text=Hint,Foreground=Brushes.White,FontSize=15,Padding=new(16,8,16,8),Background=new SolidColorBrush(Color.FromArgb(200,30,34,28)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,24,0,0),IsHitTestVisible=false});
        surface.Children.Add(box);grid.Children.Add(surface);grid.Children.Add(hint);Content=grid;
        RenderOptions.SetBitmapScalingMode(grid,BitmapScalingMode.NearestNeighbor);
        // Moving onto a display with other scaling makes WPF resize the window, so the physical bounds are set again.
        void Place(){if(new WindowInteropHelper(this).Handle is var handle&&handle!=IntPtr.Zero)SetWindowPos(handle,new IntPtr(-1),monitor.X,monitor.Y,monitor.Width,monitor.Height,0x0040);}
        SourceInitialized+=(_,_)=>Place();DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(Place);
        Loaded+=(_,_)=>{Place();Activate();Focus();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){Selection=null;Close();}};
        surface.MouseRightButtonUp+=(_,_)=>{Selection=null;Close();};
        surface.MouseLeftButtonDown+=(_,e)=>{start=e.GetPosition(surface);dragging=true;surface.CaptureMouse();Update(start);box.Visibility=Visibility.Visible;};
        surface.MouseMove+=(_,e)=>{if(dragging)Update(e.GetPosition(surface));};
        surface.MouseLeftButtonUp+=(_,e)=>{if(!dragging)return;dragging=false;surface.ReleaseMouseCapture();Finish(new Rect(start,e.GetPosition(surface)));};
    }
    private void Update(Point current){var area=new Rect(start,current);Canvas.SetLeft(box,area.X);Canvas.SetTop(box,area.Y);box.Width=area.Width;box.Height=area.Height;}
    /// <summary>Ends the selection; a click without dragging keeps the window open.</summary>
    internal bool Finish(Rect area)
    {
        if(area.Width<4||area.Height<4){box.Visibility=Visibility.Collapsed;return false;}
        var pixels=ScreenText.ToPixels(area,new Size(surface.ActualWidth,surface.ActualHeight),picture.PixelWidth,picture.PixelHeight);
        if(pixels.IsEmpty)return false;
        Selection=pixels;Close();return true;
    }
    /// <summary>The selection in virtual-screen pixels.</summary>
    internal Int32Rect? ScreenSelection=>Selection is {} s?new Int32Rect(monitor.X+s.X,monitor.Y+s.Y,s.Width,s.Height):null;
}
