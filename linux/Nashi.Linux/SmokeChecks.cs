using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Controls.ApplicationLifetimes;
using Nashi.Core;
using Nashi.Windows;
namespace Nashi.Linux;

internal static class SmokeChecks
{
    internal static int ExitCode{get;private set;}=1;
    internal static async Task Run(MainWindow window,string directory)
    {
        Directory.CreateDirectory(directory);var checks=0;void Check(bool condition,string message){Assert(condition,message);checks++;}
        try
        {
            await Task.Delay(600);
            Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Your files. Their next form."),"Nashi Home heading");
            Capture(window,Path.Combine(directory,"home.png"));
            window.Add([Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"smoke.png"))]);
            var fixture=Path.Combine(directory,"source.png");
            using(var source=new ImageMagick.MagickImage(ImageMagick.MagickColors.Gold,320,240))source.Write(fixture);
            window.Add([fixture]);await Task.Delay(250);
            Check(window.GetVisualDescendants().OfType<Button>().Any(b=>b.Content as string=="Edit Photo"),"shared image tools visible");
            Capture(window,Path.Combine(directory,"image-tools.png"));
            foreach(var action in new[]{"cropImage","editImage","annotateImage","frameImage","redactImage","compress"})
            {
                var editor=new ToolEditor(action,null,[fixture],new LinuxEngine());editor.Show(window);await Task.Delay(100);
                Check(editor.GetVisualDescendants().OfType<Button>().Any(b=>b.Content as string=="Save new copy"),"editor "+action);Capture(editor,Path.Combine(directory,action+".png"));editor.Close();
            }
            if(SystemTools.Find("ffmpeg") is {} ffmpeg){
                var media=Path.Combine(directory,"video.mp4");if(File.Exists(media))File.Delete(media);
                await ProcessRunner.RunAsync(ffmpeg,["-v","error","-f","lavfi","-i","color=c=gold:s=320x240:d=2","-c:v","mpeg4",media]);
                var editor=new ToolEditor("trimVideo",null,[media],new LinuxEngine());editor.Show(window);await Task.Delay(800);
                var player=editor.GetVisualDescendants().OfType<MediaPlayer>().Single();Check(player.Available,"embedded Linux media playback: "+player.Error);Check(player.Duration>1,"native player duration");player.Toggle();await Task.Delay(500);Check(player.Position>.1,"native playback advances");player.Pause();player.Seek(1);await Task.Delay(200);Check(Math.Abs(player.Position-1)<.2,"native seek");Capture(editor,Path.Combine(directory,"trimVideo.png"));editor.Close();
            }
            var pdf=(await new LinuxEngine().RunAsync("createPDF",[fixture,fixture],directory,new()))[0];
            var organizer=new PdfOrganizer(await NativePdf.RenderAsync(pdf,72,CancellationToken.None));organizer.Show(window);await Task.Delay(150);Check(organizer.GetVisualDescendants().OfType<ListBoxItem>().Count()==2,"PDF thumbnails");Capture(organizer,Path.Combine(directory,"pdf-organizer.png"));organizer.Close();
            var metadata=new MetadataEditor(window,new Dictionary<string,string>{{"SourceFile",fixture},{"EXIF:Artist","Nashi"}});metadata.Show(window);await Task.Delay(150);Check(metadata.GetVisualDescendants().OfType<TextBox>().Any(t=>t.Text=="Nashi"),"metadata editor existing values");Capture(metadata,Path.Combine(directory,"metadata.png"));metadata.Close();
            Check(X11DragMonitor.ParseUris("# files\n"+new Uri(fixture).AbsoluteUri+"\nfile://remote-server/tmp/other").SequenceEqual([fixture]),"Xdnd local file URI filtering");
            var storage=await window.StorageProvider.TryGetFileFromPathAsync(new Uri(fixture));Check(storage is not null,"drag fixture storage item");
            using(var data=new DataTransfer()){
                data.Add(DataTransferItem.CreateFile(storage!));
                foreach(var modifier in new[]{KeyModifiers.Control,KeyModifiers.Shift,KeyModifiers.Alt}){
                    window.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent,data,window,new Point(200,200),modifier));await Task.Delay(150);
                    var desktop=(IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;var wheel=desktop.Windows.OfType<WheelWindow>().Single(w=>w.IsVisible);
                    Check(wheel.Title=="Nashi · "+(modifier==KeyModifiers.Control?"Tools":modifier==KeyModifiers.Shift?"Convert":"Files"),"drag modifier opens correct wheel: "+modifier);Capture(wheel,Path.Combine(directory,"wheel-"+modifier+".png"));
                    if(modifier!=KeyModifiers.Alt){
                        var point=new Point(330,80);wheel.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent,data,wheel,point,modifier));wheel.RaiseEvent(new DragEventArgs(DragDrop.DropEvent,data,wheel,point,modifier));await Task.Delay(150);
                        Check(wheel.Dropped,"drop selects wheel action");var opened=desktop.Windows.OfType<ToolEditor>().Single(e=>e.IsVisible);Check(opened.Title?.Contains(modifier==KeyModifiers.Control?"Compress":"Convert to JPG")==true,"drop opens matching editor");opened.Close();
                    }else wheel.Close();
                }
            }
            var cropBounds=new Size(320,240);var beforeCrop=new Rect(80,60,160,120);
            foreach(var handle in Enum.GetValues<CropGeometry.Handle>()){
                foreach(var ratio in new double?[]{null,1,16d/9,9d/16})foreach(var target in new[]{new Point(-50,-50),new Point(160,120),new Point(500,400)}){
                    var resized=CropGeometry.Resize(beforeCrop,handle,target,cropBounds,ratio);
                    Check(resized.Width>0&&resized.Height>0&&resized.Left>=-.001&&resized.Top>=-.001&&resized.Right<=320.001&&resized.Bottom<=240.001,"crop handle stays inside source: "+handle);
                    if(ratio is {} expected)Check(Math.Abs(resized.Width/resized.Height-expected)<.0001,"crop handle preserves aspect: "+handle);
                }
            }
            Check(CropGeometry.Move(beforeCrop,new(80,60),new(-100,-100),cropBounds)==new Rect(0,0,160,120),"crop move clamps to source");
            Check(CropGeometry.Selection(new(300,220),new(500,400),cropBounds,1)==new Rect(300,220,20,20),"new crop locks ratio and bounds");
            var cropEditor=new ToolEditor("cropImage",null,[fixture],new LinuxEngine());cropEditor.Show(window);await Task.Delay(150);
            var cropSurface=cropEditor.GetVisualDescendants().OfType<ImageSurface>().Single();cropSurface.SetCrop(beforeCrop);
            void CropGesture(Point start,Point end,KeyModifiers modifiers=KeyModifiers.None){
                var drawing=cropSurface.GetVisualDescendants().OfType<Canvas>().First(c=>c.Children.OfType<Image>().Any());var pointer=new Pointer(71,PointerType.Mouse,true);
                Point Root(Point point)=>drawing.TranslatePoint(point,cropEditor)!.Value;
                var down=new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed);
                drawing.RaiseEvent(new PointerPressedEventArgs(drawing,pointer,cropEditor,Root(start),1,down,modifiers,1));
                drawing.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,drawing,pointer,cropEditor,Root(end),2,down,modifiers));
                drawing.RaiseEvent(new PointerReleasedEventArgs(drawing,pointer,cropEditor,Root(end),3,new PointerPointProperties(),modifiers,MouseButton.Left));
            }
            bool CropNear(Rect expected)=>cropSurface.CropBounds is {} r&&Math.Abs(r.X-expected.X)<.0001&&Math.Abs(r.Y-expected.Y)<.0001&&Math.Abs(r.Width-expected.Width)<.0001&&Math.Abs(r.Height-expected.Height)<.0001;
            CropGesture(new(80,60),new(40,20));Check(CropNear(new Rect(40,20,200,160)),"crop pointer corner resize: "+cropSurface.CropBounds);
            cropSurface.SetCrop(beforeCrop);CropGesture(new(160,120),new(180,140));Check(CropNear(new Rect(100,80,160,120)),"crop pointer interior move");
            cropSurface.SetCrop(beforeCrop);CropGesture(new(10,10),new(50,40));Check(CropNear(new Rect(10,10,40,30)),"crop pointer outside starts selection");
            var cropWidth=cropEditor.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="Width");cropWidth.Text="100";await Task.Delay(100);Check(CropNear(new Rect(10,10,100,30)),"numeric crop width updates selection: "+cropSurface.CropBounds);
            var aspectChoice=cropEditor.GetVisualDescendants().OfType<ComboBox>().Single();aspectChoice.SelectedItem="16:9";
            Check(cropSurface.CropBounds is {} selected&&Math.Abs(selected.Width/selected.Height-16d/9)<.0001,"crop preset updates visible selection");await Task.Delay(150);Capture(cropEditor,Path.Combine(directory,"crop-handles.png"));cropEditor.Close();
            var shelf=new FloatWindow([fixture],_=>{});shelf.Show(window);await Task.Delay(150);
            shelf.AddFiles([fixture,Path.Combine(directory,"missing.png")]);Check(shelf.Files.Count==1,"Float ignores duplicate and missing files");
            using(var transfer=new DataTransfer()){transfer.Add(DataTransferItem.CreateFile((await window.StorageProvider.TryGetFileFromPathAsync(new Uri(pdf)))!));shelf.RaiseEvent(new DragEventArgs(DragDrop.DropEvent,transfer,shelf,new Point(60,80),KeyModifiers.None));}
            Check(shelf.Files.SequenceEqual([fixture,pdf]),"Float drop adds local files");await Task.Delay(150);Capture(shelf,Path.Combine(directory,"float.png"));
            shelf.ToggleCollapsed();Check(shelf.Height==170,"Float collapse");shelf.ToggleCollapsed();Check(shelf.Height==390,"Float expand");shelf.Close();
            foreach(var appearance in new[]{"light","dark"}){
                var store=new LibraryStore(Path.Combine(directory,appearance+"-state"));store.SetAppearance(appearance);var screen=new MainWindow([],store,monitorDesktop:false);screen.Show();await Task.Delay(150);Capture(screen,Path.Combine(directory,"home-"+appearance+".png"));screen.Width=640;screen.Height=520;await Task.Delay(100);Capture(screen,Path.Combine(directory,"compact-"+appearance+".png"));screen.Close();Check(true,appearance+" native rendering");
            }
            await File.WriteAllTextAsync(Path.Combine(directory,"smoke.json"),System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks}));
            ExitCode=0;Console.WriteLine($"Linux native UI smoke: {checks} checks passed.");
        }
        catch(Exception error){Console.Error.WriteLine("Linux native UI smoke failed: "+error);ExitCode=1;}
        finally{window.Close();}
    }
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Capture(Window window,string path)
    {
        using var image=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height),new Vector(96,96));image.Render(window);image.Save(path,new PngBitmapEncoderOptions());
    }
}
