using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Nashi.Windows;

public sealed partial class MainWindow
{
    private ScreenshotWindow? screenshotWindow;private bool choosingScreenArea;
    private void OpenScreenshot(bool selectImmediately=false)
    {
        if(busy||choosingScreenArea)return;if(screenshotWindow is {} existing){ShowMain();existing.Show();Raise(existing);if(selectImmediately)_=existing.SelectAreaAsync();return;}if(OpenModal() is {} modal){Raise(modal);return;}
        ShowMain();var window=new ScreenshotWindow(this,outputs=>Record("Screenshot",outputs));screenshotWindow=window;window.CaptureArea=SelectScreenAreaAsync;
        if(selectImmediately){RoutedEventHandler? start=null;start=async(_,_)=>{window.Loaded-=start;await window.SelectAreaAsync();};window.Loaded+=start;}
        window.Closed+=(_,_)=>{if(screenshotWindow==window)screenshotWindow=null;};window.Show();
    }
    private async Task<BitmapSource?> SelectScreenAreaAsync()
    {
        choosingScreenArea=true;var visible=Application.Current.Windows.OfType<Window>().Where(window=>window.IsVisible).ToArray();foreach(var window in visible)window.Hide();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);await Task.Delay(160);
            var selector=new ScreenRegionWindow(ScreenCapture.Capture());return selector.ShowDialog()==true?selector.SelectedImage:null;
        }
        finally{choosingScreenArea=false;foreach(var window in visible)window.Show();screenshotWindow?.Activate();}
    }
}
