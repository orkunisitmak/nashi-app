using Avalonia;
namespace Nashi.Linux;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args is ["--version"]){Console.WriteLine("Nashi "+typeof(Program).Assembly.GetName().Version?.ToString(3)+" for Linux");return 0;}
        if(args.Length>0&&args[0]=="--self-test")return Engine.SelfTest.RunAsync(args.Length>1?args[1]:null).GetAwaiter().GetResult();
        var files=args.Where(a=>!a.StartsWith("--",StringComparison.Ordinal)).ToArray();
        // A second launch (for example "Open with Nashi") hands its files to the open window.
        if(!args.Contains("--new-instance")&&SingleInstance.HandOff(files))return 0;
        try{SingleInstance.Listen();}catch(System.Net.Sockets.SocketException){}catch(IOException){}
        // Logout and `kill` send SIGTERM; remove the socket then too (the default exit still follows).
        using var terminate=System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM,_=>SingleInstance.Stop());
        try{return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);}
        finally{SingleInstance.Stop();}
    }
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
