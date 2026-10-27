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
            foreach(var appearance in new[]{"light","dark"}){
                var store=new LibraryStore(Path.Combine(directory,appearance+"-state"));store.SetAppearance(appearance);var screen=new MainWindow([],store,monitorDesktop:false);screen.Show();await Task.Delay(150);Capture(screen,Path.Combine(directory,"home-"+appearance+".png"));screen.Width=640;screen.Height=520;await Task.Delay(100);Capture(screen,Path.Combine(directory,"compact-"+appearance+".png"));screen.Close();Check(true,appearance+" native rendering");
            }
            await File.WriteAllTextAsync(Path.Combine(directory,"smoke.json"),System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks}));
            Console.WriteLine($"Linux native UI smoke: {checks} checks passed.");
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
        finally{window.Close();}
    }
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Capture(Window window,string path)
    {
        using var image=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height),new Vector(96,96));image.Render(window);image.Save(path,new PngBitmapEncoderOptions());
    }
}
