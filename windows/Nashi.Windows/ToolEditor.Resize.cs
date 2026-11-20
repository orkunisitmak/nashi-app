using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;
/// <summary>Resize › templates: ready-made sizes (social media, web, print) and Fit / Fill / Stretch, with a live preview.</summary>
public sealed partial class ToolEditor
{
    internal const string TemplateLabel="Template",CustomSize="Custom size",ShapeLabel="When the shape differs",MarginsLabel="Margins";
    internal const string FitInside="Fit inside (keep everything)",FillCrop="Fill and crop",StretchToSize="Stretch to size";
    internal const string BlurredMargins="Blurred copy",WhiteMargins="White margins",BlackMargins="Black margins",TransparentMargins="Transparent";
    /// <summary>A template entry shown as "Instagram · Portrait post · 1080 × 1350".</summary>
    internal sealed record TemplateChoice(ImageTemplate? Template)
    {
        public override string ToString()=>Template is null?L10n.Text(CustomSize):$"{L10n.Text(Template.Group)} · {L10n.Text(Template.Name)} · {Template.Width} × {Template.Height}";
    }
    private readonly ComboBox templateChoice=new(){ItemsSource=FitLayout.Templates.Select(t=>new TemplateChoice(t)).Prepend(new TemplateChoice(null)).ToArray(),SelectedIndex=0};
    private readonly ComboBox shapeChoice=new(){ItemsSource=new[]{StretchToSize,FitInside,FillCrop},SelectedIndex=0};
    private readonly ComboBox marginChoice=new(){ItemsSource=new[]{BlurredMargins,WhiteMargins,BlackMargins,TransparentMargins},SelectedIndex=0};
    private int resizeVersion;private Task resizePreview=Task.CompletedTask;
    internal ComboBox TemplateForTest=>templateChoice;internal ComboBox ShapeForTest=>shapeChoice;internal ComboBox MarginsForTest=>marginChoice;internal Task ResizePreviewForTest=>resizePreview;

    internal FitMode FitSetting=>shapeChoice.SelectedItem switch{FitInside=>FitMode.Fit,FillCrop=>FitMode.Fill,_=>FitMode.Stretch};
    internal string MatteSetting=>marginChoice.SelectedItem switch{WhiteMargins=>"white",BlackMargins=>"black",TransparentMargins=>"transparent",_=>"blur"};

    private void BuildResizeTemplates()
    {
        if(tool!="resize")return;
        // Templates go first: choosing one fills Width and Height below.
        var index=options.Children.IndexOf(options.Children.OfType<TextBlock>().First(t=>t.Text==L10n.Text("Width")));
        UIElement[] controls=[L10n.Label(new TextBlock{Text=TemplateLabel,FontWeight=FontWeights.SemiBold,Margin=new(4,0,4,0)}),templateChoice];
        foreach(var control in controls.Reverse())options.Children.Insert(Math.Max(0,index),control);
        options.Children.Add(L10n.Label(new TextBlock{Text=ShapeLabel,Margin=new(4,12,4,0)}));options.Children.Add(shapeChoice);
        options.Children.Add(L10n.Label(new TextBlock{Text=MarginsLabel,Margin=new(4,12,4,0)}));options.Children.Add(marginChoice);
        templateChoice.SelectionChanged+=(_,_)=>
        {
            if(templateChoice.SelectedItem is not TemplateChoice{Template:{} chosen})return;
            fields["Width"].Text=Number(chosen.Width);fields["Height"].Text=Number(chosen.Height);
            // A template rarely has the photo's shape; keep the whole photo unless another way was chosen.
            if(FitSetting==FitMode.Stretch)shapeChoice.SelectedItem=FitInside;
            RefreshResize();
        };
        shapeChoice.SelectionChanged+=(_,_)=>RefreshResize();marginChoice.SelectionChanged+=(_,_)=>RefreshResize();
        fields["Width"].TextChanged+=(_,_)=>{if(templateChoice.SelectedItem is TemplateChoice{Template:{} t}&&fields["Width"].Text!=Number(t.Width))templateChoice.SelectedIndex=0;RefreshResize();};
        fields["Height"].TextChanged+=(_,_)=>{if(templateChoice.SelectedItem is TemplateChoice{Template:{} t}&&fields["Height"].Text!=Number(t.Height))templateChoice.SelectedIndex=0;RefreshResize();};
        RefreshResize();
    }

    private void RefreshResize()
    {
        if(bitmap is null||ResizedImage is not {} image)return;
        marginChoice.IsEnabled=FitSetting==FitMode.Fit;
        var version=++resizeVersion;
        int width,height;try{width=(int)Value("Width");height=(int)Value("Height");}catch(ArgumentException){return;}
        if(width<1||height<1||width>30000||height>30000){return;}
        if(FitSetting==FitMode.Stretch&&width==bitmap.PixelWidth&&height==bitmap.PixelHeight){ShowResizePreview(image,bitmap,bitmap.PixelWidth,bitmap.PixelHeight);return;}
        // The preview keeps the target's shape at no more than 900 px on its longer side.
        var scale=Math.Min(1,900d/Math.Max(width,height));var previewWidth=Math.Max(1,(int)Math.Round(width*scale));var previewHeight=Math.Max(1,(int)Math.Round(height*scale));
        var source=bitmap.IsFrozen?bitmap:bitmap.Clone();if(!source.IsFrozen)source.Freeze();var mode=FitSetting;var matte=MatteSetting;
        // RenderTargetBitmap needs an STA thread with a dispatcher, so the preview renders on the UI thread
        // after the current input settles, and only the latest request is drawn.
        resizePreview=Dispatcher.InvokeAsync(()=>
        {
            if(version!=resizeVersion)return;
            ShowResizePreview(image,ImageEngine.Fitted(source,previewWidth,previewHeight,mode,matte),previewWidth,previewHeight);
        },System.Windows.Threading.DispatcherPriority.Background).Task;
    }
    private void ShowResizePreview(Image image,BitmapSource result,int width,int height)
    {
        image.Source=result;image.Width=width;image.Height=height;drawing.Width=width;drawing.Height=height;
        drawing.Background=FitSetting==FitMode.Fit&&MatteSetting=="transparent"?TransparencyGrid():System.Windows.Media.Brushes.Transparent;
    }
    internal Image? PreviewImageForTest=>ResizedImage;
    private Image? ResizedImage=>drawing.Children.OfType<Image>().FirstOrDefault();
    private static System.Windows.Media.Brush TransparencyGrid()
    {
        var group=new System.Windows.Media.DrawingGroup();
        group.Children.Add(new System.Windows.Media.GeometryDrawing(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(236,236,236)),null,new System.Windows.Media.RectangleGeometry(new Rect(0,0,16,16))));
        group.Children.Add(new System.Windows.Media.GeometryDrawing(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(204,204,204)),null,new System.Windows.Media.GeometryGroup{Children={new System.Windows.Media.RectangleGeometry(new Rect(0,0,8,8)),new System.Windows.Media.RectangleGeometry(new Rect(8,8,8,8))}}));
        var brush=new System.Windows.Media.DrawingBrush(group){TileMode=System.Windows.Media.TileMode.Tile,Viewport=new Rect(0,0,16,16),ViewportUnits=System.Windows.Media.BrushMappingMode.Absolute};brush.Freeze();return brush;
    }
}
