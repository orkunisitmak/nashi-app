using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
namespace Nashi.Linux;
public sealed partial class App:Application
{
    public override void Initialize()=>AvaloniaXamlLoader.Load(this);
    public static void ApplyAppearance(string appearance)
    {
        if(Current is {} app)app.RequestedThemeVariant=appearance switch{"light"=>ThemeVariant.Light,"dark"=>ThemeVariant.Dark,_=>ThemeVariant.Default};
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window=new UI.MainWindow(desktop.Args??[]);desktop.MainWindow=window;
            desktop.ShutdownMode=ShutdownMode.OnMainWindowClose;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
