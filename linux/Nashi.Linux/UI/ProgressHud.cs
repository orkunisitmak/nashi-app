using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Nashi.Core;
namespace Nashi.Linux.UI;

/// A small always-on-top progress card for work started while the Nashi window is hidden
/// (from a gesture or the tray): label, progress and Cancel in the bottom-right corner of the
/// screen. A desktop notification takes over when the work ends.
public sealed class ProgressHud:Window
{
    private readonly Toast toast;
    public ProgressHud(Window owner,string title,Action cancel)
    {
        Title="Nashi";WindowDecorations=WindowDecorations.None;Topmost=true;ShowInTaskbar=false;CanResize=false;SizeToContent=SizeToContent.WidthAndHeight;
        TransparencyLevelHint=[WindowTransparencyLevel.Transparent];Background=Brushes.Transparent;
        toast=new Toast(title,cancel){Margin=new(12)};Content=toast;
        Opened+=(_,_)=>
        {
            var screen=owner.Screens.Primary??owner.Screens.All.FirstOrDefault();if(screen is null)return;
            var area=screen.WorkingArea;var scale=screen.Scaling;
            Position=new PixelPoint(area.Right-(int)(Bounds.Width*scale)-16,area.Bottom-(int)(Bounds.Height*scale)-16);
        };
    }
    public void Update(OperationProgress progress)=>toast.Update(progress);
}
