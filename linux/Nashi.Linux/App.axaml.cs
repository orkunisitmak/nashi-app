using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
namespace Nashi.Linux;
public sealed partial class App:Application
{
    public override void Initialize(){AvaloniaXamlLoader.Load(this);ApplyTheme(LinuxSettings.Load().Theme);}
    private static Avalonia.Styling.IStyle? theme;
    /// Swaps the palette and shapes (Themes/*.axaml) while the app runs; every open window follows.
    public static void ApplyTheme(string name)
    {
        if(Current is not {} app)return;
        var file=name switch{"glass"=>"Glass","frosted"=>"Frosted","brutalist"=>"Brutalist",_=>"Classic"};
        var next=(Avalonia.Styling.IStyle)AvaloniaXamlLoader.Load(new Uri($"avares://nashi/Themes/{file}.axaml"));
        if(theme is not null)app.Styles.Remove(theme);
        app.Styles.Add(next);theme=next;
    }
    public static void ApplyAppearance(string appearance)
    {
        if(Current is {} app)app.RequestedThemeVariant=appearance switch{"light"=>ThemeVariant.Light,"dark"=>ThemeVariant.Dark,_=>ThemeVariant.Default};
    }
    /// Tray on: closing the window keeps Nashi (and its shortcuts) running. Tray off: closing quits.
    public static void SetBackground(UI.MainWindow window,bool enabled)
    {
        if(Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.ShutdownMode=enabled?ShutdownMode.OnExplicitShutdown:ShutdownMode.OnMainWindowClose;
        if(enabled)Tray(window);else if(Current is not null)TrayIcon.SetIcons(Current,[]);
    }
    private static void Tray(UI.MainWindow window)
    {
        var open=new NativeMenuItem(UI.L10n.T("Open Nashi"));open.Click+=(_,_)=>window.Bring();
        var gesture=new NativeMenuItem(UI.L10n.T("Settings"));gesture.Click+=async(_,_)=>{window.Bring();await window.OpenSettingsAsync();};
        var quit=new NativeMenuItem(UI.L10n.T("Quit Nashi"));quit.Click+=async(_,_)=>await window.QuitAsync();
        var tray=new TrayIcon{ToolTipText="Nashi",Menu=new NativeMenu{Items={open,gesture,new NativeMenuItemSeparator(),quit}}};
        using(var stream=typeof(App).Assembly.GetManifestResourceStream("Nashi.Mark.png")!)tray.Icon=new WindowIcon(stream);
        tray.Clicked+=(_,_)=>window.Bring();
        TrayIcon.SetIcons(Current!,[tray]);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args=desktop.Args??[];var window=new UI.MainWindow(args);
            // In the background (tray) Nashi keeps its global shortcuts and gestures ready.
            var background=window.LinuxSettings.RunInBackground;
            SetBackground(window,background);
            // --background (used at login) starts in the tray without showing the window.
            if(background&&args.Contains("--background"))Avalonia.Threading.Dispatcher.UIThread.Post(()=>desktop.MainWindow=window);
            else desktop.MainWindow=window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
