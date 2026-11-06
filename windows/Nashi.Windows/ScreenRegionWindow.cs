using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace Nashi.Windows;

internal sealed class ScreenRegionWindow:Window
{
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    private readonly ScreenFrame frame;private readonly Canvas selection=new(){Background=Brushes.Transparent};private readonly System.Windows.Shapes.Path shade=new(){Fill=new SolidColorBrush(Color.FromArgb(125,0,0,0)),IsHitTestVisible=false};
    private readonly Rectangle outline=new(){StrokeThickness=2,IsHitTestVisible=false};private Point? start;private Rect selected=Rect.Empty;
    internal BitmapSource? SelectedImage{get;private set;}
    internal Canvas SelectionSurface=>selection;
    internal ScreenRegionWindow(ScreenFrame frame)
    {
        this.frame=frame;Title="Select a screen area · Nashi";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Topmost=true;Cursor=Cursors.Cross;Left=0;Top=0;Width=frame.Bounds.Width;Height=frame.Bounds.Height;Background=Brushes.Black;
        var root=new Grid();Content=root;root.Children.Add(new Image{Source=frame.Image,Stretch=Stretch.Fill});root.Children.Add(selection);selection.Children.Add(shade);selection.Children.Add(outline);outline.SetResourceReference(Shape.StrokeProperty,"Gold");
        var hint=new Border{Background=Theme.Brush("Surface"),CornerRadius=new(12),Padding=new(18,12,18,12),Margin=new(20),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false,Child=MainWindow.Text("Drag to select an area. Esc to cancel.",16,"Ink")};root.Children.Add(hint);
        Loaded+=(_,_)=>{Place();Activate();Shade(Rect.Empty);};DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(Place);
        selection.SizeChanged+=(_,_)=>Shade(selected);
        selection.MouseLeftButtonDown+=(_,e)=>{start=e.GetPosition(selection);selection.CaptureMouse();e.Handled=true;};
        selection.MouseMove+=(_,e)=>{if(start is {} first)UpdateSelection(first,e.GetPosition(selection));};
        selection.MouseLeftButtonUp+=(_,e)=>{if(start is not {} first)return;selection.ReleaseMouseCapture();Select(first,e.GetPosition(selection));start=null;e.Handled=true;};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}else if(e.Key==Key.Enter){SelectedImage=frame.Image;DialogResult=true;e.Handled=true;}};MouseRightButtonUp+=(_,_)=>Close();
    }
    private void Place(){var r=frame.Bounds;SetWindowPos(new WindowInteropHelper(this).Handle,new(-1),r.X,r.Y,r.Width,r.Height,0x10);}
    internal void UpdateSelection(Point first,Point last){selected=new Rect(first,last);Canvas.SetLeft(outline,selected.Left);Canvas.SetTop(outline,selected.Top);outline.Width=selected.Width;outline.Height=selected.Height;Shade(selected);}
    private void Shade(Rect selected)
    {
        var all=new RectangleGeometry(new Rect(0,0,Math.Max(0,selection.ActualWidth),Math.Max(0,selection.ActualHeight)));shade.Data=selected.IsEmpty?all:new CombinedGeometry(GeometryCombineMode.Exclude,all,new RectangleGeometry(selected));
    }
    internal void Select(Point first,Point last)
    {
        var rectangle=ScreenCapture.Selection(selection.PointToScreen(first),selection.PointToScreen(last),frame.Bounds);if(rectangle.Width<4||rectangle.Height<4)return;
        var image=new WriteableBitmap(new CroppedBitmap(frame.Image,rectangle));image.Freeze();SelectedImage=image;DialogResult=true;
    }
}
