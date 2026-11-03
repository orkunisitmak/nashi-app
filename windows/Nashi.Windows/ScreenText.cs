using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using global::Windows.Graphics.Imaging;
using global::Windows.Media.Ocr;
using global::Windows.Storage.Streams;
namespace Nashi.Windows;
/// <summary>
/// Copy text from screen: the user drags over text on the display under the cursor and Windows'
/// built-in OCR reads it. Like Scan QR codes on screen, the capture only happens when asked, stays
/// in memory and is never saved or sent; OCR runs on the device in the user's Windows languages.
/// </summary>
internal static class ScreenText
{
    internal const string NoOcrLanguage="Install an OCR language in Windows Language settings to detect text.";
    internal const string NoText="No text found in the selected area. Select a larger area or zoom in first.";
    [StructLayout(LayoutKind.Sequential)]private struct Point32{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct Rect32{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public Rect32 Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point32 point);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromPoint(Point32 point,uint flags);
    [DllImport("user32.dll")]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);

    /// <summary>The display under the cursor, in physical virtual-screen pixels.</summary>
    internal static Int32Rect MonitorUnderCursor()
    {
        GetCursorPos(out var point);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromPoint(point,2),ref info))throw new InvalidOperationException(ScreenQr.CaptureFailed);
        return new(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right-info.Monitor.Left,info.Monitor.Bottom-info.Monitor.Top);
    }

    /// <summary>Part of a capture as an image; <paramref name="area"/> is in virtual-screen pixels.</summary>
    internal static BitmapSource Crop(ScreenQr.Screen screen,Int32Rect area)
    {
        var x=Math.Clamp(area.X-screen.Left,0,screen.Width-1);var y=Math.Clamp(area.Y-screen.Top,0,screen.Height-1);
        var width=Math.Clamp(area.Width,1,screen.Width-x);var height=Math.Clamp(area.Height,1,screen.Height-y);
        var image=BitmapSource.Create(screen.Width,screen.Height,96,96,PixelFormats.Bgra32,null,screen.Pixels,screen.Width*4);
        var crop=new CroppedBitmap(image,new Int32Rect(x,y,width,height));crop.Freeze();return crop;
    }

    /// <summary>A selection on a surface of <paramref name="surface"/> DIPs mapped onto an image of width×height pixels.</summary>
    internal static Int32Rect ToPixels(Rect selection,Size surface,int width,int height)
    {
        if(surface.Width<=0||surface.Height<=0||selection.IsEmpty)return Int32Rect.Empty;
        var sx=width/surface.Width;var sy=height/surface.Height;
        var left=(int)Math.Floor(Math.Clamp(selection.Left*sx,0,width));var top=(int)Math.Floor(Math.Clamp(selection.Top*sy,0,height));
        var right=(int)Math.Ceiling(Math.Clamp(selection.Right*sx,0,width));var bottom=(int)Math.Ceiling(Math.Clamp(selection.Bottom*sy,0,height));
        return right-left<1||bottom-top<1?Int32Rect.Empty:new(left,top,right-left,bottom-top);
    }

    /// <summary>The text in an image, one line per OCR line; empty when there is none.</summary>
    internal static async Task<string> RecognizeAsync(BitmapSource image,CancellationToken token=default)
    {
        var engine=OcrEngine.TryCreateFromUserProfileLanguages()??OcrEngine.TryCreateFromLanguage(new global::Windows.Globalization.Language("en-US"))??throw new InvalidOperationException(NoOcrLanguage);
        // Screen text is small; OCR reads it better enlarged, within the engine's size limit.
        var longest=Math.Max(image.PixelWidth,image.PixelHeight);var scale=longest<1200?Math.Min(3,1200d/longest):1;
        scale=Math.Min(scale,OcrEngine.MaxImageDimension/(double)longest);
        if(Math.Abs(scale-1)>.01){var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);var w=(int)Math.Round(image.PixelWidth*scale);var h=(int)Math.Round(image.PixelHeight*scale);using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,w,h));dc.DrawImage(image,new Rect(0,0,w,h));}var target=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);target.Render(visual);target.Freeze();image=target;}
        using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(ImageEngine.PngBytes(image));await writer.StoreAsync();}
        stream.Seek(0);token.ThrowIfCancellationRequested();
        var decoder=await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied);
        var result=await engine.RecognizeAsync(bitmap);token.ThrowIfCancellationRequested();
        return string.Join(Environment.NewLine,result.Lines.Select(l=>l.Text.Trim()).Where(l=>l.Length>0));
    }
}
