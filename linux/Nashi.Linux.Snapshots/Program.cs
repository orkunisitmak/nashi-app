using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Nashi.Linux;
using Nashi.Linux.UI;

// Usage: nashi-snapshots <output folder> <fixture folder>
// Keep Library and settings away from the user's own: point XDG_DATA_HOME at a temp folder first.
var output=Path.GetFullPath(args.Length>0?args[0]:"snapshots");var fixtures=args.Length>1?Path.GetFullPath(args[1]):null;Directory.CreateDirectory(output);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().SetupWithoutStarting();
void Pump(){for(var n=0;n<6;n++){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();}}
void Save(Window window,string name){Pump();using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(output,name+".png"));Console.WriteLine("saved "+name);}
string[] Fixtures(params string[] names)=>fixtures is null?[]:names.Select(n=>Path.Combine(fixtures,n)).Where(File.Exists).ToArray();

App.ApplyAppearance("light");
var main=new MainWindow([]);main.Width=900;main.Height=900;main.Show();Save(main,"01-home-empty");
main.UseFiles(Fixtures("Photo.png","Second.jpg"));Save(main,"02-home-two-images");
main.ShowWheel();Save(main,"03-wheel-tools");main.Close();

main=new MainWindow(Fixtures("Clip.mp4"));main.Width=900;main.Height=900;main.Show();Save(main,"04-home-video");
main.ShowWheel("convert");Save(main,"05-wheel-convert");main.Close();

// The window reads appearance and language from the saved Library and settings, like a real start.
var store=new Nashi.Core.LibraryStore();var prefs0=new Nashi.Core.PreferencesStore(store.DirectoryPath);
store.SetAppearance("dark");
main=new MainWindow(Fixtures("Photo.png"));main.Width=900;main.Height=900;main.Show();Save(main,"06-home-dark");main.ShowWheel();Save(main,"06b-wheel-dark");main.Close();
store.SetAppearance("light");

prefs0.Save(prefs0.Value with{Language="tr"});
main=new MainWindow(Fixtures("Notes.txt"));main.Width=900;main.Height=900;main.Show();Save(main,"07-home-turkish");main.ShowWheel();Save(main,"07b-wheel-turkish");main.Close();
prefs0.Save(prefs0.Value with{Language="en"});L10n.Language="en";

if(fixtures is not null)
{
    var photo=Path.Combine(fixtures,"Photo.png");var prefs=new Nashi.Core.PreferencesStore(Path.Combine(output,"prefs"));
    var (png,w,h)=(Nashi.Linux.Engine.ImageEngine.PreviewPng(photo),640,400);
    var editors=new (string Name,ToolEditor Editor)[]
    {
        ("08-editor-crop",new ToolEditor("crop","Crop Image",[photo],null,prefs,preview:png,sourceWidth:w,sourceHeight:h)),
        ("09-editor-photo",new ToolEditor("editImage","Edit Photo",[photo],null,prefs,preview:png,sourceWidth:w,sourceHeight:h)),
        ("10-editor-convert",new ToolEditor("convert","Convert",[photo],Nashi.Core.FeatureCatalog.Formats([photo]),prefs,"webp")),
        ("11-editor-redact",new ToolEditor("redactImage","Redact Photo",[photo],null,prefs,preview:png,sourceWidth:w,sourceHeight:h)),
    };
    foreach(var (name,editor) in editors){editor.Show();Save(editor,name);editor.Close();}
    main=new MainWindow([]);main.Show();var setup=new SetupWindow(main);setup.Show();Save(setup,"12-setup");setup.Close();
    var settings=new SettingsWindow(main);settings.Show();Save(settings,"13-settings");settings.Close();main.Close();
}
