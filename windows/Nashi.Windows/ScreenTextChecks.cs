using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using global::Windows.Media.Ocr;
namespace Nashi.Windows;
/// <summary>Copy text from screen: selection mapping, crops of a capture, OCR of rendered text and the selection window.</summary>
internal static class ScreenTextChecks
{
    internal static void Run(Action<bool,string> check)
    {
        // A 960×540 DIP surface showing a 1920×1080 display (200 % scaling).
        check(ScreenText.ToPixels(new Rect(100,50,200,100),new Size(960,540),1920,1080)==new Int32Rect(200,100,400,200),"A selection must map to display pixels by proportion");
        check(ScreenText.ToPixels(new Rect(new Point(300,150),new Point(100,50)),new Size(960,540),1920,1080)==new Int32Rect(200,100,400,200),"A selection dragged up and left must map the same way");
        check(ScreenText.ToPixels(new Rect(-50,-50,100,100),new Size(960,540),1920,1080)==new Int32Rect(0,0,100,100),"A selection must stay on the display");
        check(ScreenText.ToPixels(new Rect(10,10,0,0),new Size(960,540),1920,1080).IsEmpty&&ScreenText.ToPixels(new Rect(0,0,10,10),new Size(0,0),10,10).IsEmpty,"An empty selection must give no area");
        // A capture of two displays, the left one at negative coordinates.
        var pixels=new byte[400*100*4];for(var i=0;i<pixels.Length;i+=4){var x=i/4%400;pixels[i]=(byte)(x<200?10:200);pixels[i+3]=255;}
        var screen=new ScreenQr.Screen(pixels,400,100,-200,0);var right=ScreenText.Crop(screen,new Int32Rect(0,0,200,100));var one=new byte[4];right.CopyPixels(new Int32Rect(0,0,1,1),one,4,0);
        check(right.PixelWidth==200&&one[0]==200,"A display at negative coordinates must be cropped from the right place");
        var clipped=ScreenText.Crop(screen,new Int32Rect(150,50,500,500));check(clipped.PixelWidth==50&&clipped.PixelHeight==50,"A crop must stay inside the capture");

        // OCR of rendered text, only inside the chosen area.
        if(OcrEngine.TryCreateFromUserProfileLanguages() is null&&OcrEngine.TryCreateFromLanguage(new global::Windows.Globalization.Language("en-US")) is null){check(true,"No OCR language on this system");}
        else
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White,null,new Rect(0,0,1200,400));
                void Write(string text,double x,double y,double size)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,Brushes.Black,1),new Point(x,y));
                Write("Invoice number 2026-0417",40,40,22);Write("Total due 1,250.00 EUR",40,80,22);Write("OUTSIDE AREA",40,300,22);
            }
            var image=new RenderTargetBitmap(1200,400,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();
            var area=new CroppedBitmap(image,new Int32Rect(20,30,600,90));area.Freeze();
            var task=ScreenText.RecognizeAsync(area);var until=DateTime.UtcNow.AddSeconds(20);while(!task.IsCompleted&&DateTime.UtcNow<until)DesktopInput.Pump(30);
            var text=task.IsCompletedSuccessfully?task.Result:"";
            check(text.Contains("Invoice")&&text.Contains("2026-0417")&&text.Contains("Total due")&&text.Contains("EUR"),"OCR must read the selected text: "+text);
            check(text.Contains('\n')&&!text.Contains("OUTSIDE"),"OCR must keep lines and read only the selected area: "+text);
            var empty=new CroppedBitmap(image,new Int32Rect(700,150,300,100));empty.Freeze();var blank=ScreenText.RecognizeAsync(empty);until=DateTime.UtcNow.AddSeconds(20);while(!blank.IsCompleted&&DateTime.UtcNow<until)DesktopInput.Pump(30);
            check(blank.IsCompletedSuccessfully&&blank.Result.Length==0,"An area without text must give no text");
        }

        // The selection window, kept small so a local run does not cover the screen.
        var picture=BitmapSource.Create(400,300,96,96,PixelFormats.Bgra32,null,new byte[400*300*4],1600);
        var selector=new ScreenSelectionWindow(picture,new Int32Rect(40,40,400,300));selector.Show();DesktopInput.Pump(300);
        var size=((FrameworkElement)selector.Content).RenderSize;
        check(!selector.Finish(new Rect(10,10,2,2))&&selector.IsVisible,"A click without dragging must keep the selection open");
        check(selector.Finish(new Rect(size.Width/4,size.Height/4,size.Width/2,size.Height/2))&&selector.Selection is {} s&&Math.Abs(s.X-100)<=1&&Math.Abs(s.Y-75)<=1&&Math.Abs(s.Width-200)<=2&&Math.Abs(s.Height-150)<=2&&selector.ScreenSelection?.X==s.X+40,"The dragged area must map onto the display picture: "+selector.Selection);
        var cancelled=new ScreenSelectionWindow(picture,new Int32Rect(40,40,400,300));cancelled.Show();DesktopInput.Pump(200);cancelled.Close();check(cancelled.Selection is null,"Closing without a selection must cancel");
    }
}
