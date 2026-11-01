using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Nashi.Windows;
public sealed partial class ToolEditor
{
    private string? cropGesture;private Rect cropBefore;private readonly ComboBox aspectRatio=new(){ItemsSource=new[]{"Free","1:1","16:9","9:16","4:3","3:4"},SelectedIndex=0};
    private void BuildCropOptions()
    {
        if(tool is not ("crop" or "cropVideo"))return;options.Children.Add(L10n.Label(new TextBlock{Text="Aspect ratio",Margin=new(4,12,4,0)}));options.Children.Add(aspectRatio);
        aspectRatio.SelectionChanged+=(_,_)=>{var ratio=CropRatio();if(ratio is null)return;var w=Math.Min(drawing.Width,drawing.Height*ratio.Value);var h=w/ratio.Value;SetCrop(new((drawing.Width-w)/2,(drawing.Height-h)/2,w,h));};
        options.Children.Add(L10n.Label(new TextBlock{Text="Drag inside to move. Drag near a corner to resize. Drag outside to start a new selection.",TextWrapping=TextWrapping.Wrap,Margin=new(4,10,4,10)}));
        options.Children.Add(L10n.Label(new TextBlock{Text="Alignment",FontWeight=FontWeights.SemiBold,Margin=new(4,8,4,2)}));
        var hPanel=new WrapPanel{Margin=new(0,0,0,4)};
        hPanel.Children.Add(L10n.Label(new TextBlock{Text="Horizontal",Width=72,VerticalAlignment=VerticalAlignment.Center}));
        hPanel.Children.Add(MainWindow.Button("Left",()=>AlignCrop(horizontal:-1)));
        hPanel.Children.Add(MainWindow.Button("Center",()=>AlignCrop(horizontal:0)));
        hPanel.Children.Add(MainWindow.Button("Right",()=>AlignCrop(horizontal:1)));
        options.Children.Add(hPanel);
        var vPanel=new WrapPanel{Margin=new(0,0,0,4)};
        vPanel.Children.Add(L10n.Label(new TextBlock{Text="Vertical",Width=72,VerticalAlignment=VerticalAlignment.Center}));
        vPanel.Children.Add(MainWindow.Button("Top",()=>AlignCrop(vertical:-1)));
        vPanel.Children.Add(MainWindow.Button("Center",()=>AlignCrop(vertical:0)));
        vPanel.Children.Add(MainWindow.Button("Bottom",()=>AlignCrop(vertical:1)));
        options.Children.Add(vPanel);
        options.Children.Add(MainWindow.Button("Center Selection",()=>AlignCrop(horizontal:0,vertical:0)));
    }
    private void AlignCrop(int? horizontal = null, int? vertical = null)
    {
        if(tool is not ("crop" or "cropVideo"))return;
        var cur = selection is not null && selection.Width > 0 && selection.Height > 0
            ? new Rect(Canvas.GetLeft(selection),Canvas.GetTop(selection),selection.Width,selection.Height)
            : new Rect(0,0,drawing.Width,drawing.Height);
        var x = cur.X;
        var y = cur.Y;
        if(horizontal is {} h)
        {
            var maxW = Math.Max(0, drawing.Width - cur.Width);
            x = h < 0 ? 0 : h == 0 ? Math.Floor(maxW / 2) : maxW;
        }
        if(vertical is {} v)
        {
            var maxH = Math.Max(0, drawing.Height - cur.Height);
            y = v < 0 ? 0 : v == 0 ? Math.Floor(maxH / 2) : maxH;
        }
        SetCrop(new(x, y, cur.Width, cur.Height));
    }
    private double? CropRatio(){if(aspectRatio.SelectedItem is not string value||value=="Free")return null;var p=value.Split(':');return double.Parse(p[0],System.Globalization.CultureInfo.InvariantCulture)/double.Parse(p[1],System.Globalization.CultureInfo.InvariantCulture);}
    private bool BeginCropGesture(Point point)
    {
        cropGesture=null;if(tool is not ("crop" or "cropVideo")||selection is null||selection.Width<1||selection.Height<1)return false;
        cropBefore=new(Canvas.GetLeft(selection),Canvas.GetTop(selection),selection.Width,selection.Height);var tolerance=Math.Max(10,drawing.Width/45);
        var corners=new[]{("NW",cropBefore.TopLeft),("NE",cropBefore.TopRight),("SW",cropBefore.BottomLeft),("SE",cropBefore.BottomRight)};
        foreach(var (kind,corner) in corners)if((point-corner).Length<=tolerance){cropGesture=kind;return true;}
        if(cropBefore.Contains(point)){cropGesture="move";return true;}return false;
    }
    private bool MoveCropGesture(Point point)
    {
        if(cropGesture is null)return false;var delta=point-origin;
        if(cropGesture=="move"){SetCrop(new(Math.Clamp(cropBefore.X+delta.X,0,drawing.Width-cropBefore.Width),Math.Clamp(cropBefore.Y+delta.Y,0,drawing.Height-cropBefore.Height),cropBefore.Width,cropBefore.Height));return true;}
        var anchor=cropGesture switch{"NW"=>cropBefore.BottomRight,"NE"=>cropBefore.BottomLeft,"SW"=>cropBefore.TopRight,_=>cropBefore.TopLeft};var dx=point.X-anchor.X;var dy=point.Y-anchor.Y;var w=Math.Abs(dx);var h=Math.Abs(dy);var ratio=CropRatio()??(System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)?cropBefore.Width/cropBefore.Height:(double?)null);if(ratio is {} r){w=Math.Min(w,h*r);h=w/r;}SetCrop(new(Math.Min(anchor.X,anchor.X+Math.CopySign(w,dx)),Math.Min(anchor.Y,anchor.Y+Math.CopySign(h,dy)),Math.Max(1,w),Math.Max(1,h)));return true;
    }
    private void SetCrop(Rect rect)
    {
        if(selection is null){selection=new Rectangle{Stroke=Brushes.Gold,StrokeThickness=Math.Max(2,drawing.Width/350),Fill=new SolidColorBrush(Color.FromArgb(35,224,189,82)),IsHitTestVisible=false};drawing.Children.Add(selection);}
        Canvas.SetLeft(selection,rect.X);Canvas.SetTop(selection,rect.Y);selection.Width=rect.Width;selection.Height=rect.Height;fields["X"].Text=Number(Math.Floor(rect.X));fields["Y"].Text=Number(Math.Floor(rect.Y));fields["Width"].Text=Number(Math.Floor(rect.Width));fields["Height"].Text=Number(Math.Floor(rect.Height));
    }
}
