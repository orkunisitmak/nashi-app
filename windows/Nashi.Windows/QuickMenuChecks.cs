using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
namespace Nashi.Windows;
/// <summary>The quick menu shortcut: parsing, real registration and WM_HOTKEY delivery, a shortcut another owner holds, and the menu's keys.</summary>
internal static class QuickMenuChecks
{
    [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    internal static void Run(Action<bool,string> check)
    {
        check(QuickMenuHotkey.Parse("Win+Alt+N")==(0x4000u|0x8|0x1,(uint)'N')&&QuickMenuHotkey.Parse("Ctrl+Shift+Space")==(0x4000u|0x2|0x4,0x20u),"Shortcuts must parse into modifiers and a key");
        check(QuickMenuHotkey.Parse(QuickMenuHotkey.Off) is null&&QuickMenuHotkey.Parse("N") is null&&QuickMenuHotkey.Parse("Ctrl+Shift") is null&&QuickMenuHotkey.Parse("Ctrl+F13") is null&&QuickMenuHotkey.Parse(null) is null,"Off, a bare key, no key or an unknown key must not register");
        check(QuickMenuHotkey.Choices.Take(QuickMenuHotkey.Choices.Length-1).All(c=>QuickMenuHotkey.Parse(c) is not null)&&QuickMenuHotkey.Choices[0]==new Nashi.Core.Preferences().QuickMenuShortcut,"Every offered shortcut must be valid, and the default must be offered first");

        // A combination nobody uses, registered for real, then delivered as WM_HOTKEY.
        var pressed=0;using(var hotkey=new QuickMenuHotkey(()=>pressed++))
        {
            hotkey.Apply("Ctrl+Alt+Shift+Z");check(hotkey.Registered,"The quick menu shortcut must register");
            PostMessage(hotkey.Handle,0x0312,new IntPtr(7702),IntPtr.Zero);DesktopInput.Pump(200);
            check(pressed==1,"Pressing the shortcut must open the quick menu");
            PostMessage(hotkey.Handle,0x0312,new IntPtr(7701),IntPtr.Zero);DesktopInput.Pump(150);check(pressed==1,"Another shortcut's message must be ignored");
            using(var other=new QuickMenuHotkey(()=>{})){other.Apply("Ctrl+Alt+Shift+Z");check(!other.Registered,"A shortcut another owner holds must be reported, not taken");}
            hotkey.Apply(QuickMenuHotkey.Off);check(!hotkey.Registered,"Off must release the shortcut");
            using(var after=new QuickMenuHotkey(()=>{})){after.Apply("Ctrl+Alt+Shift+Z");check(after.Registered,"A released shortcut must be free for others");}
        }

        // The menu: a number key runs its entry after the menu closed; Esc closes without running anything.
        var ran=new List<string>();
        QuickMenuWindow.Entry Entry(string name)=>new(name,"•",()=>ran.Add(name));
        var menu=new QuickMenuWindow([Entry("first"),Entry("second"),Entry("third")],"Win+Alt+N");menu.Show();DesktopInput.Pump(250);
        check(menu.ButtonsForTest.Count==3&&menu.IsVisible,"The quick menu must list its entries");
        menu.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(menu)!,0,Key.D2){RoutedEvent=Keyboard.PreviewKeyDownEvent});DesktopInput.Pump(200);
        check(!menu.IsVisible&&ran.SequenceEqual(["second"]),"Key 2 must close the menu, then run the second entry: "+string.Join(",",ran));
        var cancelled=new QuickMenuWindow([Entry("first")],null);cancelled.Show();DesktopInput.Pump(200);
        cancelled.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(cancelled)!,0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});DesktopInput.Pump(150);
        check(!cancelled.IsVisible&&ran.Count==1,"Esc must close the menu without running anything");
        var twice=new QuickMenuWindow([Entry("once")],null);twice.Show();DesktopInput.Pump(150);twice.Choose(0);twice.Choose(0);DesktopInput.Pump(150);
        check(ran.Count(r=>r=="once")==1,"An entry must run once");
        var closedByCode=new QuickMenuWindow([Entry("never")],null);closedByCode.Show();DesktopInput.Pump(200);closedByCode.Activate();DesktopInput.Pump(100);closedByCode.Close();DesktopInput.Pump(150);
        check(!closedByCode.IsVisible&&!ran.Contains("never"),"Closing the menu from code (the shortcut pressed again) must not close it twice or run anything");
        var window=new MainWindow(checkForUpdates:false,offerToolSetup:false);
        check(window.QuickMenuEntries.Select(e=>e.Title).SequenceEqual([MainWindow.ScreenTextTitle,MainWindow.ScreenQrTitle,"Download copied link","Open Nashi"]),"The quick menu must offer screen text first, then QR, copied link and Nashi");
        window.Close();
    }
}
