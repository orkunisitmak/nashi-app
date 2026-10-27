using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
