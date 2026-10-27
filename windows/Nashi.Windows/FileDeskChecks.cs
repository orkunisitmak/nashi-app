using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;

/// Native-input proof for the in-app desk: cards move, a modifier opens the
/// matching wheel at the pointer, releasing on a slice opens that tool, and
/// Escape puts the cards back.
internal static class FileDeskChecks
{
    private static IEnumerable<DependencyObject> Children(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Children(child))yield return nested;}}
    /// Renders only Nashi's own window, never the rest of the desktop.
    private static void Render(Window window,string file)
    {
        var scale=VisualTreeHelper.GetDpi(window);var content=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)(content.ActualWidth*scale.DpiScaleX),(int)(content.ActualHeight*scale.DpiScaleY),scale.PixelsPerInchX,scale.PixelsPerInchY,PixelFormats.Pbgra32);
        var frame=new DrawingVisual();using(var g=frame.RenderOpen()){g.DrawRectangle(window.Background??Brushes.Transparent,null,new(0,0,content.ActualWidth,content.ActualHeight));g.DrawRectangle(new VisualBrush(content),null,new(0,0,content.ActualWidth,content.ActualHeight));}
        bitmap.Render(frame);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(file);encoder.Save(stream);
    }
    internal static void Run(string root,Action<bool,string> check)
    {
        root=Path.GetFullPath(root);Directory.CreateDirectory(root);var paths=new[]{"Desk one.png","Desk two.png"}.Select(name=>Path.Combine(root,name)).ToArray();
        foreach(var path in paths){var bitmap=BitmapSource.Create(64,48,96,96,PixelFormats.Bgr32,null,Enumerable.Repeat((byte)0x7a,64*48*4).ToArray(),64*4);using var stream=File.Create(path);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(stream);}
        var library=new LibraryStore(Path.Combine(root,"Library"));var window=new MainWindow(paths,library,checkForUpdates:false,offerToolSetup:false);window.Show();window.Activate();DesktopInput.Pump(600);
        var desk=Children(window).OfType<FileDesk>().Single();var cards=Children(desk).OfType<Border>().Where(b=>b.Tag is string).ToArray();
        check(desk.IsVisible&&cards.Length==2,"Desk did not show one card per file");
        var card=cards[0];Point Spot(Border b)=>new(Canvas.GetLeft(b),Canvas.GetTop(b));var start=Spot(card);
        void Press(Border b){DesktopInput.MovePointer(b.PointToScreen(new(30,30)));DesktopInput.Pump(80);DesktopInput.HoldMouse(true);DesktopInput.Pump(80);}
        void Travel(Border b,Vector by){var from=b.PointToScreen(new(30,30));for(var n=1;n<=6;n++){DesktopInput.MovePointer(from+by*n/6);DesktopInput.Pump(40);}}
        WheelWindow? Wheel()=>Application.Current.Windows.OfType<WheelWindow>().FirstOrDefault(w=>w.IsVisible);
        // Plain drag moves the card and keeps it there.
        Press(card);Travel(card,new(140,10));DesktopInput.HoldMouse(false);DesktopInput.Pump(300);
        check(Spot(card).X>start.X+60,"Dragging a card did not move it on the desk");check(Wheel() is null,"A plain drag opened a wheel");
        // Shift during the drag opens the convert wheel; releasing on a slice opens that tool.
        var moved=Spot(card);string? opened=null;var closer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};closer.Tick+=(_,_)=>{foreach(var editor in Application.Current.Windows.OfType<ToolEditor>().Where(w=>w.IsVisible)){opened=editor.Title;editor.Close();}};closer.Start();
        Press(card);Travel(card,new(20,0));DesktopInput.Hold(Key.LeftShift,true);DesktopInput.Pump(80);Travel(card,new(4,0));var wheel=Wheel();
        check(wheel is not null&&Children(wheel).OfType<TextBlock>().Any(t=>t.Text is "Convert" or "Dönüştür"),"Shift did not open the convert wheel during a desk drag: "+(wheel is null?"no wheel":string.Join("|",Children(wheel).OfType<TextBlock>().Select(t=>t.Text))));
        if(wheel is not null)
        {
            Render(wheel,Path.Combine(root,"file-desk-wheel.png"));
            // Slices sit between radius 108 and 250 of the 540-unit wheel.
            var slice=wheel.PointToScreen(new(wheel.ActualWidth/2,wheel.ActualHeight/2-wheel.ActualWidth*180/540));DesktopInput.MovePointer(slice+new Vector(0,3));DesktopInput.Pump(120);DesktopInput.MovePointer(slice);DesktopInput.Pump(120);check(wheel.SelectedIndex>=0,"Pointer over a slice did not highlight it");
        }
        DesktopInput.Hold(Key.LeftShift,false);DesktopInput.HoldMouse(false);for(var n=0;n<20&&opened is null;n++)DesktopInput.Pump(100);closer.Stop();
        check(opened is not null,"Releasing on a convert slice did not open the converter");check(Wheel() is null,"Wheel stayed open after release");
        check((Spot(card)-moved).Length<2,"Card did not return home after using the wheel");
        // Ctrl opens Tools; Escape cancels and returns the card.
        Press(card);Travel(card,new(24,0));DesktopInput.Hold(Key.LeftCtrl,true);DesktopInput.Pump(150);wheel=Wheel();
        check(wheel is not null&&Children(wheel).OfType<TextBlock>().Any(t=>t.Text is "Tools" or "Araçlar"),"Ctrl did not open the tools wheel during a desk drag: "+(wheel is null?"no wheel":string.Join("|",Children(wheel).OfType<TextBlock>().Select(t=>t.Text))));
        DesktopInput.Hold(Key.LeftCtrl,false);DesktopInput.Key(window,Key.Escape);DesktopInput.HoldMouse(false);DesktopInput.Pump(300);
        check(Wheel() is null,"Escape did not close the desk wheel");check((Spot(card)-moved).Length<2,"Escape did not return the card");
        // With a card selected, a lone Shift tap opens the convert wheel; another key in between does not.
        card.Focus();DesktopInput.Pump(150);DesktopInput.Hold(Key.LeftShift,true);DesktopInput.Pump(60);DesktopInput.Hold(Key.LeftShift,false);DesktopInput.Pump(300);wheel=Wheel();
        check(wheel is not null&&Children(wheel).OfType<TextBlock>().Any(t=>t.Text is "Convert" or "Dönüştür"),"Tapping Shift on a selected card did not open the convert wheel");
        if(wheel is not null)
        {
            // The tapped wheel waits for the pointer: move onto a slice and click it like a normal button.
            Render(wheel,Path.Combine(root,"file-desk-tap-wheel.png"));opened=null;closer.Start();
            var slice=wheel.PointToScreen(new(wheel.ActualWidth/2,wheel.ActualHeight/2-wheel.ActualWidth*180/540));DesktopInput.MovePointer(slice+new Vector(0,4));DesktopInput.Pump(120);DesktopInput.MovePointer(slice);DesktopInput.Pump(150);
            DesktopInput.HoldMouse(true);DesktopInput.Pump(80);DesktopInput.HoldMouse(false);for(var n=0;n<20&&opened is null;n++)DesktopInput.Pump(100);closer.Stop();
            check(opened is not null,"Clicking a slice of the tapped wheel did not open the tool");if(Wheel() is {} left){left.Close();DesktopInput.Pump(150);}
        }
        window.Activate();card.Focus();DesktopInput.Key(window,Key.D1,control:true);DesktopInput.Pump(200);check(Wheel() is null,"Ctrl+1 opened the tools wheel as if Ctrl had been tapped");
        // Adding more files appends instead of replacing, and different kinds share the desk.
        var note=Path.Combine(root,"Desk note.txt");File.WriteAllText(note,"desk");window.AcceptLaunch([note]);DesktopInput.Pump(300);
        check(window.SelectedFiles.Count==3&&window.SelectedFiles.Contains(paths[0])&&window.SelectedFiles.Contains(note),"Adding a file replaced the desk instead of joining it: "+string.Join(" | ",window.SelectedFiles.Select(Path.GetFileName)));
        // Duplicate makes a real copy beside the original and puts its card on the desk.
        DesktopInput.MovePointer(card.PointToScreen(new(40,60)));DesktopInput.Pump(250);var duplicateButton=Children(card).OfType<Button>().FirstOrDefault(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Duplicate");
        check(duplicateButton is {IsVisible:true},"Hovering a card did not show its Duplicate button");
        if(duplicateButton is not null){duplicateButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));DesktopInput.Pump(300);}
        var copies=Directory.GetFiles(root,"Desk one *.png");check(copies.Length==1&&window.SelectedFiles.Contains(copies[0]),"Duplicate did not create and add a copy: "+string.Join(" | ",Directory.GetFiles(root).Select(Path.GetFileName))+" :: "+string.Join(" | ",window.SelectedFiles.Select(Path.GetFileName)));
        Render(window,Path.Combine(root,"file-desk.png"));
        // The info panel uses the text font, not the icon button's icon font it is placed against.
        var info=Children(window).OfType<Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Shortcuts");DesktopInput.MovePointer(info.PointToScreen(new(17,17)));DesktopInput.Pump(500);
        var popup=PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>().Select(s=>s.RootVisual).Where(v=>v is not null&&v is not Window).SelectMany(v=>Children(v!)).OfType<TextBlock>().Where(t=>t.Text=="Anywhere"||t.Text=="Her yerde").FirstOrDefault();
        check(popup is not null&&!popup.FontFamily.Source.Contains("Icons",StringComparison.OrdinalIgnoreCase),"Info panel did not open or uses the icon font: "+popup?.FontFamily.Source);
        DesktopInput.MovePointer(card.PointToScreen(new(40,60)));DesktopInput.Pump(400);
        // A video gets its cover on the card and a play button that opens the previewer.
        var tools=new Toolchain(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools","v1"));
        if(tools.Ready)
        {
            var video=Path.Combine(root,"Desk clip.mp4");ProcessRunner.RunAsync(tools.Executable("ffmpeg"),["-v","error","-nostdin","-f","lavfi","-i","testsrc=size=320x180:rate=10","-t","3","-pix_fmt","yuv420p","-y",video]).GetAwaiter().GetResult();
            window.AcceptLaunch([video]);Border? clip=null;for(var n=0;n<80;n++){DesktopInput.Pump(100);clip=Children(desk).OfType<Border>().FirstOrDefault(b=>b.Tag as string==video);if(clip is not null&&Children(clip).OfType<Image>().Any(i=>i.Source is BitmapSource{PixelWidth:>40}))break;}
            check(clip is not null&&Children(clip).OfType<Image>().Any(i=>i.Source is BitmapSource{PixelWidth:>40}),"Video card did not show a cover frame");
            var play=clip is null?null:Children(clip).OfType<Button>().FirstOrDefault(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Play");check(play is not null,"Video card has no play button");
            if(play is not null)
            {
                Render(window,Path.Combine(root,"file-desk-video.png"));play.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));DesktopInput.Pump(600);
                var preview=Application.Current.Windows.OfType<FilePreviewWindow>().FirstOrDefault(w=>w.IsVisible);check(preview is not null&&preview.CurrentPath==video,"Play did not open the video in the previewer");preview?.Close();DesktopInput.Pump(200);
            }
        }
        // Popups (menus) live in their own HwndSources; look there for what is open.
        IEnumerable<DependencyObject> Popups()=>PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>().Select(s=>s.RootVisual).Where(v=>v is not null&&v is not Window).SelectMany(v=>Children(v!));
        void CloseMenus(){foreach(var open in Application.Current.Windows.OfType<Window>().SelectMany(w=>Children(w)).OfType<FrameworkElement>().Select(e=>e.ContextMenu).Concat(Popups().OfType<ContextMenu>()).Where(m=>m is {IsOpen:true}))open!.IsOpen=false;DesktopInput.Pump(150);}
        // One real click on a tile opens its menu, even right after a card had focus.
        window.Activate();cards=Children(desk).OfType<Border>().Where(b=>b.Tag is string).ToArray();var first=cards.First(b=>File.Exists((string)b.Tag)&&FeatureCatalog.IsImage((string)b.Tag));
        DesktopInput.MovePointer(first.PointToScreen(new(60,60)));DesktopInput.Pump(120);DesktopInput.HoldMouse(true);DesktopInput.Pump(60);DesktopInput.HoldMouse(false);DesktopInput.Pump(400);
        var tile=Children(window).OfType<Button>().FirstOrDefault(b=>b.IsVisible&&Children(b).OfType<TextBlock>().Any(t=>t.Text is "Convert" or "Dönüştür"));check(tile is not null,"Convert tile is missing");
        if(tile is not null)
        {
            tile.BringIntoView();DesktopInput.Pump(200);DesktopInput.MovePointer(tile.PointToScreen(new(tile.ActualWidth/2,tile.ActualHeight/2)));DesktopInput.Pump(120);DesktopInput.HoldMouse(true);DesktopInput.Pump(60);DesktopInput.HoldMouse(false);DesktopInput.Pump(400);
            check(Popups().OfType<MenuItem>().Any(m=>m.IsVisible),"The first click on the Convert tile did not open its menu");CloseMenus();
        }
        // Dragging on an empty spot draws a selection rectangle over the cards it crosses.
        window.Activate();var canvas=Children(desk).OfType<Canvas>().First(c=>c.Children.OfType<Border>().Any());desk.BringIntoView();DesktopInput.Pump(200);
        var free=new Point(canvas.ActualWidth-30,canvas.ActualHeight-30);DesktopInput.MovePointer(canvas.PointToScreen(free));DesktopInput.Pump(100);DesktopInput.HoldMouse(true);DesktopInput.Pump(80);
        for(var n=1;n<=10;n++){DesktopInput.MovePointer(canvas.PointToScreen(new(free.X-(free.X-4)*n/10,free.Y-(free.Y-4)*n/10)));DesktopInput.Pump(40);}
        DesktopInput.HoldMouse(false);DesktopInput.Pump(250);check(desk.Selection.Count==window.SelectedFiles.Count&&desk.Selection.Count>=3,"Dragging a rectangle did not select the cards under it: "+desk.Selection.Count);
        // Right-click on a card: the Windows-style menu with the three wheels.
        var target=cards.First(b=>FeatureCatalog.IsImage((string)b.Tag));DesktopInput.MovePointer(target.PointToScreen(new(60,90)));DesktopInput.Pump(120);
        DesktopInput.Click(target.PointToScreen(new(60,90)),right:true);DesktopInput.Pump(400);
        var items=Popups().OfType<MenuItem>().Where(m=>m.IsVisible).Select(m=>System.Windows.Automation.AutomationProperties.GetName(m)).ToArray();
        check(items.Contains("Convert…")&&items.Contains("Tools…")&&items.Contains("File actions…")&&items.Contains("One by one"),"Right-click menu is missing the wheels: "+string.Join("|",items));
        if(Popups().OfType<ContextMenu>().FirstOrDefault() is {} shown)Render(Window.GetWindow(window)!,Path.Combine(root,"file-desk-menu-window.png"));
        CloseMenus();
        // Two jobs queue: the second waits its turn in the notifications, both finish in order, and their files land on the desk.
        var order=new List<string>();Func<string,IProgress<OperationProgress>,CancellationToken,Task<IReadOnlyList<string>>> Job(string name)=>async(folder,progress,token)=>{for(var n=1;n<=5;n++){progress.Report(new(n/5.0,"Working…"));await Task.Delay(120,token);}var file=Path.Combine(root,name);File.WriteAllText(file,name);order.Add(name);return [file];};
        var one=window.QueueForCheck(Job("Queued one.txt"),"Queued one");var two=window.QueueForCheck(Job("Queued two.txt"),"Queued two");DesktopInput.Pump(150);
        var waiting=Notifications.Shared.Cards.FirstOrDefault(c=>c.Heading=="Queued two");check(waiting is not null&&Children(waiting).OfType<TextBlock>().Any(t=>t.Text.Contains("next in line")||t.Text.Contains("sıradaki")),"The second job did not wait in line in the notifications");
        for(var n=0;n<60&&!(one.IsCompleted&&two.IsCompleted);n++)DesktopInput.Pump(100);
        check(order.SequenceEqual(["Queued one.txt","Queued two.txt"]),"Queued jobs did not run one after another: "+string.Join(",",order));
        check(window.SelectedFiles.Any(f=>f.EndsWith("Queued one.txt"))&&window.SelectedFiles.Any(f=>f.EndsWith("Queued two.txt")),"Finished jobs did not put their files on the desk");
        Render(window,Path.Combine(root,"notifications.png"));
        // The Home desk is in Library at all times; Clear empties it and keeps that Library entry.
        var kept=new LibraryStore(library.DirectoryPath).Entries.FirstOrDefault(e=>e.Action=="Desk");
        check(kept is not null&&window.SelectedFiles.All(f=>kept.Paths.Contains(f,StringComparer.OrdinalIgnoreCase)),"The desk was not saved to Library as it changed");
        var clear=Children(window).OfType<Button>().FirstOrDefault(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Clear");check(clear is not null,"Clear button missing");
        var before=window.SelectedFiles.ToArray();clear?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));DesktopInput.Pump(300);
        check(window.SelectedFiles.Count==0&&new LibraryStore(library.DirectoryPath).Entries.Any(e=>e.Action=="Desk"&&before.All(f=>e.Paths.Contains(f,StringComparer.OrdinalIgnoreCase))),"Clear did not empty the desk into Library");
        // Notifications live inside Nashi's window, in its top-right corner.
        var toast=Notifications.Shared;var corner=toast.IsVisible?toast.TranslatePoint(new(toast.ActualWidth,0),(UIElement)window.Content):new Point(-1,-1);
        check(toast.IsVisible&&Window.GetWindow(toast)==window&&corner.X>((FrameworkElement)window.Content).ActualWidth-40&&corner.Y<60,"Clearing did not show a notification in the window's top-right corner");
        // Library shows every kept file on one desk.
        window.ShowLibrary(true);DesktopInput.Pump(400);check(before.All(f=>window.WorkspaceFiles.Contains(f,StringComparer.OrdinalIgnoreCase)),"Library's All files desk is missing the cleared files");
        Render(window,Path.Combine(root,"library-desk.png"));window.ShowLibrary(false);window.AcceptLaunch(before);DesktopInput.Pump(300);
        // The video bar spans the previewer.
        if(before.FirstOrDefault(FeatureCatalog.IsVideo) is {} clipPath)
        {
            var player=new FilePreviewWindow(window,[clipPath]);player.Show();DesktopInput.Pump(800);var scrub=Children(player).OfType<Slider>().FirstOrDefault();
            check(scrub is not null&&scrub.ActualWidth>player.ActualWidth*.5,"The video timeline does not stretch across the previewer: "+scrub?.ActualWidth);Render(player,Path.Combine(root,"preview-video.png"));player.Close();DesktopInput.Pump(200);
        }
        cards=Children(desk).OfType<Border>().Where(b=>b.Tag is string).ToArray();card=cards.First(b=>(string)b.Tag==paths[0]||FeatureCatalog.IsImage((string)b.Tag));paths[0]=(string)card.Tag;
        // Delete removes the focused card from the selection, not from disk.
        card.Focus();DesktopInput.Key(window,Key.Delete);check(!window.SelectedFiles.Contains(paths[0])&&File.Exists(paths[0]),"Delete on a desk card did not just remove it from the selection");
        window.Close();
    }
}
