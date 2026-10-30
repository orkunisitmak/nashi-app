using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;
internal static class SmokeTests
{
    public static void VerifyUpdateState()
    {
        var output=Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY")??throw new Exception("Choose a proof directory.");Directory.CreateDirectory(output);
        var library=new LibraryStore();var entry=library.Entries.Single(e=>e.Action=="Update persistence fixture");
        if(library.Appearance!="dark"||entry.AvailablePaths.Length!=2||!entry.Paths.Any(p=>Path.GetExtension(p)==".txt"&&File.ReadAllText(p).Contains("Persistent clipboard")))throw new Exception("Update did not preserve Library, appearance or imports.");
        var window=new MainWindow(library:library,checkForUpdates:false);window.Show();window.ShowLibrary(true);window.UpdateLayout();DesktopInput.Pump();
        if(!Descendants(window).OfType<TextBlock>().Any(t=>t.Text.Contains("Update persistence fixture")))throw new Exception("Updated Library did not render persisted entry.");
        var capture=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);capture.Render(window);ImageEngine.Save(capture,Path.Combine(output,"windows-updated-library.png"),"png");window.Close();
        File.WriteAllText(Path.Combine(output,"preserved-state.json"),JsonSerializer.Serialize(new{library=true,clipboardText=true,clipboardImage=true,appearance=true,nativeLibraryRendered=true}));
    }
    public static void Run()
    {
        Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var root=Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY")??Path.Combine(Path.GetTempPath(),"Nashi-Smoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var checks=0;
        using var watchdog=new System.Threading.Timer(_=>{File.WriteAllText(Path.Combine(root,"suite-timeout.txt"),"Native desktop verification exceeded 15 minutes.");DesktopInput.HoldMouse(false);Environment.Exit(1);},null,TimeSpan.FromMinutes(15),System.Threading.Timeout.InfiniteTimeSpan);
        var testTools=ParitySmokeTests.Tools(root);
        void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
        TrayIconRecoveryChecks.Run(Check);
        EditorReliabilityChecks.Run(root,Check);
        InteractionChecks.Run(root,Check);
        IslandInteractionChecks.Run(Check);
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brushes.DarkOliveGreen,null,new Rect(0,0,320,180));dc.DrawRectangle(Brushes.Gold,null,new Rect(20,20,120,80));}
        var bitmap=new RenderTargetBitmap(320,180,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var source=Path.Combine(root,"Source.png");ImageEngine.Save(bitmap,source,"png");
        const string query="/tEXt/{str=Comment}";
        var tagged=Path.Combine(root,"Tagged.png");var metadata=new BitmapMetadata("png");metadata.SetQuery(query,"Nashi metadata fixture");
        var taggedEncoder=new PngBitmapEncoder();taggedEncoder.Frames.Add(BitmapFrame.Create(bitmap,null,metadata,null));using(var taggedFile=File.Create(tagged))taggedEncoder.Save(taggedFile);
        bool HasTag(string file,string tag){using var stream=File.OpenRead(file);var decoded=BitmapDecoder.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);return decoded.Frames[0].Metadata is BitmapMetadata data&&data.ContainsQuery(tag);}
        Check(HasTag(tagged,query),"Metadata fixture was not tagged");
        var cleaned=ImageEngine.Apply("removeMetadata",[tagged],root,new());
        Check(!HasTag(cleaned[0],query),"Metadata removal retained the source comment");
        var colors=new[]{Colors.Red,Colors.Lime,Colors.Blue,Colors.Yellow};var quadrants=new DrawingVisual();
        using(var dc=quadrants.RenderOpen())for(var i=0;i<4;i++)dc.DrawRectangle(new SolidColorBrush(colors[i]),null,new Rect(i%2*40,i/2*20,40,20));
        var quadrantBitmap=new RenderTargetBitmap(80,40,96,96,PixelFormats.Pbgra32);quadrantBitmap.Render(quadrants);
        var topLeft=new[]{0,1,3,2,0,2,3,1};
        for(ushort orientation=1;orientation<=8;orientation++)
        {
            var original=Path.Combine(root,$"Orientation {orientation}.jpg");var tags=new BitmapMetadata("jpg");tags.SetQuery("/app1/ifd/{ushort=274}",orientation);
            var jpeg=new JpegBitmapEncoder{QualityLevel=100};jpeg.Frames.Add(BitmapFrame.Create(quadrantBitmap,null,tags,null));using(var file=File.Create(original))jpeg.Save(file);
            var oriented=ImageEngine.Load(original);Check(oriented.PixelWidth==(orientation>=5?40:80)&&oriented.PixelHeight==(orientation>=5?80:40),"Orientation dimensions wrong");
            var pixel=new byte[4];new FormatConvertedBitmap(oriented,PixelFormats.Bgra32,null,0).CopyPixels(new Int32Rect(5,5,1,1),pixel,4,0);var expected=colors[topLeft[orientation-1]];
            Check(Math.Abs(pixel[2]-expected.R)<30&&Math.Abs(pixel[1]-expected.G)<30&&Math.Abs(pixel[0]-expected.B)<30,"Orientation pixels wrong: "+orientation);
            var exported=ImageEngine.Apply("removeMetadata",[original],root,new());Check(!HasTag(exported[0],"/app1/ifd/{ushort=274}"),"EXIF orientation tag survived metadata removal");
        }
        var crop=ImageEngine.Apply("crop",[source],root,new(Crop:new Int32Rect(10,10,80,60)));Check(ImageEngine.Load(crop[0]).PixelWidth==80,"Crop width mismatch");
        var resized=ImageEngine.Apply("resize",[source],root,new(Width:160));Check(ImageEngine.Load(resized[0]).PixelHeight==90,"Resize aspect mismatch");
        var rotated=ImageEngine.Apply("rotate",[source],root,new(Rotation:90));Check(ImageEngine.Load(rotated[0]).PixelWidth==180,"Rotate mismatch");
        foreach(var ext in ImageEngine.Formats){var converted=ImageEngine.Apply("convert",[source],root,new(){EncoderPath=testTools.Ready?testTools.Executable("ffmpeg"):null},ext);Check(new FileInfo(converted[0]).Length>0,"Empty "+ext);}
        var pdfs=Directory.GetFiles(root,"*.pdf");var merged=DocumentEngine.Pdf("mergePDFs",[pdfs[0],pdfs[0]],root);var parts=DocumentEngine.Pdf("splitPDF",merged,root);Check(parts.Count==2,"PDF page count mismatch");
        var zip=DocumentEngine.Zip([source],root);var extracted=DocumentEngine.Unzip(zip,root);Check(File.Exists(Path.Combine(extracted[0],"Source.png")),"ZIP output missing");
        var library=new LibraryStore(Path.Combine(root,"UserData"));library.SetAppearance("dark");
        var window=new MainWindow([source],library,checkForUpdates:false);window.Show();window.UpdateLayout();DesktopInput.Pump();
        LocalizationChecks.Run(root,window,source,Check);
        ToolSetupChecks.Run(root,window,Check);
        FileArtworkSmoke.Run(root,Check);
        RangeAccessibilityChecks.Run(root,window,Check);
        Button FindButton(string name)=>Descendants(window).OfType<Button>().First(b=>b.Content as string==name);
        var clipboardText="Nashi clipboard · Türkçe ıİşğü 🍐\r\nSecond line";
        Clipboard.SetText(clipboardText);FindButton("Home").Focus();DesktopInput.Key(window,Key.V,control:true);
        Check(window.SelectedFiles.Count==1&&Path.GetExtension(window.SelectedFiles[0])==".txt","Native Ctrl+V did not import text");
        Check(File.ReadAllText(window.SelectedFiles[0])==clipboardText,"Unicode or line breaks changed while pasting text");
        var importedText=window.SelectedFiles[0];Check(library.Entries.Count==1,"Pasted text missing from Library");
        DesktopInput.Key(window,Key.F,control:true);Check(window.LibraryVisible&&window.SearchField.IsKeyboardFocused,"Ctrl+F did not focus Library search");
        Clipboard.SetText("Pasted text");DesktopInput.Key(window,Key.V,control:true);
        Check(window.SearchField.Text=="Pasted text"&&library.Entries.Count==1,"Ctrl+V in a text field imported a new file or failed normal paste");
        DesktopInput.Key(window,Key.D1,control:true);Check(!window.LibraryVisible,"Ctrl+1 did not switch Home");
        Clipboard.SetImage(bitmap);FindButton("Home").Focus();DesktopInput.Key(window,Key.V,control:true);
        var importedImage=window.SelectedFiles.Single();var pasted=ImageEngine.Load(importedImage);
        Check(pasted.PixelWidth==320&&pasted.PixelHeight==180,"Native image paste changed image dimensions");
        Check(library.Entries.Count==2&&File.Exists(importedText),"Pasting an image replaced previous imported text");
        var imagePixel=new byte[4];new FormatConvertedBitmap(pasted,PixelFormats.Bgra32,null,0).CopyPixels(new Int32Rect(30,30,1,1),imagePixel,4,0);
        Check(imagePixel[2]>200&&imagePixel[1]>150&&imagePixel[0]<50,"Clipboard image pixels changed");
        var drops=new System.Collections.Specialized.StringCollection{source,tagged};Clipboard.SetFileDropList(drops);FindButton("Home").Focus();DesktopInput.Key(window,Key.V,control:true);
        Check(window.SelectedFiles.Count==2&&window.SelectedFiles.Contains(source),"File-list paste failed");
        Clipboard.SetText("https://www.youtube.com/watch?v=fixture");FindButton("Home").Focus();DesktopInput.Key(window,Key.D,control:true,shift:true);
        Check(window.LinkField.Text.Contains("watch?v=fixture")&&window.LinkField.IsKeyboardFocused,"Copied-link shortcut failed");Capture(window.DownloadWindow,"windows-download.png");
        Clipboard.SetText("ordinary field paste");DesktopInput.Key(window.DownloadWindow,Key.V,control:true);
        Check(window.LinkField.Text.Contains("ordinary field paste")&&library.Entries.Count==3,"Link field paste was intercepted");window.DownloadWindow.Close();
        Clipboard.Clear();window.Import(new DataObject());Check(library.Entries.Count==3,"Empty clipboard created a Library item");
        var reloaded=new LibraryStore(library.DirectoryPath);Check(reloaded.Entries.Count==3&&reloaded.Appearance=="dark","Library or appearance did not survive reload");
        var remove=ImageEngine.Apply("resize",[source],root,new(Width:120))[0];library.Record("Resize",[remove]);File.Delete(remove);reloaded=new LibraryStore(library.DirectoryPath);
        Check(reloaded.Search("Resize").Single().AvailablePaths.Length==0,"Moved/deleted Library file treated as available");
        window.ShowLibrary(true);window.SearchField.Clear();window.UpdateLayout();DesktopInput.Pump();
        Capture(window,"windows-library.png");
        window.ShowLibrary(false);window.LinkField.Clear();window.UpdateLayout();DesktopInput.Pump();
        var screenshot=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);screenshot.Render(window);ImageEngine.Save(screenshot,Path.Combine(root,"windows-home.png"),"png");
        var wheel=new WheelWindow(window,[new("crop","Crop","⌗"),new("resize","Resize","⤢"),new("annotate","Annotate","✎")],_=>{});wheel.Show();wheel.UpdateLayout();DesktopInput.Pump();
        DesktopInput.Key(wheel,Key.Right);Check(wheel.SelectedIndex==1,"Wheel Right arrow failed");
        DesktopInput.Key(wheel,Key.Left);Check(wheel.SelectedIndex==0,"Wheel Left arrow failed");
        DesktopInput.Key(wheel,Key.Left);Check(wheel.SelectedIndex==2,"Wheel did not wrap to last tool");
        var wheelImage=new RenderTargetBitmap((int)wheel.ActualWidth,(int)wheel.ActualHeight,96,96,PixelFormats.Pbgra32);wheelImage.Render(wheel);ImageEngine.Save(wheelImage,Path.Combine(root,"windows-wheel.png"),"png");DesktopInput.Key(wheel,Key.Escape);Check(!wheel.IsVisible,"Escape did not close wheel");
        ToolChoice? chosen=null;var enterWheel=new WheelWindow(window,[new("crop","Crop","⌗"),new("resize","Resize","⤢")],choice=>chosen=choice);enterWheel.Show();DesktopInput.Pump();DesktopInput.Key(enterWheel,Key.Tab);DesktopInput.Key(enterWheel,Key.Enter);Check(chosen?.Id=="resize","Tab/Enter did not activate the focused wheel tool");
        var editor=new ToolEditor(window,"crop","Crop",source,preview:bitmap);editor.Show();editor.UpdateLayout();DesktopInput.Pump();
        var cancel=Descendants(editor).OfType<Button>().Single(b=>b.Content as string=="Cancel");Check(cancel.ActualWidth<180,"Editor Cancel button stretched across the footer");Check(editor.FieldForTest("Width").ActualHeight<48,"Text box padding doubled its height");
        var canvas=Descendants(editor).OfType<Canvas>().First();
        DesktopInput.Drag(canvas,new Point(20,20),new Point(180,130),shift:true);
        var cw=double.Parse(editor.FieldForTest("Width").Text);var ch=double.Parse(editor.FieldForTest("Height").Text);
        Check(cw>1&&Math.Abs(cw/ch-1)<.03,"Shift-drag crop did not make a square selection");
        editor.FieldForTest("Width").Focus();DesktopInput.Key(editor,Key.Left);Check(editor.IsVisible,"Arrow in a crop field closed editor");
        var editorImage=new RenderTargetBitmap((int)editor.ActualWidth,(int)editor.ActualHeight,96,96,PixelFormats.Pbgra32);editorImage.Render(editor);ImageEngine.Save(editorImage,Path.Combine(root,"windows-crop.png"),"png");DesktopInput.Key(editor,Key.Escape);Check(!editor.IsVisible,"Escape did not close editor from an active text field");
        var rangeOwner=new Window{Owner=window,Width=600,Height=180,WindowStartupLocation=WindowStartupLocation.CenterOwner};Theme.Attach(rangeOwner);var range=new RangeTimeline{Margin=new(30)};rangeOwner.Content=range;range.Set(1800,0,1800);rangeOwner.Show();rangeOwner.UpdateLayout();DesktopInput.Pump();
        var width=range.ActualWidth-24;DesktopInput.Drag(range,new Point(12+width,27),new Point(12+width*.7,27));
        Check(Math.Abs(range.End-1260)<25,"Long-video Out handle did not follow native mouse drag");
        DesktopInput.Drag(range,new Point(12,27),new Point(12+width*.2,27));Check(Math.Abs(range.Start-360)<25&&range.End>range.Start,"Long-video In handle stuck or crossed Out");rangeOwner.Close();
        var wave=Path.Combine(root,"Keyboard preview.wav");
        using(var writer=new BinaryWriter(File.Create(wave)))
        {var samples=44100*5;writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(44100);writer.Write(88200);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);for(var i=0;i<samples;i++)writer.Write((short)(Math.Sin(i*2*Math.PI*440/44100)*1500));}
        var audioEditor=new ToolEditor(window,"trimAudio","Trim Audio",wave,new MediaInfo(5,new FileInfo(wave).Length,[new MediaStream(0,"audio","pcm_s16le",null,null,false)]));audioEditor.Show();audioEditor.UpdateLayout();DesktopInput.Pump();audioEditor.Timeline.Focus();
        DesktopInput.Key(audioEditor,Key.Space);Check(audioEditor.Playing,"Space did not start media preview command");DesktopInput.Pump(350);DesktopInput.Key(audioEditor,Key.Space);Check(!audioEditor.Playing,"Space did not pause media preview command");
        var position=audioEditor.PreviewSeconds;DesktopInput.Key(audioEditor,Key.Right);Check(Math.Abs(audioEditor.PreviewSeconds-position-1)<.1,"Right arrow did not seek one second");
        DesktopInput.Key(audioEditor,Key.Left);Check(Math.Abs(audioEditor.PreviewSeconds-position)<.1,"Left arrow did not seek back one second");
        DesktopInput.Key(audioEditor,Key.I);DesktopInput.Key(audioEditor,Key.Right);DesktopInput.Key(audioEditor,Key.O);Check(audioEditor.Timeline.End>audioEditor.Timeline.Start&&audioEditor.Timeline.End<5,"I/O did not synchronize trim range");
        audioEditor.FieldForTest("Start").Focus();position=audioEditor.PreviewSeconds;DesktopInput.Key(audioEditor,Key.Left);Check(Math.Abs(audioEditor.PreviewSeconds-position)<.1,"Arrow in trim text field incorrectly sought media");DesktopInput.Key(audioEditor,Key.Escape);Check(!audioEditor.IsVisible,"Escape did not close trim editor");
        var themeSettings=new SettingsWindow(window,new PreferencesStore(library.DirectoryPath),()=>{},()=>Task.CompletedTask,library);themeSettings.Show();DesktopInput.Pump();
        var appearance=Descendants(themeSettings).OfType<ComboBox>().Single(c=>c.Items.Contains("dark"));appearance.Focus();DesktopInput.Key(themeSettings,Key.Home);DesktopInput.Key(themeSettings,Key.Down);Check(!Theme.Dark&&library.Appearance=="light","Settings appearance keyboard selection did not apply and persist light theme");window.UpdateLayout();Capture(window,"windows-home-light.png");DesktopInput.Key(themeSettings,Key.End);Check(Theme.Dark&&library.Appearance=="dark","Dark theme selection failed");themeSettings.Close();
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")=="true")checks+=ParitySmokeTests.Run(root,window,source,bitmap,testTools);
        DesktopParityChecks.Run(root,window,source,importedText,importedImage,testTools,Check);
        PdfPreviewChecks.Run(root,Check);
        var toast=new ToastWindow(window,null);toast.Show();Check(toast.WindowStyle==WindowStyle.None&&toast.ResizeMode==ResizeMode.NoResize,"Toast has window chrome");toast.Update(new(.42,"Encoding your fresh copy…"));toast.UpdateLayout();Capture(toast,"windows-progress.png");toast.Close();window.Close();
        var restarted=new MainWindow(library:new LibraryStore(library.DirectoryPath),checkForUpdates:false);restarted.Show();DesktopInput.Pump();
        Check(new LibraryStore(library.DirectoryPath).Entries.Count==4&&File.Exists(importedText)&&File.Exists(importedImage),"Restart lost Library or clipboard files");Capture(restarted,"windows-home-empty.png");restarted.Close();
        WindowManagementSmoke.Run(root,Check);
        Check(UIAudit.Run(Path.Combine(root,"ui-audit"))==0,"Isolated light/dark EN/TR UI audit failed; see ui-audit/manifest.json");
        Check(PdfRasterDocument.OpenDocuments==0,"Closed PDF previews retained native documents or pinned source buffers");
        File.WriteAllText(Path.Combine(root,"smoke.json"),JsonSerializer.Serialize(new{passed=checks,scope="Native Windows SendInput keyboard routing, real OS clipboard text/image/files, Library restart/search/missing files, theme renders, image/PDF/ZIP operations.",extendedParity=Environment.GetEnvironmentVariable("GITHUB_ACTIONS")=="true"}));
        void Capture(Window view,string name){view.UpdateLayout();var capture=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);capture.Render(view);ImageEngine.Save(capture,Path.Combine(root,name),"png");}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
