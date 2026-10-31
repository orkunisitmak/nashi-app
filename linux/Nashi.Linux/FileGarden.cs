using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
namespace Nashi.Linux;
// Same composition, vector paths and original pear artwork as Windows FileGarden.
internal sealed class FileGarden:Grid
{
    internal FileGarden()
    {
        Width=166;Height=150;IsHitTestVisible=false;
        Children.Add(new Ellipse{Width=144,Height=144,Fill=NashiTheme.Brush(NashiTheme.Dark?"#3C3421":"#F6E7BE")});
        AddCard("PDF",-16,-34,12,NashiTheme.Muted);AddCard("PNG",7,31,-2,NashiTheme.Gold);
        using var stream=AssetLoader.Open(new Uri("avares://Nashi/Assets/NashiMark.png"));Children.Add(new Image{Source=new Bitmap(stream),Width=65,Height=65,RenderTransform=new TranslateTransform(0,42)});
    }
    private void AddCard(string label,double angle,double x,double y,IBrush color)
    {
        var content=new StackPanel{Spacing=8};
        content.Children.Add(new Avalonia.Controls.Shapes.Path{Width=32,Height=30,Data=Geometry.Parse(label=="PDF"?"M5,1 L22,1 L29,8 L29,29 L5,29 Z M22,1 L22,8 L29,8 M10,14 L24,14 M10,19 L24,19 M10,24 L20,24":"M2,4 L30,4 L30,27 L2,27 Z M3,25 L12,14 L18,20 L23,14 L30,23 M11,10 A2,2 0 1 1 7,10 A2,2 0 1 1 11,10"),StrokeThickness=2,Stroke=color});
        content.Children.Add(new TextBlock{Text=label,FontSize=10,FontWeight=FontWeight.Bold,Foreground=color});
        content.Children.Add(new Border{Width=37,Height=4,CornerRadius=new(2),Background=color,Opacity=.18,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left});
        var transforms=new TransformGroup();transforms.Children.Add(new RotateTransform(angle));transforms.Children.Add(new TranslateTransform(x,y));
        Children.Add(new Border{Width=74,Height=100,Padding=new(14),Background=NashiTheme.Surface,CornerRadius=new(16),Child=content,RenderTransform=transforms});
    }
}
