using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;
/// <summary>
/// Scan QR codes on screen: decoding several, inverted and small codes with their areas, and
/// (only in the isolated smoke desktop) a real capture of a fixture window, with Nashi's own
/// windows kept out of the capture and shown again afterwards. No capture is written to disk.
/// </summary>
internal static class ScreenQrChecks
{
    internal static void Run(Action<bool,string> check)
    {
        Decoding(check);
        if(App.IsSmokeTest)Capture(check);
    }

    private static int[] Qr(string text,int size,out int width)
    {
        var data=new ZXing.BarcodeWriterPixelData{Format=ZXing.BarcodeFormat.QR_CODE,Options=new ZXing.Common.EncodingOptions{Width=size,Height=size,Margin=2}}.Write(text);
        width=data.Width;var pixels=new int[data.Width*data.Height];Buffer.BlockCopy(data.Pixels,0,pixels,0,data.Pixels.Length);return pixels;
    }
    private static void Paint(byte[] canvas,int canvasWidth,string text,int size,int x,int y,bool invert=false)
    {
        var code=Qr(text,size,out var width);
        for(var row=0;row<width;row++)for(var column=0;column<width;column++)
        {
            var dark=(code[row*width+column]&0xFF)<128;if(invert)dark=!dark;var value=(byte)(dark?20:235);var i=((y+row)*canvasWidth+x+column)*4;
            canvas[i]=canvas[i+1]=canvas[i+2]=value;canvas[i+3]=255;
        }
    }
    private static byte[] Blank(int width,int height,byte shade=255){var canvas=new byte[width*height*4];for(var i=0;i<canvas.Length;i+=4){canvas[i]=canvas[i+1]=canvas[i+2]=shade;canvas[i+3]=255;}return canvas;}

    private static void Decoding(Action<bool,string> check)
    {
        const int width=1600,height=900;var canvas=Blank(width,height,120);
        Paint(canvas,width,"https://nashi.example/first",220,900,80);
        // Dark themes show light-on-dark codes.
        Paint(canvas,width,"Dark theme · Türkçe",180,120,560,invert:true);
        Paint(canvas,width,"https://nashi.example/first",160,1300,600);
        // A small code, as on a high-resolution display or in a video call.
        Paint(canvas,width,"WIFI:T:WPA;S:Nashi;P:pear;;",64,500,400);
        var found=ScreenQr.Decode(canvas,width,height);
        check(found.Length==3,"Every distinct on-screen code must be read once: "+string.Join(" | ",found.Select(f=>f.Text)));
        check(found.Select(f=>f.Text).SequenceEqual(["https://nashi.example/first","WIFI:T:WPA;S:Nashi;P:pear;;","Dark theme · Türkçe"]),"On-screen codes must be listed top to bottom: "+string.Join(" | ",found.Select(f=>f.Text)));
        bool Covers(Int32Rect area,int x,int y,int size)=>area.X<=x+size*.2&&area.Y<=y+size*.2&&area.X+area.Width>=x+size*.8&&area.Y+area.Height>=y+size*.8&&area.Width<=size*2;
        check(Covers(found[0].Area,900,80,220)&&Covers(found[1].Area,500,400,64)&&Covers(found[2].Area,120,560,180),"Each preview must show its own code: "+string.Join(" | ",found.Select(f=>f.Area)));
        check(found.All(f=>f.Area.X>=0&&f.Area.Y>=0&&f.Area.X+f.Area.Width<=width&&f.Area.Y+f.Area.Height<=height&&f.Area.Width>0&&f.Area.Height>0),"Preview areas must stay inside the screen");
        var edge=Blank(400,300);Paint(edge,400,"edge",120,280,180);var atEdge=ScreenQr.Decode(edge,400,300);
        check(atEdge.Length==1&&atEdge[0].Area.X+atEdge[0].Area.Width<=400&&atEdge[0].Area.Y+atEdge[0].Area.Height<=300,"A code at the screen's corner must be read with its preview clamped to the screen");
        check(ScreenQr.Decode(Blank(800,600),800,600).Length==0,"A screen without codes must report none");
        var preview=ScreenQr.Preview(new(canvas,width,height,0,0),found[0].Area);
        check(preview.PixelWidth==found[0].Area.Width&&preview.PixelHeight==found[0].Area.Height&&preview.IsFrozen,"The result preview must be the code's own area");
        check(ScreenQr.Area(null,1,640,480)==new Int32Rect(0,0,640,480),"A code without corner points must preview the whole screen");
    }

    private static void Capture(Action<bool,string> check)
    {
        // A borderless Nashi window holding a code, first left visible, then hidden by the scan.
        var code=Qr("https://nashi.example/screen-fixture",320,out var size);var bgra=new byte[size*size*4];
        for(var i=0;i<code.Length;i++){var value=(byte)((code[i]&0xFF)<128?0:255);bgra[i*4]=bgra[i*4+1]=bgra[i*4+2]=value;bgra[i*4+3]=255;}
        var bitmap=BitmapSource.Create(size,size,96,96,PixelFormats.Bgra32,null,bgra,size*4);
        var fixture=new Window{WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Topmost=true,ShowInTaskbar=false,Left=60,Top=60,SizeToContent=SizeToContent.WidthAndHeight,Background=Brushes.White,Content=new Image{Source=bitmap,Width=size,Height=size,Stretch=Stretch.None,Margin=new(24)}};
        RenderOptions.SetBitmapScalingMode(fixture,BitmapScalingMode.NearestNeighbor);
        fixture.Show();DesktopInput.Pump(500);
        try
        {
            var screen=ScreenQr.Capture();
            check(screen.Width>0&&screen.Height>0&&screen.Pixels.Length==screen.Width*screen.Height*4,"The screen capture has no pixels");
            check(ScreenQr.Decode(screen.Pixels,screen.Width,screen.Height).Any(f=>f.Text=="https://nashi.example/screen-fixture"),"A code shown on screen must be read from a real capture");
            var scan=MainWindow.CaptureWithoutNashiAsync();var until=DateTime.UtcNow.AddSeconds(15);while(!scan.IsCompleted&&DateTime.UtcNow<until)DesktopInput.Pump(30);
            check(scan.IsCompletedSuccessfully,"Scanning the screen did not finish");
            var hidden=scan.Result;
            check(!ScreenQr.Decode(hidden.Pixels,hidden.Width,hidden.Height).Any(f=>f.Text=="https://nashi.example/screen-fixture"),"Nashi's own windows must be kept out of the scan");
            check(fixture.IsVisible,"Nashi's windows must come back after the scan");
        }
        finally{fixture.Close();}
    }
}
