using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace Nashi.Windows;

// The same composition and exported pear artwork as macOS HomeView.FileGarden.
internal sealed class FileGarden:Grid
{
    private readonly Image mark;
    internal FileGarden()
    {
        Width=166;Height=150;IsHitTestVisible=false;
        var circle=new Ellipse{Width=144,Height=144};circle.SetResourceReference(Shape.FillProperty,"Sand");Children.Add(circle);
        AddCard("PDF",-16,-34,12,"Muted");AddCard("PNG",7,31,-2,"Gold");
        mark=new Image{Width=65,Height=65,RenderTransform=new TranslateTransform(0,42)};Children.Add(mark);SetSelected(false);
    }
    internal void SetSelected(bool selected)=>mark.Source=Artwork(selected?"NashiMarkHappy":"NashiMark");
    internal static BitmapImage Artwork(string name)=>new(new Uri("pack://application:,,,/Assets/"+(name is "NashiMark" or "NashiMarkHappy"?"NashiMascot":name)+".png"));
    private void AddCard(string label,double angle,double x,double y,string color)
    {
        var content=new StackPanel();
        // Draw the small document/photo symbols rather than depending on
        // platform glyph fallbacks, which can turn the photo into a square.
        var glyph=new Canvas{Width=32,Height=30,Margin=new(0,0,0,8),HorizontalAlignment=HorizontalAlignment.Left};
        var outline=new System.Windows.Shapes.Path{Data=Geometry.Parse(label=="PDF"?"M5,1 L22,1 L29,8 L29,29 L5,29 Z M22,1 L22,8 L29,8 M10,14 L24,14 M10,19 L24,19 M10,24 L20,24":"M2,4 L30,4 L30,27 L2,27 Z M3,25 L12,14 L18,20 L23,14 L30,23 M11,10 A2,2 0 1 1 7,10 A2,2 0 1 1 11,10"),StrokeThickness=2,StrokeLineJoin=PenLineJoin.Round};outline.SetResourceReference(Shape.StrokeProperty,color);glyph.Children.Add(outline);content.Children.Add(glyph);
        var block=L10n.Label(new TextBlock{Text=label,FontSize=10,FontWeight=FontWeights.Bold,Margin=new(0,0,0,8)});block.SetResourceReference(TextBlock.ForegroundProperty,color);content.Children.Add(block);
        var line=new Border{Width=37,Height=4,CornerRadius=new(2),HorizontalAlignment=HorizontalAlignment.Left,Opacity=.18};line.SetResourceReference(Border.BackgroundProperty,color);content.Children.Add(line);
        var transforms=new TransformGroup();transforms.Children.Add(new RotateTransform(angle));transforms.Children.Add(new TranslateTransform(x,y));
        var card=new Border{Width=77,Height=103,CornerRadius=new(15),Padding=new(13),Child=content,BorderThickness=new(1),RenderTransformOrigin=new(.5,.5),RenderTransform=transforms};card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Line");Children.Add(card);
    }
}
