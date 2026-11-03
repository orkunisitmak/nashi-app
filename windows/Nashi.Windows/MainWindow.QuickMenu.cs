using System.Windows;
namespace Nashi.Windows;
public sealed partial class MainWindow
{
    internal const string QuickMenuShortcutLabel="Quick menu shortcut";
    internal const string QuickMenuHint="Opens a small Nashi menu at the cursor from any app: copy text from screen, scan QR codes, use a copied link or open Nashi.";
    internal const string QuickMenuTaken="Another app already uses this shortcut. Choose another one.";
    private QuickMenuHotkey? quickMenu;private QuickMenuWindow? quickMenuWindow;
    internal QuickMenuWindow? QuickMenuForTest=>quickMenuWindow;
    internal IReadOnlyList<QuickMenuWindow.Entry> QuickMenuEntries=>
    [
        new(ScreenTextTitle,"¶",()=>_ = CopyScreenTextAsync()),
        new(ScreenQrTitle,"▧",()=>_ = ScanScreenQrAsync()),
        new("Download copied link","↓",OpenCopiedLink),
        new("Open Nashi","◉",ShowMain),
    ];
    /// <summary>Shows the quick menu at the cursor; pressing the shortcut again closes it.</summary>
    internal void ShowQuickMenu()
    {
        if(quickMenuWindow is {IsVisible:true} open){open.Close();return;}
        quickMenuWindow=new QuickMenuWindow(QuickMenuEntries,quickMenu?.Registered==true?quickMenu.Shortcut:null);quickMenuWindow.Closed+=(_,_)=>quickMenuWindow=null;quickMenuWindow.Show();
    }
}
