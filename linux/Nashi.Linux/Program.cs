using Avalonia;
namespace Nashi.Linux;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args is ["--version"]){Console.WriteLine("Nashi "+typeof(Program).Assembly.GetName().Version?.ToString(3)+" for Linux");return 0;}
        if(args is ["--shortcuts-check"])return ShortcutsCheck().GetAwaiter().GetResult();
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
    /// Registers a throwaway component with KDE, presses its shortcut through kglobalaccel and
    /// checks that Nashi hears it, then removes it again. The user's own shortcuts are untouched.
    private static async Task<int> ShortcutsCheck()
    {
        using var shortcuts=await GlobalShortcuts.TryCreateAsync("nashi-check","Nashi check");
        if(shortcuts is null){Console.WriteLine("KDE global shortcuts (kglobalaccel) are not available in this session.");return 2;}
        foreach(var combo in new[]{"Meta+Space","Meta+Alt+C","Meta+Alt+T","Meta+Alt+A","Meta+Alt+V","Meta+Alt+N","Meta+Alt+D","Ctrl+Alt+Space"})
            Console.WriteLine($"  {combo,-16} {await shortcuts.OwnerAsync(combo)??"free"}");
        var heard=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);shortcuts.Pressed+=id=>heard.TrySetResult(id);
        var accepted=await shortcuts.BindAsync("check","Self check","Meta+Ctrl+Alt+Shift+F20");
        Console.WriteLine("  bound: "+(accepted??"(refused)"));
        await shortcuts.InvokeAsync("check");
        var result=await Task.WhenAny(heard.Task,Task.Delay(3000))==heard.Task?heard.Task.Result:null;
        await shortcuts.RemoveComponentAsync();
        Console.WriteLine(result=="check"?"PASS shortcut press reached Nashi":"FAIL no press received");
        return result=="check"?0:1;
    }
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont()
        // Matches StartupWMClass in nashi.desktop, so docks and task bars show the Nashi icon.
        .With(new X11PlatformOptions{WmClass="nashi"}).LogToTrace();
}
