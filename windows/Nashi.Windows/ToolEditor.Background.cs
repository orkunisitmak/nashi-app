using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;
/// <summary>Convert › Remove background: a transparent PNG, WebP, TIFF or AVIF from an image on a plain colour, with a live preview.</summary>
public sealed partial class ToolEditor
{
    internal const string RemoveBackgroundLabel="Remove background";
    internal const string BackgroundTolerance="Background tolerance (%)";
    internal const string EdgeSoftness="Edge softness (px)";
    internal const string BackgroundHint="For product shots, logos and scans on a plain color. Only the background touching the image edges is removed; this is not AI subject detection.";
    internal const string BackgroundNeedsTransparency="Choose PNG, WebP, TIFF or AVIF to keep a transparent background.";
    private CheckBox? removeBackground;private readonly TextBlock backgroundNote=new(){TextWrapping=TextWrapping.Wrap,Margin=new(4,6,4,0)};
    private int backgroundVersion;private Task backgroundPreview=Task.CompletedTask;
    internal CheckBox? RemoveBackgroundForTest=>removeBackground;
    internal BitmapSource? PreviewForTest=>PreviewImage?.Source as BitmapSource;
    internal Task BackgroundPreviewForTest=>backgroundPreview;
    private Image? PreviewImage=>drawing.Children.OfType<Image>().FirstOrDefault();
    private bool TransparentFormat=>format.SelectedItem is string chosen&&ImageEngine.TransparentFormats.Contains(chosen);

    /// <summary>The removal settings for this export, or null when it is off or the format has no transparency.</summary>
    internal BackgroundRemoval.Options? BackgroundSettings=>removeBackground?.IsChecked==true&&TransparentFormat?new BackgroundRemoval.Options((int)Math.Round(Value(BackgroundTolerance,12)),(int)Math.Round(Value(EdgeSoftness,1))).Clamped:null;

    private void BuildBackgroundOptions()
    {
        if(tool!="convert"||bitmap is null||media is not null)return;
        removeBackground=L10n.Content(new CheckBox{Content=RemoveBackgroundLabel,Margin=new(4,18,4,0),FontWeight=FontWeights.SemiBold});
        System.Windows.Automation.AutomationProperties.SetAutomationId(removeBackground,RemoveBackgroundLabel);
        options.Children.Add(removeBackground);Field(BackgroundTolerance,12);Field(EdgeSoftness,1);options.Children.Add(backgroundNote);
        removeBackground.Checked+=(_,_)=>RefreshBackground();removeBackground.Unchecked+=(_,_)=>RefreshBackground();
        format.SelectionChanged+=(_,_)=>RefreshBackground();fields[BackgroundTolerance].TextChanged+=(_,_)=>RefreshBackground();fields[EdgeSoftness].TextChanged+=(_,_)=>RefreshBackground();
        RefreshBackground();
    }

    private void RefreshBackground()
    {
        if(removeBackground is null||bitmap is null)return;
        var allowed=TransparentFormat;var on=removeBackground.IsChecked==true&&allowed;
        removeBackground.IsEnabled=allowed;fields[BackgroundTolerance].IsEnabled=fields[EdgeSoftness].IsEnabled=on;
        L10n.SetText(backgroundNote,allowed?BackgroundHint:BackgroundNeedsTransparency);backgroundNote.Foreground=Theme.Brush("Muted");
        // Transparent areas show as a checkerboard while the preview has no background.
        drawing.Background=on?Checkerboard():Brushes.Transparent;
        var version=++backgroundVersion;var image=PreviewImage;if(image is null)return;
        BackgroundRemoval.Options? settings;try{settings=on?BackgroundSettings:null;}catch(ArgumentException){return;}
        if(settings is null){image.Source=bitmap;return;}
        var source=bitmap.IsFrozen?bitmap:bitmap.Clone();if(!source.IsFrozen)source.Freeze();
        // Large photos are processed off the UI thread; only the latest settings reach the preview.
        backgroundPreview=Task.Run(()=>ImageEngine.WithoutBackground(source,settings)).ContinueWith(done=>
        {
            if(version!=backgroundVersion)return;
            if(done.IsCompletedSuccessfully){image.Source=done.Result;return;}
            image.Source=bitmap;
            if(done.Exception?.InnerException is InvalidDataException error){L10n.SetText(backgroundNote,error.Message);backgroundNote.Foreground=Theme.Brush("Ink");}
        },TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static Brush Checkerboard()
    {
        var light=new SolidColorBrush(Color.FromRgb(236,236,236));var dark=new SolidColorBrush(Color.FromRgb(204,204,204));
        var group=new DrawingGroup();group.Children.Add(new GeometryDrawing(light,null,new RectangleGeometry(new Rect(0,0,16,16))));
        group.Children.Add(new GeometryDrawing(dark,null,new GeometryGroup{Children={new RectangleGeometry(new Rect(0,0,8,8)),new RectangleGeometry(new Rect(8,8,8,8))}}));
        var brush=new DrawingBrush(group){TileMode=TileMode.Tile,Viewport=new Rect(0,0,16,16),ViewportUnits=BrushMappingMode.Absolute};brush.Freeze();return brush;
    }
}
