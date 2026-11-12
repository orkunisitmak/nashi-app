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
var output=Path.GetFullPath(args.Length>0?args[0]:"snapshots");var fixtures=args.Length>1?Path.GetFullPath(args[1]):null;Directory.CreateDirectory(output);
// Library, settings, pasted files and the autostart entry all live under these: a test run must never
// write over the person's own (it once did, when the variables were left to the caller).
// The check comes before anything is deleted, and only the folder it checked is cleared.
var testHome=Path.Combine(output,"home");
Environment.SetEnvironmentVariable("XDG_DATA_HOME",Path.Combine(testHome,"data"));Environment.SetEnvironmentVariable("XDG_CONFIG_HOME",Path.Combine(testHome,"config"));
if(!AppPaths.Data.StartsWith(testHome+Path.DirectorySeparatorChar,StringComparison.Ordinal)){Console.Error.WriteLine("Refusing to run: app data would go to "+AppPaths.Data);return 2;}
if(Directory.Exists(testHome))Directory.Delete(testHome,true);
// Never touch the desktop's real global shortcuts from a test run.
Environment.SetEnvironmentVariable("NASHI_NO_GLOBAL_SHORTCUTS","1");
// Continuations after await must come back to the UI thread, as in the real app.
SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().SetupWithoutStarting();
void Pump(){for(var n=0;n<6;n++){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();}}
#pragma warning disable CS0618 // PNG defaults are fine for review screenshots.
void Save(Window window,string name){Pump();using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(output,name+".png"));Console.WriteLine("saved "+name);}
#pragma warning restore CS0618
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
    {
        var host=new MainWindow([]);host.Show();var look=new PreviewWindow(host,[photo,Fixtures("Second.jpg")[0]],0);look.Show();
        for(var n=0;n<40;n++){Pump();Thread.Sleep(25);}Save(look,"17-preview");
        look.KeyPress(Avalonia.Input.Key.Right,Avalonia.Input.RawInputModifiers.None,Avalonia.Input.PhysicalKey.ArrowRight,null);Pump();
        Console.WriteLine(look.Title?.StartsWith("Second.jpg")==true?"  pass  key Quick look moves with →":"  FAIL  key Quick look moves with →");look.Close();host.Close();
    }
    main=new MainWindow([]);main.Show();var setup=new SetupWindow(main);setup.Show();Save(setup,"12-setup");setup.Close();
    main.SaveLinuxSettings(main.LinuxSettings with{Watches=[new Nashi.Linux.FolderRule(fixtures,["heic","heif"],"jpg")]});
    var settings=new SettingsWindow(main);settings.Show();Save(settings,"13-settings");
    var tabs=settings.GetVisualDescendants().OfType<TabControl>().First();
    tabs.SelectedIndex=1;Save(settings,"13b-settings-appearance");tabs.SelectedIndex=2;Save(settings,"13c-settings-shortcuts");tabs.SelectedIndex=3;Save(settings,"13d-settings-automation");settings.Close();
    main.SaveLinuxSettings(main.LinuxSettings with{Watches=[]});main.Close();
    // Every theme, light and dark, on a busy Home.
    foreach(var theme in new[]{"brutalist","glass","frosted","classic"})foreach(var variant in new[]{"light","dark"})
    {
        App.ApplyTheme(theme);var themed=new MainWindow([]);App.ApplyAppearance(variant);themed.Width=900;themed.Height=900;themed.Show();themed.UseFiles(Fixtures("Photo.png","Second.jpg"));
        Save(themed,$"20-theme-{theme}-{variant}");if(variant=="light"){themed.ShowWheel();Save(themed,$"21-wheel-{theme}");}themed.Close();
    }
    App.ApplyAppearance("light");
}

// ── Keyboard ─────────────────────────────────────────────────────────────────────────
// Real key events through the window, as the desktop delivers them.
bool WheelOpen(Window w)=>w.GetVisualDescendants().OfType<WheelOverlay>().Any(o=>o.IsEffectivelyVisible);
{
    var keyFailures=0;void Expect(string name,bool ok){if(!ok)keyFailures++;Console.WriteLine($"  {(ok?"pass":"FAIL")}  key {name}");}
    var w=new MainWindow([]);w.Show();Pump();
    w.KeyPress(Avalonia.Input.Key.Space,Avalonia.Input.RawInputModifiers.Control,Avalonia.Input.PhysicalKey.Space,null);Pump();
    Expect("Ctrl+Space without files gives a hint",!WheelOpen(w)&&w.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("Choose or drop files first")==true));
    if(fixtures is not null)w.UseFiles(Fixtures("Photo.png"));Pump();
    w.KeyPress(Avalonia.Input.Key.Space,Avalonia.Input.RawInputModifiers.Control,Avalonia.Input.PhysicalKey.Space,null);Pump();
    Expect("Ctrl+Space opens the wheel",WheelOpen(w));
    w.KeyPress(Avalonia.Input.Key.Escape,Avalonia.Input.RawInputModifiers.None,Avalonia.Input.PhysicalKey.Escape,null);Pump();
    Expect("Escape closes the wheel",!WheelOpen(w));
    w.KeyPress(Avalonia.Input.Key.D2,Avalonia.Input.RawInputModifiers.Control,Avalonia.Input.PhysicalKey.Digit2,null);Pump();
    Expect("Ctrl+2 opens Library",w.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Your Library"));
    w.KeyPress(Avalonia.Input.Key.D1,Avalonia.Input.RawInputModifiers.Control,Avalonia.Input.PhysicalKey.Digit1,null);Pump();
    Expect("Ctrl+1 returns Home",w.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Let’s make something of it."));
    w.KeyPress(Avalonia.Input.Key.Space,Avalonia.Input.RawInputModifiers.Control,Avalonia.Input.PhysicalKey.Space,null);Pump();
    var wheel=w.GetVisualDescendants().OfType<WheelOverlay>().First();
    string Highlighted()=>wheel.Wheel.SelectedItem?.Title??"";
    var first=Highlighted();w.KeyPress(Avalonia.Input.Key.Right,Avalonia.Input.RawInputModifiers.None,Avalonia.Input.PhysicalKey.ArrowRight,null);Pump();
    Expect($"Arrow moves the wheel ({first} → {Highlighted()})",first.Length>0&&Highlighted()!=first);
    w.KeyPress(Avalonia.Input.Key.Tab,Avalonia.Input.RawInputModifiers.None,Avalonia.Input.PhysicalKey.Tab,null);Pump();
    Expect("Tab switches to Convert",wheel.Wheel.Items.Any(i=>i.Title=="JPG"));
    w.KeyPress(Avalonia.Input.Key.Escape,Avalonia.Input.RawInputModifiers.None,Avalonia.Input.PhysicalKey.Escape,null);Pump();
    Expect("Escape closes it again",!WheelOpen(w));
    foreach(var (modifier,expected) in new[]{(Avalonia.Input.RawInputModifiers.Shift,"JPG"),(Avalonia.Input.RawInputModifiers.Alt,"Terminal"),(Avalonia.Input.RawInputModifiers.Control,"Edit Photo")})
    {
        var data=new Avalonia.Input.DataTransfer();var item=w.StorageProvider.TryGetFileFromPathAsync(new Uri(Fixtures("Photo.png")[0])).GetAwaiter().GetResult();
        if(item is not null)data.Add(Avalonia.Input.DataTransferItem.CreateFile(item));
        w.DragDrop(new Point(300,300),Avalonia.Input.Raw.RawDragEventType.DragEnter,data,Avalonia.Input.DragDropEffects.Copy,modifier);
        w.DragDrop(new Point(300,300),Avalonia.Input.Raw.RawDragEventType.Drop,data,Avalonia.Input.DragDropEffects.Copy,modifier);Pump();
        var opened=w.GetVisualDescendants().OfType<WheelOverlay>().FirstOrDefault()?.Wheel.Items.Any(i=>i.Title==expected)==true;
        Expect($"Drop with {modifier} opens the wheel at {expected}",item is not null&&opened);
        w.KeyPress(Avalonia.Input.Key.Escape,Avalonia.Input.RawInputModifiers.None,Avalonia.Input.PhysicalKey.Escape,null);Pump();
    }
    // Gesture layer: the wheel opens under the drag; the shortcut again steps Tools → Convert → File actions.
    {
        var target=Fixtures("Photo.png")[0];w.UseFiles([]);
        Avalonia.Input.DataTransfer Drag(){var d=new Avalonia.Input.DataTransfer();var f=w.StorageProvider.TryGetFileFromPathAsync(new Uri(target)).GetAwaiter().GetResult();if(f is not null)d.Add(Avalonia.Input.DataTransferItem.CreateFile(f));return d;}
        void Over(GestureWindow l,Avalonia.Input.DataTransfer d,Point p,Avalonia.Input.Raw.RawDragEventType kind=Avalonia.Input.Raw.RawDragEventType.DragOver)=>l.DragDrop(p,kind,d,Avalonia.Input.DragDropEffects.Copy,Avalonia.Input.RawInputModifiers.None);
        w.StartGesture(null);Pump();var layer=w.Gestures.First();var data=Drag();
        Over(layer,data,new Point(600,400),Avalonia.Input.Raw.RawDragEventType.DragEnter);Pump();
        Expect($"The gesture opens the Tools wheel under the drag ({layer.Mode}, no slice chosen)",layer.Mode=="tools"&&layer.Wheel.IsVisible&&layer.Wheel.Items.Any(i=>i.Title=="Edit Photo")&&layer.Wheel.SelectedItem is null);
        Save(layer,"14-gesture-tools");
        w.PressShortcut("gesture");Pump();
        Expect($"The shortcut again opens Convert ({layer.Mode})",layer.Mode=="convert"&&layer.Wheel.Items.Any(i=>i.Title=="JPG"));
        Save(layer,"15-gesture-convert");
        w.PressShortcut("gesture");Pump();
        var petals=layer.Wheel.Items;
        Expect($"And again File actions ({string.Join(", ",petals.Select(i=>i.Title))})",layer.Mode=="actions"&&petals.Any(i=>i.Title=="Copy Path"));
        var copyIndex=petals.ToList().FindIndex(i=>i.Title=="Copy Path");var at=layer.Wheel.TranslatePoint(layer.Wheel.PetalCenter(copyIndex),layer)!.Value;
        Over(layer,data,at);Pump();
        Expect("Hovering a petal highlights it",layer.Wheel.SelectedItem?.Title=="Copy Path");
        Over(layer,data,at,Avalonia.Input.Raw.RawDragEventType.Drop);for(var n=0;n<20;n++){Pump();Thread.Sleep(10);}
        Expect("Dropping on Copy Path runs it and closes the layer",!w.Gestures.Any(l=>l.IsVisible)&&w.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="File paths copied."));
        // Pressed twice before the drag arrives: the wheel opens on Convert straight away.
        w.StartGesture(null);Pump();layer=w.Gestures.First();data=Drag();w.PressShortcut("gesture");Pump();
        Over(layer,data,new Point(600,400),Avalonia.Input.Raw.RawDragEventType.DragEnter);Pump();
        Expect($"A second press before the drag arrives opens Convert ({layer.Mode})",layer.Mode=="convert");
        // A direct wheel shortcut while the layer is up jumps to that wheel in place.
        w.PressShortcut("actions");Pump();
        Expect($"Meta+Alt+A over an open wheel switches to File actions ({layer.Mode})",layer.Mode=="actions"&&w.Gestures.Count(g=>g.IsVisible)>=1);
        w.CloseGestures();Pump();
        // No drag: the wheel opens for selected files where the pointer is, to click.
        {
            w.StartGesture(null);Pump();layer=w.Gestures.First();layer.UseSelection([target],null);Pump();
            Expect($"Selected files without a drag get a clickable wheel ({layer.Mode})",layer.Mode=="tools"&&layer.Wheel.IsVisible&&!layer.HasDrag&&layer.Wheel.Items.Any(i=>i.Title=="Edit Photo"));
            Save(layer,"14b-gesture-selection");w.CloseGestures();Pump();
        }
        // A cancelled drag (Esc) leaves the layer for good and closes it; a quick return keeps it.
        {
            w.StartGesture(null);Pump();layer=w.Gestures.First();data=Drag();
            layer.DragDrop(new Point(600,400),Avalonia.Input.Raw.RawDragEventType.DragEnter,data,Avalonia.Input.DragDropEffects.Copy,Avalonia.Input.RawInputModifiers.None);
            layer.DragDrop(new Point(550,400),Avalonia.Input.Raw.RawDragEventType.DragOver,data,Avalonia.Input.DragDropEffects.Copy,Avalonia.Input.RawInputModifiers.None);Pump();
            // (Headless timers do not run, so the wait is checked as pending and then finished by hand.)
            layer.DragLeft();var pending=layer.LeavePending;
            layer.DragDrop(new Point(540,400),Avalonia.Input.Raw.RawDragEventType.DragOver,data,Avalonia.Input.DragDropEffects.Copy,Avalonia.Input.RawInputModifiers.None);Pump();
            var kept=pending&&!layer.LeavePending&&w.Gestures.Any(l=>l.IsVisible);
            layer.DragLeft();if(layer.LeavePending)layer.LeaveNow();Pump();
            Expect($"A drag that leaves for good closes the layer, a quick return keeps it (kept {kept}, open after {w.Gestures.Any(l=>l.IsVisible)})",kept&&!w.Gestures.Any(l=>l.IsVisible));
            w.CloseGestures();Pump();
        }
        Expect("A function key alone can be the gesture shortcut, a letter alone cannot",KeyCombo.FromAvalonia(Avalonia.Input.Key.F8,Avalonia.Input.KeyModifiers.None)=="F8"&&KeyCombo.FromAvalonia(Avalonia.Input.Key.G,Avalonia.Input.KeyModifiers.None) is null&&KeyCombo.ToQt("F8")==0x01000037);
    }
    // A web link dragged from a browser gets the download wheel.
    {
        w.StartGesture(null);Pump();var layer=w.Gestures.First();var web=new Avalonia.Input.DataTransfer();web.Add(Avalonia.Input.DataTransferItem.CreateText("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
        layer.DragDrop(new Point(600,400),Avalonia.Input.Raw.RawDragEventType.DragEnter,web,Avalonia.Input.DragDropEffects.Copy,Avalonia.Input.RawInputModifiers.None);
        layer.DragDrop(new Point(760,400),Avalonia.Input.Raw.RawDragEventType.DragOver,web,Avalonia.Input.DragDropEffects.Copy,Avalonia.Input.RawInputModifiers.None);Pump();
        Expect($"A dragged link opens the download wheel ({string.Join(", ",layer.Wheel.Items.Select(i=>i.Title))})",layer.Wheel.Items.Any(i=>i.Id=="web:video")&&layer.Wheel.Items.Any(i=>i.Id=="web:copy"));
        Save(layer,"16-gesture-web");w.CloseGestures();Pump();
    }
    // A copied image (a screenshot) gets the Tools wheel through the clipboard shortcut.
    {
        var clip=new Avalonia.Input.DataTransfer();var item=new Avalonia.Input.DataTransferItem();item.Set(Nashi.Linux.ClipboardFiles.Png,File.ReadAllBytes(Fixtures("Photo.png")[0]));clip.Add(item);
        w.Clipboard!.SetDataAsync(clip).GetAwaiter().GetResult();Pump();
        w.PressShortcut("clipboard");for(var n=0;n<60&&w.Gestures.Count==0;n++){Pump();Thread.Sleep(20);}for(var n=0;n<10;n++)Pump();
        var petals=w.Gestures.FirstOrDefault()?.Wheel.Items??[];
        Expect($"A copied image opens the Tools wheel ({string.Join(", ",petals.Take(4).Select(i=>i.Title))}…)",petals.Any(i=>i.Id=="editImage"));
        w.CloseGestures();Pump();
    }
    // A watched folder converts new files on its own.
    {
        var watched=Path.Combine(output,"watched");if(Directory.Exists(watched))Directory.Delete(watched,true);Directory.CreateDirectory(watched);
        w.SaveLinuxSettings(w.LinuxSettings with{Watches=[new Nashi.Linux.FolderRule(watched,["png"],"jpg")]});
        File.Copy(Fixtures("Photo.png")[0],Path.Combine(watched,"Incoming.png"));
        var made=Path.Combine(watched,"Incoming.jpg");for(var n=0;n<300&&!File.Exists(made);n++){Pump();Thread.Sleep(50);}
        Expect($"A watched folder turns a new PNG into JPG ({w.WatchedFolders} watched)",File.Exists(made)&&!File.Exists(Path.Combine(watched,"Incoming 2.jpg")));
        w.SaveLinuxSettings(w.LinuxSettings with{Watches=[]});
    }
    // Linux extras: Copy Text and Rename appear for images; Rename plans, checks clashes and applies.
    {
        var extras=w.ChoicesFor(Fixtures("Photo.png"),"tools");
        Expect("Copy Text and Rename are on the Tools wheel",extras.Any(c=>c.Id=="copyText")&&extras.Any(c=>c.Id=="rename"));
        var folder=Path.Combine(output,"rename");if(Directory.Exists(folder))Directory.Delete(folder,true);Directory.CreateDirectory(folder);
        var a=Path.Combine(folder,"IMG_1.png");var b=Path.Combine(folder,"IMG_2.png");File.Copy(Fixtures("Photo.png")[0],a);File.Copy(Fixtures("Photo.png")[0],b);
        var rename=new RenameWindow([a,b]);var clash=!rename.Try("Same");var ok=rename.Try("Trip {n}");var done=RenameWindow.Apply(rename.Plan);
        Expect($"Rename catches clashes and renames ({string.Join(", ",done.Select(Path.GetFileName))})",clash&&ok&&File.Exists(Path.Combine(folder,"Trip 01.png"))&&File.Exists(Path.Combine(folder,"Trip 02.png"))&&!File.Exists(a));
        rename.Close();
        // A name taken after the preview: the rename fails and both files keep their original names.
        var c=Path.Combine(folder,"A.png");var d=Path.Combine(folder,"B.png");File.Copy(Fixtures("Photo.png")[0],c);File.Copy(Fixtures("Photo.png")[0],d);
        var late=new RenameWindow([c,d]);late.Try("Late {n}");File.WriteAllText(Path.Combine(folder,"Late 02.png"),"taken");
        var failed=false;try{RenameWindow.Apply(late.Plan);}catch(IOException){failed=true;}late.Close();
        Expect("A failed rename puts every file back under its own name",failed&&File.Exists(c)&&File.Exists(d)&&!Directory.GetFiles(folder,".nashi-rename-*").Any()&&!File.Exists(Path.Combine(folder,"Late 01.png")));
    }
    // Set as Wallpaper: offered for one image where the desktop supports it; the copy is converted when
    // the format is unusual and only the last five are kept. (The wallpaper itself is not changed here.)
    {
        var offered=w.ChoicesFor(Fixtures("Photo.png"),"tools").Any(c=>c.Id=="wallpaper");
        var tiff=Path.Combine(output,"Scan.tif");using(var image=new ImageMagick.MagickImage(Fixtures("Photo.png")[0]))image.Write(tiff,ImageMagick.MagickFormat.Tiff);
        var copies=Enumerable.Range(0,6).Select(_=>Wallpaper.Prepare(Fixtures("Photo.png")[0])).ToList();var converted=Wallpaper.Prepare(tiff);
        var kept=Directory.GetFiles(Wallpaper.Folder).Length;
        Expect($"Wallpaper copies ({(offered?"offered":"not offered: no plasma-apply-wallpaperimage or GNOME")}, {kept} kept, {Path.GetExtension(converted)})",(offered||!Wallpaper.Supported)&&converted.EndsWith(".png")&&File.Exists(converted)&&kept==5&&Path.GetDirectoryName(converted)==Wallpaper.Folder);
    }
    // Single instance without XDG_RUNTIME_DIR: a folder in the temp dir is used only when it is private.
    {
        var runtime=Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");var tmp=Environment.GetEnvironmentVariable("TMPDIR");
        // A short folder: a Unix socket path must stay under 108 bytes.
        var temp=Directory.CreateTempSubdirectory("nashi-test-").FullName;
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR",null);Environment.SetEnvironmentVariable("TMPDIR",temp);
        var folder=Path.Combine(Path.GetTempPath(),"nashi-"+Environment.UserName);
        try
        {
            Directory.CreateDirectory(folder);File.SetUnixFileMode(folder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|UnixFileMode.OtherRead|UnixFileMode.OtherExecute);
            SingleInstance.Listen();var refused=!File.Exists(Path.Combine(folder,"nashi.sock"))&&!SingleInstance.HandOff([]);SingleInstance.Stop();
            Directory.Delete(folder,true);
            SingleInstance.Listen();var made=File.GetUnixFileMode(folder);var works=File.Exists(Path.Combine(folder,"nashi.sock"))&&SingleInstance.HandOff([]);SingleInstance.Stop();
            Expect($"The fallback socket folder is used only when private ({made})",refused&&works&&made==(UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute));
        }
        finally{Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR",runtime);Environment.SetEnvironmentVariable("TMPDIR",tmp);Directory.Delete(temp,true);}
    }
    // PDF thumbnails render (PDFium refused the 24 DPI they used, so PDFs showed a badge).
    {
        var pdfThumb=Thumbnails.GetAsync(Directory.GetFiles(Path.GetDirectoryName(fixtures!)!,"Images.pdf",SearchOption.AllDirectories)[0],160).GetAwaiter().GetResult();
        Expect($"A PDF gets a page thumbnail ({pdfThumb?.PixelSize})",pdfThumb is not null);
    }
    // The last tool comes back first on the Tools wheel as "Again".
    w.LastChoice=new ToolChoice("convert:jpg","Convert to JPG","↗");
    var again=w.ChoicesFor(Fixtures("Photo.png"),"tools");
    Expect($"Again petal leads the Tools wheel ({again[0].Title})",again[0].Symbol=="↻"&&again[0].Id=="convert:jpg");
    w.LastChoice=null;
    // Focus in the link box must not swallow the wheel shortcut.
    var linkBox=w.GetVisualDescendants().OfType<TextBox>().First();linkBox.Focus();Pump();
    w.KeyPress(Avalonia.Input.Key.Space,Avalonia.Input.RawInputModifiers.Control,Avalonia.Input.PhysicalKey.Space,null);Pump();
    Expect("Ctrl+Space works while typing a link",WheelOpen(w));
    w.Close();
    if(keyFailures>0){Console.WriteLine($"{keyFailures} key check(s) failed");return 1;}
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
// Fit for Discord: a video over 10 MB gets the petal, a small one does not, and the result fits.
if(hasMedia)
{
    var big=Path.Combine(work,"Big Clip.mp4");
    using(var make=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg",["-v","error","-y","-f","lavfi","-i","testsrc2=size=1280x720:rate=30","-t","8","-vf","noise=alls=40:allf=t","-c:v","libx264","-preset","ultrafast","-b:v","14M","-maxrate","14M","-bufsize","28M",big]){RedirectStandardError=true})!)make.WaitForExit();
    var offered=MainWindow.ExtraTools([big]).Any(t=>t.Id=="fit:discord")&&!MainWindow.ExtraTools([flowClip]).Any(t=>t.Id.StartsWith("fit:"));
    var window=new MainWindow([]);window.Show();window.UseFiles([big]);var before=window.library.Entries.Count;
    var run=window.EditAsync(new ToolChoice("fit:discord","Fit for Discord","⇩"));for(var n=0;n<12000&&!run.IsCompleted;n++){Pump();Thread.Sleep(10);}
    var made=window.library.Entries.Count>before?window.library.Entries[0].AvailablePaths:[];
    var size=made.Length>0?new FileInfo(made[0]).Length:0;
    var ok=offered&&run.IsCompletedSuccessfully&&size>0&&size<=ToolEditor.SizePresets[0].Kilobytes*1024L;if(!ok)failures++;
    Console.WriteLine($"  {(ok?"pass":"FAIL")}  flow fit for Discord · {new FileInfo(big).Length/1048576.0:0.0} MB → {size/1048576.0:0.0} MB {(offered?"":"(petal not offered as expected)")} {(run.IsFaulted?run.Exception?.GetBaseException().Message:"")}");
    window.Close();
}
Console.WriteLine(failures==0?"flows passed":$"{failures} flow(s) failed");
return failures==0?0:1;
