using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
namespace Nashi.Linux;

internal static class Program
{
    internal static SingleInstance? Instance{get;private set;}
    [STAThread] public static void Main(string[] args)
    {
        if(args.Contains("--smoke-test")){BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);return;}
        try{using var instance=new SingleInstance();if(!instance.IsPrimary){instance.Forward(args);return;}Instance=instance;BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);}
        catch(Exception error){Console.Error.WriteLine("Nashi could not start: "+error.Message);Environment.ExitCode=1;}
    }
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect();
}
public sealed class App:Application
{
    public override void Initialize(){Styles.Add(new FluentTheme());}
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
            var args=desktop.Args??[];
            var smoke=args.Contains("--smoke-test");
            var directory=Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY")??Path.Combine(Path.GetTempPath(),"NashiLinuxSmoke-"+Guid.NewGuid().ToString("N"));
            var window=new MainWindow(args,smoke?new Nashi.Core.LibraryStore(Path.Combine(directory,"state")):null);desktop.MainWindow=window;
            Program.Instance?.SetHandler(window.HandleLaunch);
            if(smoke)window.Opened+=async(_,_)=>await SmokeChecks.Run(window,directory);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
