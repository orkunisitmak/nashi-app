using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;
/// <summary>
/// Reads every QR code on the user's screens in one step: a code in a video call, a slide, a
/// web page or a PDF needs no screenshot file first. The capture only happens when the user
/// asks for it, stays in memory, is never saved or sent, and Nashi's own windows are hidden
/// first. Light-on-dark codes from dark themes are read too.
/// </summary>
internal static class ScreenQr
{
    internal sealed record Found(string Text,Int32Rect Area);
    internal sealed record Screen(byte[] Pixels,int Width,int Height,int Left,int Top);
    [DllImport("user32.dll")]private static extern int GetSystemMetrics(int metric);
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32.dll")]private static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
    [DllImport("gdi32.dll")]private static extern bool BitBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sx,int sy,uint operation);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(IntPtr dc);
    internal const string CaptureFailed="Nashi could not capture the screen.";
    // Larger captures (beyond eight 4K displays) are refused instead of exhausting memory.
    private const long MaximumPixels=8L*3840*2160;

    /// <summary>All displays in physical pixels; the app is per-monitor DPI aware.</summary>
    internal static Screen Capture()
    {
        int left=GetSystemMetrics(76),top=GetSystemMetrics(77),width=GetSystemMetrics(78),height=GetSystemMetrics(79);
        if(width<=0||height<=0||(long)width*height>MaximumPixels)throw new InvalidOperationException(CaptureFailed);
        var screen=GetDC(IntPtr.Zero);if(screen==IntPtr.Zero)throw new InvalidOperationException(CaptureFailed);
        var memory=CreateCompatibleDC(screen);var bitmap=CreateCompatibleBitmap(screen,width,height);var original=SelectObject(memory,bitmap);
        try
        {
            // SRCCOPY | CAPTUREBLT, so layered windows such as video players are included.
            if(!BitBlt(memory,0,0,width,height,screen,left,top,0x40cc0020))throw new InvalidOperationException(CaptureFailed);
            SelectObject(memory,original);original=IntPtr.Zero;
            var source=System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
            var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);var pixels=new byte[checked(width*height*4)];converted.CopyPixels(pixels,width*4,0);
            return new(pixels,width,height,left,top);
        }
        finally{if(original!=IntPtr.Zero)SelectObject(memory,original);DeleteObject(bitmap);DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen);}
    }

    /// <summary>Every distinct QR code in a BGRA image, with the area it covers, top to bottom.</summary>
    internal static Found[] Decode(byte[] bgra,int width,int height)
    {
        var found=new List<Found>();
        void Read(byte[] pixels,int w,int h,int scale)
        {
            var reader=new ZXing.BarcodeReaderGeneric{AutoRotate=true,Options=new ZXing.Common.DecodingOptions{TryHarder=true,PossibleFormats=[ZXing.BarcodeFormat.QR_CODE]}};
            foreach(var result in reader.DecodeMultiple(pixels,w,h,ZXing.RGBLuminanceSource.BitmapFormat.BGRA32)??[])
            {
                if(string.IsNullOrEmpty(result.Text)||found.Any(f=>f.Text==result.Text))continue;
                found.Add(new(result.Text,Area(result.ResultPoints,scale,width,height)));
            }
        }
        // ZXing tries inverted images only when nothing else was found, so light-on-dark codes from
        // dark themes get a pass of their own beside ordinary ones.
        var inverted=Invert(bgra);
        Read(bgra,width,height,1);Read(inverted,width,height,1);
        // Codes a few dozen pixels wide, such as on a high-resolution display, read better enlarged.
        if(found.Count==0&&(long)width*height<=2560L*1440){Read(Enlarge(bgra,width,height),width*2,height*2,2);Read(Enlarge(inverted,width,height),width*2,height*2,2);}
        return found.OrderBy(f=>f.Area.Y).ThenBy(f=>f.Area.X).ToArray();
    }

    /// <summary>The square around a code's finder patterns, with a quiet-zone margin, inside the image.</summary>
    internal static Int32Rect Area(ZXing.ResultPoint[]? points,int scale,int width,int height)
    {
        if(points is not {Length:>0})return new(0,0,width,height);
        var xs=points.Select(p=>p.X/scale).ToArray();var ys=points.Select(p=>p.Y/scale).ToArray();
        // Finder pattern centres sit 3.5 modules inside the code, a quarter of their span for the
        // smallest codes; 40% also keeps some of the quiet zone around it.
        var pad=Math.Max(8,Math.Max(xs.Max()-xs.Min(),ys.Max()-ys.Min())*.4);
        var x=(int)Math.Clamp(xs.Min()-pad,0,width-1);var y=(int)Math.Clamp(ys.Min()-pad,0,height-1);
        var right=(int)Math.Clamp(xs.Max()+pad,x+1,width);var bottom=(int)Math.Clamp(ys.Max()+pad,y+1,height);
        return new(x,y,right-x,bottom-y);
    }

    internal static BitmapSource Preview(Screen screen,Int32Rect area)
    {
        var image=BitmapSource.Create(screen.Width,screen.Height,96,96,PixelFormats.Bgra32,null,screen.Pixels,screen.Width*4);
        var crop=new CroppedBitmap(image,area);crop.Freeze();return crop;
    }

    private static byte[] Invert(byte[] bgra){var output=new byte[bgra.Length];for(var i=0;i<bgra.Length;i+=4){output[i]=(byte)(255-bgra[i]);output[i+1]=(byte)(255-bgra[i+1]);output[i+2]=(byte)(255-bgra[i+2]);output[i+3]=bgra[i+3];}return output;}
    private static byte[] Enlarge(byte[] bgra,int width,int height)
    {
        var output=new byte[checked(width*height*16)];var stride=width*8;
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {
            var source=(y*width+x)*4;
            for(var dy=0;dy<2;dy++)for(var dx=0;dx<2;dx++)Buffer.BlockCopy(bgra,source,output,(y*2+dy)*stride+(x*2+dx)*4,4);
        }
        return output;
    }
}
