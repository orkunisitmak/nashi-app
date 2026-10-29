using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using System.Security.Cryptography;
using System.Text;
namespace Nashi.Linux;

public sealed class App : Application
{
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow=new MainWindow(desktop.Args??[]);
        base.OnFrameworkInitializationCompleted();
    }
}
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Contains("--smoke-test")&&string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY")))
        {Console.Error.WriteLine("Set NASHI_SMOKE_DIRECTORY for the isolated smoke test.");return 1;}
        // Protect the shared history/preferences from concurrent app instances.
        var identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Operations.DataDirectory)));
        using var mutex=new Mutex(false,"Nashi.Linux."+identity);
        bool owned;
        try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
        if(!owned){Console.Error.WriteLine("Nashi is already running. Drop files into its window.");return 1;}
        try{return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont()
            .LogToTrace().StartWithClassicDesktopLifetime(args);}
        finally{mutex.ReleaseMutex();}
    }
}
