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
        double typedX, typedY, typedW, typedH;
        try
        {
            // Fields are the source of truth: SetCrop keeps them in sync with the selection,
            // and anything typed afterwards must win over the drawn rectangle.
            typedX = Value("X");
            typedY = Value("Y");
            typedW = Value("Width", drawing.Width);
            typedH = Value("Height", drawing.Height);
        }
        catch(ArgumentException error)
        {
            if(App.IsSmokeTest)throw;
            MessageBox.Show(this, L10n.Text(error.Message), L10n.Text("Check crop settings"));
            return;
        }
        var hasSelection = selection is not null && selection.Width > 0 && selection.Height > 0;
        // An untouched field shows Floor(selection size); use the selection's rounded size then,
        // so a locked ratio's 1919.9999 keeps its last pixel.
        var w = hasSelection && typedW == Math.Floor(selection!.Width) ? Math.Round(selection.Width) : Math.Round(typedW);
        var h = hasSelection && typedH == Math.Floor(selection!.Height) ? Math.Round(selection.Height) : Math.Round(typedH);
        var imageW = Math.Max(1, Math.Floor(drawing.Width));
        var imageH = Math.Max(1, Math.Floor(drawing.Height));
        w = Math.Clamp(w, 1, imageW);
        h = Math.Clamp(h, 1, imageH);
        var maxX = imageW - w;
        var maxY = imageH - h;
        // The axis that is not being aligned keeps its typed value, kept inside the image.
        var x = Math.Clamp(Math.Floor(typedX), 0, maxX);
        var y = Math.Clamp(Math.Floor(typedY), 0, maxY);
        if(horizontal is {} hAlign)x = hAlign < 0 ? 0 : hAlign == 0 ? Math.Floor(maxX / 2) : maxX;
        if(vertical is {} vAlign)y = vAlign < 0 ? 0 : vAlign == 0 ? Math.Floor(maxY / 2) : maxY;
        SetCrop(new(x, y, w, h));
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
        if(cropGesture=="move"){SetCrop(new(Math.Clamp(cropBefore.X+delta.X,0,Math.Max(0,drawing.Width-cropBefore.Width)),Math.Clamp(cropBefore.Y+delta.Y,0,Math.Max(0,drawing.Height-cropBefore.Height)),cropBefore.Width,cropBefore.Height));return true;}
        var anchor=cropGesture switch{"NW"=>cropBefore.BottomRight,"NE"=>cropBefore.BottomLeft,"SW"=>cropBefore.TopRight,_=>cropBefore.TopLeft};var dx=point.X-anchor.X;var dy=point.Y-anchor.Y;var w=Math.Abs(dx);var h=Math.Abs(dy);var ratio=CropRatio()??(System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)?cropBefore.Width/cropBefore.Height:(double?)null);if(ratio is {} r){w=Math.Min(w,h*r);h=w/r;}SetCrop(new(Math.Min(anchor.X,anchor.X+Math.CopySign(w,dx)),Math.Min(anchor.Y,anchor.Y+Math.CopySign(h,dy)),Math.Max(1,w),Math.Max(1,h)));return true;
    }
    private void SetCrop(Rect rect)
    {
        if(selection is null){selection=new Rectangle{Stroke=Brushes.Gold,StrokeThickness=Math.Max(2,drawing.Width/350),Fill=new SolidColorBrush(Color.FromArgb(35,224,189,82)),IsHitTestVisible=false};drawing.Children.Add(selection);}
        Canvas.SetLeft(selection,rect.X);Canvas.SetTop(selection,rect.Y);selection.Width=rect.Width;selection.Height=rect.Height;fields["X"].Text=Number(Math.Floor(rect.X));fields["Y"].Text=Number(Math.Floor(rect.Y));fields["Width"].Text=Number(Math.Floor(rect.Width));fields["Height"].Text=Number(Math.Floor(rect.Height));
    }
}
