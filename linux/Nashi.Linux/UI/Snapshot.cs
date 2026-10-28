using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux.UI;

/// `nashi --render OUT.png [--page home|library|wheel|tools|settings] [--theme light|dark] [--lang en|tr] [--tool ID[:FORMAT] [--run]] [--press-ctrl-space] [FILE…]`
/// renders the real window off-screen with Skia, for CI and screenshots on
/// machines without a display. It uses a temporary Library, never the user's.
internal static class Snapshot
{
    internal static Task<int> RunAsync(string[] args)
    {
        if(args.Length==0){Console.Error.WriteLine("Usage: nashi --render OUT.png [--page …] [--theme …] [--lang …] [FILE…]");return Task.FromResult(64);}
        var output=Path.GetFullPath(args[0]);var page="home";var theme="light";var language="en";var files=new List<string>();var tool="";var execute=false;var press=false;var width=860;var height=820;
        for(var i=1;i<args.Length;i++)
        {
            switch(args[i]){case "--page":page=args[++i];break;case "--theme":theme=args[++i];break;case "--lang":language=args[++i];break;case "--tool":tool=args[++i];break;case "--run":execute=true;break;case "--press-ctrl-space":press=true;break;case "--size":var s=args[++i].Split('x');width=int.Parse(s[0]);height=int.Parse(s[1]);break;default:files.Add(args[i]);break;}
        }
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
        var data=Path.Combine(Path.GetTempPath(),"Nashi-render-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
        try
        {
            var library=new LibraryStore(data);var preferences=new PreferencesStore(data);library.SetAppearance(theme);preferences.Save(preferences.Value with{Language=language});
            if(files.Count>0&&!execute)library.Record("Convert to PNG",files.Where(File.Exists));
            L10n.Language=language;Brand.Apply(theme);
            var window=new MainWindow(library,preferences,files){Width=width,Height=height};window.Show();Pump();
            if(page=="library")window.ShowLibrary(true);
            if(page=="wheel")window.ShowWheel();
            Avalonia.Controls.Window? dialog=page switch{"tools"=>new ToolsWindow(new(data),()=>{}),"settings"=>new SettingsWindow(preferences,library),_=>null};
            if(tool.Length>0)Wait(window.OpenToolAsync(tool.Split(':')[0],tool.Contains(':')?tool.Split(':')[1]:null));
            // --run presses Run like a user would and reports what reached Library.
            if(execute){Wait(window.RunToolAsync());foreach(var path in library.Entries.SelectMany(e=>e.Paths))Console.WriteLine("OUTPUT "+path);}
            if(press){window.KeyPress(Avalonia.Input.Key.Space,Avalonia.Input.RawInputModifiers.Control,Avalonia.Input.PhysicalKey.Space," ");Pump();Console.WriteLine("WHEEL "+window.WheelVisible);}
            for(var n=0;n<40;n++){Pump();Thread.Sleep(25);}
            var target=dialog??window;if(dialog is not null){dialog.Show();for(var n=0;n<10;n++)Pump();}
            using var frame=target.CaptureRenderedFrame()??throw new InvalidOperationException("Nothing was rendered.");frame.Save(output);
            Console.WriteLine(output);return Task.FromResult(0);
        }
        finally{Directory.Delete(data,true);}
    }
    private static void Wait(Task task){var deadline=DateTime.UtcNow.AddMinutes(5);while(!task.IsCompleted&&DateTime.UtcNow<deadline){Pump();Thread.Sleep(10);}task.GetAwaiter().GetResult();}
    private static void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();}
}
