using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using Nashi.Core;
namespace Nashi.Windows;
internal static class GlobalGestureChecks
{
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr FindWindow(string? type,string? name);
    private delegate bool WindowVisitor(IntPtr window,IntPtr parameter);
    [DllImport("user32.dll")]private static extern bool EnumWindows(WindowVisitor visit,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(IntPtr window,System.Text.StringBuilder text,int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(IntPtr window,System.Text.StringBuilder text,int count);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr handle,out NativeRect rect);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr handle,uint message,IntPtr w,IntPtr l);
    internal static void Run(Window owner,string root,string original,Action<bool,string> check)
    {
        check(PrimaryMouseButton.VirtualKey(false)==0x01&&PrimaryMouseButton.VirtualKey(true)==0x02,"Swapped mouse buttons did not map the primary drag button");
        // Explorer is a separate source process. A same-thread WPF drag source
        // blocks the target's dispatcher inside DoDragDrop and is not a valid
        // test of the global Explorer gesture.
        var identity=Guid.NewGuid().ToString("N")[..8];var folder=Directory.CreateDirectory(Path.Combine(root,"Nashi drag "+identity));var file=Path.Combine(folder.FullName,"Source "+identity+".png");File.Copy(original,file,true);void OpenFixture()=>Process.Start(new ProcessStartInfo(folder.FullName){UseShellExecute=true});OpenFixture();
        var phase="discovering Explorer";using var watchdog=new System.Threading.Timer(_=>{File.WriteAllText(Path.Combine(root,"global-gesture-timeout.txt"),phase);DesktopInput.HoldMouse(false);Environment.Exit(1);},null,TimeSpan.FromSeconds(75),System.Threading.Timeout.InfiniteTimeSpan);
        // UI Automation must run away from Nashi's UI thread: enumerating the
        // desktop can query this process's own WPF providers.
        var discovery=Task.Run(()=>
        {
            var lastTitles=new List<string>();for(var n=0;n<50;n++)
            {
                Thread.Sleep(100);if(n==15)OpenFixture();var windows=new List<IntPtr>();lastTitles.Clear();EnumWindows((window,_)=>{var type=new System.Text.StringBuilder(256);var title=new System.Text.StringBuilder(512);GetClassName(window,type,256);GetWindowText(window,title,512);if(title.Length>0)lastTitles.Add(type+" · "+title);if(type.ToString()=="CabinetWClass"&&title.ToString().Contains(folder.Name,StringComparison.OrdinalIgnoreCase))windows.Add(window);return true;},IntPtr.Zero);
                foreach(var handle in windows)
                {
                    try{var explorer=AutomationElement.FromHandle(handle);var item=explorer.FindFirst(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.NameProperty,Path.GetFileName(file)),new PropertyCondition(AutomationElement.NameProperty,Path.GetFileNameWithoutExtension(file))));if(item is not null&&!item.Current.BoundingRectangle.IsEmpty)return(Handle:handle,Bounds:item.Current.BoundingRectangle);}catch(ElementNotAvailableException){}
                }
            }
            File.WriteAllLines(Path.Combine(root,"explorer-windows.txt"),lastTitles);throw new Exception("Explorer did not expose its synthetic source file.");
        });
        var discoveryLimit=DateTime.UtcNow.AddSeconds(25);while(!discovery.IsCompleted&&DateTime.UtcNow<discoveryLimit)DesktopInput.Pump(30);if(!discovery.IsCompleted){DesktopCapture.Save(Path.Combine(root,"explorer-discovery.png"));throw new TimeoutException("Explorer UI Automation did not respond.");}if(discovery.IsFaulted)DesktopCapture.Save(Path.Combine(root,"explorer-discovery.png"));var fixture=discovery.GetAwaiter().GetResult();var handle=fixture.Handle;var bounds=fixture.Bounds;if(bounds.IsEmpty)throw new Exception("Explorer's source file has no visible bounds.");
        var settings=new PreferencesStore(Path.Combine(root,"Gesture preferences"));string? selected=null;string[]? received=null;
        using var controller=new GlobalDragController(owner,settings,(id,data,hint)=>{selected=id;received=data.GetData(DataFormats.FileDrop) as string[];},()=>selected="link-shortcut");
        void Drag(Key modifier,string mode,string expected)
        {
            phase="dragging "+mode;selected=null;string? state=null;SetForegroundWindow(handle);DesktopInput.Pump(200);var start=new Point(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);
            var driver=Task.Run(()=>
            {
                DesktopInput.MovePointer(start);Thread.Sleep(100);DesktopInput.HoldMouse(true);Thread.Sleep(150);
                try
                {
                    DesktopInput.MovePointer(new(start.X+45,start.Y+20));Thread.Sleep(150);DesktopInput.Hold(modifier,true);var wheel=IntPtr.Zero;for(var n=0;n<30&&wheel==IntPtr.Zero;n++){Thread.Sleep(100);wheel=FindWindow(null,"Nashi · "+mode);}if(wheel==IntPtr.Zero||!GetWindowRect(wheel,out var r))throw new Exception("The global "+mode+" wheel did not open during the Explorer drag.");
                    DesktopInput.MovePointer(new((r.Left+r.Right)/2d,r.Top+(r.Bottom-r.Top)*.18));Thread.Sleep(400);state=controller.ActiveWheel?.State;owner.Dispatcher.BeginInvoke(()=>DesktopCapture.Save(Path.Combine(root,"global-drag-"+mode+".png")));Thread.Sleep(200);
                }
                finally{DesktopInput.HoldMouse(false);DesktopInput.Hold(modifier,false);}
            });
            var until=DateTime.UtcNow.AddSeconds(12);while(!driver.IsCompleted&&DateTime.UtcNow<until)DesktopInput.Pump(30);if(!driver.IsCompleted)throw new TimeoutException("Global Explorer drag timed out.");driver.GetAwaiter().GetResult();DesktopInput.Pump(350);check(selected==expected,"Real Explorer OLE drag: "+expected+"; observed="+selected+"; "+state);check(received?.SingleOrDefault()==file,"Explorer drop preserved the selected file path");check(state?.Contains("revealed=True")==true,"Real Explorer OLE drag revealed the "+mode+" wheel; "+state);
        }
        try
        {
            Drag(Key.LeftCtrl,"tools","compress");Drag(Key.LeftAlt,"actions","share");Drag(Key.LeftShift,"convert","convert:"+FeatureCatalog.Formats([file])[0]);
            // A mouse drag that carries no files (e.g. Shift+drag text selection) must not show a wheel.
            phase="plain Shift-drag";var plain=new Window{Width=320,Height=200,Left=bounds.Left,Top=bounds.Top,WindowStartupLocation=WindowStartupLocation.Manual,Content=new System.Windows.Controls.Border{Background=System.Windows.Media.Brushes.DarkSlateGray}};plain.Show();plain.Activate();DesktopInput.Pump(200);
            try
            {
                var q=plain.PointToScreen(new Point(60,70));DesktopInput.MovePointer(q);DesktopInput.HoldMouse(true);DesktopInput.Pump(80);DesktopInput.MovePointer(new(q.X+60,q.Y+10));DesktopInput.Hold(Key.LeftShift,true);DesktopInput.Pump(260);
                check(controller.ActiveWheel is not {Revealed:true},"A plain Shift-drag without files showed the global wheel");
            }
            finally{DesktopInput.HoldMouse(false);DesktopInput.Hold(Key.LeftShift,false);DesktopInput.Pump(250);plain.Close();}
            if(controller.LinkShortcutRegistered){DesktopInput.Key(owner,Key.D,control:true,shift:true);check(selected=="link-shortcut","Global copied-link shortcut Ctrl+Shift+D");}
            var main=owner as MainWindow;var beforeLaunch=main?.SelectedFiles.ToArray();var modal=new Window{Owner=owner,Width=300,Height=200,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new System.Windows.Controls.Border{Background=System.Windows.Media.Brushes.DarkOliveGreen}};Exception? modalError=null;
            modal.Dispatcher.BeginInvoke(()=>
            {
                try
                {
                    var p=modal.PointToScreen(new Point(60,70));DesktopInput.MovePointer(p);DesktopInput.HoldMouse(true);DesktopInput.Pump(80);DesktopInput.MovePointer(new(p.X+50,p.Y));DesktopInput.Hold(Key.LeftShift,true);DesktopInput.Pump(220);check(controller.ActiveWheel is null,"Modal Shift-drag did not open a competing global wheel");DesktopInput.HoldMouse(false);DesktopInput.Hold(Key.LeftShift,false);var before=selected;DesktopInput.Key(modal,Key.D,control:true,shift:true);check(selected==before,"Modal editor kept its keyboard focus during the global link shortcut");if(main is not null){main.AcceptLaunch([file]);main.AcceptLaunch([]);check(main.SelectedFiles.SequenceEqual(beforeLaunch!),"Second launch kept the modal editor's original file selection");}
                }
                catch(Exception error){modalError=error;}
                finally{DesktopInput.HoldMouse(false);DesktopInput.Hold(Key.LeftShift,false);modal.Close();}
            });modal.ShowDialog();if(modalError is not null)throw modalError;if(main is not null){DesktopInput.Pump(300);check(main.SelectedFiles.SequenceEqual(new[]{file}),"Queued launch opened its file after the modal editor closed");main.AcceptLaunch(beforeLaunch!);}
            settings.Save(settings.Value with{Enabled=false});selected="disabled";DesktopInput.Key(owner,Key.D,control:true,shift:true);check(selected=="disabled","Disabled global shortcuts ignored copied-link input");
            var data=new DataObject();using var stream=new MemoryStream(System.Text.Encoding.Unicode.GetBytes("https://www.instagram.com/p/FixtureCode/\0"));data.SetData("UniformResourceLocatorW",stream);check(DragPayload.Parse(data) is {Kind:"instagramPhotos"},"Browser URL stream was decoded as a photo post");
            data.SetData(DataFormats.Html,"<a href=\"https://www.instagram.com/reel/FixtureCode/\"><img src=\"https://cdn.example/preview.jpg\"></a>");check(DragPayload.Parse(data) is {Kind:"instagramReel"},"Browser HTML identified a reel without thumbnail actions");
        }
        finally{DesktopInput.HoldMouse(false);DesktopInput.Hold(Key.LeftCtrl,false);DesktopInput.Hold(Key.LeftAlt,false);DesktopInput.Hold(Key.LeftShift,false);PostMessage(handle,0x10,IntPtr.Zero,IntPtr.Zero);}
    }
}
