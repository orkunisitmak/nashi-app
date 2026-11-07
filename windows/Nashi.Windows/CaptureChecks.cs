using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;

internal static class CaptureChecks
{
    [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr window,int message,IntPtr w,IntPtr l);
    internal static void Run(string root)
    {
        var results=new List<object>();var failures=new List<string>();void Check(bool passed,string message){results.Add(new{passed,message});if(!passed)failures.Add(message);}
        var task=RunAsync(root,Check);var until=DateTime.UtcNow.AddSeconds(90);while(!task.IsCompleted&&DateTime.UtcNow<until)DesktopInput.Pump(25);
        if(!task.IsCompleted)throw new TimeoutException("Screen capture checks timed out.");
        try{task.GetAwaiter().GetResult();}catch(Exception error){failures.Add(error.ToString());}
        File.WriteAllText(Path.Combine(root,"capture-checks.json"),JsonSerializer.Serialize(new{checks=results.Count,failures,results}));if(failures.Count>0)throw new Exception(string.Join(Environment.NewLine,failures));
    }
    private static BitmapSource Fixture(bool dark=false)
    {
        var visual=new DrawingVisual();using(var draw=visual.RenderOpen())
        {
            draw.DrawRectangle(dark?Brushes.Black:Brushes.White,null,new Rect(0,0,800,400));
            draw.DrawText(new FormattedText("NASHI SCREEN TEXT 12345",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),34,dark?Brushes.White:Brushes.Black,1),new Point(25,50));
            draw.DrawText(new FormattedText("COPY TEXT FROM AN IMAGE",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),28,dark?Brushes.White:Brushes.Black,1),new Point(25,120));
        }
        var bitmap=new RenderTargetBitmap(800,400,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }
    private static void Capture(Window window,string path){window.UpdateLayout();var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);ImageEngine.Save(image,path,"png");}
    private static async Task RunAsync(string root,Action<bool,string> check)
    {
        var language=L10n.Language;var appearance=Theme.Dark?"dark":"light";var wheelPreferences=WheelPresentation.Preferences;var feedback=Feedback.Enabled;WheelPresentation.Preferences=wheelPreferences with{Animations=false,WheelFinish="solid"};Feedback.Enabled=false;Theme.Apply("dark");var owner=new Window{Title="Nashi isolated capture fixture",Width=400,Height=250,Content=new TextBlock{Text="Synthetic fixture"}};owner.Show();
        try
        {
            var board=new Border{Background=Brushes.Magenta};var boardWindow=new Window{WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Width=240,Height=160,Left=80,Top=80,Topmost=true,Content=board};boardWindow.Show();DesktopInput.Pump(160);
            var boardOrigin=board.PointToScreen(new(10,10));var grabbed=ScreenCapture.Capture(new((int)boardOrigin.X,(int)boardOrigin.Y,100,80));var pixel=new byte[4];new FormatConvertedBitmap(grabbed.Image,PixelFormats.Bgra32,null,0).CopyPixels(new Int32Rect(10,10,1,1),pixel,4,0);
            check(grabbed.Image.PixelWidth==100&&grabbed.Image.PixelHeight==80&&pixel[0]>200&&pixel[1]<40&&pixel[2]>200,"The actual screen capture API captures a synthetic window's pixels");boardWindow.Close();
            check(ScreenCapture.Selection(new(-1900,30),new(-1700,130),new(-1920,0,3840,1080))==new Int32Rect(20,30,200,100),"Negative monitor origins map to image pixels");
            check(ScreenCapture.Selection(new(200,150),new(-10,-10),new(0,0,100,100))==new Int32Rect(0,0,100,100),"Reverse/outside drags clamp to screen bounds");
            var fixture=Fixture();var frame=new ScreenFrame(fixture,new(40,60,800,400));var selector=new ScreenRegionWindow(frame);
            selector.Loaded+=(_,_)=>selector.Dispatcher.BeginInvoke(()=>
            {
                var origin=selector.SelectionSurface.PointToScreen(new(0,0));check(Math.Abs(origin.X-frame.Bounds.X)<2&&Math.Abs(origin.Y-frame.Bounds.Y)<2,"Selection overlay uses physical screen coordinates");
                var first=selector.SelectionSurface.PointFromScreen(new(60,90));var last=selector.SelectionSurface.PointFromScreen(new(260,190));selector.UpdateSelection(first,last);Capture(selector,Path.Combine(root,"screen-region.png"));selector.Select(first,last);
            });check(selector.ShowDialog()==true&&selector.SelectedImage is {PixelWidth:200,PixelHeight:100},"Selected area crops the actual source pixel dimensions");
            var cancelled=new ScreenRegionWindow(frame);cancelled.Loaded+=(_,_)=>cancelled.Dispatcher.BeginInvoke(()=>cancelled.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(cancelled),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent}));check(cancelled.ShowDialog()!=true&&cancelled.SelectedImage is null,"Escape cancels selection without producing a screenshot");
            var preferences=new PreferencesStore(Path.Combine(root,"shortcut-preferences"));preferences.Save(new(){Enabled=false});var count=0;
            using(var controller=new GlobalDragController(owner,preferences,(_,_,_)=>{},()=>{},()=>count++))
            {
                check(controller.ScreenShortcutRegistered,"Ctrl+Alt+S registers independently of drag shortcuts");PostMessage(new WindowInteropHelper(owner).Handle,0x0312,new(7702),IntPtr.Zero);DesktopInput.Pump(60);check(count==1,"The screen shortcut reaches its capture action");owner.IsEnabled=false;PostMessage(new WindowInteropHelper(owner).Handle,0x0312,new(7702),IntPtr.Zero);DesktopInput.Pump(60);check(count==1,"A modal/disabled owner suppresses screen capture");owner.IsEnabled=true;
            }
            var reel="https://www.instagram.com/reel/DZLzjtnRpVN/";
            check(BrowserLinkHint.IsLocationMetadata("Adres ve arama çubuğu","","")&&BrowserLinkHint.IsLocationMetadata("Address and search bar","",""),"The browser location field is recognized in Turkish and English");
            check(!BrowserLinkHint.IsLocationMetadata("Email address","","")&&!BrowserLinkHint.IsLocationMetadata("Home address","",""),"Email and address form values are never mistaken for the browser location");
            check(BrowserLinkHint.AllowsPointerReel("brave",ControlType.Pane,reel),"A browser's reel video area supports pointer-only drag");
            check(!BrowserLinkHint.AllowsPointerReel("brave",ControlType.Text,reel)&&!BrowserLinkHint.AllowsPointerReel("brave",ControlType.Edit,reel)&&!BrowserLinkHint.AllowsPointerReel("brave",ControlType.Pane,reel,true),"Comment text, editable fields and passwords never enable pointer reel gestures");
            check(!BrowserLinkHint.AllowsPointerReel("Nashi",ControlType.Pane,reel)&&!BrowserLinkHint.AllowsPointerReel("brave",ControlType.Pane,"https://www.instagram.com/user/"),"Other applications and profiles never enable pointer reel gestures");
            string? pointerAction=null;string? pointerLink=null;var pointerCalls=0;var pointerWheel=new GlobalDropWheel("convert",1,(data,action)=>{pointerCalls++;pointerAction=action;pointerLink=DragPayload.Parse(data)?.Url;},()=>reel,()=>true);pointerWheel.Show();DesktopInput.Pump(40);
            var petal=pointerWheel.PointToScreen(new(pointerWheel.ActualWidth/2,pointerWheel.ActualHeight*.18));pointerWheel.TrackPointer((int)petal.X,(int)petal.Y,reel);check(pointerWheel.Revealed,"A reel pointer drag reveals download choices without OLE data");Capture(pointerWheel,Path.Combine(root,"instagram-pointer-wheel.png"));
            check(pointerWheel.CompletePointer((int)petal.X,(int)petal.Y)&&pointerAction=="download:video"&&pointerLink==reel&&pointerCalls==1,"Releasing a pointer-only reel on Video dispatches the canonical link once");check(!pointerWheel.CompletePointer((int)petal.X,(int)petal.Y)&&pointerCalls==1,"A completed pointer reel cannot dispatch twice");
            var outsideWheel=new GlobalDropWheel("convert",1,(_,_)=>pointerCalls++,()=>reel,()=>true);outsideWheel.Show();DesktopInput.Pump(40);var top=outsideWheel.PointToScreen(new(outsideWheel.ActualWidth/2,outsideWheel.ActualHeight*.18));outsideWheel.TrackPointer((int)top.X,(int)top.Y,reel);var outside=outsideWheel.PointToScreen(new(-20,-20));check(!outsideWheel.CompletePointer((int)outside.X,(int)outside.Y)&&pointerCalls==1,"Releasing outside the reel wheel cancels rather than using a stale selection");outsideWheel.Close();
            check(BrowserLinkHint.InstagramAddress("instagram.com/reels/DZLzjtnRpVN/")==reel,"Address-bar reel links canonicalize to a downloadable reel");check(BrowserLinkHint.InstagramAddress("https://www.instagram.com/some.user/") is null,"A profile address never guesses a single reel");
            var poster=new DataObject(DataFormats.Html,"<img src=\"https://cdn.example/poster.jpg\" />");check(DragPayload.Parse(poster,reel) is {Kind:"instagramReel",Url:var url}&&url==reel,"An Instagram poster drag resolves to its reel, not an image");
            var explicitLink=new DataObject(DataFormats.UnicodeText,"https://www.instagram.com/reel/OtherVideo/");check(DragPayload.Parse(explicitLink,reel)?.Url.EndsWith("/OtherVideo/",StringComparison.Ordinal)==true,"An explicitly dragged reel wins over the page hint");
            check(DragPayload.Parse(new DataObject(DataFormats.UnicodeText,"https://www.youtube.com/watch?v=jNQXAC9IVRw"),reel)?.Kind=="youtube","Instagram hints preserve explicit YouTube drags");
            check(ImageTextRecognition.Available,"Windows has a usable local OCR language");
            if(ImageTextRecognition.Available)
            {
                var recognized=await ImageTextRecognition.Recognize(fixture,CancellationToken.None);check(recognized.Contains("NASHI",StringComparison.OrdinalIgnoreCase)&&recognized.Contains("12345",StringComparison.Ordinal),"The actual Windows OCR engine reads synthetic screen text");
                var darkText=await ImageTextRecognition.Recognize(Fixture(true),CancellationToken.None);check(darkText.Contains("NASHI",StringComparison.OrdinalIgnoreCase),"Local OCR reads light text on a dark screenshot");
                var pdfText=await PdfDocumentMedia.Recognize(ImageEngine.PngBytes(fixture),CancellationToken.None);check(pdfText.Contains("NASHI",StringComparison.OrdinalIgnoreCase),"PDF OCR keeps using the shared recognizer");
            }
            foreach(var theme in new[]{"light","dark"})foreach(var locale in new[]{"en","tr"})
            {
                Theme.Apply(theme);L10n.Language=locale;var window=new ScreenshotWindow(owner,_=>{}){Width=740,Height=480};window.Show();await window.SetImageAsync(fixture,(_,_)=>Task.FromResult("NASHI SCREEN TEXT 12345\nTürkçe: İzmir 42"));
                check(window.CopyTextButton.IsEnabled&&window.TextField.Text.Contains("İzmir",StringComparison.Ordinal),"Recognized text is editable and copyable in "+locale+"/"+theme);Capture(window,Path.Combine(root,$"screenshot-{theme}-{locale}.png"));
                string? copied=null;window.TextField.Text="Düzeltilmiş metin\nİstanbul 123";window.CopyText(value=>copied=value);check(copied==window.TextField.Text,"Copy text retains edited Unicode and line breaks");
                check(UIElementAutomationPeer.CreatePeerForElement(window.StatusField).GetLiveSetting()==AutomationLiveSetting.Polite,"Screenshot status exposes a polite screen-reader live region");
                var path=Path.Combine(root,"Screenshot.png");if(!File.Exists(path))File.WriteAllText(path,"keep");var outputs=window.SaveTo(path);check(Path.GetFileName(outputs[0])!="Screenshot.png"&&File.ReadAllText(path)=="keep"&&ImageEngine.Load(outputs[0]).PixelWidth==800,"Saving a screenshot retains pixels and existing files");window.Close();
            }
            var pending=new TaskCompletionSource<string>();var delayed=new ScreenshotWindow(owner,_=>{});delayed.Show();var reading=delayed.SetImageAsync(fixture,(_,_)=>pending.Task);delayed.TextField.Text="My correction";pending.SetResult("late result");await reading;check(delayed.TextField.Text=="My correction","A late OCR result preserves manual corrections");
            await delayed.SetImageAsync(fixture,(_,_)=>Task.FromResult(""));check(!delayed.CopyTextButton.IsEnabled,"Empty OCR disables text copying");Capture(delayed,Path.Combine(root,"screenshot-empty.png"));
            await delayed.SetImageAsync(fixture,(_,_)=>Task.FromException<string>(new InvalidOperationException("Install an OCR language in Windows Language settings to recognize text.")));Capture(delayed,Path.Combine(root,"screenshot-error.png"));check(!delayed.CopyTextButton.IsEnabled,"OCR errors leave image actions available without copying stale text");
            var cancelledRecognition=false;var waiting=delayed.SetImageAsync(fixture,async(_,token)=>{token.Register(()=>cancelledRecognition=true);await Task.Delay(Timeout.Infinite,token);return "unused";});Capture(delayed,Path.Combine(root,"screenshot-loading.png"));delayed.Close();await waiting;check(cancelledRecognition,"Closing a screenshot cancels pending recognition");
            if(Environment.GetEnvironmentVariable("NASHI_CAPTURE_VERIFY_OLE")=="1")
            {
                var original=Path.Combine(root,"gesture-source.png");ImageEngine.Save(fixture,original,"png");GlobalGestureChecks.Run(owner,root,original,check);
            }
        }
        finally{foreach(var child in Application.Current.Windows.OfType<Window>().Where(window=>window.Owner==owner).ToArray())child.Close();owner.Close();L10n.Language=language;Theme.Apply(appearance);WheelPresentation.Preferences=wheelPreferences;Feedback.Enabled=feedback;}
    }
}
