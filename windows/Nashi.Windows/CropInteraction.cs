using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Nashi.Windows;
public sealed partial class ToolEditor
{
    private string? cropGesture;private Rect cropBefore;private readonly TextBlock cropReadout=new();private System.Windows.Shapes.Path? cropShade,cropGuides;private readonly ComboBox aspectRatio=new(){ItemsSource=new[]{"Free","1:1","16:9","9:16","4:3","3:4"},SelectedIndex=0};
    private void BuildCropOptions()
    {
        if(tool is not ("crop" or "cropVideo"))return;
        // The dock: aspect ratios, the selection size, select all and turn the ratio.
        aspectRatio.Style=Ui.Style("SegmentedCombo");aspectRatio.DropDownOpened+=(_,_)=>aspectRatio.IsDropDownOpen=false;dock.Children.Add(aspectRatio);dock.Children.Add(DockDivider());
        cropReadout.FontSize=12.5;cropReadout.FontWeight=FontWeights.SemiBold;cropReadout.VerticalAlignment=VerticalAlignment.Center;var readout=new RoundBorder{Radius=99,Height=34,Padding=new(12,0,12,0),Child=cropReadout};readout.SetResourceReference(Border.BackgroundProperty,"Sel");dock.Children.Add(readout);dock.Children.Add(DockDivider());
        dock.Children.Add(DockIcon("frame","Select All",()=>{if(drawing.Width>0)SetCrop(new(0,0,drawing.Width,drawing.Height));}));
        dock.Children.Add(DockIcon("rotL","Rotate",()=>{if(aspectRatio.SelectedItem is string value&&value.Split(':') is [var a,var b])aspectRatio.SelectedItem=b+":"+a;}));
        aspectRatio.SelectionChanged+=(_,_)=>{var ratio=CropRatio();if(ratio is null)return;var w=Math.Min(drawing.Width,drawing.Height*ratio.Value);var h=w/ratio.Value;SetCrop(new((drawing.Width-w)/2,(drawing.Height-h)/2,w,h));};
        foreach(var name in new[]{"X","Y","Width","Height"})fields[name].TextChanged+=(_,_)=>ShowCropSize();ShowCropSize();
        // The handles keep their on-screen size when the window or the zoom changes.
        drawing.LayoutUpdated+=(_,_)=>{if(selection is null||cropShade is null||cropDecorUnit<=0)return;var unit=CropUnit();if(Math.Abs(unit-cropDecorUnit)/cropDecorUnit>.02)CropDecor(new Rect(Canvas.GetLeft(selection),Canvas.GetTop(selection),selection.Width,selection.Height));};
    }
    private double? CropRatio(){if(aspectRatio.SelectedItem is not string value||value=="Free")return null;var p=value.Split(':');return double.Parse(p[0],System.Globalization.CultureInfo.InvariantCulture)/double.Parse(p[1],System.Globalization.CultureInfo.InvariantCulture);}
    private bool BeginCropGesture(Point point)
    {
        cropGesture=null;if(tool is not ("crop" or "cropVideo")||selection is null||selection.Width<1||selection.Height<1)return false;
        cropBefore=new(Canvas.GetLeft(selection),Canvas.GetTop(selection),selection.Width,selection.Height);cropGesture=CropHit(point);return cropGesture is not null;
    }
    // How many picture pixels one screen pixel covers now (the picture is scaled to fit, and may be zoomed).
    private double CropUnit(){try{var span=drawing.TransformToAncestor(this).TransformBounds(new Rect(0,0,100,100)).Width;return span>.01?100/span:1;}catch(InvalidOperationException){return 1;}}
    // What a drag from this point does: a corner (within 20 screen pixels) or an edge (within 14) resizes, inside moves.
    private string? CropHit(Point point)
    {
        if(selection is null||selection.Width<1||selection.Height<1)return null;var r=new Rect(Canvas.GetLeft(selection),Canvas.GetTop(selection),selection.Width,selection.Height);var unit=CropUnit();var corner=20*unit;var edge=14*unit;
        foreach(var (kind,at) in new[]{("NW",r.TopLeft),("NE",r.TopRight),("SW",r.BottomLeft),("SE",r.BottomRight)})if((point-at).Length<=corner)return kind;
        var alongX=point.X>=r.Left-edge&&point.X<=r.Right+edge;var alongY=point.Y>=r.Top-edge&&point.Y<=r.Bottom+edge;
        if(alongY&&Math.Abs(point.X-r.Left)<=edge)return "W";if(alongY&&Math.Abs(point.X-r.Right)<=edge)return "E";if(alongX&&Math.Abs(point.Y-r.Top)<=edge)return "N";if(alongX&&Math.Abs(point.Y-r.Bottom)<=edge)return "S";
        return r.Contains(point)?"move":null;
    }
    private static System.Windows.Input.Cursor CropCursor(string? gesture)=>gesture switch{"NW" or "SE"=>System.Windows.Input.Cursors.SizeNWSE,"NE" or "SW"=>System.Windows.Input.Cursors.SizeNESW,"N" or "S"=>System.Windows.Input.Cursors.SizeNS,"E" or "W"=>System.Windows.Input.Cursors.SizeWE,"move"=>System.Windows.Input.Cursors.SizeAll,_=>System.Windows.Input.Cursors.Cross};
    private bool MoveCropGesture(Point point)
    {
        if(cropGesture is null)return false;var delta=point-origin;
        if(cropGesture=="move"){SetCrop(new(Math.Clamp(cropBefore.X+delta.X,0,drawing.Width-cropBefore.Width),Math.Clamp(cropBefore.Y+delta.Y,0,drawing.Height-cropBefore.Height),cropBefore.Width,cropBefore.Height));return true;}
        // An edge moves one side; with a fixed shape the other side follows around the middle.
        if(cropGesture is "N" or "S" or "E" or "W")
        {
            var box=cropBefore;var shape=CropRatio()??(System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)?box.Width/box.Height:(double?)null);double left=box.Left,top=box.Top,right=box.Right,bottom=box.Bottom;
            switch(cropGesture){case "W":left=Math.Clamp(point.X,0,right-1);break;case "E":right=Math.Clamp(point.X,left+1,drawing.Width);break;case "N":top=Math.Clamp(point.Y,0,bottom-1);break;default:bottom=Math.Clamp(point.Y,top+1,drawing.Height);break;}
            if(shape is {} q&&cropGesture is "W" or "E"){var wide=Math.Min(right-left,drawing.Height*q);var tall=wide/q;if(cropGesture=="W")left=right-wide;else right=left+wide;top=Math.Clamp(box.Top+box.Height/2-tall/2,0,drawing.Height-tall);bottom=top+tall;}
            else if(shape is {} p){var tall=Math.Min(bottom-top,drawing.Width/p);var wide=tall*p;if(cropGesture=="N")top=bottom-tall;else bottom=top+tall;left=Math.Clamp(box.Left+box.Width/2-wide/2,0,drawing.Width-wide);right=left+wide;}
            SetCrop(new Rect(new Point(left,top),new Point(right,bottom)));return true;
        }
        var anchor=cropGesture switch{"NW"=>cropBefore.BottomRight,"NE"=>cropBefore.BottomLeft,"SW"=>cropBefore.TopRight,_=>cropBefore.TopLeft};var dx=point.X-anchor.X;var dy=point.Y-anchor.Y;var w=Math.Abs(dx);var h=Math.Abs(dy);var ratio=CropRatio()??(System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)?cropBefore.Width/cropBefore.Height:(double?)null);if(ratio is {} r){w=Math.Min(w,h*r);h=w/r;}SetCrop(new(Math.Min(anchor.X,anchor.X+Math.CopySign(w,dx)),Math.Min(anchor.Y,anchor.Y+Math.CopySign(h,dy)),Math.Max(1,w),Math.Max(1,h)));return true;
    }
    private void SetCrop(Rect rect)
    {
        if(selection is null){selection=new Rectangle{Stroke=Brushes.White,StrokeThickness=Math.Max(2,drawing.Width/350),Fill=new SolidColorBrush(Color.FromArgb(26,255,255,255)),IsHitTestVisible=false};drawing.Children.Add(selection);}
        Canvas.SetLeft(selection,rect.X);Canvas.SetTop(selection,rect.Y);selection.Width=rect.Width;selection.Height=rect.Height;fields["X"].Text=Number(Math.Floor(rect.X));fields["Y"].Text=Number(Math.Floor(rect.Y));fields["Width"].Text=Number(Math.Floor(rect.Width));fields["Height"].Text=Number(Math.Floor(rect.Height));CropDecor(rect);
    }
    private void ShowCropSize(){if(fields.TryGetValue("Width",out var w)&&fields.TryGetValue("Height",out var h))cropReadout.Text=$"{w.Text} × {h.Text}";}
    private double cropDecorUnit;
    // Around the selection the picture dims; inside, a rule-of-thirds grid, four corner handles and a grip on each edge,
    // sized on screen so they stay easy to see and take at any zoom.
    private void CropDecor(Rect? rect)
    {
        if(tool is not ("crop" or "cropVideo"))return;
        if(rect is not {Width:>0,Height:>0} r){if(cropShade is not null){drawing.Children.Remove(cropShade);drawing.Children.Remove(cropGuides);cropShade=cropGuides=null;}return;}
        if(cropShade is null||!drawing.Children.Contains(cropShade)){cropShade=new System.Windows.Shapes.Path{Fill=new SolidColorBrush(Color.FromArgb(133,0,0,0)),IsHitTestVisible=false};cropGuides=new System.Windows.Shapes.Path{Stroke=Brushes.White,IsHitTestVisible=false,StrokeLineJoin=PenLineJoin.Miter};drawing.Children.Insert(1,cropShade);drawing.Children.Add(cropGuides);}
        var unit=CropUnit();cropDecorUnit=unit;if(selection is not null)selection.StrokeThickness=1.5*unit;cropShade.Data=new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(new Rect(0,0,drawing.Width,drawing.Height)),new RectangleGeometry(r));
        var guides=new GeometryGroup();for(var n=1;n<3;n++){guides.Children.Add(new LineGeometry(new Point(r.X+r.Width*n/3,r.Y),new Point(r.X+r.Width*n/3,r.Bottom)));guides.Children.Add(new LineGeometry(new Point(r.X,r.Y+r.Height*n/3),new Point(r.Right,r.Y+r.Height*n/3)));}
        cropGuides!.Data=guides;cropGuides.StrokeThickness=unit;cropGuides.Opacity=.45;
        var handles=new GeometryGroup();var arm=Math.Min(24*unit,Math.Min(r.Width,r.Height)/3);var inset=unit*2;var grip=Math.Min(28*unit,Math.Min(r.Width,r.Height)/4);
        foreach(var (x,y,horizontal) in new[]{(r.X+r.Width/2,r.Y-inset,true),(r.X+r.Width/2,r.Bottom+inset,true),(r.X-inset,r.Y+r.Height/2,false),(r.Right+inset,r.Y+r.Height/2,false)})handles.Children.Add(horizontal?new LineGeometry(new Point(x-grip/2,y),new Point(x+grip/2,y)):new LineGeometry(new Point(x,y-grip/2),new Point(x,y+grip/2)));
        foreach(var (x,y,dx,dy) in new[]{(r.X,r.Y,1,1),(r.Right,r.Y,-1,1),(r.X,r.Bottom,1,-1),(r.Right,r.Bottom,-1,-1)}){var corner=new PathFigure{StartPoint=new(x+dx*arm-dx*inset,y-dy*inset)};corner.Segments.Add(new PolyLineSegment([new Point(x-dx*inset,y-dy*inset),new Point(x-dx*inset,y+dy*arm-dy*inset)],true));handles.Children.Add(new PathGeometry([corner]));}
        if(drawing.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault(p=>p.Tag as string=="handles") is {} existing)drawing.Children.Remove(existing);
        drawing.Children.Add(new System.Windows.Shapes.Path{Data=handles,Stroke=Brushes.White,StrokeThickness=4*unit,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,IsHitTestVisible=false,Tag="handles"});
    }
}
