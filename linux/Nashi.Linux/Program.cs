using Avalonia;
namespace Nashi.Linux;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args is ["--version"]){Console.WriteLine("Nashi "+typeof(Program).Assembly.GetName().Version?.ToString(3)+" for Linux");return 0;}
        if(args.Length>0&&args[0]=="--self-test")return Engine.SelfTest.RunAsync(args.Length>1?args[1]:null).GetAwaiter().GetResult();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
