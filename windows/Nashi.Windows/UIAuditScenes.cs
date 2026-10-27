using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Nashi.Windows;

// Read-only scene inspection keeps the audit on production components without exposing OS input.
public sealed partial class MainWindow
{
    internal IReadOnlyList<ToolChoice> UIAuditToolChoices => Choices();
    /// Lets the desk check queue real jobs through the production runner.
    internal Task QueueForCheck(Func<string, IProgress<Nashi.Core.OperationProgress>, CancellationToken, Task<IReadOnlyList<string>>> work, string label) => RunAsync(work, label);
}

public sealed partial class ToolEditor
{
    internal bool UIAuditPreviewReady
    {
        get
        {
            if (bitmap is null || drawing.Children.Count == 0 || drawing.Children[0] is not Image { Source: BitmapSource source }) return bitmap is null;
            if (tool != "frameImage") return true;
            // The initial frame scene queues a real preview on Loaded. Await its final dimensions,
            // not just the original image or a timer interval, before claiming the scene is ready.
            return photoPreview is { IsEnabled: false } && previewGate.CurrentCount == 1 &&
                source.PixelWidth == (int)Value("Canvas width", bitmap.PixelWidth + 160) &&
                source.PixelHeight == (int)Value("Canvas height", bitmap.PixelHeight + 160);
        }
    }
}
