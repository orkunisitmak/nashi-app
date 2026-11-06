using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nashi.Linux;
using Nashi.Linux.UI;

// Usage: nashi-snapshots <output folder> <fixture folder>
// Renders every main screen to PNG, then drives real tool flows through the UI (open the tool,
// press its default button, wait for the export) and checks each output and its Library entry.
// Keep Library and settings away from the user's own: point XDG_DATA_HOME at a temp folder first.
var output=Path.GetFullPath(args.Length>0?args[0]:"snapshots");var fixtures=args.Length>1?Path.GetFullPath(args[1]):null;Directory.CreateDirectory(output);
// Continuations after await must come back to the UI thread, as in the real app.
SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
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

if(fixtures is null)return 0;
// ── Flows ────────────────────────────────────────────────────────────────────────────
var failures=0;
var hasOffice=new LinuxOffice().Executable is not null;var hasMedia=LinuxShell.FindInPath("ffmpeg") is not null&&LinuxShell.FindInPath("ffprobe") is not null;
void Flow(string name,string[] inputs,string tool,Func<string[],bool> check,Action<ToolEditor>? adjust=null)
{
    // A missing optional tool would open a setup prompt instead of the editor; report it as skipped.
    var needsOffice=tool=="convert:docx";var needsMedia=inputs.Any(Nashi.Core.FeatureCatalog.IsVideo);
    if(needsOffice&&!hasOffice||needsMedia&&!hasMedia){Console.WriteLine($"  skip  flow {name} · {(needsOffice?"LibreOffice":"FFmpeg")} not installed");return;}
    var window=new MainWindow([]);window.Show();window.UseFiles(inputs);var before=window.library.Entries.Count;
    var run=window.EditAsync(new ToolChoice(tool,tool,"↗"));
    // Find the tool's dialog, let it render, then press its default button as a person would.
    ToolEditor? editor=null;for(var n=0;n<400&&editor is null&&!run.IsCompleted;n++){Pump();editor=window.OwnedWindows.OfType<ToolEditor>().FirstOrDefault();if(editor is null)Thread.Sleep(10);}
    if(editor is not null)
    {
        for(var n=0;n<30;n++){Pump();Thread.Sleep(10);}adjust?.Invoke(editor);
        var accept=editor.GetVisualDescendants().OfType<Button>().First(b=>b.IsDefault);accept.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }
    for(var n=0;n<6000&&!run.IsCompleted;n++){Pump();Thread.Sleep(10);}
    var entry=window.library.Entries.Count>before?window.library.Entries[0]:null;var outputs=entry?.AvailablePaths??[];
    // A new Library entry proves this run exported (a compressed copy may keep the source's timestamp).
    var ok=run.IsCompletedSuccessfully&&editor is not null&&outputs.Length>0&&check(outputs);
    if(!ok)failures++;
    Console.WriteLine($"  {(ok?"pass":"FAIL")}  flow {name} · {(editor is null?"no editor":"")} {string.Join(", ",outputs.Select(Path.GetFileName))} {(run.IsFaulted?run.Exception?.GetBaseException().Message:"")}");
    window.Close();
}
var work=Path.Combine(output,"flows");if(Directory.Exists(work))Directory.Delete(work,true);Directory.CreateDirectory(work);
string Copy(string name){var target=Path.Combine(work,name);File.Copy(Path.Combine(fixtures,name),target,true);return target;}
var flowPhoto=Copy("Photo.png");var flowSecond=Copy("Second.jpg");var flowZip=Copy("Bundle.zip");var flowClip=Copy("Clip.mp4");var flowNotes=Copy("Notes.txt");
var pdf=Path.Combine(work,"Pages.pdf");File.Copy(Directory.GetFiles(Path.GetDirectoryName(fixtures)!,"Images.pdf",SearchOption.AllDirectories)[0],pdf,true);
Flow("convert PNG → WEBP",[flowPhoto],"convert:webp",o=>o[0].EndsWith(".webp"));
Flow("crop image",[flowPhoto],"cropImage",o=>{using var i=new ImageMagick.MagickImage(o[0]);return i.Width==512&&i.Height==320;});
Flow("edit photo",[flowPhoto],"editImage",o=>File.Exists(o[0]));
Flow("add background",[flowPhoto],"frameImage",o=>o[0].EndsWith(".png"));
Flow("compress images",[flowPhoto,flowSecond],"compress",o=>o.Length==2);
Flow("create collage",[flowPhoto,flowSecond],"createCollage",o=>o[0].EndsWith("Collage.png"));
Flow("create PDF",[flowPhoto,flowSecond],"createPDF",o=>o[0].EndsWith(".pdf"));
Flow("split PDF",[pdf],"splitPDF",o=>o.Length==2);
var pdf2=Path.Combine(work,"More Pages.pdf");File.Copy(pdf,pdf2,true);
Flow("merge PDFs",[pdf,pdf2],"mergePDF",o=>o[0].EndsWith(".pdf"));
Flow("organize PDF",[pdf],"organizePDF",o=>o[0].EndsWith("Organized.pdf"));
Flow("compress PDF",[pdf],"compress",o=>o[0].EndsWith(".pdf"));
Flow("PDF → PNG pages",[pdf],"convert:png",o=>o.Length==2);
Flow("extract archive",[flowZip],"extractArchive",o=>Directory.Exists(o[0]));
Flow("TXT → DOCX",[flowNotes],"convert:docx",o=>o[0].EndsWith(".docx"));
Flow("trim video",[flowClip],"trimVideo",o=>o[0].EndsWith(".mp4"),e=>{foreach(var box in e.GetVisualDescendants().OfType<TextBox>().Take(2).ToList())box.Text=box.Text=="0"?"0.5":"2";});
Flow("MP4 → GIF",[flowClip],"convert:gif",o=>o[0].EndsWith(".gif"));
Flow("video snapshots",[flowClip],"videoSnapshots",o=>o.Length==4);
Console.WriteLine(failures==0?"flows passed":$"{failures} flow(s) failed");
return failures==0?0:1;
