using System.Windows;
using System.Windows.Controls;
namespace Nashi.Windows;
public sealed partial class ToolEditor
{
    private readonly ListBox inputOrder=new(){MaxHeight=340};
    internal string[] OrderedInputs{get;private set;}=[];
    private void BuildInputOrder(IReadOnlyList<string> inputs)
    {
        OrderedInputs=inputs.ToArray();if(tool is not ("joinVideos" or "mergePDFs" or "createPDF"))return;
        options.Children.Add(L10n.Label(new TextBlock{Text="File order",Margin=new(4,12,4,4)}));foreach(var path in inputs)inputOrder.Items.Add(new ListBoxItem{Content=tool=="createPDF"?OrderRow(path):Path.GetFileName(path),Tag=path});options.Children.Add(inputOrder);
        void Move(int delta){var i=inputOrder.SelectedIndex;var next=i+delta;if(i<0||next<0||next>=inputOrder.Items.Count)return;var item=inputOrder.Items[i];inputOrder.Items.RemoveAt(i);inputOrder.Items.Insert(next,item);inputOrder.SelectedIndex=next;OrderedInputs=inputOrder.Items.Cast<ListBoxItem>().Select(x=>(string)x.Tag).ToArray();}
        var buttons=new WrapPanel();buttons.Children.Add(MainWindow.Button("Move up",()=>Move(-1)));buttons.Children.Add(MainWindow.Button("Move down",()=>Move(1)));options.Children.Add(buttons);
        if(tool=="createPDF")
        {
            // The big preview follows the list, so the pages can be checked one by one in the order they will have.
            inputOrder.SelectionChanged+=(_,_)=>ShowOrderedPage();
            var step=new WrapPanel{Margin=new(0,6,0,0)};
            step.Children.Add(MainWindow.Button("Previous image",()=>StepPage(-1)));step.Children.Add(MainWindow.Button("Next image",()=>StepPage(1)));
            pageLabel.Margin=new(10,0,0,0);pageLabel.VerticalAlignment=VerticalAlignment.Center;step.Children.Add(pageLabel);options.Children.Add(step);
            inputOrder.SelectedIndex=0;
        }
        if(tool is "createPDF" or "mergePDFs")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="File name",Margin=new(4,12,4,4)}));
            outputName.ToolTip=L10n.Text("Without .pdf. Leave empty for the default name.");options.Children.Add(outputName);
        }
    }
    private readonly TextBox outputName=new();
    private readonly TextBlock pageLabel=new();private int pageRequest;
    private void StepPage(int delta){var next=inputOrder.SelectedIndex+delta;if(next>=0&&next<inputOrder.Items.Count)inputOrder.SelectedIndex=next;}
    // Shows the selected image in the preview and its page number; a newer request makes an older load stale.
    private readonly SemaphoreSlim pageGate=new(1,1);private CancellationTokenSource? pageCancel;
    private async void ShowOrderedPage()
    {
        var index=inputOrder.SelectedIndex;if(index<0)return;
        pageLabel.Text=string.Format(L10n.Text("Page {0} of {1}"),index+1,inputOrder.Items.Count);
        var path=(string)((ListBoxItem)inputOrder.Items[index]).Tag;var request=++pageRequest;
        pageCancel?.Cancel();var cancel=pageCancel=new CancellationTokenSource();
        System.Windows.Media.Imaging.BitmapSource? page=null;
        try
        {
            // Only one image loads at a time; a newer selection skips the ones still waiting.
            await pageGate.WaitAsync(cancel.Token);
            try{if(!cancel.IsCancellationRequested)page=await Task.Run(()=>ImageEngine.Load(path));}finally{pageGate.Release();}
        }
        catch(OperationCanceledException){return;}
        catch(Exception error)when(error is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FileFormatException or OutOfMemoryException){}
        if(request!=pageRequest||drawing.Children.Count==0)return;
        if(page is null)
        {
            // An unreadable image must not leave the previous one under the new page number.
            pageLabel.Text+=" · "+L10n.Text("Preview unavailable");
            var blank=System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[4],4);blank.Freeze();SetPhotoPreview(blank);return;
        }
        SetPhotoPreview(page);
    }
    /// The name typed for the combined PDF, or null for the default.
    internal string? OutputName=>string.IsNullOrWhiteSpace(outputName.Text)?null:outputName.Text;
    // At most two full-size decodes run for thumbnails at once, so a large selection cannot pile up memory.
    private static readonly SemaphoreSlim thumbnailGate=new(2,2);
    private static async Task FillThumbnailAsync(System.Windows.Controls.Image picture,string path)
    {
        try
        {
            await thumbnailGate.WaitAsync();
            System.Windows.Media.Imaging.BitmapSource small;
            try
            {
                small=await Task.Run(()=>
                {
                    var full=ImageEngine.Load(path);var scale=Math.Min(1d,72d/Math.Max(full.PixelWidth,full.PixelHeight));
                    var scaled=new System.Windows.Media.Imaging.TransformedBitmap(full,new System.Windows.Media.ScaleTransform(scale,scale));scaled.Freeze();return scaled;
                });
            }
            finally{thumbnailGate.Release();}
            picture.Source=small;
        }
        catch(Exception error)when(error is NotSupportedException or IOException or InvalidOperationException or UnauthorizedAccessException or FileFormatException or InvalidDataException or OutOfMemoryException){}
    }
    // A small preview next to the name, so every selected image is visible while ordering a PDF.
    private static FrameworkElement OrderRow(string path)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        System.Windows.Media.Imaging.BitmapSource? thumbnail=null;
        try
        {
            var small=new System.Windows.Media.Imaging.BitmapImage();small.BeginInit();small.UriSource=new Uri(path);small.DecodePixelWidth=72;small.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;small.EndInit();small.Freeze();thumbnail=small;
        }
        catch(Exception error)when(error is NotSupportedException or IOException or InvalidOperationException or UnauthorizedAccessException or FileFormatException){}
        var picture=new System.Windows.Controls.Image{Width=36,Height=36,Stretch=System.Windows.Media.Stretch.UniformToFill,Margin=new(0,2,10,2),Source=thumbnail};
        row.Children.Add(picture);
        // WPF cannot decode every format Nashi reads (SVG, for example): the engine's own loader fills the picture in
        // later, off the UI thread, and only a small copy is kept.
        if(thumbnail is null)_=FillThumbnailAsync(picture,path);
        row.Children.Add(new TextBlock{Text=Path.GetFileName(path),VerticalAlignment=VerticalAlignment.Center});return row;
    }
}
