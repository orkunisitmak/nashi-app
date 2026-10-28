using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;

internal static class WindowManagementSmoke
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint FindWindow(string? type,string title);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]private static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    [DllImport("user32.dll")]private static extern void keybd_event(byte key,byte scan,uint flags,nuint extra);

    internal static void Run(string output,Action<bool,string> check)
    {
        using var manager=new WindowManagement();
        var trace=Path.Combine(output,"window-management-trace.txt");
        var traceLock=new object();
        manager.Trace=message=>{lock(traceLock)File.AppendAllText(trace,DateTime.UtcNow.ToString("O")+" "+message+Environment.NewLine);};
        using var fixture=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,ArgumentList={"--window-management-fixture"}})??throw new Exception("Window fixture could not start.");
        nint handle=0;var deadline=DateTime.UtcNow.AddSeconds(10);
        while(handle==0&&DateTime.UtcNow<deadline){DesktopInput.Pump(100);handle=FindWindow(null,"Nashi Window Layout Fixture");}
        try
        {
            check(handle!=0,"Window fixture did not appear.");
            var area=WindowManagement.WorkArea(new WindowManagement.Native.Point{X=100,Y=100});
            foreach(var placement in Enum.GetValues<WindowPlacement>())
            {
                var expected=placement.Frame(area);
                // Exercise the production retry path: DWM's invisible borders may
                // still be settling when a newly created HWND first appears.
                var arrange=manager.ArrangeAsync(handle,placement,area);var until=DateTime.UtcNow.AddSeconds(5);
                while(!arrange.IsCompleted&&DateTime.UtcNow<until)DesktopInput.Pump(30);
                check(arrange.IsCompletedSuccessfully,"Native layout did not complete: "+placement);
                arrange.GetAwaiter().GetResult();var actual=WindowManagement.Bounds(handle);
                check(Math.Abs(actual.X-expected.X)<3&&Math.Abs(actual.Y-expected.Y)<3&&Math.Abs(actual.Width-expected.Width)<3&&Math.Abs(actual.Height-expected.Height)<3,$"Native window bounds mismatch: {placement}; expected={expected}; actual={actual}");
            }
            var index=0;
            foreach(var placement in Enum.GetValues<WindowPlacement>())
            {
                var shift=index++%2==0;
                Gesture(handle,manager,placement,shift:shift,cancel:false);
                var expected=placement.Frame(area);var actual=WindowManagement.Bounds(handle);
                check(Math.Abs(actual.X-expected.X)<3&&Math.Abs(actual.Y-expected.Y)<3&&Math.Abs(actual.Width-expected.Width)<3&&Math.Abs(actual.Height-expected.Height)<3,
                    $"{(shift?"Shift":"Alt")} native title-bar drag failed for {placement}. Expected {expected}; actual {actual}");
            }
            Gesture(handle,manager,WindowPlacement.RightHalf,shift:true,cancel:false,resize:true);
            var resized=WindowManagement.Bounds(handle);var resizeTarget=WindowPlacement.RightHalf.Frame(area);
            check(Math.Abs(resized.X-resizeTarget.X)<3&&Math.Abs(resized.Y-resizeTarget.Y)<3&&Math.Abs(resized.Width-resizeTarget.Width)<3&&Math.Abs(resized.Height-resizeTarget.Height)<3,"Native resize-border drag did not arrange the window.");
            Gesture(handle,manager,WindowPlacement.LeftTwoThirds,shift:true,cancel:true);
            check(manager.ActiveWheel is null&&!manager.PlacementPending,"Escape left the window wheel open or queued a placement.");
            var cancelled=WindowManagement.Bounds(handle);
            check(Math.Abs(cancelled.Width-620)<3&&Math.Abs(cancelled.Height-420)<3,"Escape applied a layout instead of preserving the native drag size.");

            var wheel=new WheelWindow(null,Enum.GetValues<WindowPlacement>().Select(p=>new ToolChoice(p.ToString(),p.Title(),"",p)).ToArray(),_=>{},externalDrag:true);
            wheel.Show();wheel.UpdateLayout();DesktopInput.Pump();
            var capture=new RenderTargetBitmap((int)wheel.ActualWidth,(int)wheel.ActualHeight,96,96,PixelFormats.Pbgra32);capture.Render(wheel);ImageEngine.Save(capture,Path.Combine(output,"windows-window-management.png"),"png");wheel.Close();
            using var constrained=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,ArgumentList={"--window-management-fixture","--minimum-window-width","1000"}})??throw new Exception("Constrained fixture could not start.");
            nint limited=0;deadline=DateTime.UtcNow.AddSeconds(10);while(limited==0&&DateTime.UtcNow<deadline){DesktopInput.Pump(100);limited=FindWindow(null,"Nashi Constrained Layout Fixture");}
            try
            {
                check(limited!=0,"Constrained window fixture did not appear");
                // Force the real native minimum, then let the child WPF layout
                // and DWM invisible resize borders settle before measuring it.
                _=WindowManagement.RequestFrame(limited,new(80,90,160,420));DesktopInput.Pump(300);
                var minimum=WindowManagement.Bounds(limited).Width;
                foreach(var placement in new[]{WindowPlacement.LeftThird,WindowPlacement.MiddleThird,WindowPlacement.RightThird,WindowPlacement.Maximize})
                {
                    Gesture(limited,manager,placement,shift:true,cancel:false);var expected=placement.Frame(area,minimum);var actual=WindowManagement.Bounds(limited);
                    check(Math.Abs(actual.X-expected.X)<3&&Math.Abs(actual.Width-expected.Width)<3&&Math.Abs(actual.Y-expected.Y)<3&&Math.Abs(actual.Height-expected.Height)<3,"Minimum-width window lost its selected anchor: "+placement+"; "+actual);
                }
            }
            finally{constrained.CloseMainWindow();if(!constrained.WaitForExit(2000))constrained.Kill();}
        }
        finally{fixture.CloseMainWindow();if(!fixture.WaitForExit(2000))fixture.Kill();}
    }
    private static void Gesture(nint target,WindowManagement manager,WindowPlacement choice,bool shift,bool cancel,bool resize=false)
    {
        var reset=new WindowFrame(80,90,620,420);_ =WindowManagement.RequestFrame(target,reset);DesktopInput.Pump(150);
        var task=Task.Run(()=>
        {
            var key=(byte)(shift?0x10:0x12);var down=false;
            try
            {
                SetForegroundWindow(target);Thread.Sleep(100);
                if(!WindowManagement.Native.GetWindowRect(target,out var raw))throw new Exception("Fixture bounds unavailable.");
                var x=resize?raw.Right-3:320;var y=resize?raw.Bottom-3:104;
                SetCursorPos(x,y);Thread.Sleep(100);
                mouse_event(2,0,0,0,0);down=true;Thread.Sleep(70);SetCursorPos(x+30,y+26);Thread.Sleep(80);
                keybd_event(key,0,0,0);SetCursorPos(x+55,y+41);
                nint wheel=0;var frame=default(WindowFrame);var deadline=DateTime.UtcNow.AddSeconds(3);
                var dpi=WindowManagement.Native.GetDpiForWindow(target);var expectedSize=(int)Math.Round(WheelPresentation.Diameter*(dpi>0?dpi:96)/96d);
                while(DateTime.UtcNow<deadline)
                {
                    Thread.Sleep(50);if(!manager.WheelReady)continue;var candidate=FindWindow(null,"Nashi Window Management");
                    if(candidate==0||!WindowManagement.Native.IsWindowVisible(candidate))continue;
                    var bounds=WindowManagement.Bounds(candidate);
                    // HWND creation precedes Show/Position; do not aim at the unpositioned default rectangle.
                    if(Math.Abs(bounds.Width-expectedSize)>2||Math.Abs(bounds.Height-expectedSize)>2)continue;
                    wheel=candidate;frame=bounds;break;
                }
                if(wheel==0)throw new Exception("Window wheel did not finish positioning during a native move.");
                var placements=Enum.GetValues<WindowPlacement>();
                manager.Trace?.Invoke($"test wheel={frame} choice={choice} resizeBorder={resize}");var index=Array.IndexOf(placements,choice);
                var angle=(WheelPresentation.StartAngle(placements.Length)+index*360d/placements.Length)*Math.PI/180;
                SetCursorPos((int)(frame.X+frame.Width*(.5+.33*Math.Cos(angle))),(int)(frame.Y+frame.Height*(.5+.33*Math.Sin(angle))));Thread.Sleep(180);
                if(cancel){keybd_event(0x1B,0,0,0);Thread.Sleep(100);keybd_event(0x1B,0,2,0);Thread.Sleep(100);}
            }
            finally{if(down)mouse_event(4,0,0,0,0);keybd_event(key,0,2,0);}
        });
        var timeout=DateTime.UtcNow.AddSeconds(8);while(!task.IsCompleted&&DateTime.UtcNow<timeout)DesktopInput.Pump(50);
        if(!task.IsCompleted)throw new Exception("Native window drag timed out.");task.GetAwaiter().GetResult();
        var completion=DateTime.UtcNow.AddSeconds(3);
        do{DesktopInput.Pump(100);}while((manager.ActiveWheel is not null||manager.PlacementPending)&&DateTime.UtcNow<completion);
    }
}
