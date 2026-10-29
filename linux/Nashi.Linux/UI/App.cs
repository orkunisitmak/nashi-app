using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux.UI;

internal sealed class App:Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());Brand.Install(this);Name="Nashi";
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var library=new LibraryStore(Paths.Data);var preferences=new PreferencesStore(Paths.Data);
            L10n.Language=preferences.Value.Language;Brand.Apply(library.Appearance);
            var request=LaunchRequest.Parse(desktop.Args??[]);
            var window=new MainWindow(library,preferences,request.Clipboard?[]:request.Files);desktop.MainWindow=window;
            // Closing to the tray keeps Nashi one click away, like the macOS menu bar item.
            desktop.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window.Opened+=(_,_)=>{if(request.Wheel)_=Window(desktop).HandleAsync(request);};
            SingleInstance.Listen(next=>Dispatcher.UIThread.Post(()=>_=Window(desktop).HandleAsync(next)));
            TrayIcon.SetIcons(this,[Tray(desktop)]);
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static MainWindow Window(IClassicDesktopStyleApplicationLifetime desktop)=>(MainWindow)desktop.MainWindow!;
    private static TrayIcon Tray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        NativeMenuItem Item(string title,Action<MainWindow> action){var item=new NativeMenuItem(L10n.T(title));item.Click+=(_,_)=>{var window=Window(desktop);window.Reveal();action(window);};return item;}
        var quit=new NativeMenuItem(L10n.T("Quit Nashi"));quit.Click+=(_,_)=>{if(Window(desktop).Busy)Window(desktop).Reveal();else desktop.Shutdown();};
        var menu=new NativeMenu
        {
            Item("Choose Files…",w=>_=w.ChooseFromTrayAsync()),Item("Download from Link…",w=>_=w.PasteLinkFromTrayAsync()),
            Item("Settings…",w=>w.OpenSettings()),Item("Recent Conversions",w=>w.ShowLibrary(true)),new NativeMenuItemSeparator(),
            Item("Show Nashi",_=>{}),quit
        };
        var tray=new TrayIcon{Icon=new WindowIcon(Brand.Asset("NashiIcon")),ToolTipText="Nashi",Menu=menu};
        tray.Clicked+=(_,_)=>Window(desktop).Reveal();
        return tray;
    }
}
