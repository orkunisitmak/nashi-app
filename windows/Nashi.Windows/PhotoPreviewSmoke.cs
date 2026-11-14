using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;

// A small, isolated real-WPF regression; does not depend on audio device or global keyboard focus.
internal static class PhotoPreviewSmoke
{
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);yield return child;
            foreach(var nested in Children(child))yield return nested;
        }
    }
    internal static void Run(string root,Action<bool,string> check)
    {
        Directory.CreateDirectory(root);
        var pixels=BitmapSource.Create(100,60,96,96,PixelFormats.Bgra32,null,new byte[100*60*4],400);pixels.Freeze();
        var path=Path.Combine(root,"PhotoPreviewSmoke.png");ImageEngine.Save(pixels,path,"png");
        var owner=new Window{Width=600,Height=400};owner.Show();
        var photo=new ToolEditor(owner,"editImage","Edit Photo",path,preview:pixels);
        try
        {
            photo.Show();DesktopInput.Pump(200);
            using var started=new ManualResetEventSlim(false);
            using var release=new ManualResetEventSlim(false);
            var uiThread=Thread.CurrentThread.ManagedThreadId;
            var workerIsSta=false;
            var image=(Image)Children(photo).OfType<Canvas>().First(c=>c.Children.Count>0&&c.Children[0] is Image).Children[0];
            // Block the ACTUAL photo-render task and verify that the WPF window stays responsive.
            photo.PhotoWorkerGateForTest=()=>
            {
                workerIsSta=Thread.CurrentThread.GetApartmentState()==ApartmentState.STA
                    && Thread.CurrentThread.ManagedThreadId!=uiThread;
                started.Set();
                if(!release.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException("Photo preview fixture worker did not release.");
            };
            try
            {
                photo.FieldForTest("Degrees").Text="45";
                var wait=Stopwatch.StartNew();
                while(!started.IsSet&&wait.Elapsed<TimeSpan.FromSeconds(5))DesktopInput.Pump(80);
                check(started.IsSet,"The actual 45-degree photo render did not reach the STA worker");
                var responsive=false;
                owner.Dispatcher.BeginInvoke(new Action(()=>responsive=true));
                DesktopInput.Pump(150);
                check(workerIsSta&&responsive&&image.Source is not BitmapSource{PixelWidth:114,PixelHeight:114},
                    "Real photo render blocked the UI dispatcher or ran outside the background STA");
            }
            finally{release.Set();photo.PhotoWorkerGateForTest=null;}
            var previewWait=Stopwatch.StartNew();
            while((image.Source is not BitmapSource{PixelWidth:114,PixelHeight:114})&&previewWait.Elapsed<TimeSpan.FromSeconds(15))
                DesktopInput.Pump(100);
            check(image.Source is BitmapSource{PixelWidth:114,PixelHeight:114},"45-degree photo preview never rendered correct dimensions");
            photo.AcceptForTest();var output=ImageEngine.Load(ImageEngine.Apply("editImage",[path],root,photo.ImageOptions)[0]);
            check(output.PixelWidth==114&&output.PixelHeight==114,"45-degree exported photo differs from preview");
            photo.FieldForTest("Degrees").Text="0";previewWait.Restart();
            while((image.Source is not BitmapSource{PixelWidth:100,PixelHeight:60})&&previewWait.Elapsed<TimeSpan.FromSeconds(15))
                DesktopInput.Pump(100);
            check(image.Source is BitmapSource{PixelWidth:100,PixelHeight:60},"Editing photo settings did not replace the old preview");
        }
        finally{photo.Close();owner.Close();}
    }
}
