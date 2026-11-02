using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;
public sealed partial class MainWindow
{
    internal const string ScreenQrTitle="Scan QR codes on screen";
    internal const string NoScreenQr="No QR codes on your screens. Bring the whole code into view, larger and sharper.";
    internal const string CloseDialogFirst="Close Nashi's open window first.";
    /// <summary>Reads every QR code on the screens. Nothing is captured until the user asks, and the capture is never saved.</summary>
    internal async Task ScanScreenQrAsync()
    {
        if(busy)return;
        if(ComponentDispatcher.IsThreadModal){L10n.SetText(status,CloseDialogFirst);return;}
        ScreenQr.Screen screen;
        try{screen=await CaptureWithoutNashiAsync();}
        catch(InvalidOperationException error){MessageBox.Show(L10n.Text(error.Message),"Nashi");return;}
        SetBusy(true);ScreenQr.Found[] found;
        try{found=await Task.Run(()=>ScreenQr.Decode(screen.Pixels,screen.Width,screen.Height));}
        finally{SetBusy(false);}
        ShowQrResults(found.Select(f=>("On screen",f.Text,(BitmapSource?)ScreenQr.Preview(screen,f.Area))).ToList(),NoScreenQr);
    }
    /// <summary>Nashi's own windows must not be read, so they step aside for the capture and come back right after.</summary>
    internal static async Task<ScreenQr.Screen> CaptureWithoutNashiAsync()
    {
        var hidden=Application.Current.Windows.OfType<Window>().Where(w=>w.IsVisible).ToArray();
        try
        {
            foreach(var window in hidden)window.Hide();
            // Give the desktop compositor a moment to repaint what the windows covered.
            await Task.Delay(300);
            return ScreenQr.Capture();
        }
        finally{foreach(var window in hidden)window.Show();}
    }
}
