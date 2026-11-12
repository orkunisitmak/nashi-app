using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;

// The magnifier: a framed loupe showing the spot under it twice as large. It stays editable: drag it to move
// (the picture inside follows), drag a corner to resize, Delete removes it; a plain click drops a standard one.
public sealed partial class ToolEditor
{
    private Border? loupe;private int loupeCorner;private Rect loupeFrom;private Point loupeGrab;private Adorner? loupeHandles;
    private static bool IsLoupe(object? element)=>element is Border{Tag:"loupe"};
    private static Rect LoupeArea(Border mark){var x=Canvas.GetLeft(mark);var y=Canvas.GetTop(mark);if(mark.RenderTransform is TranslateTransform t){x+=t.X;y+=t.Y;}return new(x,y,mark.Width,mark.Height);}
    private static Border NewLoupe(){var mark=new Border{Tag="loupe",BorderBrush=Brushes.White,IsHitTestVisible=false,Effect=new DropShadowEffect{Color=Colors.Black,Opacity=.35,ShadowDepth=0}};RenderOptions.SetBitmapScalingMode(mark,BitmapScalingMode.HighQuality);return mark;}
    private void PlaceLoupe(Border mark,Rect area)
    {
        mark.RenderTransform=Transform.Identity;Canvas.SetLeft(mark,area.X);Canvas.SetTop(mark,area.Y);mark.Width=Math.Max(1,area.Width);mark.Height=Math.Max(1,area.Height);
        var side=Math.Min(area.Width,area.Height);mark.CornerRadius=new(side*.14);mark.BorderThickness=new(Math.Max(2,side/60));if(mark.Effect is DropShadowEffect shade)shade.BlurRadius=Math.Max(8,side*.1);
        if(bitmap is null)return;
        // Twice the detail: the middle half of the area, kept inside the picture.
        int w=Math.Clamp((int)(area.Width/2),1,bitmap.PixelWidth),h=Math.Clamp((int)(area.Height/2),1,bitmap.PixelHeight);
        var x=Math.Clamp((int)Math.Round(area.X+area.Width/2-w/2d),0,bitmap.PixelWidth-w);var y=Math.Clamp((int)Math.Round(area.Y+area.Height/2-h/2d),0,bitmap.PixelHeight-h);
        mark.Background=new ImageBrush(new CroppedBitmap(bitmap,new Int32Rect(x,y,w,h))){Stretch=Stretch.Fill};
    }
    // What a point takes of the topmost loupe under it: 0–3 a corner (clockwise from top left), 4 the body.
    private (Border Mark,int Part)? LoupeAt(Point point)
    {
        var reach=10*PictureUnit();
        foreach(var mark in drawing.Children.OfType<Border>().Where(IsLoupe).Reverse())
        {
            var r=LoupeArea(mark);Point[] corners=[r.TopLeft,r.TopRight,r.BottomRight,r.BottomLeft];
            for(var n=0;n<4;n++)if((corners[n]-point).Length<=reach)return(mark,n);
            if(r.Contains(point))return(mark,4);
        }
        return null;
    }
    // Picture pixels per screen point: the picture may be shown smaller or larger than it is.
    private double PictureUnit()=>Unit(drawing);
    private static double Unit(Visual visual)
    {
        try{var a=visual.PointToScreen(new Point(0,0));var b=visual.PointToScreen(new Point(100,0));return 100/Math.Max(1e-3,(b-a).Length/VisualTreeHelper.GetDpi(visual).DpiScaleX);}
        catch(InvalidOperationException){return 1;}
    }
    private void GrabLoupe(Border mark,int part){loupe=mark;loupeCorner=part;loupeFrom=LoupeArea(mark);loupeGrab=origin;selectedAnnotation=mark;ShowLoupeHandles(mark);}
    private void DragLoupe(Point point)
    {
        if(loupe is null)return;var r=loupeFrom;var d=point-loupeGrab;
        if(loupeCorner==4){PlaceLoupe(loupe,new Rect(Math.Clamp(r.X+d.X,0,Math.Max(0,drawing.Width-r.Width)),Math.Clamp(r.Y+d.Y,0,Math.Max(0,drawing.Height-r.Height)),r.Width,r.Height));return;}
        // A corner follows the pointer while the opposite one stays put; Shift keeps it square.
        Point[] corners=[r.TopLeft,r.TopRight,r.BottomRight,r.BottomLeft];var anchor=corners[(loupeCorner+2)%4];var moving=corners[loupeCorner]+d;
        var dx=moving.X-anchor.X;var dy=moving.Y-anchor.Y;var least=16*PictureUnit();
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){var side=Math.Max(Math.Abs(dx),Math.Abs(dy));dx=Math.CopySign(side,dx);dy=Math.CopySign(side,dy);}
        if(Math.Abs(dx)<least)dx=Math.CopySign(least,dx);if(Math.Abs(dy)<least)dy=Math.CopySign(least,dy);
        var next=Rect.Intersect(new Rect(anchor,new Point(anchor.X+dx,anchor.Y+dy)),new Rect(0,0,drawing.Width,drawing.Height));if(!next.IsEmpty&&next.Width>=1&&next.Height>=1)PlaceLoupe(loupe,next);
    }
    private void EndLoupe()
    {
        if(loupe is null)return;
        if(loupeCorner<0)
        {
            // A click (or a tiny drag) drops a standard loupe centered where it landed.
            var r=LoupeArea(loupe);
            if(double.IsNaN(r.Width)||double.IsNaN(r.Height)||r.Width<12*PictureUnit()||r.Height<12*PictureUnit())
            {
                var side=Math.Min(Math.Min(drawing.Width,drawing.Height),Math.Max(48,Math.Min(drawing.Width,drawing.Height)*.28));
                PlaceLoupe(loupe,new Rect(Math.Clamp(origin.X-side/2,0,Math.Max(0,drawing.Width-side)),Math.Clamp(origin.Y-side/2,0,Math.Max(0,drawing.Height-side)),side,side));
            }
            selectedAnnotation=loupe;ShowLoupeHandles(loupe);
        }
        loupe=null;
    }
    private void HoverLoupe(Point point)
    {
        if(drawingTool.SelectedIndex is not (8 or 11)){drawing.Cursor=null;return;}
        drawing.Cursor=LoupeAt(point) is {} hit?hit.Part switch{0 or 2=>Cursors.SizeNWSE,1 or 3=>Cursors.SizeNESW,_=>Cursors.SizeAll}:null;
    }
    // The chosen loupe shows its four corner grips; they live above the picture and are never saved with it.
    private void ShowLoupeHandles(Border? mark)
    {
        if(loupeHandles is not null){AdornerLayer.GetAdornerLayer(loupeHandles.AdornedElement)?.Remove(loupeHandles);loupeHandles=null;}
        if(mark is null||AdornerLayer.GetAdornerLayer(mark) is not {} layer)return;loupeHandles=new LoupeGrips(mark);layer.Add(loupeHandles);
    }
    private sealed class LoupeGrips(UIElement mark):Adorner(mark)
    {
        private static readonly Pen Ring=MakeRing();
        private static Pen MakeRing(){var pen=new Pen(new SolidColorBrush(Color.FromArgb(110,0,0,0)),1);pen.Freeze();return pen;}
        protected override void OnRender(DrawingContext dc)
        {
            var unit=Unit(AdornedElement);var r=new Rect(AdornedElement.RenderSize);var ring=Ring.Clone();ring.Thickness=unit;
            foreach(var corner in new[]{r.TopLeft,r.TopRight,r.BottomRight,r.BottomLeft})dc.DrawEllipse(Brushes.White,ring,corner,5*unit,5*unit);
        }
    }
}
